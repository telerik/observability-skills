namespace CustomAgent;

/// <summary>
/// Provides local search and reading methods. Load reads the files declared under Content:Sources from docs/ and
/// data/; the constructor only indexes documents that are already in memory. An agent whose tools only compute or
/// read approved hosts declares no content and gets an empty knowledge base. During custom agent generation, the
/// coding agent chooses which methods to use in Tools.cs; KnowledgeBase.cs stays unchanged.
/// </summary>
public class KnowledgeBase
{
    private const int MaxFiles = 10;
    private const long MaxFileBytes = 1_048_576;
    private const long MaxTotalBytes = 5_242_880;
    private const int MaxChunkCharacters = 1_200;
    private const int MaxReadCharacters = 8_000;

    private readonly Dictionary<string, SourceDocument> _sources =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ContentChunk> _chunks = [];

    private KnowledgeBase(IReadOnlyList<SourceDocument> documents)
    {
        // Index each loaded document by its label and split it into the chunks that Search and SearchData score.
        foreach (var source in documents)
        {
            _sources.Add(source.Label, source);
            foreach (var chunk in SplitIntoChunks(source.Content))
            {
                _chunks.Add(new ContentChunk(
                    source.Label,
                    chunk.Section,
                    chunk.Content,
                    source.Provenance,
                    source.IsStructuredData));
            }
        }
    }

    public static KnowledgeBase Load(string contentRoot, IConfigurationSection sourceConfiguration)
    {
        // Only declared files load, each with mock or supplied provenance; an undeclared or missing file fails startup.
        var root = Path.GetFullPath(contentRoot);
        var declared = ReadDeclaredSources(sourceConfiguration);
        var documents = new List<SourceDocument>();
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var totalBytes = 0L;

        LoadFolder(root, "docs", new HashSet<string>(StringComparer.Ordinal) { ".md", ".txt" }, false);
        LoadFolder(root, "data", new HashSet<string>(StringComparer.Ordinal) { ".json", ".csv" }, true);

        var unmatched = declared.Keys
            .Where(label => !loaded.Contains(label))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (unmatched is not null)
            throw new InvalidOperationException($"Declared local content source was not loaded: {unmatched}");
        return new KnowledgeBase(documents);

        void LoadFolder(
            string baseDirectory,
            string folder,
            HashSet<string> allowedExtensions,
            bool isStructuredData)
        {
            // Links and hidden files are never read, and file count and size limits apply while loading.
            var directory = Path.Combine(baseDirectory, folder);
            if (!Directory.Exists(directory)) return;
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException($"Local content directory must not be a link: {folder}");

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            };
            foreach (var path in Directory.EnumerateFiles(directory, "*", options)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!allowedExtensions.Contains(Path.GetExtension(path))) continue;
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                if (relative.Split('/').Any(segment => segment.StartsWith(".", StringComparison.Ordinal)))
                    continue;

                var label = $"{folder}/{relative}";
                if (!declared.TryGetValue(label, out var provenance))
                    throw new InvalidOperationException($"Local content source is missing provenance: {label}");

                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes)
                    throw new InvalidOperationException($"Local content file '{label}' exceeds the 1 MiB limit.");
                if (documents.Count >= MaxFiles)
                    throw new InvalidOperationException($"Local content may contain at most {MaxFiles} files.");
                if (info.Length > MaxTotalBytes - totalBytes)
                    throw new InvalidOperationException("Local content exceeds the 5 MiB total limit.");

                totalBytes += info.Length;
                if (!loaded.Add(label))
                    throw new InvalidOperationException($"Local content source is listed twice: {label}");
                documents.Add(new SourceDocument(label, File.ReadAllText(path), provenance, isStructuredData));
            }
        }
    }

    public int SourceCount => _sources.Count;

    public string Search(string query, int topK = 3)
        => SearchChunks(query, _chunks, "local_content", topK);

    public string SearchData(string query, int topK = 3)
        => SearchChunks(query, _chunks.Where(chunk => chunk.IsStructuredData), "structured_data", topK);

    public string Read(string sourceLabel)
    {
        // Read a whole source by its label or file name, capped at MaxReadCharacters.
        var normalized = NormalizeLabel(sourceLabel);
        var source = _sources.Values.FirstOrDefault(item =>
            string.Equals(item.Label, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(item.Label), normalized, StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return "status=not_found; reason=local_source_missing";

        var content = source.Content.Length <= MaxReadCharacters
            ? source.Content
            : source.Content[..MaxReadCharacters] + "\n[truncated]";
        return $"status=found; provenance={ProvenanceLabel(source.Provenance)}; source={source.Label}; " +
               $"section=full_document; content_complete={source.Content.Length <= MaxReadCharacters}\n{content}";
    }

    private static Dictionary<string, SourceProvenance> ReadDeclaredSources(
        IConfigurationSection configuration)
    {
        var result = new Dictionary<string, SourceProvenance>(StringComparer.Ordinal);
        foreach (var item in configuration.GetChildren())
        {
            var label = item.Key;
            if (!IsValidSourceLabel(label) || !result.TryAdd(label, ParseProvenance(item.Value, label)))
                throw new InvalidOperationException($"Invalid or duplicate local content source declaration: {label}");
        }
        if (result.Count > MaxFiles)
            throw new InvalidOperationException($"Content:Sources may declare at most {MaxFiles} local files.");
        return result;
    }

    private static SourceProvenance ParseProvenance(string? value, string label) => value switch
    {
        "mock" => SourceProvenance.Mock,
        "supplied" => SourceProvenance.Supplied,
        _ => throw new InvalidOperationException(
            $"Content source '{label}' must have provenance 'mock' or 'supplied'."),
    };

    private static bool IsValidSourceLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || label != label.Trim() || label.Contains('\\') ||
            Path.IsPathRooted(label) || label.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".." || segment.StartsWith(".", StringComparison.Ordinal)))
        {
            return false;
        }

        var extension = Path.GetExtension(label);
        return label.StartsWith("docs/", StringComparison.Ordinal) && extension is ".md" or ".txt" ||
               label.StartsWith("data/", StringComparison.Ordinal) && extension is ".json" or ".csv";
    }

    private static IEnumerable<SectionChunk> SplitIntoChunks(string content)
    {
        // Each Markdown heading starts a section; blank lines end paragraphs, and long paragraphs become fixed-size chunks.
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var section = "(no heading)";
        var paragraph = new List<string>();
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim();
            var headingLength = trimmed.TakeWhile(character => character == '#').Count();
            var isHeading = headingLength is >= 1 and <= 6 &&
                            (trimmed.Length == headingLength || char.IsWhiteSpace(trimmed[headingLength]));
            if (isHeading || trimmed.Length == 0)
            {
                foreach (var chunk in SplitParagraph(section, paragraph)) yield return chunk;
                paragraph.Clear();
                if (isHeading) section = trimmed[headingLength..].Trim();
            }
            else paragraph.Add(line);
        }
        foreach (var chunk in SplitParagraph(section, paragraph)) yield return chunk;
    }

    private static IEnumerable<SectionChunk> SplitParagraph(string section, List<string> lines)
    {
        var paragraph = string.Join('\n', lines).Trim();
        for (var offset = 0; offset < paragraph.Length; offset += MaxChunkCharacters)
        {
            yield return new SectionChunk(
                section,
                paragraph.Substring(offset, Math.Min(MaxChunkCharacters, paragraph.Length - offset)));
        }
    }

    private static string SearchChunks(
        string query,
        IEnumerable<ContentChunk> candidates,
        string emptyReason,
        int topK = 3)
    {
        // Score chunks by how many query words appear in their text or section heading; return at most three.
        var terms = QueryTerms(query);
        if (terms.Length == 0)
            return "status=not_found; reason=query_has_no_search_terms";

        var hits = candidates
            .Select(item => new
            {
                Chunk = item,
                Score = terms.Count(term =>
                    item.Content.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (item.Section != "(no heading)" &&
                     item.Section.Contains(term, StringComparison.OrdinalIgnoreCase))),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.Source, StringComparer.Ordinal)
            .Take(Math.Clamp(topK, 1, 3))
            .ToArray();
        if (hits.Length == 0)
            return $"status=not_found; reason=no_matching_{emptyReason}";

        return string.Join("\n\n", hits.Select(hit =>
            $"status=found; provenance={ProvenanceLabel(hit.Chunk.Provenance)}; " +
            $"source={hit.Chunk.Source}; section={hit.Chunk.Section}\n{hit.Chunk.Content}"));
    }

    private static string[] QueryTerms(string query)
    {
        char[] separators =
        [
            ' ', '\t', '\r', '\n', '.', ',', ':', ';', '?', '!', '(', ')', '[', ']', '{', '}', '/', '-', '_', '"', '\'',
        ];
        return query
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeLabel(string value)
        => value.Trim().Replace('\\', '/').TrimStart('.', '/');

    private static string ProvenanceLabel(SourceProvenance provenance)
        => provenance == SourceProvenance.Mock ? "mock" : "supplied";

    private enum SourceProvenance { Mock, Supplied }
    private sealed record SourceDocument(
        string Label,
        string Content,
        SourceProvenance Provenance,
        bool IsStructuredData);
    private sealed record SectionChunk(string Section, string Content);
    private sealed record ContentChunk(
        string Source,
        string Section,
        string Content,
        SourceProvenance Provenance,
        bool IsStructuredData);
}

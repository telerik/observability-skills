namespace ReleaseEvidenceReviewer;

/// <summary>
/// Provides methods to search and read local release evidence for the reviewer's tools. Load reads the Markdown
/// files in docs/; the constructor indexes documents that are already in memory, keyed by file name.
/// </summary>
public class KnowledgeBase
{
    private readonly Dictionary<string, string> _documents;
    private readonly List<(string Source, string Paragraph)> _paragraphs = [];

    public KnowledgeBase(IReadOnlyDictionary<string, string> documents)
    {
        _documents = new Dictionary<string, string>(documents, StringComparer.OrdinalIgnoreCase);
        // Each blank-line-separated paragraph becomes a search candidate, labeled with its file name.
        foreach (var (source, markdown) in _documents)
        {
            foreach (var paragraph in markdown
                         .Replace("\r\n", "\n", StringComparison.Ordinal)
                         .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _paragraphs.Add((source, paragraph));
            }
        }
    }

    public static KnowledgeBase Load(string docsFolder)
    {
        // Prefer the docs copied beside the binary; fall back to the folder under the working directory.
        var outputPath = Path.Combine(AppContext.BaseDirectory, docsFolder);
        var sourcePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), docsFolder));
        var directory = Directory.Exists(outputPath) ? outputPath : sourcePath;
        var documents = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.md")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path),
                    File.ReadAllText,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new KnowledgeBase(documents);
    }

    public int DocumentCount => _documents.Count;
    public IEnumerable<string> ProjectNames => _documents.Keys
        .Where(id => id.StartsWith("project-", StringComparison.OrdinalIgnoreCase)).Select(id => id[8..]);

    public bool TryRead(string documentId, out string markdown)
        => _documents.TryGetValue(documentId, out markdown!);

    public string Search(string query, int topK = 3, IReadOnlySet<string>? sources = null)
    {
        if (_paragraphs.Count == 0)
            return "status=not_found; reason=release_evidence_corpus_empty";

        // Score each allowed paragraph by how many distinct query words (longer than two characters) it contains.
        var separators = new[]
        {
            ' ', '\t', '\r', '\n', '.', ',', ':', ';', '?', '!', '(', ')', '[', ']', '/', '-', '_',
        };
        var terms = query
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var hits = _paragraphs
            .Where(item => sources is null || sources.Contains(item.Source))
            .Select(item => new
            {
                item.Source,
                item.Paragraph,
                Score = terms.Count(term =>
                    item.Paragraph.Contains(term, StringComparison.OrdinalIgnoreCase)),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .Take(topK)
            .ToArray();

        return hits.Length == 0
            ? "status=not_found; reason=no_matching_release_evidence"
            : string.Join("\n\n", hits.Select(hit => $"[{hit.Source}] {hit.Paragraph}"));
    }
}

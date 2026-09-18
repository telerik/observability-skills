using System.Text.RegularExpressions;

namespace DocsQa;

/// <summary>
/// Load reads the bundled Markdown files in docs/; the constructor splits documents that are already in memory into
/// sections and searches them by matching words. Deliberately local lexical RAG: no vector service, embeddings or
/// hidden index.
/// </summary>
public class DocumentStore
{
    private static readonly HashSet<string> StopWords = new(
        "a an and are as at be before both by can could data do does document documentation documents explain for from have how i in information is it local many me of on or our please policy product question sample should source sources tell than that the their them there these they this to use using was we what when where which who why will with would you your about also long".Split(' '),
        StringComparer.Ordinal);
    private readonly Dictionary<string, DocumentSection> _sections = new(StringComparer.Ordinal);
    public IReadOnlyList<DocumentSection> Sections => _sections.Values.ToArray();
    public int DocumentCount { get; }

    public DocumentStore(IReadOnlyList<(string DocumentId, string Markdown)> documents)
    {
        DocumentCount = documents.Count;
        foreach (var (documentId, markdown) in documents)
        {
            var title = documentId;
            string? heading = null;
            var lines = new List<string>();
            void AddSection()
            {
                var excerpt = string.Join('\n', lines).Trim();
                if (heading is null || excerpt.Length == 0) return;
                if (excerpt.Length > 4_000) throw new InvalidOperationException("section_too_large");
                var id = documentId + "#" + Regex.Replace(heading.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                if (!_sections.TryAdd(id, new(id, documentId, title, heading, excerpt)))
                    throw new InvalidOperationException("duplicate_source_id");
            }
            // Each ## heading starts a section; the document ID (file name) and heading form its source ID.
            foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
            {
                if (line.StartsWith("# ", StringComparison.Ordinal)) title = line[2..].Trim();
                else if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    AddSection();
                    heading = line[3..].Trim();
                    lines.Clear();
                }
                else if (heading is not null) lines.Add(line);
            }
            AddSection();
        }
    }

    public static DocumentStore Load(string folder)
    {
        // A missing folder gives an empty store; the corpus is capped at 20 files of at most 64 KiB each.
        if (!Directory.Exists(folder)) return new DocumentStore([]);
        var files = Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal).ToArray();
        if (files.Length > 20) throw new InvalidOperationException("corpus_too_large");
        if (files.Any(file => new FileInfo(file).Length > 65_536)) throw new InvalidOperationException("document_too_large");
        return new DocumentStore(files.Select(file => (Path.GetFileNameWithoutExtension(file), File.ReadAllText(file))).ToArray());
    }

    public IReadOnlyList<DocumentSection> Search(string query, int limit = 4, string? sourceId = null)
    {
        ValidateQuestion(query);
        if (limit is < 1 or > 4) throw new ArgumentException("invalid_result_limit");
        var terms = Tokenize(query);
        if (terms.Count == 0) return [];
        // Give heading/title matches more weight; sort tied scores by source ID for consistent results.
        return _sections.Values.Where(section => sourceId is null || section.SourceId == sourceId).Select(section => new
        {
            Section = section,
            Score = Tokenize(section.Heading).Intersect(terms).Count() * 3 +
                        Tokenize(section.Title).Intersect(terms).Count() * 2 +
                        Tokenize(section.Excerpt).Intersect(terms).Count(),
        })
            .Where(hit => hit.Score > 0)
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Section.SourceId, StringComparer.Ordinal)
            .Take(limit).Select(hit => hit.Section).ToArray();
    }

    public DocumentSection? Read(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 160)
            throw new ArgumentException("invalid_source_id");
        // Resolve IDs only against the sections loaded at startup.
        return _sections.GetValueOrDefault(sourceId);
    }

    public string? ValidateSourceId(string? sourceId)
    {
        if (sourceId is not null && Read(sourceId) is null) throw new ArgumentException("unknown_source_id");
        return sourceId;
    }

    public static string ValidateQuestion(string? question)
    {
        var value = question?.Trim();
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("question_required");
        if (value.Length > 4_000) throw new ArgumentException("question_too_long");
        return value;
    }

    private static HashSet<string> Tokenize(string value)
        => Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]+")
            .Select(match => match.Value)
            .Where(term => term.Length > 2 && !StopWords.Contains(term))
            .Select(term => term.Length > 4 && term.EndsWith('s') ? term[..^1] : term)
            .ToHashSet(StringComparer.Ordinal);
}

public sealed record DocumentSection(string SourceId, string DocumentId, string Title, string Heading, string Excerpt);

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

return ProjectValidator.Run(args);

/// <summary>
/// Checks a generated custom agent before build and after the smoke run: fixed starter files unchanged, only allowed
/// files and settings, declared content and approved hosts, and smoke expectations. With --smoke-baseline it also
/// checks that the smoke cases and approved hosts still match the snapshot saved before the first smoke. It does not
/// analyze editable C#. Prints VALIDATION_OK, or the first problem found with exit code 2.
/// </summary>
static class ProjectValidator
{
    private const string Usage = "Usage: dotnet run --file validate-project.cs -- [--target <directory>] [--smoke-baseline <appsettings-snapshot>]";
    private const int MaxContentFiles = 10;
    private const long MaxContentFileBytes = 1_048_576;
    private const long MaxContentTotalBytes = 5_242_880;

    private static readonly HashSet<string> FixedFiles = new(StringComparer.Ordinal)
    {
        ".gitignore",
        "AgentRuntime.cs",
        "Capabilities.cs",
        "ChatHistory.cs",
        "CustomAgent.csproj",
        "KnowledgeBase.cs",
        "Program.cs",
        "README.md",
        "SmokeRunner.cs",
        "wwwroot/app.js",
        "wwwroot/index.html",
        "wwwroot/styles.css",
    };

    private static readonly HashSet<string> RequiredEditableFiles = new(StringComparer.Ordinal)
    {
        "AgentDefinition.cs",
        "Tools.cs",
        "appsettings.json",
    };

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            var (targetArgument, smokeBaseline) = ParseOptions(args);
            var asset = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(CurrentFile())!, "..", "assets", "custom-agent-starter"));
            var target = ValidateTarget(asset, targetArgument);
            var files = CollectFiles(target);
            ValidateFixedFiles(asset, target, files);
            ValidateRequiredEditableFiles(files);
            var content = ValidateAllowedFiles(target, files);
            ValidateSettings(asset, target, files, smokeBaseline);
            Console.WriteLine(
                $"VALIDATION_OK target={target} content_files={content.Count} content_bytes={content.TotalBytes}");
            return 0;
        }
        catch (Exception error) when (error is ArgumentException
                                      or IOException
                                      or UnauthorizedAccessException
                                      or InvalidDataException
                                      or JsonException)
        {
            Console.Error.WriteLine($"validate-project: {error.Message}");
            return 2;
        }
    }

    private static (string Target, string? SmokeBaseline) ParseOptions(string[] args)
    {
        string? target = null, baseline = null;
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException(Usage);
            switch (args[index])
            {
                case "--target" when target is null: target = args[index + 1]; break;
                case "--smoke-baseline" when baseline is null: baseline = args[index + 1]; break;
                default: throw new ArgumentException(Usage);
            }
        }
        return (target ?? "custom-agent", baseline);
    }

    private static string ValidateTarget(string asset, string targetArgument)
    {
        if (targetArgument.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
            throw new ArgumentException($"Target must not contain '..': {targetArgument}");

        var target = Path.GetFullPath(targetArgument);
        RejectSymlinkPathComponents(target, "Target");
        if (!Directory.Exists(target))
            throw new ArgumentException($"Target must be a real directory: {targetArgument}");

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var assetPrefix = asset.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target.Equals(asset, comparison) || target.StartsWith(assetPrefix, comparison))
            throw new ArgumentException("Target must be outside the bundled starter asset.");
        return target;
    }

    private static Dictionary<string, string> CollectFiles(string target)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(target, "", files);
        return files;

        static void Walk(string directory, string relativeDirectory, Dictionary<string, string> result)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(value => value, StringComparer.Ordinal))
            {
                if (IsLink(entry))
                    throw new InvalidDataException($"Symlinks are not allowed: {Relative(entry)}");

                var name = Path.GetFileName(entry);
                var relative = string.IsNullOrEmpty(relativeDirectory)
                    ? name
                    : $"{relativeDirectory}/{name}";
                if (Directory.Exists(entry))
                {
                    if (relative is "bin" or "obj") continue;
                    if (!IsAllowedDirectory(relative))
                        throw new InvalidDataException($"Unexpected directory: {relative}");
                    Walk(entry, relative, result);
                }
                else
                {
                    result.Add(relative, entry);
                }
            }

            string Relative(string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');
        }
    }

    private static bool IsAllowedDirectory(string relative)
        => relative is "docs" or "data" or "wwwroot" ||
           relative.StartsWith("docs/", StringComparison.Ordinal) ||
           relative.StartsWith("data/", StringComparison.Ordinal);

    private static void ValidateFixedFiles(
        string asset,
        string target,
        IReadOnlyDictionary<string, string> files)
    {
        foreach (var relative in FixedFiles)
        {
            if (!files.TryGetValue(relative, out var targetPath))
                throw new InvalidDataException($"Required fixed file is missing: {relative}");
            var assetPath = Path.Combine(asset, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.ReadAllBytes(assetPath).SequenceEqual(File.ReadAllBytes(targetPath)))
                throw new InvalidDataException($"Fixed file was modified: {relative}");
        }
    }

    private static void ValidateRequiredEditableFiles(IReadOnlyDictionary<string, string> files)
    {
        foreach (var relative in RequiredEditableFiles)
        {
            if (!files.ContainsKey(relative))
                throw new InvalidDataException($"Required customization file is missing: {relative}");
        }
    }

    private static ContentSummary ValidateAllowedFiles(
        string target,
        IReadOnlyDictionary<string, string> files)
    {
        var contentCount = 0;
        long contentBytes = 0;
        foreach (var (relative, fullPath) in files)
        {
            if (FixedFiles.Contains(relative) || RequiredEditableFiles.Contains(relative)) continue;
            if (relative == "INTEGRATION_PLAN.md")
            {
                if (new FileInfo(fullPath).Length > MaxContentFileBytes)
                    throw new InvalidDataException("INTEGRATION_PLAN.md exceeds the 1 MiB limit.");
                continue;
            }

            if (relative == ".env" || relative.StartsWith(".env.", StringComparison.Ordinal))
                throw new InvalidDataException($"Secret environment files are not allowed: {relative}");
            if (!IsSupportedContent(relative))
                throw new InvalidDataException($"Unexpected file: {relative}");
            if (relative.Split('/').Any(segment => segment.StartsWith(".", StringComparison.Ordinal)))
                throw new InvalidDataException($"Hidden content paths are not allowed: {relative}");

            var length = new FileInfo(fullPath).Length;
            if (length > MaxContentFileBytes)
                throw new InvalidDataException($"Content file exceeds the 1 MiB limit: {relative}");
            contentCount++;
            contentBytes += length;
        }

        // Content is optional: an agent whose tools only compute or read approved hosts declares none.
        if (contentCount > MaxContentFiles)
            throw new InvalidDataException($"Local content may contain at most {MaxContentFiles} files.");
        if (contentBytes > MaxContentTotalBytes)
            throw new InvalidDataException("Local content exceeds the 5 MiB total limit.");
        return new ContentSummary(contentCount, contentBytes);
    }

    private static bool IsSupportedContent(string relative)
    {
        var extension = Path.GetExtension(relative);
        if (relative.StartsWith("docs/", StringComparison.Ordinal))
            return extension is ".md" or ".txt";
        if (relative.StartsWith("data/", StringComparison.Ordinal))
            return extension is ".json" or ".csv";
        return false;
    }

    private static void ValidateLocalSourceReferences(
        string text, string relative, IReadOnlyDictionary<string, string> files)
    {
        // Validate source labels in configuration text; editable C# is reviewed separately.
        foreach (Match match in Regex.Matches(text,
                     @"\b(?:docs|data)/[^\s""'`<>;:\\]+",
                     RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            // Match the whole token before checking its extension: policy.md-v2.md
            // is one filename, not a reference to policy.md. Strip prose/link suffixes.
            var source = match.Value.Split('#', '?')[0].TrimEnd('.', ',', ')', ']', '}');
            if (Path.GetExtension(source).ToLowerInvariant() is not (".md" or ".txt" or ".json" or ".csv"))
                continue;
            if (!files.ContainsKey(source))
                throw new InvalidDataException($"Unknown local source '{source}' in {relative}. Use the actual bundled source label.");
        }
    }

    private static void ValidateJsonSourceReferences(
        JsonElement value, IReadOnlyDictionary<string, string> files)
    {
        if (value.ValueKind == JsonValueKind.String)
            ValidateLocalSourceReferences(value.GetString()!, "appsettings.json", files);
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) ValidateJsonSourceReferences(property.Value, files);
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateJsonSourceReferences(item, files);
    }

    private static void ValidateSettings(
        string asset, string target, IReadOnlyDictionary<string, string> files, string? smokeBaseline)
    {
        var targetPath = Path.Combine(target, "appsettings.json");
        if (new FileInfo(targetPath).Length > 65_536)
            throw new InvalidDataException("appsettings.json exceeds the 64 KiB limit.");

        using var targetDocument = JsonDocument.Parse(File.ReadAllText(targetPath));
        using var assetDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(asset, "appsettings.json")));
        var root = targetDocument.RootElement;
        ValidateJsonSourceReferences(root, files);
        RequireObjectProperties(root, "root", "Urls", "AzureOpenAI", "Content", "Capabilities", "Agent", "Smoke");

        var expectedUrls = RequireString(assetDocument.RootElement, "Urls", 200);
        if (!string.Equals(RequireString(root, "Urls", 200), expectedUrls, StringComparison.Ordinal))
            throw new InvalidDataException("Urls is fixed by the starter.");

        var azure = RequireObject(root, "AzureOpenAI");
        RequireObjectProperties(azure, "AzureOpenAI", "Deployment");
        var expectedDeployment = RequireString(
            RequireObject(assetDocument.RootElement, "AzureOpenAI"), "Deployment", 100);
        if (!string.Equals(RequireString(azure, "Deployment", 100), expectedDeployment, StringComparison.Ordinal))
            throw new InvalidDataException("AzureOpenAI:Deployment is fixed in this MVP.");

        var hasContent = ValidateContentSources(root, files) > 0;
        var capabilities = ValidateCapabilities(root);

        // App startup checks the Agent values (AgentDefinition.Load, AgentPresentation.Load); this only rejects
        // missing or invented settings.
        var agent = RequireObject(root, "Agent");
        RequireObjectProperties(
            agent,
            "Agent",
            "DisplayName",
            "ServiceSlug",
            "Purpose",
            "Ui",
            "Instructions",
            "Examples");
        RequireObjectProperties(RequireObject(agent, "Ui"), "Agent:Ui", "Preset", "InputPlaceholder");
        var instructions = agent.GetProperty("Instructions") is { ValueKind: JsonValueKind.String } value
            ? value.GetString()!
            : "";

        // SmokeRunner repeats the format checks, but the Smoke section must be valid before the baseline is saved.
        var smoke = RequireObject(root, "Smoke");
        RequireObjectProperties(smoke, "Smoke", "Cases");
        var cases = RequireArray(smoke, "Cases").EnumerateArray().ToArray();
        var expectedIds = new[] { "knowledge", "tool", "not-found" };
        if (cases.Length != expectedIds.Length)
            throw new InvalidDataException("Smoke:Cases must contain exactly three cases.");
        for (var index = 0; index < cases.Length; index++)
        {
            var smokeCase = cases[index];
            if (smokeCase.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Smoke:Cases:{index} must be an object.");
            RequireObjectProperties(smokeCase, $"Smoke:Cases:{index}", "Id", "Prompt", "ExpectedMarkers");
            if (!string.Equals(RequireString(smokeCase, "Id", 32), expectedIds[index], StringComparison.Ordinal))
                throw new InvalidDataException("Smoke case IDs must be knowledge, tool, and not-found in that order.");
            var prompt = RequireString(smokeCase, "Prompt", 4_000);
            var markers = ValidateStringArray(
                RequireArray(smokeCase, "ExpectedMarkers"),
                $"Smoke:Cases:{index}:ExpectedMarkers",
                1,
                4,
                120);
            ValidateSmokeMarkers(expectedIds[index], markers, prompt, instructions, hasContent);
        }
        if (smokeBaseline is not null) ValidateSmokeBaseline(target, smoke, capabilities, smokeBaseline);
    }

    private static JsonElement ValidateCapabilities(JsonElement root)
    {
        // Startup repeats these checks (Capabilities.Load), but the approved hosts are frozen with the smoke
        // baseline, so a malformed declaration must be caught before that snapshot is saved.
        var capabilities = RequireObject(root, "Capabilities");
        RequireObjectProperties(capabilities, "Capabilities", "Network");
        var network = RequireObject(capabilities, "Network");
        RequireObjectProperties(network, "Capabilities:Network", "AllowedHosts");
        var hosts = ValidateStringArray(RequireArray(network, "AllowedHosts"), "Capabilities:Network:AllowedHosts", 0, 5, 253);
        if (hosts.Any(host => Uri.CheckHostName(host) is not (UriHostNameType.Dns or UriHostNameType.IPv4)))
            throw new InvalidDataException("Capabilities:Network:AllowedHosts entries must be host names without scheme, port or path.");
        if (hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hosts.Length)
            throw new InvalidDataException("Capabilities:Network:AllowedHosts must not repeat a host.");
        return capabilities;
    }

    private static int ValidateContentSources(
        JsonElement root,
        IReadOnlyDictionary<string, string> files)
    {
        var content = RequireObject(root, "Content");
        RequireObjectProperties(content, "Content", "Sources");
        var configured = RequireObject(content, "Sources");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in configured.EnumerateObject())
        {
            if (!declared.Add(property.Name) || !IsSupportedContent(property.Name) ||
                !files.ContainsKey(property.Name))
            {
                throw new InvalidDataException(
                    $"Content:Sources contains an unknown, duplicate, or unsupported path: {property.Name}");
            }
            if (property.Value.ValueKind != JsonValueKind.String ||
                property.Value.GetString() is not ("mock" or "supplied"))
            {
                throw new InvalidDataException(
                    $"Content source '{property.Name}' must have provenance 'mock' or 'supplied'.");
            }
        }

        var actual = files.Keys.Where(IsSupportedContent).ToHashSet(StringComparer.Ordinal);
        if (declared.Count > MaxContentFiles || !declared.SetEquals(actual))
            throw new InvalidDataException("Content:Sources must declare every local content file exactly once.");
        return declared.Count;
    }

    private static void ValidateSmokeBaseline(
        string target, JsonElement smoke, JsonElement capabilities, string baselineArgument)
    {
        var baseline = Path.GetFullPath(baselineArgument);
        if (IsWithin(target, baseline))
            throw new InvalidDataException("Smoke baseline must be outside the generated project.");
        RejectSymlinkPathComponents(baseline, "Smoke baseline");
        if (!File.Exists(baseline) || new FileInfo(baseline).Length > 65_536)
            throw new InvalidDataException("Smoke baseline must be an existing appsettings snapshot of at most 64 KiB.");
        using var document = JsonDocument.Parse(File.ReadAllText(baseline));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Smoke baseline must be an appsettings JSON object.");
        // The snapshot freezes both what the smokes expect and which hosts the user approved.
        if (!JsonElement.DeepEquals(smoke, RequireObject(document.RootElement, "Smoke")))
            throw new InvalidDataException(
                "Smoke expectations changed after the baseline was captured. Restore the original Smoke section; do not replace the baseline to make tests pass.");
        if (!JsonElement.DeepEquals(capabilities, RequireObject(document.RootElement, "Capabilities")))
            throw new InvalidDataException(
                "Approved capabilities changed after the baseline was captured. Restore the original Capabilities section; a new host needs the user's approval and a new build.");
    }

    private static void ValidateSmokeMarkers(
        string caseId, IReadOnlyList<string> markers, string prompt, string instructions, bool hasContent)
    {
        if (markers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != markers.Count)
            throw new InvalidDataException($"Smoke case '{caseId}' has duplicate expected markers.");
        if (markers.Any(marker => marker.Length < 2 || !marker.Any(char.IsLetterOrDigit)))
            throw new InvalidDataException($"Smoke case '{caseId}' needs meaningful answer fragments of at least two characters each.");
        // A marker copied from the prompt or given in the instructions shows nothing the agent looked up. The not-found
        // sentence may appear in instructions: the fixed response policy already tells the model to say it.
        var checkInstructions = caseId != "not-found";
        if (!markers.Any(marker => marker.Length >= 4 &&
                                   !prompt.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
                                   !(checkInstructions && instructions.Contains(marker, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException(
                $"Smoke case '{caseId}' needs at least one fact or source path marker (four or more characters) that is not in its prompt" +
                (checkInstructions ? " or in Agent:Instructions." : "."));
        }
        if (markers.Any(marker => Regex.IsMatch(marker, @"^(status|mode|source)=", RegexOptions.IgnoreCase)))
            throw new InvalidDataException($"Smoke case '{caseId}' must check plain-text facts or source paths, not internal diagnostic tokens.");
        // Without declared content, the knowledge case checks a fact from a tool result instead of a source path.
        if (caseId == "knowledge" && hasContent && !markers.Any(marker =>
                marker.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
                marker.StartsWith("data/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Smoke case 'knowledge' must include a docs/ or data/ source path marker when local content is declared.");
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{name} must be an object.");
        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{name} must be an array.");
        return value;
    }

    private static string RequireString(JsonElement parent, string name, int maxLength)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{name} must be a string.");
        var text = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength)
            throw new InvalidDataException($"{name} must contain 1 to {maxLength} characters.");
        return text;
    }

    private static void RequireObjectProperties(JsonElement value, string label, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{label} must be an object.");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || actual.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            !actual.ToHashSet(StringComparer.Ordinal).SetEquals(names))
        {
            throw new InvalidDataException($"{label} contains missing, duplicate, or unsupported settings.");
        }
    }

    private static string[] ValidateStringArray(
        JsonElement array,
        string label,
        int minimum,
        int maximum,
        int maxLength)
    {
        var values = array.EnumerateArray().ToArray();
        if (values.Length < minimum || values.Length > maximum)
            throw new InvalidDataException($"{label} must contain {minimum} to {maximum} values.");
        foreach (var value in values)
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) ||
                value.GetString()!.Trim().Length > maxLength)
            {
                throw new InvalidDataException($"{label} values must contain 1 to {maxLength} characters.");
            }
        }
        return values.Select(value => value.GetString()!.Trim()).ToArray();
    }

    private static void RejectSymlinkPathComponents(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        var inspectionBase = InspectionBase(fullPath);
        var current = inspectionBase;
        foreach (var segment in Path.GetRelativePath(inspectionBase, fullPath).Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
                throw new ArgumentException($"{label} path must not contain symlinks: {path}");
            if (!File.Exists(current) && !Directory.Exists(current)) break;
        }
    }

    private static string InspectionBase(string fullPath)
    {
        var candidates = new List<string>
        {
            Path.GetFullPath(Directory.GetCurrentDirectory()),
            Path.GetFullPath(Path.GetTempPath()),
        };
        if (!OperatingSystem.IsWindows() && Directory.Exists("/tmp"))
            candidates.Add("/tmp");

        return candidates
                   .Distinct(StringComparer.Ordinal)
                   .Where(candidate => IsWithin(candidate, fullPath))
                   .OrderByDescending(candidate => candidate.Length)
                   .FirstOrDefault()
               ?? Path.GetPathRoot(fullPath)
               ?? throw new ArgumentException($"Path has no filesystem root: {fullPath}");
    }

    private static bool IsWithin(string directory, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.Equals(directory, comparison) || path.StartsWith(prefix, comparison);
    }

    private static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (FileNotFoundException)
        {
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string CurrentFile([CallerFilePath] string path = "") => path;

    private sealed record ContentSummary(int Count, long TotalBytes);
}

using Microsoft.Extensions.AI;

namespace CustomAgent;

/// <summary>
/// The agent's identity, purpose, instructions and example prompts, loaded from the Agent section of
/// appsettings.json, and the tools Program.cs registers with the agent (one to three AIFunction tools). CreateTools
/// receives the local content and the HTTP client that reaches only the approved hosts.
/// </summary>
public sealed record AgentDefinition(
    string DisplayName,
    string ServiceSlug,
    string Purpose,
    string Instructions,
    IReadOnlyList<string> ExamplePrompts)
{
    public static AgentDefinition Load(IConfiguration configuration)
    {
        var displayName = Require(configuration, "Agent:DisplayName", 80);
        var serviceSlug = Require(configuration, "Agent:ServiceSlug", 64);
        if (!IsServiceSlug(serviceSlug))
        {
            throw new InvalidOperationException(
                "Agent:ServiceSlug must contain only lowercase letters, digits, and single hyphens.");
        }

        var purpose = Require(configuration, "Agent:Purpose", 500);
        var instructions = Require(configuration, "Agent:Instructions", 4_000);
        var examples = configuration.GetSection("Agent:Examples")
            .GetChildren()
            .Select(item => item.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        if (examples is not { Length: >= 1 and <= 4 } || examples.Any(example => example.Length > 500))
            throw new InvalidOperationException("Agent:Examples must contain one to four prompts of at most 500 characters.");

        return new AgentDefinition(displayName, serviceSlug, purpose, instructions, examples);
    }

    public IList<AITool> CreateTools(KnowledgeBase knowledgeBase, ApprovedHttpClient http)
    {
        // The starter tools read local content only. A tool that calls an approved host takes http as well; it
        // rejects every other host (Capabilities:Network:AllowedHosts in appsettings.json).
        var tools = new AssistantTools(knowledgeBase);
        return
        [
            AIFunctionFactory.Create(tools.SearchLocalContent),
            AIFunctionFactory.Create(tools.ReadLocalSource),
            AIFunctionFactory.Create(tools.LookupLocalRecord),
        ];
    }

    private static string Require(IConfiguration configuration, string key, int maxLength)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidOperationException($"{key} must contain 1 to {maxLength} characters.");
        return value;
    }

    private static bool IsServiceSlug(string value)
    {
        if (value.Length == 0 || value[0] == '-' || value[^1] == '-' || value.Contains("--", StringComparison.Ordinal))
            return false;
        return value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }
}

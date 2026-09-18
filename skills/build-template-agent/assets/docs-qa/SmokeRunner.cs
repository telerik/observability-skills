using System.Text.Json;

namespace DocsQa;

/// <summary>
/// Runs the three --smoke cases through the same AgentRuntime as the web app, with prompts from
/// the Smoke section of appsettings.json. Prints one SMOKE_REPORT line; exit code 0 means all passed.
/// </summary>
public sealed class SmokeRunner(AgentRuntime runtime, IConfiguration configuration)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var cases = new[]
        {
            new SmokeCase("grounded-answer", "KnowledgePrompt", reply => Grounded(reply) &&
                reply.Citations.Any(source => source.SourceId == "retention#audit-history") && reply.Answer.Contains("90", StringComparison.Ordinal)),
            new SmokeCase("two-sources", "ToolPrompt", reply => Grounded(reply) &&
                reply.Citations.Any(source => source.DocumentId == "retention") &&
                reply.Citations.Any(source => source.DocumentId == "exports") &&
                reply.Answer.Contains("90", StringComparison.Ordinal) && reply.Answer.Contains("24", StringComparison.Ordinal)),
            new SmokeCase("unknown-not-found", "NotFoundPrompt", reply => reply.Status == "not_found" &&
                reply.Citations.Count == 0 && reply.ToolCalls.Contains("SearchDocuments")),
        };
        var results = new List<object>();
        var passed = true;
        foreach (var item in cases)
        {
            var prompt = DocumentStore.ValidateQuestion(configuration["Smoke:" + item.PromptKey]);
            try
            {
                var reply = await runtime.RunAsync(prompt, cancellationToken);
                var ok = item.Check(reply);
                passed &= ok;
                results.Add(new
                {
                    caseId = item.Id,
                    status = ok ? "pass" : "fail",
                    reason = ok ? "expected_behavior_observed" : "grounding_or_result_mismatch"
                });
            }
            catch (AgentRunException ex)
            {
                var reason = ex.InnerException is System.ClientModel.ClientResultException { Status: > 0 } azure
                    ? $"azure_openai_http_{azure.Status}"
                    : ex.Code;
                Console.Error.WriteLine($"SMOKE_FAILURE={reason}");
                passed = false;
                results.Add(new { caseId = item.Id, status = "fail", reason });
            }
        }
        Console.WriteLine("SMOKE_REPORT=" + JsonSerializer.Serialize(new { status = passed ? "pass" : "fail", cases = results }));
        return passed ? 0 : 1;
    }

    private static bool Grounded(AgentReply reply) => reply.Status == "answered" && reply.Citations.Count > 0 &&
        reply.ToolCalls.Contains("SearchDocuments") && reply.ToolCalls.Contains("ReadSection") &&
        reply.Citations.All(source => source.SourceId.Contains('#') && source.Excerpt.Length > 0);
    private sealed record SmokeCase(string Id, string PromptKey, Func<AgentReply, bool> Check);
}

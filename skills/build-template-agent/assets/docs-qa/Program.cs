using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace DocsQa;

/// <summary>
/// Startup: load settings -> create the model client -> enable tracing -> run HTTP endpoints or smoke cases.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var smoke = args.Contains("--smoke", StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = "wwwroot",
        });
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1_024);
        // Local user-secrets supply shared app settings; environment variables can override them.
        builder.Configuration.AddUserSecrets<AgentMarker>(optional: true).AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        if (!Uri.TryCreate(builder.Configuration["AzureOpenAI:Endpoint"], UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https")
            throw new InvalidOperationException("AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        var deployment = builder.Configuration["AzureOpenAI:Deployment"];
        if (string.IsNullOrWhiteSpace(deployment)) throw new InvalidOperationException("AzureOpenAI:Deployment is required.");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var tracingKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(tracingKey);
        var telemetryRecordInputs = builder.Configuration.GetValue("Progress:Observability:RecordInputs", true);
        var telemetryRecordOutputs = builder.Configuration.GetValue("Progress:Observability:RecordOutputs", true);
        // One combined switch: if either flag is false, no prompts, answers or tool payloads are recorded.
        var telemetryRecordContent = telemetryRecordInputs && telemetryRecordOutputs;
        var appName = builder.Configuration["Progress:Observability:AppName"] ?? "docs-qa";
        if (smoke && !tracingEnabled)
        {
            Console.Error.WriteLine("Smoke requires Progress:Observability:ApiKey (Integration key). No secret value was printed.");
            return 2;
        }
        if (!tracingEnabled) Console.Error.WriteLine("Progress tracing is disabled: Integration key is not configured.");
        try
        {
            var store = DocumentStore.Load(Path.Combine(AppContext.BaseDirectory, "docs"));
            var azureOptions = new AzureOpenAIClientOptions();
            // Smoke tests should surface the first Azure error before retries consume the deadline.
            if (smoke)
                azureOptions.RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0);
            var azure = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential(), azureOptions)
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureKey), azureOptions);
            // IChatClient is the model interface MAF uses; here it wraps the Azure OpenAI deployment.
            IChatClient chatClient = azure.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled)
            {
                // Configure export to Progress; template tags help find these traces in the Observability UI.
                ObservabilityTracer.Initialize(new ObservabilityOptions
                {
                    AppName = appName,
                    ApiKey = tracingKey!,
                    AdditionalTags = ["agent.template.id:docs-qa"],
                });
                // Trace the agent run, model calls and tool calls; AgentRuntime adds tool arguments and results.
                chatClient = chatClient.AddObservability(options =>
                {
                    options.AppName = appName;
                    options.RecordInputs = telemetryRecordContent;
                    options.RecordOutputs = telemetryRecordContent;
                });
            }
            using var chatClientLifetime = chatClient;
            // Web requests and smoke checks exercise the same agent runtime.
            var runtime = new AgentRuntime(chatClient, store, appName,
                recordToolContent: tracingEnabled && telemetryRecordContent);
            if (smoke) return await new SmokeRunner(runtime, builder.Configuration).RunAsync();
            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = store.Sections.Count > 0 ? "ready" : "degraded",
                documentsLoaded = store.DocumentCount,
                sectionsLoaded = store.Sections.Count,
                tracingEnabled,
                telemetryRecordContent,
            }));
            app.MapGet("/api/documents", () => Results.Ok(store.Sections));
            // The page posts a question here; return the completed answer and its sources as JSON.
            app.MapPost("/api/ask", async (AskRequest? request, CancellationToken token) =>
            {
                AskRequest validated;
                try { validated = (request ?? new AskRequest(null)).Validate(store); }
                catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
                try { return Results.Ok(await runtime.RunAsync(validated, token)); }
                catch (AgentRunException ex)
                {
                    return Results.Json(new { error = ex.Code }, statusCode: ex.Code == "agent_timeout" ? 504 : 502);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return Results.Json(new { error = "request_cancelled" }, statusCode: 499);
                }
            });
            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        // Flush queued spans before exit, including short-lived smoke runs.
        finally { if (tracingEnabled) ObservabilityTracer.Shutdown(); }
    }
}

internal sealed class AgentMarker;

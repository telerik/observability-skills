using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace ReleaseEvidenceReviewer;

/// <summary>
/// Startup: load settings -> create the model client -> enable tracing -> run HTTP endpoints or smoke cases.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var smokeMode = args.Contains("--smoke", StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = "wwwroot",
        });

        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1_024);
        // Local user-secrets supply shared app settings; environment variables can override them.
        builder.Configuration
            .AddUserSecrets<AgentMarker>(optional: true)
            .AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var azureEndpoint) ||
            azureEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        }

        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var appName = builder.Configuration["Progress:Observability:AppName"]
                      ?? "release-evidence-reviewer";
        var observabilityKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(observabilityKey);
        var telemetryRecordInputs = builder.Configuration.GetValue("Progress:Observability:RecordInputs", true);
        var telemetryRecordOutputs = builder.Configuration.GetValue("Progress:Observability:RecordOutputs", true);
        // One combined switch: if either flag is false, no prompts, answers or tool payloads are recorded.
        var telemetryRecordContent = telemetryRecordInputs && telemetryRecordOutputs;

        if (smokeMode && !tracingEnabled)
        {
            Console.Error.WriteLine(
                "Smoke tests require Progress:Observability:ApiKey (the Integration key). No secret value was read or printed.");
            return 2;
        }

        if (!tracingEnabled)
        {
            Console.Error.WriteLine(
                "Progress Observability tracing is disabled because Progress:Observability:ApiKey is not configured.");
        }

        try
        {
            var knowledgeBase = KnowledgeBase.Load("docs");
            var azureOptions = new AzureOpenAIClientOptions();
            // Smoke tests should surface the first Azure error before retries consume the deadline.
            if (smokeMode)
                azureOptions.RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0);
            var azureClient = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(azureEndpoint, new DefaultAzureCredential(), azureOptions)
                : new AzureOpenAIClient(azureEndpoint, new AzureKeyCredential(azureKey), azureOptions);

            // IChatClient is the model interface MAF uses; here it wraps the Azure OpenAI deployment.
            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled)
            {
                // Configure export to Progress; template tags help find these traces in the Observability UI.
                ObservabilityTracer.Initialize(new ObservabilityOptions
                {
                    AppName = appName,
                    ApiKey = observabilityKey!,
                    AdditionalTags = ["agent.template.id:release-evidence-reviewer"],
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
            var runtime = new AgentRuntime(chatClient, knowledgeBase, appName,
                recordToolContent: tracingEnabled && telemetryRecordContent);

            if (smokeMode)
                return await new SmokeRunner(runtime, builder.Configuration).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = knowledgeBase.DocumentCount > 0 ? "ready" : "degraded",
                documentsLoaded = knowledgeBase.DocumentCount,
                tracingEnabled,
                telemetryRecordContent,
            }));

            // The page posts a message with optional review context; return the answer, project, tools and verdict.
            app.MapPost("/api/chat", async (
                ChatRequest? request,
                CancellationToken cancellationToken) =>
            {
                string message;
                try { message = AgentRuntime.Validate(request?.Message, request?.Context); }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }

                try
                {
                    return Results.Ok(await runtime.RunAsync(message, cancellationToken, request?.Context));
                }
                catch (AgentRunException ex)
                {
                    return Results.Json(
                        new { error = ex.Code },
                        statusCode: ex.Code == "agent_deadline_exceeded" ? 504 : 502);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Results.Json(
                        new { error = "request_cancelled" },
                        statusCode: 499);
                }
            });

            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        finally
        {
            // Flush queued spans before exit, including short-lived smoke runs.
            if (tracingEnabled) ObservabilityTracer.Shutdown();
        }
    }

    private static string Require(IConfiguration configuration, string key)
        => configuration[key]
           ?? throw new InvalidOperationException(
               $"Missing configuration '{key}'. Set it with dotnet user-secrets or an environment variable.");
}

public sealed record ChatRequest(string? Message, ReviewContext? Context = null);

internal sealed class AgentMarker;

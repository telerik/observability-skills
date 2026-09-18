using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace OperationsDataAnalyst;

/// <summary>
/// Startup: load settings and the CSV -> create the model client -> enable tracing -> run HTTP endpoints or smoke cases.
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
        builder.Configuration.AddUserSecrets<AgentMarker>(optional: true).AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var observabilityKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(observabilityKey);
        var telemetryRecordInputs = builder.Configuration.GetValue("Progress:Observability:RecordInputs", true);
        var telemetryRecordOutputs = builder.Configuration.GetValue("Progress:Observability:RecordOutputs", true);
        // One combined switch: if either flag is false, no prompts, answers or tool payloads are recorded.
        var telemetryRecordContent = telemetryRecordInputs && telemetryRecordOutputs;
        var appName = builder.Configuration["Progress:Observability:AppName"] ?? "operations-data-analyst";
        if (smokeMode && !tracingEnabled)
        {
            Console.Error.WriteLine("Smoke tests require Progress:Observability:ApiKey (the Integration key). No secret values are printed.");
            return 2;
        }
        if (!tracingEnabled) Console.Error.WriteLine("Progress Observability tracing is disabled: Progress:Observability:ApiKey is not configured.");

        try
        {
            // The CSV shipped with the template, copied beside the binary on every build.
            var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "operations.csv");
            var metrics = MetricsStore.Load(dataPath);
            Console.WriteLine($"Analysing {metrics.RowCount} rows from {metrics.SourcePath} " +
                $"(sha256:{metrics.Fingerprint}, {metrics.Start:yyyy-MM-dd} to {metrics.End:yyyy-MM-dd}, " +
                $"services: {string.Join(", ", metrics.Services)}).");
            var azureOptions = new AzureOpenAIClientOptions();
            // Smoke tests should surface the first Azure error before retries consume the deadline.
            if (smokeMode)
                azureOptions.RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0);
            var azure = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential(), azureOptions)
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureKey), azureOptions);
            // IChatClient is the model interface MAF uses; here it wraps the Azure OpenAI deployment.
            IChatClient client = azure.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled)
            {
                // Configure export to Progress; template tags help find these traces in the Observability UI.
                ObservabilityTracer.Initialize(new ObservabilityOptions
                {
                    AppName = appName,
                    ApiKey = observabilityKey!,
                    AdditionalTags = ["agent.template.id:operations-data-analyst"],
                });
                // Trace the agent run, model calls and tool calls; AgentRuntime adds tool arguments and results.
                client = client.AddObservability(options =>
                {
                    options.AppName = appName;
                    options.RecordInputs = telemetryRecordContent;
                    options.RecordOutputs = telemetryRecordContent;
                });
            }
            using var chatClientLifetime = client;
            var runtime = new AgentRuntime(client, metrics, appName,
                recordToolContent: tracingEnabled && telemetryRecordContent);
            // Web requests and smoke checks ask through the same workflow and agent runtime.
            var workflow = new ViewWorkflow(runtime, metrics);
            if (smokeMode) return await new SmokeRunner(workflow).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = "ready",
                templateId = "operations-data-analyst",
                rowsLoaded = metrics.RowCount,
                dataSource = metrics.SourcePath,
                dataFingerprint = metrics.Fingerprint,
                datasetStart = metrics.Start,
                datasetEnd = metrics.End,
                services = metrics.Services,
                syntheticData = true,
                tracingEnabled,
                telemetryRecordContent,
            }));
            // Returns the complete agent result as one JSON response.
            app.MapPost("/api/analyze", async (AnalysisRequest? request, CancellationToken cancellationToken) =>
            {
                if (request is null) return Results.BadRequest(new { error = "question_required" });
                try
                {
                    return Results.Ok(await workflow.AskAsync(request, cancellationToken));
                }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
                catch (AgentRunException)
                {
                    return Results.Json(new { error = "agent_run_failed" }, statusCode: 502);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Results.Json(new { error = "request_cancelled" }, statusCode: 499);
                }
            });
            // The page's main route: newline-delimited JSON events with the chart (view), answer text, then done or error.
            app.MapPost("/api/analyze/stream", async (AnalysisRequest? request, HttpContext context) =>
            {
                context.Response.ContentType = "application/x-ndjson";
                context.Response.Headers.CacheControl = "no-store";
                var cancellationToken = context.RequestAborted;
                var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
                async Task Send(object message)
                {
                    await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(message, json) + "\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
                try
                {
                    if (request is null) throw new ArgumentException("question_required");
                    var reply = await workflow.AskAsync(request, cancellationToken,
                        onView: data => Send(new { type = "view", data }),
                        onText: text => Send(new { type = "text", text }));
                    await Send(new { type = "done", data = reply });
                }
                catch (ArgumentException error) { await Send(new { type = "error", error = error.Message }); }
                catch (AgentRunException)
                {
                    await Send(new { type = "error", error = "agent_run_failed" });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            });
            // Chart, tiles and rows for an exact selection, without a model call.
            app.MapPost("/api/view", (ViewSpec? view) =>
            {
                try { return Results.Ok(workflow.Rules.Data(view ?? workflow.Rules.Default)); }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            });
            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        // Flush queued spans before exit, including short-lived smoke runs.
        finally { if (tracingEnabled) ObservabilityTracer.Shutdown(); }
    }

    private static string Require(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Missing configuration '{key}'. Set it with dotnet user-secrets or an environment variable.")
            : configuration[key]!;
}

internal sealed class AgentMarker;

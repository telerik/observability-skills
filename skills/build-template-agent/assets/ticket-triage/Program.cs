using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace TicketTriage;

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
        builder.Configuration.AddUserSecrets<AgentMarker>(optional: true).AddEnvironmentVariables();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        ConfigureApi(builder.Services);

        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
        var appName = builder.Configuration["Progress:Observability:AppName"] ?? "ticket-triage";
        var observabilityKey = builder.Configuration["Progress:Observability:ApiKey"];
        var tracingEnabled = !string.IsNullOrWhiteSpace(observabilityKey);
        var telemetryRecordInputs = builder.Configuration.GetValue("Progress:Observability:RecordInputs", true);
        var telemetryRecordOutputs = builder.Configuration.GetValue("Progress:Observability:RecordOutputs", true);
        // One combined switch: if either flag is false, no prompts, answers or tool payloads are recorded.
        var telemetryRecordContent = telemetryRecordInputs && telemetryRecordOutputs;
        if (smokeMode && !tracingEnabled)
        {
            Console.Error.WriteLine("Smoke tests require Progress:Observability:ApiKey (the Integration key). No secret value was printed.");
            return 2;
        }
        if (!tracingEnabled)
            Console.Error.WriteLine("Progress Observability tracing is disabled because its Integration key is not configured.");

        try
        {
            var store = TicketStore.Load(AppContext.BaseDirectory);
            var azureOptions = new AzureOpenAIClientOptions();
            // Smoke tests should surface the first Azure error before retries consume the deadline.
            if (smokeMode)
                azureOptions.RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0);
            var azureClient = string.IsNullOrWhiteSpace(azureKey)
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential(), azureOptions)
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(azureKey), azureOptions);
            // IChatClient is the model interface MAF uses; here it wraps the Azure OpenAI deployment.
            IChatClient chatClient = azureClient.GetChatClient(deployment).AsIChatClient();
            if (tracingEnabled)
            {
                // Configure export to Progress; template tags help find these traces in the Observability UI.
                ObservabilityTracer.Initialize(new ObservabilityOptions
                {
                    AppName = appName,
                    ApiKey = observabilityKey!,
                    AdditionalTags = ["agent.template.id:ticket-triage"],
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
            if (smokeMode) return await new SmokeRunner(runtime, builder.Configuration).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            MapEndpoints(app, store, runtime, tracingEnabled, telemetryRecordContent);
            app.MapFallbackToFile("index.html");
            await app.RunAsync();
            return 0;
        }
        // Flush queued spans before exit, including short-lived smoke runs.
        finally { if (tracingEnabled) ObservabilityTracer.Shutdown(); }
    }

    public static void ConfigureApi(IServiceCollection services)
        => services.ConfigureHttpJsonOptions(options =>
        {
            // Duplicate or unknown JSON fields fail request binding, before any model call.
            options.SerializerOptions.AllowDuplicateProperties = false;
            options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
        });

    public static void MapEndpoints(
        WebApplication app,
        TicketStore store,
        AgentRuntime runtime,
        bool tracingEnabled,
        bool telemetryRecordContent = true)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = store.Tickets.Count > 0 ? "ready" : "degraded",
            ticketsLoaded = store.Tickets.Count,
            documentsLoaded = 1,
            mockData = true,
            tracingEnabled,
            telemetryRecordContent,
        }));
        app.MapGet("/api/tickets", () => Results.Ok(new { tickets = store.Tickets, mockData = true }));
        // The page posts a ticket ID with an optional question or scenario; return the answer and typed recommendation.
        app.MapPost("/api/triage", async (TriageRequest? request, CancellationToken cancellationToken) =>
        {
            if (request is null) return Results.BadRequest(new { error = "valid_ticket_id_required" });
            try { return Results.Ok(await runtime.RunAsync(request, cancellationToken)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (AgentRunException ex)
            {
                return Results.Json(new { error = ex.Code },
                    statusCode: ex.Code == "agent_deadline_exceeded" ? 504 : 502);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { return Results.Json(new { error = "request_cancelled" }, statusCode: 499); }
        });
    }

    private static string Require(IConfiguration configuration, string key)
        => string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Missing configuration '{key}'. Use dotnet user-secrets or environment variables.")
            : configuration[key]!;
}

internal sealed class AgentMarker;

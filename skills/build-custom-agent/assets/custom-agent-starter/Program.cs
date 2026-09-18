using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace CustomAgent;

/// <summary>
/// Startup: load the agent definition, approved capabilities, content and settings -> create the model client ->
/// enable tracing -> build the agent -> run HTTP endpoints or smoke cases.
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

        var definition = AgentDefinition.Load(builder.Configuration);
        var presentation = AgentPresentation.Load(builder.Configuration);
        var capabilities = Capabilities.Load(builder.Configuration);
        var knowledgeBase = KnowledgeBase.Load(
            AppContext.BaseDirectory,
            builder.Configuration.GetSection("Content:Sources"));
        // The agent's tools come from AgentDefinition.CreateTools; startup accepts one to three AIFunction tools.
        // The HTTP client they receive reaches only the approved hosts (Capabilities.cs).
        using var http = new ApprovedHttpClient(capabilities.Network.AllowedHosts);
        var tools = definition.CreateTools(knowledgeBase, http);
        if (tools is null || tools.Count is < 1 or > 3 || tools.Any(tool => tool is not AIFunction))
            throw new InvalidOperationException("Custom agent must register one to three AIFunction tools.");

        var endpointValue = Require(builder.Configuration, "AzureOpenAI:Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var azureEndpoint) ||
            azureEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint must be an absolute HTTPS URL.");
        }

        var deployment = Require(builder.Configuration, "AzureOpenAI:Deployment");
        var azureKey = builder.Configuration["AzureOpenAI:ApiKey"];
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
                    AppName = definition.ServiceSlug,
                    ApiKey = observabilityKey!,
                    AdditionalTags = ["agent.template.id:custom-agent-local-prototype", $"agent.service.slug:{definition.ServiceSlug}"],
                });
                // Trace the agent run, model calls and tool calls; tool arguments and results are added below.
                chatClient = chatClient.AddObservability(options =>
                {
                    options.AppName = definition.ServiceSlug;
                    options.RecordInputs = telemetryRecordContent;
                    options.RecordOutputs = telemetryRecordContent;
                });
            }
            using var chatClientLifetime = chatClient;
            // Run tools requested by the model, then send their results back for the next model response.
            var boundedClient = new FunctionInvokingChatClient(chatClient)
            {
                // This client allows a final answer-only request: three iterations can mean four model calls.
                MaximumIterationsPerRequest = 3,
                MaximumConsecutiveErrorsPerRequest = 0,
                AllowConcurrentInvocation = false,
                IncludeDetailedErrors = false,
            };
            // One agent serves every request; each run starts its own session in AgentRuntime.
            AIAgent agent = boundedClient.AsAIAgent(new ChatClientAgentOptions
            {
                Name = definition.ServiceSlug,
                // Reuse our bounded tool loop; MAF should not add another one.
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions
                {
                    // The configured instructions followed by the fixed response policy.
                    Instructions = definition.Instructions + "\n\n" + AgentRuntime.ResponsePolicy,
                    // Add argument/result spans when tracing and content capture are on. SDK 1.4.0 records each tool
                    // twice but executes it once; an SDK fix is expected to remove the duplicate recording.
                    Tools = tracingEnabled && telemetryRecordContent ? tools.AddToolObservability() : tools,
                    MaxOutputTokens = 800,
                    AllowMultipleToolCalls = false,
                },
            });
            // Web chat and smoke checks exercise the same agent runtime.
            var runtime = new AgentRuntime(agent);

            if (smokeMode)
                return await new SmokeRunner(runtime, builder.Configuration).RunAsync();

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/config", () => Results.Ok(new
            {
                displayName = definition.DisplayName,
                purpose = definition.Purpose,
                examples = definition.ExamplePrompts,
                uiPreset = presentation.Preset,
                inputPlaceholder = presentation.InputPlaceholder,
                prototype = true,
                chatHistory = new { maxTurns = ChatHistory.MaxTurns, maxCharacters = ChatHistory.MaxCharacters },
            }));

            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = "ready",
                sourcesLoaded = knowledgeBase.SourceCount,
                networkHosts = capabilities.Network.AllowedHosts,
                tracingEnabled,
                telemetryRecordContent,
                mode = "local_prototype",
            }));

            // The page posts a message with its bounded history here; return the answer as JSON.
            app.MapPost("/api/chat", async (
                ChatRequest? request,
                CancellationToken cancellationToken) =>
            {
                if (!ChatHistory.TryCreateMessages(request, out var messages, out var error))
                    return Results.BadRequest(new { error });

                try
                {
                    var response = await runtime.RunAsync(messages, cancellationToken);
                    return Results.Ok(new { answer = response.Answer });
                }
                catch (AgentRunException)
                {
                    return Results.Json(
                        new { error = "agent_run_failed" },
                        statusCode: StatusCodes.Status502BadGateway);
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

internal sealed class AgentMarker;

internal sealed record AgentPresentation(string Preset, string InputPlaceholder)
{
    private static readonly HashSet<string> AllowedPresets =
        ["knowledge", "review", "workflow", "analysis"];

    public static AgentPresentation Load(IConfiguration configuration)
    {
        var preset = Require(configuration, "Agent:Ui:Preset", 20);
        if (!AllowedPresets.Contains(preset))
        {
            throw new InvalidOperationException(
                "Agent:Ui:Preset must be knowledge, review, workflow, or analysis.");
        }

        return new AgentPresentation(
            preset,
            Require(configuration, "Agent:Ui:InputPlaceholder", 140));
    }

    private static string Require(IConfiguration configuration, string key, int maxLength)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidOperationException($"{key} must contain 1 to {maxLength} characters.");
        return value;
    }
}

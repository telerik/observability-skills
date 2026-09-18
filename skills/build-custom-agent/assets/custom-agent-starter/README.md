# Custom Agent local prototype

A bounded .NET 10 starter for an agent grounded in supplied local files or mock
data. It does not connect to or update a live business system, even if you
already have an adapter or connector configured. Its only outside access is
read-only requests to the hosts approved in `appsettings.json`
(see [Capabilities](#capabilities)).

For a simplified mock PoC, only the agreed local decision logic is demonstrated.
Mock inputs and assumed rules do not validate real outcomes or production
safety; `INTEGRATION_PLAN.md` describes the original goal and remaining work.

## Configure

**Development use only:** This agent is in a local development environment.
.NET user-secrets stores credentials unencrypted in a JSON file in your user
profile. For production deployment, inject credentials through environment
variables backed by your deployment’s secret manager.

Store the local builder settings once under the shared Secret Manager ID. These
commands can run from any folder, including before this project is copied:

```bash
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Deployment" "gpt-5.4-mini"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "Progress:Observability:ApiKey" "ac_p_..."
```

The bundled deployment default is `gpt-5.4-mini`; replace it in the command
with your actual Azure deployment name if different. User secrets override
`appsettings.json`; environment variables such as `AzureOpenAI__Deployment`
override both.

`AzureOpenAI:ApiKey` is optional when `DefaultAzureCredential` is configured.
The Progress value is the Integration key used by the app to write traces, not
an MCP key. Do not create or copy a `.env` file into the project.

### Cleanup after testing

To remove the stored API keys:

```bash
dotnet user-secrets remove --id Progress.AgentBuilder.Mvp "AzureOpenAI:ApiKey"
dotnet user-secrets remove --id Progress.AgentBuilder.Mvp "Progress:Observability:ApiKey"
```

The store is shared by all five agents. For a full reset,
`dotnet user-secrets clear --id Progress.AgentBuilder.Mvp` removes all settings;
agents relying on this store need reconfiguration.

If credentials were entered in terminal commands, remove those history entries
in the original shell. In Bash, use `history -d <entry-number>` then `history -w`;
other open sessions may retain entries.

## Project files

A chat message moves from the page to `POST /api/chat` in `Program.cs`, which
bounds the history with `ChatHistory.cs` and runs `AgentRuntime.cs`. The Microsoft
Agent Framework (MAF) agent, built once in `Program.cs`, calls the tools in
`Tools.cs` to search and read the local content loaded by `KnowledgeBase.cs`. A
tool that needs an approved host calls it through the client in `Capabilities.cs`.

| Path | What it does |
|---|---|
| `Program.cs` | Startup: settings, Azure OpenAI chat client, Progress tracing, the MAF agent and its tool loop, HTTP endpoints or `--smoke` |
| `AgentRuntime.cs` | Runs one chat turn or smoke case with a deadline and answer limit; the fixed response policy |
| `AgentDefinition.cs` | The agent's identity, instructions and examples from `appsettings.json`, and the tools it registers |
| `Tools.cs` | `SearchLocalContent`, `ReadLocalSource` and `LookupLocalRecord`, the tools the model calls |
| `KnowledgeBase.cs` | Loads the declared `docs/` and `data/` files and searches them by matching words |
| `Capabilities.cs` | The approved `Capabilities` from `appsettings.json` and `ApprovedHttpClient`, which reaches only the approved hosts |
| `ChatHistory.cs` | Validates and bounds the chat history the browser sends |
| `SmokeRunner.cs` | The three configured `--smoke` cases |
| `appsettings.json` | Agent definition, UI preset, content sources, approved capabilities and smoke cases |
| `docs/`, `data/` | The local prototype content |
| `wwwroot/` | The page: `index.html`, `styles.css` and `app.js` |

## Build, smoke test, and run

```bash
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

For the UI, .NET chooses an available loopback port and prints it in the
standard `Now listening on: http://127.0.0.1:<port>` line. Smoke mode runs
exactly the `knowledge`, `tool`, and `not-found` cases configured in
`appsettings.json` and prints one `SMOKE_REPORT=<json>` line. Each run is capped
at 45 seconds, three tool iterations, 800 output tokens, and 8,000 streamed
characters.

A smoke case passes when at least one registered tool returned a result and the
answer contains the expected fragments, which the model is never told. The
report lists the tools each case used. A pass does not prove the right tool was
chosen, reasoning quality, or production safety, and it is not proof of backend
ingestion: find the service, time and matching prompt on the
[Progress Tracing page](https://observability.progress.com/observations).

## Tracing

When `Progress:Observability:ApiKey` is configured, each chat turn appears in
Progress Observability as one trace with the agent run, its model calls and its
tool calls. `Program.cs` adds the Progress SDK through one `AddObservability()`
wrapper on the chat client and adds `AddToolObservability()` to the registered
tools to record their arguments and results
([SDK documentation](https://www.telerik.com/ai-observability-platform/documentation/sdk/dotnet)).
Adding `UseOpenTelemetry()` as well would duplicate spans. Every span carries the
`agent.template.id:custom-agent-local-prototype` and `agent.service.slug:<slug>`
tags for filtering.

`Progress:Observability:RecordInputs` and `Progress:Observability:RecordOutputs`
default to `true` for the local demo. They act as one switch: **setting either
flag to false disables all recorded content**, meaning prompts, answers, tool
arguments and tool results. The health response reports the effective
`telemetryRecordContent` value. To disable content recording for one run:

```bash
PROGRESS__OBSERVABILITY__RECORDINPUTS=false \
PROGRESS__OBSERVABILITY__RECORDOUTPUTS=false \
dotnet run --no-build -- --urls http://127.0.0.1:0
```

The overrides do not persist. The template does not truncate recorded content,
and SDK exception text is not covered by these flags.

Reading traces with Progress SDK 1.4.0:

- `AgentRuntime` clears `Activity.Current` for each run and restores it
  afterwards, because the SDK does not emit its automatic tool spans under the
  ASP.NET request activity. The Progress trace is therefore not linked to the
  HTTP request trace.
- Each tool runs once but appears as two spans: the SDK's automatic span under
  the `orchestrate_tools` root, with metadata only, and the
  `AddToolObservability()` span under the agent, with arguments and results.
  That wrapper ignores the record flags, so `Program.cs` adds it only when
  content recording is enabled.
- Parent spans repeat model-call token usage, so the trace token total is higher
  than actual usage. Use the individual model-call spans to inspect consumption.
- A model call that only requests tools shows an empty Output text panel; the
  requested calls appear under Tool Calls.

## Chat behavior

The chat sends up to six completed exchanges (at most 24,000 characters), plus
the current question, to the configured Azure model. History is kept only in
this browser tab's memory: **New chat** or a reload clears it. Older exchanges
fall out of context as those limits are reached; failed requests are not kept.
There is no shared server-side conversation or conversation database. Each smoke
case still starts independently.

Answers default to short plain text. Local Markdown passages retain their exact
section headings for citations; the agent can read the full bounded source when
more context is needed. Citations and model judgments still need review for
important decisions.

## Capabilities

Tools read the bundled content. The only access beyond it is declared in
`appsettings.json` and approved by the user before the build:

```json
"Capabilities": {
  "Network": {
    "AllowedHosts": ["api.open-meteo.com"]
  }
}
```

`Program.cs` builds an `ApprovedHttpClient` from that list and passes it to
`AgentDefinition.CreateTools`. It sends read-only GET requests and refuses any
other host, plain HTTP outside loopback addresses, credentials in the URL and
redirects. `GetAsync(url, cancellationToken)` returns every status code with its
body, so a tool can report data the API does not have; `GetStringAsync` returns
only a successful body. A refused request, a body over 64 KiB, a request over
10 seconds, or an unexpected status fails the run, and `--smoke` reports the
reason, such as `network_host_not_approved` or `network_http_500`. `/api/health`
lists the approved hosts as `networkHosts`; an empty list means no network access.

Nothing else is granted: no process execution, file writes, or environment or
secret reads, and no API keys or logins for the approved hosts. When content
recording is on, tool arguments and results, including the API response, are
recorded in Progress traces.

## Customization boundary

The builder may customize `AgentDefinition.cs`, `Tools.cs`, the `Content`,
`Capabilities:Network:AllowedHosts` (approved hosts only), `Agent`, and `Smoke`
values in `appsettings.json`, supported files under `docs/` and `data/`, and an
optional `INTEGRATION_PLAN.md`. Every content file must have one exact
`Content:Sources` entry whose value is `mock` or `supplied`; the fixed content
loader rejects undeclared files and does not fall back to the working
directory. An agent whose tools only compute or read approved hosts declares no
content. The runtime, web routes, UI shell, smoke engine, approved-host client,
project dependencies, and observability wiring stay fixed.

The fixed UI reads its title, Purpose, suggested prompts, one of four visual
presets (`knowledge`, `review`, `workflow`, or `analysis`), and its input hint
from the `Agent` section. Customize those values in `appsettings.json`; do not
edit the generated copy under `bin/`.

Mock data and tools representing future external sources must use provenance
`mock`; user-supplied local files must use `supplied`. For a prototype
representing SharePoint, Jira, a database, or another external source, see
`INTEGRATION_PLAN.md` for the remaining adapter work and a follow-up coding-agent
prompt. A simplified mock PoC also includes this plan to distinguish its tested
sample logic from the original full scope and deferred developer work.
Connecting the real system is a separate step, not part of this build. Other
local-file-only prototypes do not need that plan.

The project validator checks the fixed starter, allowed shape, settings
structure, declared content, declared hosts, and smoke expectations. Startup
checks the `Agent` values and requires one to three `AIFunction` tools. The validator does not
analyze editable C#; review
`AgentDefinition.cs` and `Tools.cs` for the accepted local/mock scope, source
paths, and unwanted external effects before building or running them.

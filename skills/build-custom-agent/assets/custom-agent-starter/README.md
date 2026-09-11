# Custom Agent local prototype

A bounded .NET 10 starter for an agent grounded in supplied local files or mock
data. It does not connect to or update a live business system, even if you
already have an adapter or connector configured.

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

Tracing uses the Progress SDK exporter with agent/chat `UseOpenTelemetry`
instrumentation ([SDK documentation](https://www.telerik.com/ai-observability-platform/documentation/sdk/dotnet#iagent)).
One agent span contains the model requests and real tool executions in order.
The SDK captures agent input/output and tool arguments/results automatically.
Static template identifiers use `AdditionalTags` for filtering.

`Progress:Observability:RecordInputs` and `Progress:Observability:RecordOutputs`
default to `true` for the local demo. SDK capture has one combined switch:
**setting either flag to false disables both input and output content**. The
health response reports the effective `telemetryRecordContent` value.

Model responses containing only tool calls can have an empty Output text panel.
The SDK records the structured calls under Tool Calls; the templates add no
synthetic output text.

To disable content recording for one run:

```bash
PROGRESS__OBSERVABILITY__RECORDINPUTS=false \
PROGRESS__OBSERVABILITY__RECORDOUTPUTS=false \
dotnet run --no-build -- --urls http://127.0.0.1:0
```

The overrides do not persist. SDK message/tool payloads have no additional
template truncation. SDK exception text remains outside these content switches.
The agent's Tool Results panel may remain empty even when results exist in its
raw output messages. Agent aggregate tokens also repeat model-call usage, so use
individual model spans when inspecting consumption.

A passing smoke run checks execution and expected answer fragments, without
telling the model those expected answers. It does not prove reasoning quality,
actual tool use, or production safety. The emitted trace IDs do not independently
prove backend ingestion; open the
[Progress Tracing page](https://observability.progress.com/observations) to
confirm that the traces arrived.

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

## Customization boundary

The builder may customize `AgentDefinition.cs`, `Tools.cs`, the `Content`,
`Agent`, and `Smoke` values in `appsettings.json`, supported files under `docs/`
and `data/`, and an optional `INTEGRATION_PLAN.md`. Every content file must have
one exact `Content:Sources` entry whose value is `mock` or `supplied`; the fixed
content loader rejects undeclared files and does not fall back to the working
directory. The runtime, web routes, UI shell, smoke engine, project dependencies,
and observability wiring stay fixed.

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

The project validator checks the fixed starter, allowed shape, configuration,
declared content, and smoke expectations. Startup requires one to three
`AIFunction` tools. The validator does not analyze editable C#; review
`AgentDefinition.cs` and `Tools.cs` for the accepted local/mock scope, source
paths, and unwanted external effects before building or running them.

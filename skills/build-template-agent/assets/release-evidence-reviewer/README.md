# Release Evidence Reviewer

Runtime retrieval stays within the requested review: fresh unnamed questions
search policy only; a named/selected project adds only that project's evidence.
Unrequested readiness checks are rejected. Explicit project switches supersede
the previous selection; New review does not choose a project from retrieved docs.

A small .NET 10 agent that reviews local Markdown release evidence. It reports
`Ready`, `Blocked`, or `not_found`; it never approves or performs a release.

The responsive chat UI lives in `wwwroot/index.html`, with styles in `styles.css`
and behavior in `app.js` alongside it. It has fixed release-review content and no
frontend dependencies or separate build step.

Review Atlas, then ask “What is still missing?”: the next request carries the
project identified by the real readiness tool and just the last question/answer.
The agent checks current evidence again; a prior answer is never evidence.
Descriptive follow-ups such as “What project is it?” use `SearchKnowledgeBase`
and answer the specific question briefly, without repeating the readiness report.
If the source does not describe the product, the answer says so.
Naming another project switches the review. **New review** clears the project,
last exchange and visible discussion, and cancels any in-flight response.
There is no server conversation store, database, or browser persistence.

`POST /api/chat` accepts `message` and optional `context: {project, question,
answer}`. Messages/prior questions are capped at 4,000 characters, prior answers
at 8,000, and project context at 100. Each request has fresh tool state, a
45-second deadline, at most six tool calls and four model requests. Replies
include the actual tool-derived project and tool history alongside the answer.

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
The Progress value must be the Integration key used by the app to write traces,
not an MCP key. Do not create or copy a `.env` file into the project.

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

A review message moves from the page to `POST /api/chat` in `Program.cs`, then to
`AgentRuntime.cs`, where a Microsoft Agent Framework (MAF) agent calls the tools
in `Tools.cs` to search the release evidence or check readiness before it answers.

| Path | What it does |
|---|---|
| `Program.cs` | Startup: settings, Azure OpenAI chat client, Progress tracing, HTTP endpoints or `--smoke` |
| `AgentRuntime.cs` | Reviews one message: request validation, agent instructions, tool loop and limits |
| `Tools.cs` | `SearchKnowledgeBase` and `CheckReleaseReadiness`, the tools the model calls; project scope and the typed verdict |
| `KnowledgeBase.cs` | Loads `docs/*.md` and searches their paragraphs by matching words |
| `SmokeRunner.cs` | The three `--smoke` cases |
| `appsettings.json` | Non-secret defaults: listen URL, deployment name, app name and smoke prompts |
| `docs/` | The fictional readiness policy and project evidence |
| `wwwroot/` | The page: `index.html`, `styles.css` and `app.js` |

## Build, smoke test, and run

```bash
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

Readiness replies show the documented evidence behind the verdict — every release
requirement, its documented value, whether it is satisfied, and the Markdown file
it came from — followed by the resolved project and the tools the agent actually
ran. Those rows are the typed result of `CheckReleaseReadiness`,
the same facts the model received, so the panel cannot disagree with the answer.
An unknown project shows no rows rather than inventing evidence.
Search-only replies retain the project context but show no readiness panel.

For the UI, .NET chooses an available loopback port and prints it in the
standard `Now listening on: http://127.0.0.1:<port>` line. The smoke command
runs exactly three cases and prints one parseable `SMOKE_REPORT=<json>` line
with each case ID, status, and reason. Its three non-secret prompts live
under `Smoke` in `appsettings.json`; the runner rejects missing or empty prompt
values.

A passing smoke run proves the local agent execution and tracing setup, not
backend ingestion: find the service, template tag, time and matching prompt on
the [Progress Tracing page](https://observability.progress.com/observations).

## Tracing

When `Progress:Observability:ApiKey` is configured, each review message appears in
Progress Observability as one trace with the agent run, its model calls and its
tool calls. `Program.cs` adds the Progress SDK through one `AddObservability()`
wrapper on the chat client, and `AgentRuntime.cs` adds `AddToolObservability()`
to record tool arguments and results
([SDK documentation](https://www.telerik.com/ai-observability-platform/documentation/sdk/dotnet)).
Adding `UseOpenTelemetry()` as well would duplicate spans. Every span carries the
`agent.template.id:release-evidence-reviewer` tag for filtering.

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
  That wrapper ignores the record flags, so `AgentRuntime` adds it only when
  content recording is enabled.
- Parent spans repeat model-call token usage, so the trace token total is higher
  than actual usage. Use the individual model-call spans to inspect consumption.
- A model call that only requests tools shows an empty Output text panel; the
  requested calls appear under Tool Calls.

# Ticket Triage

A small .NET 10 agent for a fictional product-support inbox. Seven JSON tickets
and one Markdown policy demonstrate read-only queue and priority suggestions.
It never assigns, sends, persists, or connects to Jira/Slack. Structured intake
fields—not title keywords or model guesses—determine the recommendation.

## Run

**Development use only:** This agent is in a local development environment.
.NET user-secrets stores credentials unencrypted in a JSON file in your user
profile. For production deployment, inject credentials through environment
variables backed by your deployment’s secret manager.

Use the shared Secret Manager ID `Progress.AgentBuilder.Mvp` to configure
`AzureOpenAI:Endpoint`, `AzureOpenAI:Deployment`, and optionally
`AzureOpenAI:ApiKey` (otherwise `DefaultAzureCredential` is used).
`Progress:Observability:ApiKey` is the Progress **Integration** key, not an MCP
key. Store secrets locally with `dotnet user-secrets` or in deployment environment
variables; never put them in this project, its fixtures, or a `.env` file.

Configure once in your terminal (replace placeholders locally, not in chat):

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

Skip the API-key command when Azure identity is configured.

```bash
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

Open the actual `Now listening on:` loopback URL printed by the started process.
The responsive `wwwroot/index.html` is an inbox/detail/recommendation UI, with
`styles.css` and `app.js` alongside it and no frontend dependencies or separate
build. Select a ticket and choose
**Suggest triage**, then **Ask about this ticket** (for example, "Why P1?"). Each
question is answered from that selected ticket's real tool evidence, not a shared
chat history. The endpoint returns the deterministic tool result separately
from the model's short explanation, so UI queue/priority badges never parse prose.

When facts are missing, only those fields appear as typed choices. Supply one or
more and choose **Re-evaluate**. This is a **temporary scenario**, not a ticket
edit: supplied facts are labeled `temporary-scenario#<id>`, bundled facts keep
`tickets.json#<id>`, and the original values remain visible. **Clear scenario**
returns to the bundled ticket. Selecting any ticket clears the question and
scenario and cancels the previous in-flight response. Nothing is saved between
requests, page reloads, or users; the page sends the current scenario explicitly.

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

A triage request moves from the page to `POST /api/triage` in `Program.cs`, then
to `AgentRuntime.cs`, where a Microsoft Agent Framework (MAF) agent calls the
tools in `Tools.cs` to inspect the selected ticket before it explains the
policy-derived recommendation.

| Path | What it does |
|---|---|
| `Program.cs` | Startup: settings, Azure OpenAI chat client, Progress tracing, HTTP endpoints or `--smoke` |
| `AgentRuntime.cs` | Triages one ticket: agent instructions, tool loop and limits, and grounding checks on the result |
| `Tools.cs` | `GetTicket`, `ReadTriagePolicy` and `SuggestTriage`, the tools the model calls, scoped to the selected ticket |
| `TriageData.cs` | Loads and validates tickets and policy; derives queue, priority and evidence from intake facts |
| `TriageRequest.cs` | The `/api/triage` request: ticket ID, optional question and temporary scenario |
| `SmokeRunner.cs` | The three `--smoke` cases |
| `appsettings.json` | Non-secret defaults: listen URL, deployment name, app name and smoke ticket IDs |
| `data/tickets.json`, `docs/triage-policy.md` | The bundled mock tickets and triage policy |
| `wwwroot/` | The page: `index.html`, `styles.css` and `app.js` |

## Small, bounded contract

- `GetTicket`, `ReadTriagePolicy`, and `SuggestTriage` are available read-only tools.
  The required scoped inspection includes a deterministic recommendation preview;
  the model can read the full policy or explicitly recalculate when useful.
- Each run gets fresh tool state, at most eight tool invocations, a 45-second
  deadline, and only the selected ticket's scope. Three tool rounds plus one final
  synthesis request bound the model loop. The model chooses tool order.
- `proposed` supplies queue, priority, evidence, and policy references.
  `needs_information` names missing required fields without guessing a priority.
  An unknown ID returns `not_found` directly from the real `GetTicket` lookup.
  Validation requires actual selected-ticket inspection and a typed, policy-derived
  outcome, not a fixed three-tool sequence. Tool history records only actual calls.
- `GET /api/health`, `GET /api/tickets`, and `POST /api/triage` with
  `{"ticketId":"T-1001","question":"Why P1?"}` form the complete API.
  An optional `scenario` object may supply only required fields missing from the
  bundled ticket. For T-1005, for example:
  `{"environment":"production","impact":"single_customer","workaroundAvailable":false}`.
  Questions are limited to 1,000 characters. Unknown fields, duplicate JSON keys,
  invalid enum/boolean values, and overrides of existing or irrelevant facts are
  rejected before a model call. No ticket-mutation route exists.
- JSON unknown fields, invalid enum values, duplicate IDs, malformed input, and
  missing policy headings fail fixture loading. Missing intake facts are allowed
  and explicitly represented as null. This is routing, not free-text classification.

`--smoke` requires the model and Integration-key configuration, runs exactly
`clear-routing`, `missing-information`, and `unknown-not-found`, and prints one
`SMOKE_REPORT=<json>` with each case's status and reason. Assertions check actual
tool use and typed decisions/evidence, not only answer wording. Each case is a
real model run; local deterministic/scripted tests do not prove this live execution.
A passing smoke is not proof of backend ingestion: find the service, template tag,
time and matching ticket on the
[Progress Tracing page](https://observability.progress.com/observations).

HTTP error responses expose only allowlisted internal reason codes, never raw
provider exceptions or credential values.
The bundled policy is illustrative, not a production SLA. Add real intake,
authentication, and reviewed operational policy separately before production use.

## Tracing

When `Progress:Observability:ApiKey` is configured, each triage request appears in
Progress Observability as one trace with the agent run, its model calls and its
tool calls. `Program.cs` adds the Progress SDK through one `AddObservability()`
wrapper on the chat client, and `AgentRuntime.cs` adds `AddToolObservability()`
to record tool arguments and results
([SDK documentation](https://www.telerik.com/ai-observability-platform/documentation/sdk/dotnet)).
Adding `UseOpenTelemetry()` as well would duplicate spans. Every span carries the
`agent.template.id:ticket-triage` tag for filtering.

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

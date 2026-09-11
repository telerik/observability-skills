# Docs Q&A (RAG)

A small .NET 10 read-only document workspace: ask about four bundled synthetic
Markdown manuals, inspect answers and open the exact source sections. Retrieval
is meaningful-token lexical search, not embeddings or a vector database.
Search returns section identifiers and titles only; `ReadSection` supplies the
evidence text. Every displayed citation is captured from an actual `ReadSection` call after
`SearchDocuments`; source IDs are never scraped from model prose. Citations prove
which excerpts were read, not that every generated claim is automatically correct.

## Configure and run

**Development use only:** This agent is in a local development environment.
.NET user-secrets stores credentials unencrypted in a JSON file in your user
profile. For production deployment, inject credentials through environment
variables backed by your deployment’s secret manager.

Uses the same `Progress.AgentBuilder.Mvp` user-secrets ID as Release Evidence
Reviewer. Set `AzureOpenAI:Endpoint`, `AzureOpenAI:Deployment`, optionally
`AzureOpenAI:ApiKey` (otherwise `DefaultAzureCredential`), and
`Progress:Observability:ApiKey` through .NET user-secrets or environment variables.
The Progress key is an Integration key, not an MCP key. Do not copy secrets or
`.env` files into this package.

```bash
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Deployment" "gpt-5.4-mini"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "Progress:Observability:ApiKey" "ac_p_..."
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

The bundled deployment default is `gpt-5.4-mini`; replace it in the command
with your actual Azure deployment name if different. User secrets override
`appsettings.json`; environment variables such as `AzureOpenAI__Deployment`
override both.

Open the actual `Now listening on:` loopback URL printed by the process. There
is no frontend build, database, upload endpoint or external document connector.
The source pane works without a model call, but application startup still needs
the configured Azure endpoint/deployment.

Use **Ask a follow-up** below an answer to carry the last question/answer into a
new lookup. **Ask about this section** scopes the next question to the current
source preview; the scope chip stays visible until cleared. Follow-ups can keep
that scope. **New question** cancels pending work and clears context, scope and
results. Merely previewing a source does not change the active scope. Each result
identifies its submitted question, prior question (if any), and source scope.

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

## Small runtime contract

- `GET /api/health`: corpus readiness and tracing state, not model connectivity.
- `GET /api/documents`: bundled sections only.
- `POST /api/ask` with `{ "question": "How long are audit logs retained?" }`:
  `status`, `answer`, structured `citations`, actual `toolCalls`, and `traceId`.
  Each answer shows its trace ID and the tools the agent actually ran beneath it.
- Optional request fields: `previousTurn: { "question": "...", "answer": "..." }`
  and `sourceId: "exports#export-availability"`. Context is one previous Q/A pair,
  not full conversation history; omit both fields for an independent lookup.
- Question, prior question and prior answer each allow 1–4000 trimmed characters;
  answers are capped at 4000 characters. Runs have a 45-second deadline,
  four model round trips and at most six tool calls. Tools/citations are isolated
  per request: follow-ups retrieve/read again, and prior model text never becomes
  citation evidence. No conversation is stored on the server. Search has no match
  on common words alone; an exact scope restricts both search and reads. Unknown
  or invalid source IDs return a safe 400 before any model request. Partial answers
  must state what the selected evidence does not cover; clear scope to search wider.
- UI uses text-only DOM rendering, accessible source buttons, responsive panels,
  and explicit loading, empty, no-match and failure states.

`--smoke` runs exactly `grounded-answer`, `two-sources`, `unknown-not-found`,
checking actual tool calls, source IDs, known fixture values and W3C trace IDs.
It requires model access and a Progress Integration key, and prints one
`SMOKE_REPORT=<json>` line.

Tracing uses the Progress SDK exporter with agent/chat `UseOpenTelemetry`
instrumentation ([SDK documentation](https://www.telerik.com/ai-observability-platform/documentation/sdk/dotnet#iagent)).
One agent span contains the model requests and real tool executions in order.
The SDK captures agent input/output and tool arguments/results automatically.
Static template identifiers use `AdditionalTags` for filtering.

`Progress:Observability:RecordInputs` and `Progress:Observability:RecordOutputs`
default to `true` for the local demo. Content capture has one combined switch:
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

A passing local smoke is not proof of backend ingestion: verify those exact
trace IDs on the [Progress Tracing page](https://observability.progress.com/observations).

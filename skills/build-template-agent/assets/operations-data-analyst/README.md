# Operations Data Analyst

A compact .NET 10 / Microsoft Agent Framework (MAF) agent over **synthetic local CSV**:
90 daily rows, three services, August 1–30, 2026. It explores requests, errors,
error rates and response times. No database or operational writes.

Ask a question, adjust the filters, or click a chart point/bar. The agent selects
a relevant view, calls the local metrics tool, and explains the result in a few
sentences. The explanation is the page's primary result; the metric tiles, chart,
dates, service and source rows follow the same selection.

- “Which service contributes most errors?” shows error counts by service.
- “Which day had the most requests?” shows daily request volume.
- “What is the maximum latency for auth?” reports the peak day and its value;
  “when was checkout slowest?” and “which service is fastest?” work the same way.
- “Show checkout latency from August 20 to August 30” focuses that service/range.
- “Compare August 17–23 with August 24–30” shows two period bars, retaining the
  selected service and metric.
- “What does this chart show?” explains the current view without changing it.
- Follow-ups can ask about the meaning of a metric, interpret the current view,
  request a simpler explanation, or ask what the data can support.
- Click a service bar to explore its daily trend; click a day to inspect it.

The four metric tiles are rebuilt for the current selection instead of showing a
fixed set: a daily view reports the overall figure, the peak day, the 95th
percentile day and the median day; a service ranking reports the overall figure,
the highest and lowest service and their spread; a period comparison reports both
windows, their change and the overall scope. A tile whose exact number the agent
tool already returns is a button that explores it; the derived percentile, median
and spread tiles are a read-out only, so no click can ask the agent about a
figure it was not given by the exploration tool. Typed questions about those
tiles use the read-only context tool, which includes their values and captions.

The chart arrives as soon as the tool finishes, before the explanation.
Tile, chart and filter clicks show the chosen data immediately and send that exact
selection to the real agent. Reset clears question context without a model call;
Stop cancels an in-progress analysis. Questions retain only the previous question
and current view as refinement context; there is no stored conversation.
Filters, source rows, automatic checks and tool evidence are expandable.

## Configure and run

**Development use only:** This agent is in a local development environment.
.NET user-secrets stores credentials unencrypted in a JSON file in your user
profile. For production deployment, inject credentials through environment
variables backed by your deployment’s secret manager.

Use the same local development Secret Manager ID as the other templates:

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

The Azure key is optional with configured `DefaultAzureCredential`. Progress uses
the **Integration** key, never an MCP key. Do not copy/source `.env` files.
Use the running process's `Now listening on:` URL; `/api/health` confirms the
fixture. Opening the dashboard makes no model call.

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

A question moves from the page to `POST /api/analyze/stream` in `Program.cs`, through
`ViewWorkflow.cs`, to `AgentRuntime.cs`, where the MAF agent calls one tool from
`Tools.cs`. The chart streams back as soon as that tool finishes, followed by the
explanation.

| Path | What it does |
|---|---|
| `Program.cs` | Startup: settings, CSV load, Azure OpenAI chat client, Progress tracing, HTTP endpoints or `--smoke` |
| `ViewWorkflow.cs` | Validates a question and its view; `ViewRules` builds charts, tiles, peaks and lows |
| `AgentRuntime.cs` | Answers one question: agent instructions, the one-tool loop, streaming and grounding checks |
| `Tools.cs` | `ExploreMetrics`, `GetCurrentContext` and `ExplainLimitation`, the tools the model calls |
| `MetricsStore.cs` | Loads and validates `data/operations.csv`; summaries, comparisons and outliers |
| `Models.cs` | Request, view, result and reply records shared by the files above |
| `SmokeRunner.cs` | The three `--smoke` cases |
| `appsettings.json` | Non-secret defaults: listen URL, deployment name and app name |
| `data/operations.csv` | The synthetic metrics |
| `wwwroot/` | The page: `index.html`, `styles.css` and `app.js` |

## How the agent works

A single bounded MAF session calls `ExploreMetrics` to select and calculate a
view, or `GetCurrentContext` to explain the current chart, tiles and data limits
without changing the dashboard, then writes a short explanation. The normal flow
uses two model requests.
The tool accepts typed, nullable view changes, inherits unspecified filters,
and validates services, metrics, dates and comparison windows in C#. Each run
has a 45-second deadline and at most one tool invocation plus synthesis.
The model interprets follow-ups from the question, current view and tool evidence.
`ExplainLimitation` supplies scope limitations when needed, and the model writes
a brief answer or clarification specific to the question. Context and limitation
tools leave the dashboard unchanged.
Explicit UI selections are fixed for that request. Tool evidence reports the
actual normalized selection, complete calculated result, and the focused payload
returned to the model. Single-metric questions send only the selected values;
questions requesting several metrics can include the additional figures.

`data/operations.csv` is the only analysis source. It is copied beside the binary
on every build and loaded once at startup, so an edited CSV needs a rebuild and a
restart. Startup logging and `/api/health` report the resolved absolute path, row
count, date bounds and a content fingerprint, so a stale copy is visible at once.
It contains `date,service,requests,errors,total_response_ms`.
Error rates are `sum(errors) / sum(requests) * 100`; response time is
`sum(total_response_ms) / sum(requests)`. These are request-weighted, never means
of daily means. Zero-request values remain undefined (`null`, displayed as —).
Peaks, lows and ties for the selected metric are calculated locally from the
exact plotted series, never ranked by the model, so a highest/lowest answer
cannot disagree with the chart. Undefined points are skipped, never counted as
zero. When the plotted series is grouped by service or period, the peak and low
describe services or periods rather than days.
Comparison changes use percentage points and milliseconds. Outliers compare each
service/day with the other selected days' median; they require at least three
nonzero-request days, at least 2× the median, and an increase of 2 percentage
points or 100 ms. This is a heuristic, not a causal explanation.

Metric tiles are calculated from the exact plotted series, so they cannot disagree
with the chart or the answer. Percentiles and medians take the nearest rank, so
every tile reports a value some day actually recorded rather than an interpolated
one, and the captions state the sample they came from: `95th percentile day` of
11 daily averages is not a per-request p95, which the daily CSV cannot support.
Differences between percentages are labelled `pp`.

The deliberate checkout spike on August 24 has 5,000 requests, 500 errors and
900 ms average response time; it is the dataset's only anomaly. Response times
are otherwise constant per service (auth 120 ms except 130 ms on August 1,
checkout 200 ms, search 80 ms) and daily error counts are fixed, so error rates
drift only as request volume grows. All charts and KPIs are calculated locally.
Unsupported metrics and invalid ranges cannot generate chart values. Empty
selections remain empty. Period bars label their exact windows; KPIs and source
rows use the labeled overall scope. Missing periods have no inferred changes.

`POST /api/view` returns deterministic view/chart/dashboard/highlight data without
a model call. `POST /api/analyze` returns the complete agent result.
`POST /api/analyze/stream` streams newline-delimited JSON events: `view`, `text`,
then `done` (or `error`). Context answers omit `view` events and return null
dashboard/chart/highlights, preserving the visible data and last exploration question.
Analysis accepts `{question,view,lastQuestion?,approvedView?}`.
The optional `approvedView` fixes the selection made by a UI control; it is
revalidated by the server. Free-form requests to preserve or change the view are
interpreted by the model. If explanation fails after data arrives, the UI keeps
the calculated view and reports that the explanation could not finish.

The three live smoke cases are `known-aggregate`, `comparison-spike`, and
`empty-selection`. They use the same agent workflow as an explicit UI selection, fixing the test
view and asserting exact tool-derived numeric results. Free-form questions and
changes of view are covered separately by protocol tests and browser checks.
`SMOKE_REPORT=<json>` includes each case's status, reason and tools; model and
Integration settings are required.

A passing local smoke is not proof of backend ingestion: find the service,
template tag, time and matching question on the
[Progress Tracing page](https://observability.progress.com/observations).
This is a small example, not a production monitoring system.

## Tracing

When `Progress:Observability:ApiKey` is configured, each question appears in
Progress Observability as one trace with the agent run, its model calls and its
tool call. `Program.cs` adds the Progress SDK through one `AddObservability()`
wrapper on the chat client, and `AgentRuntime.cs` adds `AddToolObservability()`
to record tool arguments and results
([SDK documentation](https://www.telerik.com/ai-observability-platform/documentation/sdk/dotnet)).
Adding `UseOpenTelemetry()` as well would duplicate spans. Every span carries the
`agent.template.id:operations-data-analyst` tag for filtering.

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

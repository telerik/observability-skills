---
name: build-custom-agent
description: Build a bounded .NET 10 local agent prototype from a short Purpose. For requests requiring live business-system access, credentials, real effects, or complex orchestration, offer an integration plan and, when meaningful, a separately confirmed simplified mock PoC. Use for the custom-agent path, not a ready template or an existing app.
---

# Build a custom agent

This workflow produces either a safe local prototype or a zero-write integration
plan in chat. It never implements live business-system access or real effects.
`Try-and-refine: on|off` is an optional workflow preference, not Purpose; retain
the latest explicit value across revisions and path changes without asking for it.
When unspecified, offer it only after the local prototype is ready.

Throughout intake, build, testing, and troubleshooting, never run
`dotnet user-secrets list`, read secret-store files, or dump environment values.
This also forbids listing only key names, checking which keys exist, or filtering
or redacting the output: those operations still inspect the secret store.
Run the documented app or smoke command directly and let the app report missing
configuration. Report only the missing key names and refer to the README;
never request, inspect, or print credential values.

When Purpose is present, read `references/scope-routing.md` without a scenario
summary. For a complex path choice, go directly to the question tool: its body
contains the scope explanation. Do not duplicate that explanation in commentary.

## Intake and approval

1. If Purpose is absent, ask exactly:

   > In 1-2 short sentences, what should this agent help someone accomplish?

2. Apply the routing reference and verify requested capabilities against the
   relevant starter implementation. Infer a bounded Name, Knowledge, and no
   more than three Actions. Reading a public HTTPS API without credentials is
   network access the user must approve before the build; process execution,
   file writes, and environment or secret reads cannot be granted. Ask at most
   the one permitted ambiguity clarification; never ask for credentials or
   secret values.

3. A buildable request gets a compact **Proposed local prototype** in chat using
   the reference's Markdown layout. Say which sources are user-supplied and
   which are mock, which hosts need network access, and that no live adapter
   will be used. Before Build/Revise, disclose any requested capability being
   simplified or deferred, its local substitute, and what will not be built.
   Nothing is built yet. A complex/live request instead gets only the
   reference's scope-first question; do not show a plan preview or proposal
   block.

   Use exactly the applicable two choices:

   | Situation | Choices |
   |---|---|
   | Local proposal | `Build proposed agent` / `Revise proposed agent` |
   | Local proposal with network access | `Approve network access and build` / `Revise proposed agent` |
   | Complex request with a useful bounded mock slice and mocks not rejected | `Show implementation plan` / `Propose a simplified mock PoC` |
   | Complex request without an eligible mock slice or with mocks rejected | `Show implementation plan` / `Revise proposed agent` |

   In the same turn as the proposal, call the host's question tool (for example
   `vscode_askQuestions` in VS Code or `ask_user` in Copilot CLI) with only
   `How would you like to continue?` and exactly the applicable labels. Do not
   move or repeat the proposal inside the picker, and never end the turn with
   that question as plain text while a question tool exists. With network
   access listed, that click approves exactly the listed hosts; adding a host
   later needs a new proposal and approval.
   For a complex request, put the scope explanation and that question together
   in the tool's question body, with the applicable structured choices. If no
   question tool exists, present the same context and two numbered choices.
   Wait for the user's answer. Cancellation, empty input, or tool failure
   selects nothing.

   An explicit implementation-plan request selects the plan directly. Declining
   mocks does not; keep plan/revise unless the user asks for the plan.

4. Interpret a returned selection only as follows:

   - `Propose a simplified mock PoC`: show the reduced four fields plus excluded
     behavior, then ask Build/Revise and wait. Preserve the full goal for
     `INTEGRATION_PLAN.md`; do not restart intake or create files.
   - `Build proposed agent` or `Approve network access and build`: approve only
     the latest displayed local scope, including its listed hosts. Begin without
     more customization questions; prerequisites may still stop it.
   - `Show implementation plan`: read and follow `references/plan-only.md`.
   - `Revise proposed agent`: use the reference's one-reply revision, reroute,
     and ask the appropriate next choice.

No project writes occur before explicit Build approval. Use an interactive
session for the guided flow. In noninteractive mode, return the proposal and
end the turn unless the conversation already contains explicit approval of
that exact displayed scope. Tool execution permissions do not approve a
proposed scope.

## Build the local prototype

Use the requested target or infer a lowercase-hyphen folder. Require .NET 10,
locate this skill, and run:

```bash
dotnet run --file <skill-directory>/scripts/copy-template.cs -- --target <target>
```

Accept only a missing target or a real empty directory. Never bypass a refusal,
overwrite content, reconstruct the starter, or source/copy a parent `.env`.

Edits are limited to:

- `AgentDefinition.cs`: its bounded definition and one-to-three registrations;
- `Tools.cs`: at most three side-effect-free local, mock, or approved-host tools;
- regular `.md`/`.txt` files under `docs/` and `.json`/`.csv` under `data/`;
- `appsettings.json`: `Content:Sources`, `Capabilities:Network:AllowedHosts`
  (only the approved hosts), `Agent`, and the three fixed `Smoke` cases.
  `Content:Sources` must map every content path exactly once to `mock` or
  `supplied`; use `supplied` only for files the user provided;
- `INTEGRATION_PLAN.md`, required only for an external-source prototype or
  simplified mock PoC.

Remove starter content and tools the approved scope does not use; an agent whose
tools only compute or read approved hosts declares no content. Never add content,
citations, provenance lines, instructions, or markers only to satisfy the
validator or a smoke; if a check cannot pass honestly, stop and report it. Keep
the summary comment of each file you change accurate.

Choose UI preset `knowledge` for Q&A, `review` for evidence checking,
`workflow` for triage/process work, or `analysis` for summaries/metrics. Keep
the input hint short and scenario-specific. Instructions must preserve concise
plain text, exact source/section citations, provenance, qualifications, and
honest missing evidence. Keep conditional rules within their source's scope;
a requirement for one case must not become a requirement for all cases.
Every literal `docs/` or `data/` reference must resolve.

All other files are fixed, including runtime and standard SDK instrumentation,
HTTP/UI/health, project/packages, capabilities, and smoke engine. Add no
dependencies. Generate no process execution, file writes, secret reads, live
effects, or background control loops. Do not use an installed adapter,
connector, or MCP tool during intake, build, or smoke. Azure OpenAI and Progress
runtime settings remain as documented user-secrets. The validator checks project
structure, settings shape and fixed values, declared content and hosts, and
smoke expectations; app startup checks the `Agent` values. It does not analyze
editable C# or establish its safety.

Network access goes only through the `ApprovedHttpClient` that `CreateTools`
receives, to the hosts under `Capabilities:Network:AllowedHosts`. Before writing
a tool that calls a host, read `references/network-tools.md`. Never create an
`HttpClient` or add API keys or logins; an API that needs them is a complex request.

Preserve the fixed tracing setup (one `AddObservability()` chat-client wrapper,
`AddToolObservability()` when content capture is on, the `AgentRuntime` activity
reset); add no other wrapper. `Progress:Observability:RecordInputs` and
`Progress:Observability:RecordOutputs` default to `true`; setting either to
`false` disables all recorded content (use the README's overrides when
requested). SDK exception text stays outside these switches.

## Required gates

Before building, review `AgentDefinition.cs` and `Tools.cs`: confirm the accepted
scope, one to three functions, and that literal `docs/` and `data/` paths
resolve. Network access must go only through `ApprovedHttpClient` to the approved
hosts; remove any other external calls, process execution, file writes, secret
access, live effects, or background loops. A validator pass is not a substitute
for this review.

Validate before building. Once that passes, copy the current
`appsettings.json` to a temporary `smoke-baseline.json` outside the project,
before the first smoke. Never replace it. Every later validation, including
after repair, uses that snapshot; a genuine expectation redesign makes the
build unverified rather than authorizing a weaker baseline.

```bash
dotnet run --file <skill-directory>/scripts/validate-project.cs -- --target <target>
# Save the validated appsettings.json outside the project now.
dotnet build <target>/CustomAgent.csproj
dotnet run --project <target>/CustomAgent.csproj -- --smoke
```

Require one `SMOKE_REPORT=<json>` with overall `pass` and exactly the passing
IDs `knowledge`, `tool`, `not-found`. Prompts are ordinary questions; expected
markers remain separate and use stable facts/source paths, not internal
diagnostic tokens. Each case needs one marker of four or more characters that is
not in its prompt or, for `knowledge` and `tool`, in `Agent:Instructions`; with
declared content, `knowledge` also needs a `docs/` or `data/` path marker. A
case passes only when a registered tool returned a result and every marker is in
the answer. Smokes do not prove the right tool, reasoning quality, ingestion,
live outcomes, or production safety. On missing configuration, follow the
credential-handling rule above; do not inspect configuration as a prerequisite
check.

Resolve the copied project directory to `<absolute-target>`. Start the built app
persistently on an OS-assigned loopback port using that absolute path, regardless
of the current working directory:

```bash
dotnet run --project "<absolute-target>/CustomAgent.csproj" --no-build -- --urls http://127.0.0.1:0
```

Use the host's persistent terminal or background-process mechanism with readable
stdout and stderr. Once launched, immediately read that process's output in the
same turn unless the URL is already present. Use short bounded reads for at most
30 seconds total and stop if the process exits. Do not wait for the server
command to complete, because a healthy server remains running, and do not end
the turn with a passive "Waiting..." status. If process output is not directly
readable, use one combined launch log beside the requested target, never a
shared `/tmp` filename. Disclose if the process cannot outlive the session.

Read only this process's own
`Now listening on: http://127.0.0.1:<port>` line and health-check that exact
process at `<url>/api/health`. Never guess or scan ports. A process exit,
missing listen line at the deadline, or failed health is a failed gate. Never
start a replacement because an output read was incomplete, and never restart
an already healthy process. Finally validate again:

```bash
dotnet run --file <skill-directory>/scripts/validate-project.cs -- --target <target> --smoke-baseline <snapshot>
```

## After the required gates

Before any further message to the user, read and follow
`references/finish.md`: the readiness summary, the optional try-and-refine
offer, and the handoff with its required note. Plan-only requests and failed
required gates never reach it.

# Custom-agent scope routing

Use this reference after Purpose is available to choose a truthful local
prototype or zero-write integration plan, optionally through a separately
confirmed reduced mock PoC.

## Normalize and decide

Purpose is actionable when it names a concrete outcome and enough subject,
input, or action for a safe demonstration. For only a vague topic or role, ask
the one allowed clarification:

> What should the agent mainly do: answer from information, analyze or summarize
> records, or take actions in another system?

Ask nothing further. If uncertainty remains, offer plan/revise without inventing
a scenario. A user-selected revision is a separate editing flow, not another
automatic clarification.

Normalize internally:

```yaml
purpose: <required, concise>
name: <inferred display name>
service_slug: <lowercase-hyphen slug>
knowledge:
  mode: workspace-files | generated-docs | mock-records | none
  workspace_sources: [<workspace-relative paths only>]
actions:
  - name: <at most three>
    behavior: local-read | local-compute | mock-read | network-read
    host: <network-read only: the public HTTPS host, without credentials>
external_systems: [<future adapters>]
requested_side_effects: [<writes, approvals, notifications>]
recommended_outcome: local-prototype | plan-only
reason: <one concise user-visible sentence>
```

Local fields describe a possible prototype, not a rewritten live goal. Preserve
the original sources/actions for later revision and planning. Include no secrets,
raw document content, absolute paths, or hidden reasoning.

Ask internally:

> Can the core behavior be demonstrated safely and meaningfully without
> target-business-system credentials or real side effects, using at most
> read-only calls to a public API that needs no credentials?

Choose a local prototype only when yes. Supplied safe files, generated documents,
and deterministic mock records are allowed. A named system alone does not force
plan-only: advisory Jira triage or SharePoint-style Q&A may use disclosed local
content. Never infer live access or effects the user did not request, and honor
rejected source types or mocks.

A `network-read` action is allowed when the host is a public HTTPS API that
needs no key, login or payment, the call only reads, and the user approves the
host before the build; the runtime then refuses every other host. Process
execution, file writes, and environment or secret reads cannot be granted by
this version's runtime: name the capability in the complex question and offer
the plan or a mock slice, never a silent substitute.

Choose plan-only for essential live data/credentials, real writes or
notifications, approval or durable multi-step execution, production RAG,
multi-agent orchestration, deployment, or provider switching. Uncertainty about
a useful safe slice means no mock offer. Reapply this decision after revisions;
classification and proposals never authorize a build.

For a technique, integration, or automation named in the request, inspect the
concrete tool/helper implementation and permitted edits before claiming support;
follow delegated calls rather than inferring behavior from tool names. Explain
how it works and which parts are simulated, simplified, or deferred. Repeating
the requested feature name or demonstrating its high-level goal with mock data
does not establish that the requested method is implemented. If a required
capability cannot fit those boundaries, use the complex choice. Honor rejected
mocks with plan/revise, without offering a mock slice.

## Present the path choice

A buildable request gets this proposal in chat before the native Build/Revise
picker. Render it as Markdown, with a blank line after the heading and one
bullet per field with a bold label, rather than as a code block or a single
paragraph in the picker:

```markdown
**Proposed local prototype**

- **Purpose:** <concise purpose>
- **Name:** <agent name>
- **Knowledge:** <sources, identifying each as user-supplied or mock>
- **Actions:** <up to three actions>
- **Network access:** none | <host> for <action> (read-only, public API, no credentials)

No live adapter will be used.

**Scope limit:** <Requested capability> will use <concrete implemented method>.
<What is simulated, simplified, or excluded from the requested behavior>.
```

Use this layout for initial proposals, simplified mock proposals, and revised
local proposals. With network access listed, the picker offers `Approve network
access and build` instead of `Build proposed agent`; the hosts named there are
exactly the ones written to `Capabilities:Network:AllowedHosts`. When a
requested capability is simplified or deferred, make
Purpose describe the actual demonstration and add a concise **Scope limit:**
paragraph after the fields and before the picker. Name the requested capability,
what the prototype will do in its place, and what will not be implemented.
Tailor this explanation to the request; omit unrelated exclusions. Labels such
as `mock data` or `no live adapter` alone do not explain a change in capability.
Omit the scope-limit paragraph only when no requested capability is reduced.

Distinguish mock inputs or simulated actions from behavior that actually runs.
Keep user-supplied sources labeled as supplied and starter/generated examples
as mock. Carry the accepted limits into revisions and handoff, and preserve
the full goal as deferred developer work in the integration plan when required.
Do not imply the agent already exists.

For a complex/live request, put two to four plain sentences only in the native
question body: why the full goal is outside this MVP and how an implementation
plan helps. Describe a mock slice and its exclusions only when that option is
eligible. If mocks are rejected or no useful slice fits, explain that revision
can narrow the scope and offer plan/revise. Append a blank line and
`How would you like to continue?`; show neither a plan preview nor four fields.
Go directly to this question after reading the reference, without a scope-summary
commentary message. Use the host's question tool with the applicable two choices.

For the complex/live choice, keep all rationale in that box, with short
structured choice labels. Use the same native mechanism for plan/revise and
build/revise; follow `SKILL.md` for fallback and cancellation. Declining mocks
retains plan/revise unless the user explicitly requests the plan. A plan
selection does not start implementation.

For an external-system prototype, prefer concrete disclosure such as `mock Jira
issues`, not `synthetic data`, and say it will not use even an available adapter,
connector, or MCP connection. User-supplied safe files are `supplied`, never
mock. Plan-only creates neither a prototype nor mock dataset.

## Simplified mock PoC

Offer `Propose a simplified mock PoC` only when a useful advisory or analytical
slice fits local content and at most three deterministic read/compute tools. Do
not offer it when mocks were rejected, only a plan was requested, or no meaningful
slice fits; do not repeatedly re-offer a declined mock unless the user revises
that preference.

Selecting this option requests only a proposal. Create no files, inspect no
credentials, and call no business system. Show the reduced Purpose, Name,
Knowledge, Actions, and excluded behavior, then ask exactly `Build proposed
agent` / `Revise proposed agent`. Preserve the original goal and deferred work
for `INTEGRATION_PLAN.md`.

For example, production Kubernetes auto-remediation can become **Kubernetes
Remediation Advisor** over mock incidents and an example runbook. Explicitly
exclude cluster access, continuous monitoring, restart/rollback execution, and
implemented approvals, and state that sample decisions do not prove production
safety. Only explicit Build approval of that displayed scope starts the existing
prototype flow. Add no new runtime, workflow engine, live tools, or background
monitor. Results remain recommendations or missing evidence, never completed
restarts, approvals, payments, or notifications. Keep this boundary in Purpose,
instructions, answers, smoke interpretation, and handoff. `INTEGRATION_PLAN.md`
holds only the separate developer continuation; never execute it in this build.

## Revise in one reply

On `Revise proposed agent`, show one copyable block:

```text
Purpose: <current purpose>
Name: <current name>
Knowledge: <current data sources>
Actions: <current actions>
Network access: <current hosts or none>
```

Ask for all desired changes in one reply; accept an edited block or prose and
retain untouched fields. Do not ask four questions. Flag conflicts rather than
silently rewriting fields. Rerun routing and show either the revised local
proposal with Build/Revise or the complex explanation with its choices. Even a
name-only revision is not approval. New live/effect requirements return to the
complex choice; never drop requirements to fit the starter.

## Representative intents

Apply the user's constraints first; the examples do not override rejected mocks.

| Purpose | Outcome |
|---|---|
| Triage Jira items into smaller actionable chunks. | Local proposal from supplied content or mock Jira issues; no plan/mock gate. |
| Show the current weather for a city. | Local proposal with network access to `api.open-meteo.com` (read-only, no key); `Approve network access and build` records the approval. |
| Fetch stock prices from a paid API with our key. | Plan or mock quotes; a host that needs a key or login cannot be approved for the prototype. |
| Answer HR questions from SharePoint. | Local proposal from supplied files or mock policies plus future-adapter plan; required live retrieval uses the complex choice. |
| Read the current Jira backlog through our adapter. | Developer plan or separately proposed mock slice; adapter remains unused. |
| Update Jira, collect approval, and notify Slack. | Plan or mock recommendations; no writes, workflow, or delivery. |
| Monitor Kubernetes and restart/roll back services. | Plan or mock incident/remediation advisor, with reduced scope confirmed. |
| Use SAP/bank APIs to approve, reimburse, and notify. | Plan or mock claims assessment; never approve, pay, or notify. |
| Remediate the real cluster; mocks are useless. | Plan/revise only; no files without later revised scope and Build approval. |

## Prototype content and continuation

Only workspace-relative regular `.md`, `.txt`, `.json`, or `.csv` files are
allowed: no links, hidden paths, `.env`, more than 10 files, files over 1 MiB,
or totals over 5 MiB. In `appsettings.json`, `Content:Sources` exhaustively maps
each exact path to `supplied` or `mock`. Generated content is `mock`; only actual
user-provided files are `supplied`. Content is optional: with **Knowledge:**
none, remove the starter samples and declare no content.

Tools are deterministic over that content, plus read-only requests to approved
hosts through the provided client. They make no other API/database calls and no
effects. For an external-source or reduced mock prototype,
`INTEGRATION_PLAN.md` briefly records the original goal, local slice, sources,
assumptions, unvalidated behavior, and future adapter/tool contracts, following
the MAF guidance in `plan-only.md`. List authentication/permissions, failure
handling, tests, and ordered developer work.
An existing adapter may be assessed later but is not used or validated now.

Preserve requested automation. Mark a safer alternative such as human approval
as a **Recommended changed assumption** and explain its effect; never imply
acceptance. End the file with a copyable coding-agent prompt for separate developer-led
implementation after permissions, scope, and tests are confirmed. Do not run it.

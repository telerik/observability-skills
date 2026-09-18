# Integration plan only

Use this reference after `Show implementation plan`. The MAF guidance below also
applies to adapter and tool contracts in a prototype's `INTEGRATION_PLAN.md`.

## Plan-only response

Create no files and run no copier, build, smoke, app, trace, credential, or
external-system commands. Return a concise chat plan of ordered developer steps
for the full goal: MAF shape; each adapter, auth owner, and permission;
request/response and source-of-truth contracts; effect
approval/idempotency/retry/compensation and failures; local, contract,
integration, and end-to-end tests; deployment, secrets, and observability; and
builder versus developer ownership. Make dependencies and the first step clear.
Preserve requested automation; label safer alternatives as changed assumptions
rather than implying acceptance. Execute nothing and create no
`INTEGRATION_PLAN.md` or other file.

## MAF guidance for plans

- Function tool: on-demand lookup/action after developers supply typed client,
  authentication, contracts, and safety behavior.
- Middleware: validation, authorization, redaction, logging, and errors—not the
  business adapter.
- Context provider: configured history/profile/retrieval injected per run; it
  neither creates nor authorizes a source.
- Runtime Agent Skill: reusable domain instructions/trusted resources, distinct
  from this builder skill and not a connector.
- Workflow: explicit ordering, branching, approvals, checkpoints, or costly
  effects; plan it rather than adding it to this prototype.

MAF coordinates developer-provided components. It does not provision access,
invent contracts/rules, make effects idempotent, create production retrieval,
or turn checkpoints into cross-system transactions.

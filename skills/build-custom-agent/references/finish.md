# Finish a custom agent

Use this reference once every required gate in `SKILL.md` has passed. It covers
the readiness summary, the optional try-and-refine offer, and the final handoff.

## Readiness and the try-and-refine offer

After all required gates pass, verify `/` and `/api/health` at the running app's
URL. Show a brief readiness summary with the project path, clickable UI link
and three passing smoke results before considering this optional phase.

- Explicit `Try-and-refine: on`: run once without asking again.
- Explicit `Try-and-refine: off`: proceed to handoff with
  `Behavior check: skipped (off)`.
- No preference: reuse the working question-tool format from Build/Revise with
  `Finish here` and `Run try-and-refine`. Put the readiness summary (project
  path, clickable UI link and three passing smoke results) and this question
  together in the tool's question body:

  > Would you like me to try four additional scenarios and make one small fix
  > if needed? This makes extra model calls and may take several minutes.

  Without a question tool, present the same context and two choices in chat.
  Keep the app running while awaiting the answer. Only `Run try-and-refine` or an equivalent
  explicit request approves execution. `Finish here`, cancellation, empty input
  or a failed question tool proceeds to handoff with
  `Behavior check: not run (no opt-in)`; never infer approval or ask repeatedly.
  In noninteractive mode, finish without this question or phase unless already
  explicitly requested.

Only after opt-in, read and follow
[the behavior-check procedure](try-and-refine.md); prepare its
scenarios and start its time budget then. If unavailable, report it skipped
without reconstructing it. This preference is not a generated setting, and
declining it does not invalidate a successful basic build.

## Handoff

After all checks, including optional behavior checks, leave the app running
in the persistent session. If it was stopped, relaunch it with the
persistent-start procedure in `SKILL.md`. Immediately before the final response,
verify both `/` and `/api/health` at its current URL, then include a clickable UI
link. Do not stop the app as test cleanup or substitute a launch command for a
live link.

Report success only after copy, both validations, build, all smokes, and health.
Include the absolute project path, verified UI URL, three smoke results, the
approved network hosts or `none`, the behavior-check line above, and exactly one
link: `https://observability.progress.com/observations`. Do not make
per-trace links; state that backend trace ingestion is not independently
verified. For external/mock prototypes, name mock and supplied sources, confirm
no live adapter was used, link `INTEGRATION_PLAN.md`, and distinguish tested
sample decisions from unvalidated production behavior.

Append this note verbatim to the successful local-prototype handoff:

> **Development use only:** This agent is in a local development environment. .NET user-secrets stores credentials unencrypted in a JSON file in your user profile. For production deployment, inject credentials through environment variables backed by your deployment’s secret manager.
> If you used user-secrets or entered credentials directly in terminal commands, I can guide you through removing the saved development credentials and relevant shell-history entries when you finish testing.

Keep the cleanup offer conditional: a successful run does not establish that
user-secrets were used. Do not inspect secret-store contents or shell history
to decide. Perform cleanup only when requested, after testing; explain that
`Progress.AgentBuilder.Mvp` is shared across all five agents before removing
settings. Refer to the copied README for targeted removal and shell-specific
guidance; do not automatically clear the store or unrelated shell history.

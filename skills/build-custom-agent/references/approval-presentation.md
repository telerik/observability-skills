# Present the scope and approval choices

Use the host's available native question tool to display the complete scope
with exactly the two choices from `SKILL.md`. Follow the available tool's
schema; tool names and argument names are host-specific. A local gate contains
the filled-in **Proposed local prototype** block from `scope-routing.md`,
including every field and any scope limit. A complex gate contains that
reference's scope explanation instead of a local proposal.

## VS Code

Use one `vscode_askQuestions` call. In its single `questions` item set:

- `header`: `Build or revise` for a local proposal; `Choose a path` for a complex request
- `question`: `How would you like to continue?`
- `message`: the complete proposal Markdown or complex scope explanation
- `options`: exactly the two applicable labels from `SKILL.md`

The `message` field displays the scope inside the native picker beside the
choices. It is required for these gates. Keep the full scope out of the
length-limited `question` field. Send the actual scope, not an announcement or
a reference to text that has not been displayed. Do not send a separate chat
copy or an announcement before this tool call.

## Other coding tools

Use the equivalent native question tool and its supported body/details or
question field for the complete scope and continuation question, with the
same two choices. For example, use Copilot CLI's `ask_user` with the content and
structured-choice fields exposed by its current schema. Include
`How would you like to continue?` and do not assume VS Code's argument names.

If the tool cannot display the complete scope, send it as a user-visible
assistant message before calling the tool with the short question and choices.
If no question tool exists, present the complete scope and two numbered choices.
Never replace an available native choice tool with only a plain-text question.
Wait for the user's answer; cancellation, empty input, or tool failure selects
nothing. Apply this presentation to initial, simplified, and revised proposals.

// DOM, constants and state
const $ = (id) => document.getElementById(id);
let tickets = [],
  selected = null,
  controller = null,
  revision = 0,
  lastReply = null,
  scenario = {};
const labels = {
  issueType: 'Issue type',
  environment: 'Environment',
  impact: 'Customer impact',
  workaroundAvailable: 'Workaround'
};
const choices = {
  issueType: [
    ['outage', 'Outage'],
    ['defect', 'Defect'],
    ['howto', 'How-to question']
  ],
  environment: [
    ['production', 'Production'],
    ['test', 'Test']
  ],
  impact: [
    ['multiple_customers', 'Multiple customers'],
    ['single_customer', 'Single customer']
  ],
  workaroundAvailable: [
    ['true', 'Available'],
    ['false', 'Not available']
  ]
};
const friendly = (value) =>
  value === null || value === undefined
    ? 'Not provided'
    : value === true || value === 'true'
      ? 'Available'
      : value === false || value === 'false'
        ? 'Not available'
        : String(value).replaceAll('_', ' ');
const sameScenario = (left, right) =>
  Object.keys({ ...left, ...right }).every((key) => left[key] === right[key]);

// Rendering
function element(tag, text, className) {
  const node = document.createElement(tag);
  node.textContent = text;
  if (className) node.className = className;
  return node;
}

function clearQuestionResponse(clearInput = true) {
  if (clearInput) $('question').value = '';
  $('question').placeholder = 'Why does this priority apply?';
  $('question-progress').hidden = true;
  $('question-progress').textContent = '';
  $('question-error').hidden = true;
  $('question-error').textContent = '';
  $('question-response').hidden = true;
  $('answered-question').textContent = '';
  $('question-answer').textContent = '';
  $('question-grounding-copy').textContent = '';
}

function renderInbox() {
  const query = $('filter').value.trim().toLowerCase();
  const visible = tickets.filter((ticket) =>
    (ticket.id + ' ' + ticket.title).toLowerCase().includes(query)
  );
  $('ticket-list').replaceChildren();
  $('count').textContent = String(tickets.length);
  $('inbox-state').hidden = visible.length > 0;
  $('inbox-state').textContent = tickets.length
    ? 'No tickets match your filter.'
    : 'No mock tickets are available.';
  for (const ticket of visible) {
    const item = document.createElement('li');
    const button = element('button', '', 'ticket');
    button.type = 'button';
    button.setAttribute('aria-pressed', String(selected?.id === ticket.id));
    button.append(
      element('span', ticket.id, 'ticket-id'),
      element('span', ticket.title, 'ticket-title'),
      element('span', friendly(ticket.issueType), 'ticket-type')
    );
    button.addEventListener('click', () => selectTicket(ticket));
    item.append(button);
    $('ticket-list').append(item);
  }
}

function selectTicket(ticket, preserveQuestionDraft = false) {
  revision++;
  controller?.abort();
  selected = ticket;
  lastReply = null;
  scenario = {};
  clearQuestionResponse(!preserveQuestionDraft);
  $('error').hidden = true;
  $('no-selection').hidden = true;
  $('selected-ticket').hidden = false;
  $('detail-id').textContent = ticket.id;
  $('detail-title').textContent = ticket.title;
  $('detail-description').textContent = ticket.description;
  $('question-label').textContent = `Ask about ${ticket.id}`;
  $('facts').replaceChildren();
  for (const [key, label] of Object.entries(labels)) {
    const group = document.createElement('div');
    const value = element('dd', friendly(ticket[key]));
    if (ticket[key] === null || ticket[key] === undefined) value.className = 'missing';
    group.append(element('dt', label), value);
    $('facts').append(group);
  }
  $('result-content').hidden = true;
  $('result-placeholder').hidden = false;
  $('result-placeholder').replaceChildren(
    element('strong', 'Evidence first. Recommendation second.'),
    element('span', 'Choose “Suggest triage” to review this ticket against the local policy.')
  );
  $('recommendation-answer').hidden = true;
  $('recommendation-answer').textContent = '';
  $('result').removeAttribute('data-status');
  $('result').setAttribute('aria-busy', 'false');
  setBusy(false);
  $('live-status').textContent = `Selected ${ticket.id}.`;
  renderInbox();
}

function renderQuestionAnswer(reply) {
  const evidenceCount = reply.recommendation.evidence.length,
    policyCount = reply.recommendation.policyRefs.length;
  const grounding = [];
  if (evidenceCount)
    grounding.push(`${evidenceCount} documented ticket ${evidenceCount === 1 ? 'fact' : 'facts'}`);
  if (policyCount)
    grounding.push(`${policyCount} policy ${policyCount === 1 ? 'reference' : 'references'}`);
  $('answered-question').textContent = `You asked: “${reply.question}”`;
  $('question-answer').textContent = reply.answer;
  $('question-grounding-copy').textContent = grounding.length
    ? `Grounded in ${grounding.join(' and ')}.`
    : 'No supporting ticket evidence was returned.';
  $('question-error').hidden = true;
  $('question-response').hidden = false;
  $('question').value = '';
  $('question').placeholder = 'Ask another question about this ticket…';
  $('question-response').scrollIntoView({ block: 'nearest' });
  $('question-response').focus({ preventScroll: true });
}

function renderRecommendation(reply) {
  const decision = reply.recommendation;
  $('result').dataset.status = decision.status;
  $('result-placeholder').hidden = true;
  $('result-content').hidden = false;
  $('result-status').textContent =
    decision.status === 'proposed'
      ? 'SUGGESTION ONLY'
      : decision.status === 'needs_information'
        ? 'NEEDS INFORMATION'
        : 'NOT FOUND';
  $('result-status').className = 'pill ' + (decision.status === 'proposed' ? 'green' : 'amber');
  $('queue').textContent = decision.suggestedQueue ?? 'Not proposed';
  $('priority').textContent = decision.suggestedPriority ?? 'Not proposed';
  $('missing-fields').hidden = !decision.missingFields.length;
  if (reply.question) renderQuestionAnswer(reply);
  else {
    $('recommendation-answer').textContent = reply.answer;
    $('recommendation-answer').hidden = false;
  }
  $('missing-list').replaceChildren(
    ...decision.missingFields.map((field) => element('li', labels[field] ?? field))
  );
  $('evidence').replaceChildren(
    ...decision.evidence.map((fact) => {
      const row = document.createElement('li');
      row.append(
        element('span', `${labels[fact.field] ?? fact.field}: ${friendly(fact.value)}`),
        element('small', fact.source)
      );
      return row;
    })
  );
  if (!decision.evidence.length)
    $('evidence').append(element('li', 'No matching ticket evidence.'));
  $('policy-refs').replaceChildren(
    ...decision.policyRefs.map((source) => element('span', source, 'source'))
  );
  if (!decision.policyRefs.length)
    $('policy-refs').append(element('span', 'No policy decision was made.'));
  $('trace').textContent = `tools: ${reply.toolsUsed.join(', ')}`;
  $('scenario-summary').hidden = !reply.scenario.active;
  $('scenario-values').replaceChildren(
    ...reply.scenario.suppliedFields.map((fact) =>
      element(
        'li',
        `${labels[fact.field]}: bundled ${friendly(selected[fact.field])} → scenario ${friendly(fact.value)} (${fact.source})`
      )
    )
  );
  $('scenario-form').hidden = decision.missingFields.length === 0;
  $('scenario-fields').replaceChildren();
  for (const field of decision.missingFields) {
    const label = element('label', labels[field]);
    const select = document.createElement('select');
    select.id = `scenario-${field}`;
    select.dataset.field = field;
    select.append(element('option', 'Not supplied yet'));
    select.firstChild.value = '';
    for (const [value, text] of choices[field]) {
      const option = element('option', text);
      option.value = value;
      select.append(option);
    }
    label.append(select);
    $('scenario-fields').append(label);
  }
  $('live-status').textContent = reply.question
    ? `Answer ready for ${reply.recommendation.ticketId}.`
    : `Triage complete: ${decision.status.replaceAll('_', ' ')}.`;
}

function setBusy(busy, activity = null) {
  $('result').setAttribute('aria-busy', String(busy));
  $('question-form').setAttribute('aria-busy', String(busy && activity === 'question'));
  for (const id of ['triage', 'ask', 'question', 'reevaluate', 'reset-scenario'])
    $(id).disabled = busy;
  $('scenario-fields')
    .querySelectorAll('select')
    .forEach((select) => (select.disabled = busy));
  $('triage').textContent = busy && activity !== 'question' ? 'Reviewing…' : 'Suggest triage ↗';
  $('ask').textContent = busy && activity === 'question' ? 'Answering…' : 'Ask';
  $('question-progress').hidden = !(busy && activity === 'question');
}

// Requests
async function load() {
  try {
    const response = await fetch('/api/tickets');
    if (!response.ok)
      throw new Error('The mock inbox could not load. Reload the page to try again.');
    const result = await response.json();
    tickets = result.tickets;
    renderInbox();
    if (tickets.length) selectTicket(tickets[0]);
  } catch {
    $('count').textContent = 'Unavailable';
    $('inbox-state').textContent = 'The mock inbox could not load. Reload the page to try again.';
  }
  try {
    const response = await fetch('/api/health');
    if (!response.ok) throw new Error();
    const health = await response.json();
    $('telemetry').textContent = health.tracingEnabled
      ? 'Tracing enabled'
      : 'Tracing not configured';
  } catch {
    $('telemetry').textContent = 'Trace setup unavailable';
  }
}

async function review(question = null, requestedScenario = scenario) {
  if (!selected) return;
  const answering = question !== null;
  const ownRevision = ++revision;
  controller?.abort();
  const requestController = new AbortController();
  controller = requestController;
  const timeout = setTimeout(() => requestController.abort(), 48000);
  $('error').hidden = true;
  $('question-error').hidden = true;
  if (answering) {
    $('question-response').hidden = true;
    $('question-progress').textContent = `Finding an answer to “${question}”…`;
  } else {
    $('result-placeholder').hidden = false;
    $('result-placeholder').textContent = lastReply
      ? 'Reviewing… The previous result remains below until this request completes.'
      : 'Reading the ticket and checking the triage policy…';
  }
  setBusy(true, answering ? 'question' : 'triage');
  $('live-status').textContent = answering
    ? `Finding an answer about ${selected.id}.`
    : 'Reviewing the selected mock ticket.';
  try {
    const response = await fetch('/api/triage', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ticketId: selected.id, question, scenario: requestedScenario }),
      signal: requestController.signal
    });
    const reply = await response.json();
    if (ownRevision !== revision) return;
    if (!response.ok) {
      const allowed = [
        'agent_run_failed',
        'grounded_recommendation_required',
        'answer_too_long',
        'tool_call_limit_exceeded',
        'ticket_outside_selected_scope',
        'question_too_long',
        'scenario_requires_known_ticket',
        'scenario_fields_invalid',
        'scenario_field_or_value_invalid',
        'scenario_only_missing_fields_allowed'
      ];
      const reason = allowed.includes(reply.error) ? reply.error : 'request_failed';
      const task = answering ? 'answer' : 'review';
      throw new Error(
        reply.error === 'agent_deadline_exceeded'
          ? `The ${task} timed out. Please try again.`
          : `The ${task} could not complete (${reason}). No ticket was changed.`
      );
    }
    const scenarioChanged = !sameScenario(scenario, requestedScenario);
    scenario = { ...requestedScenario };
    if (scenarioChanged) clearQuestionResponse(false);
    lastReply = reply;
    renderRecommendation(reply);
  } catch (error) {
    if (ownRevision !== revision) return;
    const message =
      error.name === 'AbortError'
        ? `The ${answering ? 'answer' : 'review'} timed out. No ticket was changed. Please try again.`
        : error.message;
    if (answering) {
      $('question-error').textContent = message;
      $('question-error').hidden = false;
      $('live-status').textContent = 'The question could not be answered. No ticket was changed.';
    } else {
      $('error').textContent = message;
      $('error').hidden = false;
      $('result-placeholder').hidden = !!lastReply;
      if (!lastReply)
        $('result-placeholder').textContent =
          'No recommendation is available. You can retry the review.';
      $('live-status').textContent = 'Review failed. No ticket was changed.';
    }
  } finally {
    clearTimeout(timeout);
    if (ownRevision === revision) setBusy(false);
  }
}

// Event handlers
$('filter').addEventListener('input', renderInbox);
$('triage').addEventListener('click', () => review());
$('question-form').addEventListener('submit', (event) => {
  event.preventDefault();
  const question = $('question').value.trim();
  if (question) review(question);
});
$('scenario-form').addEventListener('submit', (event) => {
  event.preventDefault();
  const proposed = { ...scenario };
  let added = false;
  $('scenario-fields')
    .querySelectorAll('select')
    .forEach((select) => {
      if (select.value) {
        proposed[select.dataset.field] =
          select.dataset.field === 'workaroundAvailable' ? select.value === 'true' : select.value;
        added = true;
      }
    });
  if (!added) {
    $('error').textContent =
      'Choose at least one missing intake value to try a temporary scenario.';
    $('error').hidden = false;
    return;
  }
  review(null, proposed);
});
$('reset-scenario').addEventListener('click', () => {
  const ticket = selected;
  selectTicket(ticket, true);
  review();
});

// Startup
load();

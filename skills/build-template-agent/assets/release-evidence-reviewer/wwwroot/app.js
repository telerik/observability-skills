// DOM, constants and state
const conversation = document.querySelector('#conversation');
const input = document.querySelector('#message');
const send = document.querySelector('#send');
const initialConversation = conversation.innerHTML;
let reviewContext = null,
  revision = 0,
  controller = null,
  busy = false;

// Rendering
function updateContext() {
  document.querySelector('#review-scope').textContent = reviewContext?.project
    ? `Reviewing ${reviewContext.project}`
    : reviewContext
      ? 'Policy discussion · no project selected'
      : 'New review · no project selected';
  document
    .querySelectorAll('[data-followup]')
    .forEach((button) => (button.hidden = !reviewContext?.project));
  input.placeholder = reviewContext?.project
    ? `Ask a follow-up about ${reviewContext.project}…`
    : 'Ask about release evidence…';
}

function setBusy(value) {
  busy = value;
  send.disabled = value;
  input.disabled = value;
  send.textContent = value ? 'Reviewing…' : 'Send';
  conversation.setAttribute('aria-busy', String(value));
  document.querySelectorAll('[data-prompt]').forEach((button) => (button.disabled = value));
}

function addMessage(kind, text, project, toolsUsed, evidence) {
  document.querySelector('#empty')?.remove();
  const message = document.createElement('div');
  message.className = `message ${kind}`;
  const body = document.createElement('div');
  body.textContent = text;
  message.append(body);
  if (evidence && (evidence.checks || []).length) {
    const panel = document.createElement('div');
    panel.className = 'evidence';
    const heading = document.createElement('h4');
    heading.textContent = `Documented evidence · ${evidence.project} · ${evidence.status}`;
    panel.append(heading);
    for (const check of evidence.checks) {
      const row = document.createElement('div');
      row.className = 'check';
      const mark = document.createElement('span');
      mark.className = `mark ${check.satisfied ? 'met' : 'unmet'}`;
      mark.textContent = check.satisfied ? '\u2713' : '\u2717';
      const label = document.createElement('span');
      label.textContent = check.requirement;
      const value = document.createElement('span');
      value.className = 'value';
      value.textContent = check.documentedValue || 'not documented';
      const src = document.createElement('span');
      src.className = 'src';
      src.textContent = check.sourceId;
      row.append(mark, label, value, src);
      panel.append(row);
    }
    message.append(panel);
  }
  const ran = (toolsUsed || []).join(', ');
  if (project || ran) {
    const trace = document.createElement('div');
    trace.className = 'trace';
    trace.textContent = [project ? `project: ${project}` : '', ran ? `tools: ${ran}` : '']
      .filter(Boolean)
      .join(' · ');
    message.append(trace);
  }
  conversation.append(message);
  message.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

// Requests
async function checkHealth() {
  try {
    const response = await fetch('/api/health');
    if (!response.ok) throw new Error();
    const health = await response.json();
    document.querySelector('#status-dot').classList.add('ready');
    document.querySelector('#status-text').textContent =
      `${health.status} · ${health.documentsLoaded} evidence documents · tracing ${health.tracingEnabled ? 'on' : 'off'}`;
  } catch {
    document.querySelector('#status-dot').classList.add('error');
    document.querySelector('#status-text').textContent = 'Local agent unavailable';
  }
}

async function submitReview(event) {
  event.preventDefault();
  const text = input.value.trim();
  if (!text || busy) return;
  const ownRevision = ++revision;
  const submittedContext = reviewContext;
  const requestController = new AbortController();
  controller = requestController;
  const timeout = setTimeout(() => requestController.abort(), 48000);
  addMessage('user', text);
  input.value = '';
  setBusy(true);
  try {
    const response = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ message: text, context: submittedContext }),
      signal: requestController.signal
    });
    const result = await response.json();
    if (ownRevision !== revision) return;
    addMessage(
      'agent',
      response.ok ? result.answer : `Request failed: ${result.error}`,
      result.project,
      result.toolsUsed,
      result.evidence
    );
    if (response.ok) {
      reviewContext = { project: result.project, question: text, answer: result.answer };
      updateContext();
    } else input.value = text;
  } catch (error) {
    if (ownRevision !== revision) return;
    addMessage(
      'agent',
      error.name === 'AbortError'
        ? 'The review timed out. You can retry; no release action was taken.'
        : 'Could not reach the local agent.'
    );
    input.value = text;
  } finally {
    clearTimeout(timeout);
    if (ownRevision === revision) {
      controller = null;
      setBusy(false);
      input.focus();
    }
  }
}

// Event handlers
document.querySelector('#new-review').addEventListener('click', () => {
  revision++;
  controller?.abort();
  controller = null;
  reviewContext = null;
  conversation.innerHTML = initialConversation;
  input.value = '';
  setBusy(false);
  updateContext();
  input.focus();
});
document.querySelector('#chat-form').addEventListener('submit', submitReview);
document.querySelectorAll('[data-prompt]').forEach((button) => {
  button.addEventListener('click', () => {
    input.value = button.dataset.prompt;
    input.focus();
  });
});

// Startup
checkHealth();

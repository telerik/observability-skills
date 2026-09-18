// DOM, constants and state
const $ = (selector) => document.querySelector(selector);
let sections = [];
let busy = false;
let preview = null,
  lastTurn = null,
  followUp = false,
  sourceId = null;
let requestVersion = 0,
  controller = null;

// Rendering
function showSource(section) {
  preview = section;
  $('#scope-source').hidden = false;
  $('#source-document').textContent = section.title;
  $('#source-heading').textContent = section.heading;
  $('#source-excerpt').textContent = section.excerpt;
  $('#source-id').textContent = section.sourceId;
  document
    .querySelectorAll('.section-button')
    .forEach((button) =>
      button.setAttribute('aria-pressed', String(button.dataset.sourceId === section.sourceId))
    );
}

function sourceButton(section, className) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = className;
  button.disabled = busy;
  button.textContent = section.heading;
  button.dataset.sourceId = section.sourceId;
  button.setAttribute('aria-label', `Read ${section.title}: ${section.heading}`);
  if (className === 'section-button') button.setAttribute('aria-pressed', 'false');
  button.addEventListener('click', () => {
    showSource(section);
    if (window.innerWidth < 680) $('#source-panel').scrollIntoView({ block: 'nearest' });
  });
  return button;
}

function updateContext() {
  $('#follow-up-context').hidden = !followUp;
  $('#follow-up-label').textContent = followUp ? `Following up on: ${lastTurn.question}` : '';
  $('#scope-context').hidden = !sourceId;
  $('#scope-label').textContent = sourceId
    ? `Only this section: ${sections.find((section) => section.sourceId === sourceId)?.heading || sourceId}`
    : '';
  $('#ask').textContent = followUp ? 'Ask follow-up →' : 'Find an answer →';
}

function setBusy(value) {
  busy = value;
  document
    .querySelectorAll(
      '#ask, #question, #follow-up, #clear-follow-up, #scope-source, #clear-scope, [data-question], .section-button, .citation'
    )
    .forEach((element) => (element.disabled = value));
  $('#answer-panel').setAttribute('aria-busy', String(value));
  $('#progress').hidden = !value;
}

function newQuestion() {
  requestVersion++;
  controller?.abort();
  controller = null;
  setBusy(false);
  lastTurn = null;
  followUp = false;
  sourceId = null;
  preview = null;
  updateContext();
  $('#question').value = '';
  $('#result').hidden = true;
  $('#error').hidden = true;
  $('#empty').hidden = false;
  $('#answer').textContent = '';
  $('#submitted-question').textContent = '';
  $('#citations').replaceChildren();
  $('#trace').textContent = '';
  $('#answer-status').textContent = 'Ready to explore';
  $('#scope-source').hidden = true;
  $('#source-document').textContent = 'Select a source';
  $('#source-heading').textContent = 'Look a little closer';
  $('#source-excerpt').textContent =
    "Choose a section from the library or an answer's source buttons to read the exact bundled excerpt.";
  $('#source-id').textContent = '';
  document
    .querySelectorAll('.section-button')
    .forEach((button) => button.setAttribute('aria-pressed', 'false'));
  $('#question').focus();
}

// Requests
async function loadLibrary() {
  try {
    const [healthResponse, docsResponse] = await Promise.all([
      fetch('/api/health'),
      fetch('/api/documents')
    ]);
    if (!healthResponse.ok || !docsResponse.ok) throw new Error();
    const health = await healthResponse.json();
    sections = await docsResponse.json();
    $('#health-dot').className = `dot ${health.status === 'ready' ? 'ready' : 'degraded'}`;
    $('#health-text').textContent =
      `${health.status} · ${health.documentsLoaded} documents · ${health.sectionsLoaded} sections · tracing ${health.tracingEnabled ? 'on' : 'off'}`;
    $('#library-count').textContent = `${health.documentsLoaded} DOCS`;
    $('#library').replaceChildren();
    const groups = new Map();
    sections.forEach((section) => {
      if (!groups.has(section.documentId)) {
        const details = document.createElement('details');
        details.open = true;
        const summary = document.createElement('summary');
        summary.textContent = section.title;
        details.append(summary);
        groups.set(section.documentId, details);
        $('#library').append(details);
      }
      groups.get(section.documentId).append(sourceButton(section, 'section-button'));
    });
    if (!sections.length)
      $('#library').textContent = 'No bundled sections were found. Check the docs folder.';
  } catch {
    $('#health-dot').className = 'dot error';
    $('#health-text').textContent = 'Local document service unavailable';
    $('#library').textContent =
      'Could not load the library. Refresh when the local app is running.';
  }
}

async function submitQuestion(event) {
  event.preventDefault();
  const question = $('#question').value.trim();
  if (!question || busy) return;
  const request = { question, previousTurn: followUp ? { ...lastTurn } : null, sourceId };
  const scope = sections.find((section) => section.sourceId === sourceId);
  const version = ++requestVersion;
  controller = new AbortController();
  setBusy(true);
  $('#empty').hidden = true;
  $('#error').hidden = true;
  $('#result').hidden = true;
  $('#progress').hidden = false;
  $('#answer-status').textContent = 'Reading';
  try {
    const response = await fetch('/api/ask', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(request),
      signal: AbortSignal.any([controller.signal, AbortSignal.timeout(50000)])
    });
    const result = await response.json();
    if (version !== requestVersion) return;
    if (!response.ok) {
      $('#error').textContent =
        `Could not complete this lookup: ${result.error || 'request_failed'}.`;
      $('#error').hidden = false;
      $('#answer-status').textContent = 'Try again';
      return;
    }
    $('#submitted-question').textContent =
      `Answering: ${question}${request.previousTurn ? `\nFollow-up to: ${request.previousTurn.question}` : ''}\nScope: ${scope ? `${scope.title} · ${scope.heading}` : 'all bundled documents'}`;
    $('#answer').textContent = result.answer;
    $('#result').hidden = false;
    lastTurn = { question, answer: result.answer };
    followUp = false;
    updateContext();
    $('#answer-status').textContent =
      result.status === 'not_found'
        ? 'No matching information'
        : `${result.citations.length} source${result.citations.length === 1 ? '' : 's'} read`;
    $('#citations').replaceChildren(
      ...result.citations.map((section) => sourceButton(section, 'citation'))
    );
    $('#citations').hidden = result.citations.length === 0;
    const ran = (result.toolCalls || []).join(', ');
    $('#trace').textContent = ran ? `tools: ${ran}` : '';
    if (result.citations.length) showSource(result.citations[0]);
  } catch {
    if (version !== requestVersion) return;
    $('#error').textContent =
      'The local service did not respond in time. Check that it is running and try again.';
    $('#error').hidden = false;
    $('#answer-status').textContent = 'Service unavailable';
  } finally {
    if (version === requestVersion) {
      controller = null;
      setBusy(false);
      $('#question').focus();
    }
  }
}

// Event handlers
$('#ask-form').addEventListener('submit', submitQuestion);
$('#new-question').addEventListener('click', newQuestion);
$('#follow-up').addEventListener('click', () => {
  if (busy || !lastTurn) return;
  followUp = true;
  updateContext();
  $('#question').value = '';
  $('#question').focus();
});
$('#clear-follow-up').addEventListener('click', () => {
  followUp = false;
  updateContext();
});
$('#scope-source').addEventListener('click', () => {
  if (busy || !preview) return;
  sourceId = preview.sourceId;
  followUp = false;
  updateContext();
  $('#question').value = '';
  $('#question').focus();
});
$('#clear-scope').addEventListener('click', () => {
  sourceId = null;
  updateContext();
});
document.querySelectorAll('[data-question]').forEach((button) =>
  button.addEventListener('click', () => {
    newQuestion();
    $('#question').value = button.dataset.question;
    $('#question').focus();
  })
);

// Startup
loadLibrary();

// DOM, constants and state
const conversation = document.querySelector('#conversation');
const input = document.querySelector('#message');
const send = document.querySelector('#send');
const newChat = document.querySelector('#new-chat');
const emptyState = document.querySelector('#empty').cloneNode(true);
const history = [];
let historyLimits = { maxTurns: 6, maxCharacters: 24000 };
let historyTrimmed = false;
const supportedPresets = new Set(['knowledge', 'review', 'workflow', 'analysis']);

// Rendering and conversation history
function addMessage(kind, text) {
  document.querySelector('#empty')?.remove();
  const message = document.createElement('div');
  message.className = `message ${kind}`;
  const body = document.createElement('div');
  body.textContent = text;
  message.append(body);
  conversation.append(message);
  message.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  return message;
}

function updateHistoryNote() {
  document.querySelector('#history-note').textContent = historyTrimmed
    ? 'Older exchanges are no longer sent. Start a new chat to clear the context.'
    : `Remembers up to ${historyLimits.maxTurns} recent exchanges in this tab.`;
}

function remember(user, assistant) {
  history.push({ user, assistant });
  // Drop whole exchanges, never a detached answer or half a user's message.
  while (
    history.length > historyLimits.maxTurns ||
    history.reduce((size, turn) => size + turn.user.length + turn.assistant.length, 0) >
      historyLimits.maxCharacters
  ) {
    history.shift();
    historyTrimmed = true;
  }
  updateHistoryNote();
}

// Requests
async function loadConfig() {
  try {
    const response = await fetch('/api/config');
    if (!response.ok) throw new Error();
    const config = await response.json();
    historyLimits = config.chatHistory;
    updateHistoryNote();
    const preset = supportedPresets.has(config.uiPreset) ? config.uiPreset : 'knowledge';
    document.body.dataset.preset = preset;
    document.querySelector('#agent-icon').setAttribute('href', `#icon-${preset}`);
    document.title = config.displayName;
    document.querySelector('#agent-name').textContent = config.displayName;
    document.querySelector('#agent-purpose').textContent = config.purpose;
    document.querySelector('#chat-panel').setAttribute('aria-label', `${config.displayName} chat`);
    input.placeholder = config.inputPlaceholder;
    const examples = document.querySelector('#examples');
    config.examples.forEach((prompt) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = prompt;
      button.addEventListener('click', () => usePrompt(prompt));
      examples.append(button);
    });
  } catch {
    document.querySelector('#agent-purpose').textContent = 'Could not load the configured purpose.';
  }
}

async function checkHealth() {
  const dot = document.querySelector('#status-dot');
  try {
    const response = await fetch('/api/health');
    if (!response.ok) throw new Error();
    const health = await response.json();
    dot.classList.add('ready');
    document.querySelector('#status-text').textContent =
      `${health.status} · ${health.sourcesLoaded} local sources · tracing ${health.tracingEnabled ? 'on' : 'off'}`;
  } catch {
    dot.classList.add('error');
    document.querySelector('#status-text').textContent = 'Local agent unavailable';
  }
}

async function submitMessage(event) {
  event.preventDefault();
  const message = input.value.trim();
  if (!message || send.disabled) return;
  addMessage('user', message);
  input.value = '';
  send.disabled = true;
  newChat.disabled = true;
  const pending = addMessage('agent pending', 'Working on your answer…');
  try {
    const response = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ message, history })
    });
    const result = await response.json();
    if (response.ok) remember(message, result.answer);
    pending.remove();
    addMessage('agent', response.ok ? result.answer : `Request failed: ${result.error}`);
  } catch {
    pending.remove();
    addMessage('agent', 'Could not reach the local agent.');
  } finally {
    send.disabled = false;
    newChat.disabled = false;
    input.focus();
  }
}

// Event handlers
newChat.addEventListener('click', () => {
  if (send.disabled) return;
  history.length = 0;
  historyTrimmed = false;
  conversation.replaceChildren(emptyState.cloneNode(true));
  input.value = '';
  updateHistoryNote();
  input.focus();
});

function usePrompt(prompt) {
  input.value = prompt;
  input.focus();
}
document.querySelector('#chat-form').addEventListener('submit', submitMessage);

// Startup
loadConfig();
checkHealth();

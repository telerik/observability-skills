// DOM, constants and state
const $ = (id) => document.getElementById(id);
const fmt = (value, digits = 0) =>
  value == null
    ? '—'
    : new Intl.NumberFormat('en-US', { maximumFractionDigits: digits }).format(value);
const plain = (value) => value.replace(/\*\*/g, '').replace(/`/g, '');
const labels = {
  requests: 'Requests',
  errors: 'Errors',
  errorRatePercent: 'Error rate',
  averageResponseMs: 'Average response time'
};
// Percentages and percentage-point differences keep two decimals; milliseconds one; counts none.
const precision = { '%': 2, pp: 2, ms: 1 };
const amount = (value, unit) =>
  value == null
    ? '—'
    : fmt(value, precision[unit] ?? 0) + (unit === '%' ? '%' : unit ? ' ' + unit : '');
let current = null,
  view = null,
  chartData = null,
  highlights = [],
  lastQuestion = null,
  visible = 30,
  version = 0,
  activeRequest = null;
const evidencePanel = document.createElement('details'),
  evidenceTitle = document.createElement('summary'),
  evidenceList = document.createElement('ul');
evidencePanel.className = 'method';
evidencePanel.hidden = true;
evidenceTitle.textContent = 'Tool evidence';
evidencePanel.append(evidenceTitle, evidenceList);
$('trace').before(evidencePanel);

// Rendering
function describe(value) {
  return (
    (value.service === 'all' ? 'All services' : value.service) +
    ' · ' +
    value.start +
    ' to ' +
    value.end +
    ' · ' +
    labels[value.metric] +
    ' · ' +
    value.grouping
  );
}

function message(text, error = false) {
  $('status').textContent = text;
  $('status').classList.toggle('error', error);
}

function busy(value) {
  $('ask').disabled = value || !view;
  $('question').disabled = value;
  $('cancel').hidden = !value;
  $('ask-form').setAttribute('aria-busy', String(value));
  $('kpis').setAttribute('aria-busy', String(value));
  document
    .querySelectorAll(
      '#kpis button,[data-question],#filters input,#filters select,#apply,#grouping'
    )
    .forEach((control) => (control.disabled = value));
}

function clearNotes(text) {
  $('answer').textContent = text;
  $('submitted-question').textContent = '';
  $('trace').textContent = '';
  $('analyst-title').textContent = 'What would you like to explore?';
  renderEvidence([]);
}

function renderEvidence(items) {
  evidencePanel.hidden = !items.length;
  evidenceList.replaceChildren();
  const summary = (value) =>
    value
      ? fmt(value.requests) +
        ' requests · ' +
        fmt(value.errors) +
        ' errors · ' +
        fmt(value.errorRatePercent, 4) +
        (value.errorRatePercent == null ? '' : '%') +
        ' · ' +
        fmt(value.averageResponseMs, 2) +
        (value.averageResponseMs == null ? '' : ' ms')
      : 'No matching data';
  for (const item of items) {
    const row = document.createElement('li'),
      result = item.result,
      scope = result.view;
    row.textContent = item.tool + (scope ? ' · ' + describe(scope) : '');
    if (!scope) {
      row.textContent += ' · ' + (result.message || result.status);
      evidenceList.append(row);
      continue;
    }
    const totals = document.createElement('p');
    totals.textContent = result.dataScope || summary(result.summary?.summary);
    row.append(totals);
    if (result.comparison) {
      const periods = document.createElement('p');
      periods.textContent =
        'Before: ' +
        summary(result.comparison.before) +
        '. After: ' +
        summary(result.comparison.after);
      row.append(periods);
    }
    if (result.summary?.busiestDays?.length) {
      const peak = document.createElement('p');
      peak.textContent =
        'Busiest: ' +
        result.summary.busiestDays.join(', ') +
        ' · ' +
        fmt(result.summary.maxDailyRequests) +
        ' requests';
      row.append(peak);
    }
    if (item.modelResult) {
      const details = document.createElement('details'),
        label = document.createElement('summary'),
        payload = document.createElement('pre');
      label.textContent = 'Data sent to the analyst';
      payload.textContent = JSON.stringify(item.modelResult, null, 2);
      details.append(label, payload);
      row.append(details);
    }
    evidenceList.append(row);
  }
}

function cell(row, value) {
  const td = document.createElement('td');
  td.textContent = value;
  row.append(td);
  return td;
}

function renderRows() {
  const body = $('rows');
  body.replaceChildren();
  const rows = current?.rows || [];
  if (!rows.length) {
    const tr = document.createElement('tr');
    tr.className = 'empty-row';
    cell(tr, 'No source rows match this selection.').colSpan = 6;
    body.append(tr);
  }
  for (const item of rows.slice(0, visible)) {
    const tr = document.createElement('tr');
    cell(tr, item.date);
    const service = cell(tr, item.service);
    if (current.outliers.some((flag) => flag.date === item.date && flag.service === item.service)) {
      const badge = document.createElement('span');
      badge.className = 'flag';
      badge.textContent = 'Spike';
      service.append(badge);
    }
    cell(tr, fmt(item.requests));
    cell(tr, fmt(item.errors));
    cell(tr, item.requests ? fmt((item.errors / item.requests) * 100, 2) + '%' : '—');
    cell(tr, item.requests ? fmt(item.totalResponseMs / item.requests, 1) : '—');
    body.append(tr);
  }
  $('shown').textContent =
    'Showing ' + Math.min(visible, rows.length) + ' of ' + rows.length + ' rows';
  $('row-count').textContent = rows.length + ' rows';
  $('source-count').textContent = '· ' + rows.length + ' records';
  $('more').hidden = visible >= rows.length;
}

function renderHighlights() {
  const box = $('kpis');
  box.replaceChildren();
  // Captured with the rendered series, so a tile can never act on a newer selection.
  const selected = view ? { ...view } : null,
    points = chartData?.points || [];
  for (const item of highlights) {
    const index = item.focus == null ? -1 : points.findIndex((point) => point.label === item.focus);
    const target = selected && index >= 0 ? drillTarget(selected, points[index], index) : null;
    // Only tiles the agent tool actually returns can be explored; the rest stay a read-out.
    const explorable = !!selected && item.explorable && (item.focus == null || target !== null);
    const tile = document.createElement(explorable ? 'button' : 'div');
    tile.className = 'kpi';
    const label = document.createElement('span');
    label.className = 'kpi-label';
    label.textContent = item.label;
    const value = document.createElement('span');
    value.className = 'kpi-value';
    value.textContent = amount(item.value, item.unit);
    const caption = document.createElement('small');
    caption.textContent = item.caption;
    tile.append(label, value, caption);
    if (explorable) {
      tile.type = 'button';
      tile.setAttribute(
        'aria-label',
        'Explore ' +
          item.label.toLowerCase() +
          ': ' +
          amount(item.value, item.unit) +
          ', ' +
          item.caption
      );
      tile.addEventListener('click', () =>
        target
          ? explore(target.view, target.question)
          : ask(selected, 'Explain the ' + item.label.toLowerCase() + ' in the current view.')
      );
    }
    box.append(tile);
  }
}

function svgNode(name, attributes, text) {
  const node = document.createElementNS('http://www.w3.org/2000/svg', name);
  for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, String(value));
  if (text !== undefined) node.textContent = text;
  return node;
}

function renderChart() {
  const box = $('chart');
  box.replaceChildren();
  const points = chartData?.points || [],
    values = points.filter((point) => point.value != null);
  if (!values.length) {
    const empty = document.createElement('p');
    empty.className = 'chart-empty';
    empty.textContent = 'No defined chart values for this selection.';
    box.append(empty);
    return;
  }
  const unit = chartData.unit,
    display = (value) =>
      fmt(value, 2) + (value == null ? '' : unit === '%' ? '%' : unit ? ' ' + unit : '');
  const svg = svgNode('svg', {
    viewBox: '0 0 640 245',
    role: 'group',
    'aria-labelledby': 'trend-title trend-desc'
  });
  svg.append(
    svgNode('title', { id: 'trend-title' }, chartData.title),
    svgNode(
      'desc',
      { id: 'trend-desc' },
      points.map((point) => point.label + ': ' + display(point.value)).join('; ') +
        '. Values are computed from local CSV rows.'
    )
  );
  const maximum = Math.max(1, ...values.map((point) => point.value)) * 1.16,
    y = (value) => 190 - (value / maximum) * 155;
  const axis = (value) =>
    new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(
      value
    ) + (unit === '%' ? '%' : '');
  for (let i = 0; i <= 4; i++) {
    const value = (maximum * i) / 4;
    svg.append(
      svgNode('line', {
        x1: 51,
        y1: y(value),
        x2: 609,
        y2: y(value),
        stroke: '#e7eeea',
        'stroke-dasharray': i ? '3 4' : '0'
      }),
      svgNode('text', { x: 44, y: y(value) + 4, 'text-anchor': 'end' }, axis(value))
    );
  }
  if (chartData.kind === 'line') {
    const x = (index) => 53 + (points.length === 1 ? 273 : (index * 550) / (points.length - 1));
    let path = '',
      connected = false;
    points.forEach((point, index) => {
      if (point.value == null) {
        connected = false;
        return;
      }
      path += (connected ? ' L' : ' M') + x(index) + ' ' + y(point.value);
      connected = true;
    });
    svg.append(
      svgNode('path', {
        d: path,
        fill: 'none',
        stroke: '#067e74',
        'stroke-width': 2.5,
        'stroke-linecap': 'round',
        'stroke-linejoin': 'round'
      })
    );
    points.forEach((point, index) => {
      if (point.value == null) return;
      const dot = svgNode('circle', {
        cx: x(index),
        cy: y(point.value),
        r: points.length < 8 ? 4 : 2.5,
        fill: '#067e74'
      });
      dot.append(svgNode('title', {}, point.label + ': ' + display(point.value)));
      const hit = svgNode('circle', {
        cx: x(index),
        cy: y(point.value),
        r: 10,
        fill: 'transparent'
      });
      makeDrilldown(hit, point, index, display);
      svg.append(dot, hit);
    });
    for (const index of new Set([0, Math.floor((points.length - 1) / 2), points.length - 1]))
      svg.append(
        svgNode(
          'text',
          { x: x(index), y: 216, 'text-anchor': 'middle' },
          points[index].label.slice(5)
        )
      );
  } else {
    const slot = 550 / points.length,
      width = Math.min(110, slot * 0.55);
    points.forEach((point, index) => {
      const x = 53 + slot * (index + 0.5),
        height = point.value == null ? 0 : 190 - y(point.value);
      const bar = svgNode('rect', {
        x: x - width / 2,
        y: 190 - height,
        width,
        height: Math.max(3, height),
        rx: 5,
        fill: index % 2 ? '#6bb7a4' : '#067e74'
      });
      bar.append(svgNode('title', {}, point.label + ': ' + display(point.value)));
      makeDrilldown(bar, point, index, display);
      svg.append(
        bar,
        svgNode(
          'text',
          { x, y: Math.max(20, 180 - height), 'text-anchor': 'middle' },
          display(point.value)
        ),
        svgNode(
          'text',
          { x, y: 216, 'text-anchor': 'middle' },
          view.grouping === 'period' ? (index ? 'After' : 'Before') : point.label
        )
      );
    });
  }
  box.append(svg);
}
// Same selection a chart click produces, shared with the metric tiles that name one point.

function drillTarget(selected, point, index) {
  if (point.value == null) return null;
  let next = { ...selected, grouping: 'day', comparison: null };
  if (selected.grouping === 'service') next.service = point.label;
  else if (selected.grouping === 'day') {
    next.start = point.label;
    next.end = point.label;
    next.grouping = selected.service === 'all' ? 'service' : 'day';
  } else {
    next.start = index ? selected.comparison.afterStart : selected.comparison.beforeStart;
    next.end = index ? selected.comparison.afterEnd : selected.comparison.beforeEnd;
  }
  return {
    view: next,
    question:
      'Show ' +
      labels[next.metric].toLowerCase() +
      ' for ' +
      (next.service === 'all' ? 'all services' : next.service) +
      ' from ' +
      next.start +
      ' to ' +
      next.end +
      (next.grouping === 'service' ? ' by service.' : '.')
  };
}

function makeDrilldown(node, point, index, display) {
  if (point.value == null) return;
  node.setAttribute('role', 'button');
  node.setAttribute('tabindex', '0');
  node.setAttribute('class', 'chart-point');
  node.setAttribute('aria-label', 'Explore ' + point.label + ': ' + display(point.value));
  // Capture the rendered scope; every chart action uses the same real agent as typed questions.
  const target = drillTarget({ ...view }, point, index);
  const drill = () => explore(target.view, target.question);
  node.addEventListener('click', drill);
  node.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      drill();
    }
  });
}

function render() {
  const summary = current?.summary;
  $('chart-title').textContent = chartData?.title || 'Metrics';
  $('chart-subtitle').textContent = view
    ? (view.service === 'all' ? 'All services' : view.service) +
      ' · ' +
      view.start +
      ' → ' +
      view.end
    : '';
  $('chart-foot').textContent = view?.comparison
    ? 'Scope totals and rows cover ' +
      view.start +
      ' to ' +
      view.end +
      '. Bars compare ' +
      view.comparison.beforeStart +
      '–' +
      view.comparison.beforeEnd +
      ' with ' +
      view.comparison.afterStart +
      '–' +
      view.comparison.afterEnd +
      '.'
    : 'Click a point, bar or metric tile to explore it with the analyst.';
  $('view-summary').textContent = view
    ? 'Current view: ' + describe(view)
    : 'Preparing the current view…';
  if (view) {
    $('service').value = view.service;
    $('metric').value = view.metric;
    $('start').value = view.start;
    $('end').value = view.end;
    $('grouping').options[2].disabled = !view.comparison;
    $('grouping').value = view.grouping;
  }
  renderHighlights();
  renderChart();
  renderRows();
  const findings = $('findings');
  findings.replaceChildren();
  const flags = current?.outliers || [];
  const notes = !summary
    ? ['No matching data. Try August 1–30, 2026.']
    : !flags.length
      ? [
          'No unusual values under this rule. A short selection may not provide enough baseline days.'
        ]
      : flags.map(
          (item) =>
            item.service +
            ' · ' +
            item.date +
            ': ' +
            (item.metric === 'error_rate_percent'
              ? 'error rate ' + fmt(item.value, 2) + '%'
              : 'response time ' + fmt(item.value, 1) + ' ms') +
            ' vs. median ' +
            fmt(item.baseline, 2) +
            (item.metric === 'error_rate_percent' ? '%' : ' ms') +
            '.'
        );
  for (const text of notes) {
    const item = document.createElement('li');
    item.textContent = text;
    findings.append(item);
  }
}

function applyData(data) {
  current = data.dashboard;
  view = data.view;
  chartData = data.chart;
  highlights = data.highlights || [];
  visible = 30;
  if ($('service').options.length === 1)
    for (const service of current.services) {
      const option = document.createElement('option');
      option.value = service;
      option.textContent = service[0].toUpperCase() + service.slice(1);
      $('service').append(option);
    }
  render();
}

// Requests
async function loadView(next, reset = false) {
  const id = ++version;
  activeRequest?.abort();
  activeRequest = new AbortController();
  busy(true);
  if (reset) {
    lastQuestion = null;
    $('question').value = '';
  }
  clearNotes('Ask a question or choose a metric to get a focused chart and a short explanation.');
  message('Updating view…');
  try {
    const response = await fetch('/api/view', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(next),
      signal: activeRequest.signal
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error || 'invalid_view');
    if (id !== version) return false;
    applyData(data);
    message(
      current.status === 'empty'
        ? 'No matching data for these dates.'
        : reset
          ? 'Ready to explore.'
          : 'View updated.'
    );
    return true;
  } catch (error) {
    if (error.name !== 'AbortError' && id === version)
      message('Check the date range and try again.', true);
    return false;
  } finally {
    if (id === version) busy(false);
  }
}

async function explore(next, question) {
  if (await loadView(next)) {
    $('question').value = question;
    await ask(next, question);
  }
}

async function ask(approvedView = null, selectedQuestion = null) {
  const question = (selectedQuestion || $('question').value).trim();
  if (!question || !view) {
    $('question').focus();
    return;
  }
  const id = ++version;
  activeRequest?.abort();
  activeRequest = new AbortController();
  busy(true);
  clearNotes('Choosing the right view and calculating the metrics…');
  $('analyst-title').textContent = 'Exploring your question';
  $('submitted-question').textContent = question;
  let receivedView = false,
    finished = false,
    answerText = '';
  const started = performance.now();
  const elapsed = () => ((performance.now() - started) / 1000).toFixed(1) + 's';
  const progress = () => {
    if (id !== version) return;
    message(
      (receivedView ? 'View ready · writing the explanation' : 'Analyst is exploring the data') +
        ' · ' +
        elapsed()
    );
  };
  progress();
  const timer = setInterval(progress, 1000);
  const receive = (event) => {
    if (id !== version) return;
    if (event.type === 'view') {
      applyData(event.data);
      receivedView = true;
      $('answer').textContent = 'The data is ready. Writing the explanation…';
      progress();
    } else if (event.type === 'text') {
      answerText += event.text;
      $('answer').textContent = plain(answerText);
    } else if (event.type === 'error') {
      throw new Error(event.error);
    } else if (event.type === 'done') {
      const data = event.data;
      if (data.dashboard && data.chart) applyData(data);
      lastQuestion = data.lastQuestion;
      $('answer').textContent = plain(data.answer);
      renderEvidence(data.evidence || []);
      $('analyst-title').textContent =
        data.status === 'answered'
          ? data.chart
            ? 'Here’s what the data shows'
            : 'About this view'
          : 'Try another question';
      $('trace').textContent = (data.evidence?.length || 0) + ' agent tool call · ' + elapsed();
      finished = true;
      clearInterval(timer);
      message(
        data.status === 'answered'
          ? (data.chart ? 'Updated for your question · ' : 'Answered · ') + elapsed()
          : 'Ask about requests, errors, response times or a comparison.'
      );
    }
  };
  try {
    const response = await fetch('/api/analyze/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question, view, lastQuestion, approvedView }),
      signal: activeRequest.signal
    });
    if (!response.ok || !response.body) throw new Error('analysis_failed');
    const reader = response.body.getReader(),
      decoder = new TextDecoder();
    let pending = '';
    while (true) {
      const { value, done } = await reader.read();
      pending += decoder.decode(value, { stream: !done });
      let newline;
      while ((newline = pending.indexOf('\n')) >= 0) {
        const line = pending.slice(0, newline);
        pending = pending.slice(newline + 1);
        if (line.trim()) receive(JSON.parse(line));
      }
      if (done) {
        if (pending.trim()) receive(JSON.parse(pending));
        break;
      }
    }
    if (!finished && id === version) throw new Error('incomplete_response');
  } catch (error) {
    if (error.name !== 'AbortError' && id === version) {
      $('analyst-title').textContent = 'Explanation unavailable';
      $('answer').textContent = receivedView
        ? 'The calculated view is ready, but the explanation could not finish. Ask again to retry.'
        : 'The analyst could not finish. Please try again.';
      message(
        receivedView ? 'Data updated; explanation failed.' : 'Analysis could not complete.',
        true
      );
    }
  } finally {
    clearInterval(timer);
    if (id === version) {
      busy(false);
      activeRequest = null;
    }
  }
}

// Event handlers
$('ask-form').addEventListener('submit', (event) => {
  event.preventDefault();
  ask();
});
$('question').addEventListener('keydown', (event) => {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    if (!$('ask').disabled) $('ask-form').requestSubmit();
  }
});
document.querySelectorAll('[data-question]').forEach((button) =>
  button.addEventListener('click', () => {
    $('question').value = button.dataset.question;
    $('ask-form').requestSubmit();
  })
);
$('filters').addEventListener('submit', (event) => {
  event.preventDefault();
  if (view)
    explore(
      {
        ...view,
        service: $('service').value,
        metric: $('metric').value,
        start: $('start').value,
        end: $('end').value,
        grouping: view.grouping === 'period' ? 'day' : view.grouping,
        comparison: null
      },
      'Explain ' + labels[$('metric').value].toLowerCase() + ' in this selection. What stands out?'
    );
});
$('grouping').addEventListener('change', () => {
  if (view)
    explore(
      {
        ...view,
        grouping: $('grouping').value,
        comparison: $('grouping').value === 'period' ? view.comparison : null
      },
      'Explain ' +
        labels[view.metric].toLowerCase() +
        ' in this ' +
        $('grouping').selectedOptions[0].text.toLowerCase() +
        '.'
    );
});
$('reset').addEventListener('click', () => loadView(null, true));
$('cancel').addEventListener('click', () => {
  ++version;
  activeRequest?.abort();
  activeRequest = null;
  busy(false);
  $('analyst-title').textContent = 'Analysis stopped';
  $('answer').textContent = 'Stopped. The currently displayed data is still available.';
  message('Stopped. Ask another question when ready.');
});
$('more').addEventListener('click', () => {
  visible += 30;
  renderRows();
});

// Startup
loadView(null, true);

const elements = {
  statusText: document.getElementById("statusText"),
  logBox: document.getElementById("logBox"),
  targetInput: document.getElementById("targetInput"),
  targetHint: document.getElementById("targetHint"),
  modeSelect: document.getElementById("modeSelect"),
  resultsBox: document.getElementById("resultsBox"),
  summaryBox: document.getElementById("summaryBox"),
  statusIndicator: document.getElementById("statusIndicator"),
  modeDisplay: document.getElementById("modeDisplay"),
  timerDisplay: document.getElementById("timerDisplay"),
  historyBox: document.getElementById("historyBox"),
  progressBar: document.getElementById("progressBar"),
  findingCount: document.getElementById("findingCount"),
  patchCount: document.getElementById("patchCount"),
  portCount: document.getElementById("portCount"),
  lineCount: document.getElementById("lineCount"),
  resultCount: document.getElementById("resultCount"),
  lastRunDisplay: document.getElementById("lastRunDisplay"),
  scanBtn: document.getElementById("scanBtn"),
  exploitBtn: document.getElementById("exploitBtn"),
  patchBtn: document.getElementById("patchBtn"),
  exportBtn: document.getElementById("exportBtn"),
  copyBtn: document.getElementById("copyBtn"),
  clearBtn: document.getElementById("clearBtn"),
  clearHistoryBtn: document.getElementById("clearHistoryBtn")
};

const modeLabels = {
  fast: "fast discovery",
  standard: "full port sweep",
  service: "service fingerprint",
  aggressive: "deep audit",
  vuln: "vulnerability scripts"
};

const actionConfig = {
  scan: {
    endpoint: "scan",
    label: "Scan",
    status: "Scanning target",
    progressStart: 12
  },
  risk: {
    endpoint: "exploit",
    label: "Risk Review",
    status: "Reviewing exposed risks",
    progressStart: 18
  },
  patch: {
    endpoint: "patch",
    label: "Hardening Plan",
    status: "Building hardening plan",
    progressStart: 18
  }
};

const state = {
  busy: false,
  eventSource: null,
  timer: null,
  startTime: 0,
  logs: [],
  results: [],
  history: [],
  lastRun: null,
  scanResults: {
    vulnerabilities: 0,
    patches: 0,
    ports: 0
  }
};

document.addEventListener("DOMContentLoaded", () => {
  loadHistory();
  setupTabs();
  bindActions();
  updateModeDisplay();
  updateMetrics();
});

function bindActions() {
  elements.scanBtn.addEventListener("click", () => runAction("scan"));
  elements.exploitBtn.addEventListener("click", () => runAction("risk"));
  elements.patchBtn.addEventListener("click", () => runAction("patch"));
  elements.exportBtn.addEventListener("click", exportReport);
  elements.copyBtn.addEventListener("click", copyLogs);
  elements.clearBtn.addEventListener("click", resetWorkspace);
  elements.clearHistoryBtn.addEventListener("click", clearHistory);
  elements.modeSelect.addEventListener("change", updateModeDisplay);
  elements.targetInput.addEventListener("input", validateTarget);
  elements.targetInput.addEventListener("keydown", event => {
    if (event.key === "Enter") {
      runAction("scan");
    }
  });
}

function setupTabs() {
  document.querySelectorAll(".tab-btn").forEach(button => {
    button.addEventListener("click", () => switchTab(button.dataset.tab));
  });
}

function switchTab(tabName) {
  document.querySelectorAll(".tab-btn").forEach(button => {
    button.classList.toggle("active", button.dataset.tab === tabName);
  });

  document.querySelectorAll(".tab-content").forEach(content => {
    content.classList.toggle("active", content.id === `${tabName}-tab`);
  });
}

function validateTarget() {
  const target = elements.targetInput.value.trim();
  const valid = !target || /^[a-zA-Z0-9._:/-]+$/.test(target);

  elements.targetInput.classList.toggle("invalid", !valid);
  elements.targetHint.classList.toggle("invalid", !valid);
  elements.targetHint.textContent = valid
    ? "Use only systems you own or are authorized to assess."
    : "Target contains unsupported characters.";

  return valid;
}

function normalizedTarget() {
  return elements.targetInput.value.trim() || "127.0.0.1";
}

function runAction(actionKey) {
  if (state.busy) {
    addLog("System is busy. Let the current operation finish first.", "warning");
    return;
  }

  if (!validateTarget()) {
    addLog("Target validation failed. Check the target field.", "error");
    return;
  }

  const config = actionConfig[actionKey];
  const target = normalizedTarget();
  const mode = elements.modeSelect.value;
  const url = `http://127.0.0.1:5000/${config.endpoint}?target=${encodeURIComponent(target)}&mode=${encodeURIComponent(mode)}`;

  startRun(config, target, mode);

  try {
    state.eventSource = new EventSource(url);
  } catch (error) {
    finishRun("Unable to open event stream.", "error");
    addLog(error.message, "error");
    return;
  }

  let receivedAnyData = false;

  state.eventSource.onmessage = event => {
    receivedAnyData = true;
    const message = event.data.trim();

    if (!message) {
      return;
    }

    addLog(message, classifyLog(message));
    state.logs.push(message);
    parseLine(message);
    advanceProgress(message);

    if (isCompletionLine(message)) {
      finishRun("Done", "success");
      addToHistory(target, config.label, mode);
    }
  };

  state.eventSource.onerror = () => {
    if (receivedAnyData) {
      addLog("Event stream closed.", "success");
      finishRun("Done", "success");
      addToHistory(target, config.label, mode);
    } else {
      addLog("Unable to connect to backend at http://127.0.0.1:5000.", "error");
      finishRun("Backend offline", "error");
    }
  };
}

function startRun(config, target, mode) {
  state.busy = true;
  state.logs = [];
  state.results = [];
  state.scanResults = { vulnerabilities: 0, patches: 0, ports: 0 };
  state.lastRun = {
    action: config.label,
    target,
    mode,
    startedAt: new Date()
  };

  elements.logBox.textContent = "";
  elements.resultsBox.className = "results-box empty-state";
  elements.resultsBox.textContent = "Collecting events and extracting findings...";
  elements.summaryBox.className = "summary-box empty-state";
  elements.summaryBox.textContent = "Run in progress.";
  elements.statusText.textContent = config.status;
  elements.statusIndicator.className = "status-dot busy";
  elements.statusIndicator.setAttribute("aria-label", "Busy");
  elements.progressBar.style.width = `${config.progressStart}%`;
  elements.lastRunDisplay.textContent = "Running";

  setButtonsDisabled(true);
  updateModeDisplay();
  updateMetrics();
  startTimer();
  addLog(`[START] ${config.label} on ${target} using ${modeLabels[mode]}`, "info");
}

function finishRun(message, status) {
  if (!state.busy && status !== "error") {
    return;
  }

  state.busy = false;
  closeStream();
  stopTimer();
  setButtonsDisabled(false);
  elements.statusText.textContent = message;
  elements.statusIndicator.className = `status-dot ${status}`;
  elements.statusIndicator.setAttribute("aria-label", status);
  elements.progressBar.style.width = status === "success" ? "100%" : "0%";

  if (state.lastRun) {
    state.lastRun.finishedAt = new Date();
    elements.lastRunDisplay.textContent = state.lastRun.finishedAt.toLocaleString();
  }

  renderResults();
  renderSummary();
}

function closeStream() {
  if (state.eventSource) {
    state.eventSource.close();
    state.eventSource = null;
  }
}

function startTimer() {
  stopTimer();
  state.startTime = Date.now();
  elements.timerDisplay.textContent = "Elapsed: 0s";
  state.timer = window.setInterval(() => {
    const elapsed = Math.floor((Date.now() - state.startTime) / 1000);
    elements.timerDisplay.textContent = `Elapsed: ${elapsed}s`;
  }, 1000);
}

function stopTimer() {
  if (state.timer) {
    window.clearInterval(state.timer);
    state.timer = null;
  }
}

function setButtonsDisabled(disabled) {
  elements.scanBtn.disabled = disabled;
  elements.exploitBtn.disabled = disabled;
  elements.patchBtn.disabled = disabled;
}

function addLog(message, level = "info") {
  const row = document.createElement("div");
  row.className = `log-line ${level}`;

  const time = document.createElement("span");
  time.className = "log-time";
  time.textContent = new Date().toLocaleTimeString();

  const text = document.createElement("span");
  text.className = "log-message";
  text.textContent = message;

  row.append(time, text);
  elements.logBox.appendChild(row);
  elements.logBox.scrollTop = elements.logBox.scrollHeight;
  elements.lineCount.textContent = `${elements.logBox.children.length} lines`;
}

function classifyLog(message) {
  const upper = message.toUpperCase();

  if (upper.includes("ERROR") || upper.includes("FAILED")) {
    return "error";
  }

  if (upper.includes("FOUND") || upper.includes("WARNING") || upper.includes("RISK")) {
    return "warning";
  }

  if (upper.includes("COMPLETE") || upper.includes("DONE") || upper.includes("SECURED")) {
    return "success";
  }

  return "info";
}

function parseLine(line) {
  const trimmed = line.replace(/^[-*•]\s*/, "").trim();
  const lower = trimmed.toLowerCase();

  if (trimmed.includes("FOUND:")) {
    state.scanResults.vulnerabilities += 1;
    state.results.push({
      type: "vulnerability",
      title: "Potential Exposure",
      detail: trimmed.replace("FOUND:", "").trim()
    });
  }

  if (/^(UPDATE|CONFIGURE|HARDEN|IMPLEMENT|MONITOR|DOCUMENT):/.test(trimmed)) {
    state.scanResults.patches += 1;
    const [title, ...detail] = trimmed.split(":");
    state.results.push({
      type: "security",
      title: title.toLowerCase().replace(/^\w/, char => char.toUpperCase()),
      detail: detail.join(":").trim()
    });
  }

  if (/\b(open|closed|filtered)\b/.test(lower) && /\d+\/(tcp|udp)/.test(lower)) {
    state.scanResults.ports += 1;
    state.results.push({
      type: "network",
      title: "Port Observation",
      detail: trimmed
    });
  }

  updateMetrics();
}

function advanceProgress(message) {
  const currentWidth = Number.parseInt(elements.progressBar.style.width || "0", 10);
  const nextWidth = Math.min(92, Math.max(currentWidth + 4, currentWidth));

  if (!isCompletionLine(message)) {
    elements.progressBar.style.width = `${nextWidth}%`;
  }
}

function isCompletionLine(message) {
  const lower = message.toLowerCase();
  return lower.includes("complete") ||
    lower.includes("done") ||
    lower.includes("system secured") ||
    lower.includes("access granted");
}

function renderResults() {
  elements.resultsBox.className = "results-box";
  elements.resultsBox.textContent = "";

  if (!state.results.length) {
    elements.resultsBox.className = "results-box empty-state";
    elements.resultsBox.textContent = "No structured findings were detected in the stream.";
    elements.resultCount.textContent = "0 items";
    return;
  }

  const fragment = document.createDocumentFragment();

  state.results.forEach(item => {
    const card = document.createElement("article");
    card.className = `result-item ${item.type}`;

    const title = document.createElement("strong");
    title.textContent = item.title;

    const detail = document.createElement("p");
    detail.textContent = item.detail;

    card.append(title, detail);
    fragment.appendChild(card);
  });

  elements.resultsBox.appendChild(fragment);
  elements.resultCount.textContent = `${state.results.length} items`;
}

function renderSummary() {
  elements.summaryBox.className = "summary-box";
  elements.summaryBox.textContent = "";

  const run = state.lastRun;
  const elapsed = run && run.finishedAt
    ? `${Math.max(0, Math.round((run.finishedAt - run.startedAt) / 1000))}s`
    : "0s";

  const summaryItems = [
    ["Target", run ? run.target : "None"],
    ["Action", run ? run.action : "None"],
    ["Mode", run ? modeLabels[run.mode] : modeLabels[elements.modeSelect.value]],
    ["Elapsed", elapsed],
    ["Findings", state.scanResults.vulnerabilities],
    ["Recommendations", state.scanResults.patches],
    ["Ports Observed", state.scanResults.ports],
    ["Log Lines", state.logs.length]
  ];

  const grid = document.createElement("div");
  grid.className = "summary-grid";

  summaryItems.forEach(([label, value]) => {
    const card = document.createElement("article");
    card.className = "summary-card";

    const title = document.createElement("strong");
    title.textContent = label;

    const detail = document.createElement("p");
    detail.textContent = String(value);

    card.append(title, detail);
    grid.appendChild(card);
  });

  elements.summaryBox.appendChild(grid);
}

function updateMetrics() {
  elements.findingCount.textContent = state.scanResults.vulnerabilities;
  elements.patchCount.textContent = state.scanResults.patches;
  elements.portCount.textContent = state.scanResults.ports;
}

function updateModeDisplay() {
  elements.modeDisplay.textContent = `Mode: ${modeLabels[elements.modeSelect.value]}`;
}

function addToHistory(target, action, mode) {
  const entry = {
    target,
    action,
    mode,
    timestamp: new Date().toLocaleString()
  };

  state.history = [
    entry,
    ...state.history.filter(item => !(item.target === target && item.action === action && item.mode === mode))
  ].slice(0, 12);

  localStorage.setItem("scanHistory", JSON.stringify(state.history));
  renderHistory();
}

function loadHistory() {
  try {
    state.history = JSON.parse(localStorage.getItem("scanHistory") || "[]");
  } catch {
    state.history = [];
  }

  renderHistory();
}

function renderHistory() {
  elements.historyBox.textContent = "";

  if (!state.history.length) {
    elements.historyBox.className = "history-box empty-state";
    elements.historyBox.textContent = "No recent activity";
    return;
  }

  elements.historyBox.className = "history-box";

  state.history.forEach(item => {
    const button = document.createElement("button");
    button.className = "history-item";
    button.type = "button";

    const title = document.createElement("strong");
    title.textContent = `${item.action} | ${item.target}`;

    const meta = document.createElement("span");
    meta.textContent = `${modeLabels[item.mode] || item.mode} | ${item.timestamp}`;

    button.append(title, meta);
    button.addEventListener("click", () => {
      elements.targetInput.value = item.target;
      elements.modeSelect.value = item.mode;
      updateModeDisplay();
      validateTarget();
      addLog(`Loaded history item: ${item.action} on ${item.target}`, "info");
    });

    elements.historyBox.appendChild(button);
  });
}

function clearHistory() {
  state.history = [];
  localStorage.removeItem("scanHistory");
  renderHistory();
}

function reportText() {
  const run = state.lastRun;
  const lines = [
    "HackOps Security Console Report",
    "================================",
    `Target: ${run ? run.target : normalizedTarget()}`,
    `Action: ${run ? run.action : "None"}`,
    `Mode: ${run ? modeLabels[run.mode] : modeLabels[elements.modeSelect.value]}`,
    `Generated: ${new Date().toLocaleString()}`,
    `Status: ${elements.statusText.textContent}`,
    "",
    "Metrics",
    "-------",
    `Findings: ${state.scanResults.vulnerabilities}`,
    `Recommendations: ${state.scanResults.patches}`,
    `Ports Observed: ${state.scanResults.ports}`,
    "",
    "Structured Findings",
    "-------------------",
    ...state.results.map(item => `- ${item.title}: ${item.detail}`),
    "",
    "Event Log",
    "---------",
    ...state.logs
  ];

  return lines.join("\n");
}

function exportReport() {
  const blob = new Blob([reportText()], { type: "text/plain" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");

  link.href = url;
  link.download = `hackops-report-${Date.now()}.txt`;
  link.click();
  URL.revokeObjectURL(url);

  addLog("Report exported.", "success");
}

async function copyLogs() {
  const text = state.logs.length ? state.logs.join("\n") : "No logs available.";

  try {
    await navigator.clipboard.writeText(text);
    addLog("Logs copied to clipboard.", "success");
  } catch {
    addLog("Clipboard access is unavailable in this browser.", "warning");
  }
}

function resetWorkspace() {
  if (state.busy) {
    closeStream();
    state.busy = false;
    stopTimer();
    setButtonsDisabled(false);
  }

  state.logs = [];
  state.results = [];
  state.scanResults = { vulnerabilities: 0, patches: 0, ports: 0 };
  state.lastRun = null;

  elements.logBox.textContent = "";
  elements.resultsBox.className = "results-box empty-state";
  elements.resultsBox.textContent = "Run an assessment to populate findings.";
  elements.summaryBox.className = "summary-box empty-state";
  elements.summaryBox.textContent = "Summary details will appear after a scan, risk review, or hardening plan.";
  elements.statusText.textContent = "Ready";
  elements.statusIndicator.className = "status-dot idle";
  elements.statusIndicator.setAttribute("aria-label", "Idle");
  elements.progressBar.style.width = "0%";
  elements.timerDisplay.textContent = "Elapsed: 0s";
  elements.lineCount.textContent = "0 lines";
  elements.resultCount.textContent = "0 items";
  elements.lastRunDisplay.textContent = "No run yet";
  updateMetrics();
  updateModeDisplay();
}

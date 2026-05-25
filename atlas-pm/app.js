const STORAGE_KEY = "atlas-pm-state-v1";
const THEME_KEY = "atlas-pm-theme";
const STATUSES = ["Backlog", "Ready", "In Progress", "Review", "Done"];

const today = new Date();
const addDays = (days) => {
  const date = new Date(today);
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
};

const seedState = {
  people: [
    { id: "p1", name: "Siphiwe Mavuso", role: "Product Lead", capacity: 34, color: "#1f7a5a" },
    { id: "p2", name: "Avery Chen", role: "Design", capacity: 28, color: "#4267b2" },
    { id: "p3", name: "Jordan Patel", role: "Engineering", capacity: 36, color: "#b86e1d" },
    { id: "p4", name: "Maya Ross", role: "Operations", capacity: 24, color: "#8a4fb5" }
  ],
  projects: [
    {
      id: "pr1",
      name: "Client Portal Launch",
      ownerId: "p1",
      status: "Active",
      due: addDays(18),
      description: "Ship the customer-facing portal with onboarding, messaging, and billing visibility."
    },
    {
      id: "pr2",
      name: "Mobile Field App",
      ownerId: "p3",
      status: "At risk",
      due: addDays(31),
      description: "Stabilize offline task capture and sync for field teams before pilot rollout."
    },
    {
      id: "pr3",
      name: "Q3 Planning System",
      ownerId: "p4",
      status: "Discovery",
      due: addDays(47),
      description: "Create a repeatable planning workflow across goals, budgets, staffing, and risks."
    }
  ],
  tasks: [
    {
      id: "t1",
      title: "Finalize portal acceptance criteria",
      projectId: "pr1",
      assigneeId: "p1",
      status: "In Progress",
      priority: "High",
      due: addDays(2),
      estimate: 6,
      notes: "Confirm legal copy and account permissions."
    },
    {
      id: "t2",
      title: "Prototype billing activity view",
      projectId: "pr1",
      assigneeId: "p2",
      status: "Review",
      priority: "Medium",
      due: addDays(5),
      estimate: 10,
      notes: "Needs empty, loading, and error states."
    },
    {
      id: "t3",
      title: "Resolve offline conflict handling",
      projectId: "pr2",
      assigneeId: "p3",
      status: "In Progress",
      priority: "High",
      due: addDays(1),
      estimate: 14,
      notes: "Current sync merge is too optimistic."
    },
    {
      id: "t4",
      title: "Pilot readiness checklist",
      projectId: "pr2",
      assigneeId: "p4",
      status: "Ready",
      priority: "Medium",
      due: addDays(9),
      estimate: 7,
      notes: "Include support escalation paths."
    },
    {
      id: "t5",
      title: "Interview department leads",
      projectId: "pr3",
      assigneeId: "p1",
      status: "Backlog",
      priority: "Low",
      due: addDays(14),
      estimate: 8,
      notes: "Collect goals, constraints, and planning rituals."
    },
    {
      id: "t6",
      title: "Publish launch notes",
      projectId: "pr1",
      assigneeId: "p4",
      status: "Done",
      priority: "Low",
      due: addDays(-1),
      estimate: 3,
      notes: "Shared with success team."
    }
  ]
};

let state = loadState();
let selectedProjectId = state.projects[0]?.id || "";
let selectedPersonId = state.people[0]?.id || "";
let activeView = "dashboard";
let draggedTaskId = null;
let currentTheme = localStorage.getItem(THEME_KEY) || "light";

const el = (id) => document.getElementById(id);
const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));

function loadState() {
  const saved = localStorage.getItem(STORAGE_KEY);
  if (!saved) return structuredClone(seedState);
  try {
    const parsed = JSON.parse(saved);
    if (!parsed.people || !parsed.projects || !parsed.tasks) return structuredClone(seedState);
    return parsed;
  } catch {
    return structuredClone(seedState);
  }
}

function saveState(message) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  render();
  if (message) showToast(message);
}

function applyTheme(theme) {
  currentTheme = theme === "dark" ? "dark" : "light";
  document.documentElement.dataset.theme = currentTheme;
  localStorage.setItem(THEME_KEY, currentTheme);
  const button = el("theme-toggle");
  if (!button) return;
  button.textContent = currentTheme === "dark" ? "Light" : "Dark";
  button.setAttribute("aria-pressed", String(currentTheme === "dark"));
}

function uid(prefix) {
  return `${prefix}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

function personById(id) {
  return state.people.find((person) => person.id === id);
}

function projectById(id) {
  return state.projects.find((project) => project.id === id);
}

function formatDate(value) {
  if (!value) return "No date";
  return new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric", year: "numeric" }).format(
    new Date(`${value}T12:00:00`)
  );
}

function daysUntil(value) {
  if (!value) return Infinity;
  const due = new Date(`${value}T12:00:00`);
  const current = new Date(today.toISOString().slice(0, 10) + "T12:00:00");
  return Math.round((due - current) / 86400000);
}

function taskMatchesFilters(task) {
  const projectFilter = el("project-filter").value;
  const assigneeFilter = el("assignee-filter").value;
  const priorityFilter = el("priority-filter").value;
  const query = el("search-input").value.trim().toLowerCase();
  const project = projectById(task.projectId);
  const assignee = personById(task.assigneeId);
  const haystack = [task.title, task.notes, task.priority, project?.name, assignee?.name]
    .join(" ")
    .toLowerCase();

  return (
    (projectFilter === "all" || task.projectId === projectFilter) &&
    (assigneeFilter === "all" || task.assigneeId === assigneeFilter) &&
    (priorityFilter === "all" || task.priority === priorityFilter) &&
    (!query || haystack.includes(query))
  );
}

function projectProgress(projectId) {
  const projectTasks = state.tasks.filter((task) => task.projectId === projectId);
  if (!projectTasks.length) return 0;
  return Math.round((projectTasks.filter((task) => task.status === "Done").length / projectTasks.length) * 100);
}

function openTaskDialog(taskId) {
  const task = state.tasks.find((item) => item.id === taskId);
  el("task-dialog-title").textContent = task ? "Edit Task" : "New Task";
  el("task-id").value = task?.id || "";
  el("task-title").value = task?.title || "";
  el("task-project").value = task?.projectId || state.projects[0]?.id || "";
  el("task-assignee").value = task?.assigneeId || state.people[0]?.id || "";
  el("task-status").value = task?.status || "Backlog";
  el("task-priority").value = task?.priority || "Medium";
  el("task-due").value = task?.due || addDays(7);
  el("task-estimate").value = task?.estimate ?? 4;
  el("task-notes").value = task?.notes || "";
  el("delete-task-button").style.visibility = task ? "visible" : "hidden";
  el("task-dialog").showModal();
}

function closeTaskDialog() {
  el("task-dialog").close();
}

function render() {
  renderSelects();
  renderMetrics();
  renderProjects();
  renderProjectForm();
  renderBoard();
  renderTimeline();
  renderPeople();
  renderPersonForm();
  renderReports();
}

function renderSelects() {
  const projectOptions = [
    '<option value="all">All projects</option>',
    ...state.projects.map((project) => `<option value="${project.id}">${escapeHtml(project.name)}</option>`)
  ].join("");
  const projectOnlyOptions = state.projects
    .map((project) => `<option value="${project.id}">${escapeHtml(project.name)}</option>`)
    .join("");
  const personOptions = [
    '<option value="all">All people</option>',
    ...state.people.map((person) => `<option value="${person.id}">${escapeHtml(person.name)}</option>`)
  ].join("");
  const personOnlyOptions = state.people
    .map((person) => `<option value="${person.id}">${escapeHtml(person.name)}</option>`)
    .join("");

  preserveSelect(el("project-filter"), projectOptions);
  preserveSelect(el("assignee-filter"), personOptions);
  preserveSelect(el("project-owner"), personOnlyOptions);
  preserveSelect(el("task-project"), projectOnlyOptions);
  preserveSelect(el("task-assignee"), personOnlyOptions);
}

function preserveSelect(select, html) {
  const value = select.value;
  select.innerHTML = html;
  if ([...select.options].some((option) => option.value === value)) select.value = value;
}

function renderMetrics() {
  const openTasks = state.tasks.filter((task) => task.status !== "Done");
  const dueThisWeek = openTasks.filter((task) => daysUntil(task.due) >= 0 && daysUntil(task.due) <= 7);
  const totalCapacity = state.people.reduce((sum, person) => sum + Number(person.capacity || 0), 0);
  const assignedWork = openTasks.reduce((sum, task) => sum + Number(task.estimate || 0), 0);
  const capacity = totalCapacity ? Math.min(100, Math.round((assignedWork / totalCapacity) * 100)) : 0;

  el("metric-projects").textContent = state.projects.filter((project) => project.status !== "Complete").length;
  el("metric-open").textContent = openTasks.length;
  el("metric-due").textContent = dueThisWeek.length;
  el("metric-capacity").textContent = `${capacity}%`;
  el("sidebar-summary").textContent = `${openTasks.length} open tasks across ${state.projects.length} projects. ${dueThisWeek.length} item(s) are due this week.`;
  document.querySelector(".health-meter span").style.width = `${Math.max(12, 100 - capacity / 2)}%`;

  const priorityTasks = [...openTasks]
    .sort((a, b) => priorityRank(a.priority) - priorityRank(b.priority) || daysUntil(a.due) - daysUntil(b.due))
    .slice(0, 6);
  el("priority-list").innerHTML = priorityTasks.length
    ? priorityTasks.map(taskCard).join("")
    : emptyState("No priority work. Enjoy the quiet.");

  el("project-progress-list").innerHTML = state.projects
    .map((project) => {
      const progress = projectProgress(project.id);
      return `<article class="progress-row">
        <header><span>${escapeHtml(project.name)}</span><span>${progress}%</span></header>
        <div class="progress-track"><span style="width:${progress}%"></span></div>
      </article>`;
    })
    .join("");
}

function renderProjects() {
  if (!state.projects.some((project) => project.id === selectedProjectId)) selectedProjectId = state.projects[0]?.id || "";
  el("projects-list").innerHTML = state.projects.length
    ? state.projects
        .map((project) => {
          const owner = personById(project.ownerId);
          const progress = projectProgress(project.id);
          return `<button class="project-card ${project.id === selectedProjectId ? "is-selected" : ""}" data-project-id="${project.id}" type="button">
            <h3>${escapeHtml(project.name)}</h3>
            <p>${escapeHtml(project.description || "No project description yet.")}</p>
            <div class="project-meta">
              <span class="pill">${escapeHtml(project.status)}</span>
              <span>${escapeHtml(owner?.name || "Unassigned")}</span>
              <span>${formatDate(project.due)}</span>
            </div>
            <div class="progress-track"><span style="width:${progress}%"></span></div>
          </button>`;
        })
        .join("")
    : emptyState("Create your first project to start planning.");
}

function renderProjectForm() {
  const project = projectById(selectedProjectId);
  el("project-id").value = project?.id || "";
  el("project-name").value = project?.name || "";
  el("project-owner").value = project?.ownerId || state.people[0]?.id || "";
  el("project-status").value = project?.status || "Active";
  el("project-due").value = project?.due || addDays(30);
  el("project-description").value = project?.description || "";
  el("delete-project-button").disabled = !project;
}

function renderBoard() {
  const board = el("kanban-board");
  board.innerHTML = STATUSES.map((status) => {
    const tasks = state.tasks.filter((task) => task.status === status && taskMatchesFilters(task));
    return `<section class="kanban-column" data-status="${status}">
      <header><span>${status}</span><span>${tasks.length}</span></header>
      ${tasks.length ? tasks.map(taskCard).join("") : emptyState("No tasks")}
    </section>`;
  }).join("");
}

function renderTimeline() {
  const tasks = [...state.tasks].filter((task) => task.status !== "Done").sort((a, b) => daysUntil(a.due) - daysUntil(b.due));
  el("timeline-count").textContent = `${tasks.length} scheduled item(s)`;
  el("timeline-list").innerHTML = tasks.length
    ? tasks
        .map((task) => {
          const project = projectById(task.projectId);
          const assignee = personById(task.assigneeId);
          return `<article class="timeline-item">
            <div class="timeline-date">${formatDate(task.due)}</div>
            <div>
              <h3>${escapeHtml(task.title)}</h3>
              <div class="task-meta">
                <span class="pill ${task.priority.toLowerCase()}">${task.priority}</span>
                <span>${escapeHtml(project?.name || "No project")}</span>
                <span>${escapeHtml(assignee?.name || "Unassigned")}</span>
                <span>${task.estimate || 0}h</span>
              </div>
            </div>
          </article>`;
        })
        .join("")
    : emptyState("No upcoming work.");
}

function renderPeople() {
  if (!state.people.some((person) => person.id === selectedPersonId)) selectedPersonId = state.people[0]?.id || "";
  el("people-list").innerHTML = state.people.length
    ? state.people
        .map((person) => {
          const assigned = state.tasks
            .filter((task) => task.assigneeId === person.id && task.status !== "Done")
            .reduce((sum, task) => sum + Number(task.estimate || 0), 0);
          const load = person.capacity ? Math.round((assigned / person.capacity) * 100) : 0;
          return `<button class="person-card ${person.id === selectedPersonId ? "is-selected" : ""}" data-person-id="${person.id}" type="button">
            <span class="avatar" style="background:${person.color}">${initials(person.name)}</span>
            <span>
              <h3>${escapeHtml(person.name)}</h3>
              <span class="person-meta">${escapeHtml(person.role)} | ${assigned}h / ${person.capacity}h | ${load}%</span>
            </span>
          </button>`;
        })
        .join("")
    : emptyState("Add teammates to manage workload.");
}

function renderPersonForm() {
  const person = personById(selectedPersonId);
  el("person-id").value = person?.id || "";
  el("person-name").value = person?.name || "";
  el("person-role").value = person?.role || "";
  el("person-capacity").value = person?.capacity || 32;
  el("person-color").value = person?.color || "#1f7a5a";
  el("delete-person-button").disabled = !person;
}

function renderReports() {
  const statusCounts = STATUSES.map((status) => ({
    label: status,
    value: state.tasks.filter((task) => task.status === status).length
  }));
  renderBarChart(el("status-chart"), statusCounts);

  const workload = state.people.map((person) => ({
    label: person.name,
    value: state.tasks
      .filter((task) => task.assigneeId === person.id && task.status !== "Done")
      .reduce((sum, task) => sum + Number(task.estimate || 0), 0)
  }));
  renderBarChart(el("workload-chart"), workload);

  const risks = state.tasks
    .filter((task) => task.status !== "Done" && (task.priority === "High" || daysUntil(task.due) < 3))
    .sort((a, b) => daysUntil(a.due) - daysUntil(b.due));
  el("risk-list").innerHTML = risks.length ? risks.map(taskCard).join("") : emptyState("No high-priority risks found.");
}

function renderBarChart(container, rows) {
  const max = Math.max(1, ...rows.map((row) => row.value));
  container.innerHTML = rows
    .map(
      (row) => `<div class="bar-row">
        <span>${escapeHtml(row.label)}</span>
        <div class="bar-track"><span style="width:${Math.max(3, (row.value / max) * 100)}%"></span></div>
        <strong>${row.value}</strong>
      </div>`
    )
    .join("");
}

function taskCard(task) {
  const project = projectById(task.projectId);
  const assignee = personById(task.assigneeId);
  const dueIn = daysUntil(task.due);
  const dueLabel = dueIn < 0 ? "Overdue" : dueIn === 0 ? "Today" : `${dueIn}d`;
  return `<article class="task-card" draggable="true" data-task-id="${task.id}">
    <h3>${escapeHtml(task.title)}</h3>
    <div class="task-meta">
      <span class="pill ${task.priority.toLowerCase()}">${task.priority}</span>
      <span>${escapeHtml(project?.name || "No project")}</span>
      <span>${escapeHtml(assignee?.name || "Unassigned")}</span>
      <span>${dueLabel}</span>
      <span>${task.estimate || 0}h</span>
    </div>
  </article>`;
}

function emptyState(message) {
  return `<div class="empty-state">${message}</div>`;
}

function priorityRank(priority) {
  return { High: 1, Medium: 2, Low: 3 }[priority] || 4;
}

function initials(name) {
  return escapeHtml(
    name
      .split(" ")
      .map((part) => part[0])
      .join("")
      .slice(0, 2)
      .toUpperCase()
  );
}

function escapeHtml(value = "") {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function showToast(message) {
  const toast = el("toast");
  toast.textContent = message;
  toast.classList.add("is-visible");
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => toast.classList.remove("is-visible"), 2200);
}

function switchView(view) {
  activeView = view;
  $$(".view").forEach((item) => item.classList.toggle("is-active", item.id === `${view}-view`));
  $$(".nav-item").forEach((item) => item.classList.toggle("is-active", item.dataset.view === view));
  el("page-title").textContent = view[0].toUpperCase() + view.slice(1);
}

function bindEvents() {
  $$(".nav-item").forEach((button) => button.addEventListener("click", () => switchView(button.dataset.view)));
  $$("[data-view-jump]").forEach((button) => button.addEventListener("click", () => switchView(button.dataset.viewJump)));
  el("new-task-button").addEventListener("click", () => openTaskDialog());
  el("close-task-dialog").addEventListener("click", closeTaskDialog);
  el("theme-toggle").addEventListener("click", () => {
    applyTheme(currentTheme === "dark" ? "light" : "dark");
    showToast(`${currentTheme === "dark" ? "Dark" : "Light"} theme enabled`);
  });
  el("search-input").addEventListener("input", render);
  ["project-filter", "assignee-filter", "priority-filter"].forEach((id) => el(id).addEventListener("change", renderBoard));

  document.addEventListener("click", (event) => {
    const task = event.target.closest("[data-task-id]");
    const project = event.target.closest("[data-project-id]");
    const person = event.target.closest("[data-person-id]");
    if (task) openTaskDialog(task.dataset.taskId);
    if (project) {
      selectedProjectId = project.dataset.projectId;
      renderProjects();
      renderProjectForm();
    }
    if (person) {
      selectedPersonId = person.dataset.personId;
      renderPeople();
      renderPersonForm();
    }
  });

  el("task-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const id = el("task-id").value || uid("t");
    const task = {
      id,
      title: el("task-title").value.trim(),
      projectId: el("task-project").value,
      assigneeId: el("task-assignee").value,
      status: el("task-status").value,
      priority: el("task-priority").value,
      due: el("task-due").value,
      estimate: Number(el("task-estimate").value || 0),
      notes: el("task-notes").value.trim()
    };
    state.tasks = state.tasks.some((item) => item.id === id)
      ? state.tasks.map((item) => (item.id === id ? task : item))
      : [...state.tasks, task];
    closeTaskDialog();
    saveState("Task saved");
  });

  el("delete-task-button").addEventListener("click", () => {
    const id = el("task-id").value;
    state.tasks = state.tasks.filter((task) => task.id !== id);
    closeTaskDialog();
    saveState("Task deleted");
  });

  el("new-project-button").addEventListener("click", () => {
    selectedProjectId = "";
    renderProjectForm();
    el("project-name").focus();
  });

  el("project-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const id = el("project-id").value || uid("pr");
    const project = {
      id,
      name: el("project-name").value.trim(),
      ownerId: el("project-owner").value,
      status: el("project-status").value,
      due: el("project-due").value,
      description: el("project-description").value.trim()
    };
    state.projects = state.projects.some((item) => item.id === id)
      ? state.projects.map((item) => (item.id === id ? project : item))
      : [...state.projects, project];
    selectedProjectId = id;
    saveState("Project saved");
  });

  el("delete-project-button").addEventListener("click", () => {
    const id = el("project-id").value;
    if (!id) return;
    state.projects = state.projects.filter((project) => project.id !== id);
    state.tasks = state.tasks.filter((task) => task.projectId !== id);
    selectedProjectId = state.projects[0]?.id || "";
    saveState("Project and related tasks deleted");
  });

  el("new-person-button").addEventListener("click", () => {
    selectedPersonId = "";
    renderPersonForm();
    el("person-name").focus();
  });

  el("person-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const id = el("person-id").value || uid("p");
    const person = {
      id,
      name: el("person-name").value.trim(),
      role: el("person-role").value.trim(),
      capacity: Number(el("person-capacity").value || 0),
      color: el("person-color").value
    };
    state.people = state.people.some((item) => item.id === id)
      ? state.people.map((item) => (item.id === id ? person : item))
      : [...state.people, person];
    selectedPersonId = id;
    saveState("Person saved");
  });

  el("delete-person-button").addEventListener("click", () => {
    const id = el("person-id").value;
    if (!id) return;
    state.people = state.people.filter((person) => person.id !== id);
    state.tasks = state.tasks.map((task) => (task.assigneeId === id ? { ...task, assigneeId: state.people[0]?.id || "" } : task));
    state.projects = state.projects.map((project) =>
      project.ownerId === id ? { ...project, ownerId: state.people[0]?.id || "" } : project
    );
    selectedPersonId = state.people[0]?.id || "";
    saveState("Person removed");
  });

  document.addEventListener("dragstart", (event) => {
    const card = event.target.closest("[data-task-id]");
    if (!card) return;
    draggedTaskId = card.dataset.taskId;
    event.dataTransfer.effectAllowed = "move";
  });

  document.addEventListener("dragover", (event) => {
    if (event.target.closest(".kanban-column")) event.preventDefault();
  });

  document.addEventListener("drop", (event) => {
    const column = event.target.closest(".kanban-column");
    if (!column || !draggedTaskId) return;
    state.tasks = state.tasks.map((task) => (task.id === draggedTaskId ? { ...task, status: column.dataset.status } : task));
    draggedTaskId = null;
    saveState("Task moved");
  });

  el("export-data").addEventListener("click", () => {
    const blob = new Blob([JSON.stringify(state, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `atlas-pm-${new Date().toISOString().slice(0, 10)}.json`;
    link.click();
    URL.revokeObjectURL(url);
  });

  el("import-data").addEventListener("click", () => el("import-file").click());
  el("import-file").addEventListener("change", async (event) => {
    const file = event.target.files[0];
    if (!file) return;
    try {
      const next = JSON.parse(await file.text());
      if (!next.people || !next.projects || !next.tasks) throw new Error("Invalid Atlas PM export");
      state = next;
      selectedProjectId = state.projects[0]?.id || "";
      selectedPersonId = state.people[0]?.id || "";
      saveState("Workspace imported");
    } catch (error) {
      showToast(error.message);
    } finally {
      event.target.value = "";
    }
  });
}

bindEvents();
applyTheme(currentTheme);
render();
switchView(activeView);

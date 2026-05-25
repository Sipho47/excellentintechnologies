# Atlas PM

Atlas PM is a complete browser-based project management application built with plain HTML, CSS, and JavaScript. It runs without a build step, stores workspace data locally, and provides a polished portfolio-ready example of a full frontend product workflow.

## Overview

The app is designed as a compact project command center for managing projects, tasks, timelines, team workload, and delivery risk. It is intentionally dependency-free so it can be opened directly in a browser, hosted as static files, or used as a prototype before moving to a larger full-stack architecture.

## Features

- Dashboard with active project, open task, due date, and capacity metrics
- Project management with owner, status, due date, description, and progress tracking
- Kanban board with drag-and-drop status changes
- Task creation, editing, deletion, priorities, estimates, due dates, assignees, and notes
- Timeline view for upcoming work
- Team management with roles, weekly capacity, profile colors, and workload visibility
- Reports for status breakdown, workload, risks, and blockers
- Search and filters across board work
- Toast notifications for key actions
- Dark/light theme toggle with saved preference
- Responsive layout, including mobile-friendly Kanban columns
- Local persistence with `localStorage`
- JSON import and export for workspace backups

## Installation

No installation is required.

Open [index.html](./index.html) in a modern browser.

## Usage

1. Open the dashboard to review workspace health.
2. Add or edit projects from the Projects view.
3. Create tasks with the New Task button.
4. Use the Board view to drag tasks across statuses.
5. Track upcoming work in Timeline.
6. Manage team capacity in People.
7. Review delivery risks and workload in Reports.
8. Use Export to save a JSON backup, and Import to restore one.

## Technologies

- HTML5
- CSS3
- Vanilla JavaScript
- Browser `localStorage`
- Native drag-and-drop APIs
- Native `dialog` element

## Architecture

The application is currently organized as a small static frontend:

- `index.html` defines the application shell, views, forms, modal, and controls.
- `styles.css` contains the responsive layout, visual system, dark theme, and component styling.
- `app.js` owns state, persistence, rendering, CRUD workflows, filters, drag-and-drop, import/export, and theme preference.

The current architecture favors readability and portability. A natural next step is modularizing the JavaScript into focused files such as `storage.js`, `ui.js`, `kanban.js`, and `reports.js`.

## Data Model

Atlas PM stores three primary collections:

- `projects`: project metadata, owner, status, due date, and description
- `tasks`: task title, project, assignee, status, priority, due date, estimate, and notes
- `people`: team member name, role, weekly capacity, and display color

All data is saved under the `atlas-pm-state-v1` localStorage key.

## Deployment

Because Atlas PM is static, it can be hosted on:

- GitHub Pages
- Netlify
- Vercel
- Any static file server

## Future Enhancements

- Modular JavaScript files
- Task comments and activity history
- Gantt chart or roadmap visualization
- File attachments
- Recurring tasks
- Due-date reminders
- Role-based permissions
- React frontend
- Node.js API
- PostgreSQL persistence

## License

This project is available for personal, educational, and portfolio use.

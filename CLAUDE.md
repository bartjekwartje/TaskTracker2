# TaskTracker2 - CLAUDE.md

## Project Overview

A weekly habit/task tracker. Users create tasks organized into groups, then mark daily progress across a 7-day week view. Two task types are supported: `checklist` (done/not done) and `counter` (numeric quantity).

## Architecture

```
TaskTrackerSolution2/
├── src/
│   ├── backend/TaskTrackerServer2/   # ASP.NET Core Minimal API (.NET 10)
│   │   ├── Program.cs                # All API endpoints (single file)
│   │   ├── appsettings.json
│   │   └── TaskTrackerServer2.csproj
│   └── html/
│       └── TaskTracker2.html         # Entire frontend (single HTML file)
├── infrastructure/
│   ├── Dockerfile                    # Production: Alpine multi-stage build (nginx + Tailscale)
│   ├── DevDockerfile                 # Development: Debian-based (nginx, no Tailscale)
│   ├── nginx-prod.conf               # Container nginx config (production, /api/ → :5000)
│   ├── nginx-dev.conf                # Container nginx config (development, /api/ → :8080)
│   ├── start.sh                      # Production startup (tailscaled + dotnet + nginx)
│   └── start-dev.sh                  # Dev startup (dotnet + nginx only)
├── docs/
│   └── ARCHITECTURE.md               # Deployment & network architecture reference
├── build_docker.sh                   # Builds & pushes to Docker Hub (run from repo root)
└── TaskTrackerSolution2.slnx         # Solution file
```

## Backend

**Stack:** ASP.NET Core Minimal API, Dapper, Npgsql, PostgreSQL

**All API logic lives in `Program.cs`** — no controllers, no service layer.

### API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/week-data` | Returns all tasks + progress dict keyed as `{taskId}_{yyyy-MM-dd}` |
| POST | `/api/tasks` | Create a task (body: `name`, `groupName`, `categoryName`) |
| PUT | `/api/progress` | Upsert progress (body: `taskId`, `date`, `status`) |
| DELETE | `/api/tasks/{id}` | Delete a task |
| PUT | `/api/tasks/{id}/move` | Move task to a new group (body: `newGroupName`) |

### Database Schema

```sql
-- Lookup table for task types (must be pre-seeded; e.g. 'checklist', 'counter')
task_categories(
  category_name VARCHAR(50) PRIMARY KEY,
  description   TEXT
)

-- Groups (cascade name changes to tasks)
groups(
  name       VARCHAR(100) PRIMARY KEY,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
)

-- Tasks
tasks(
  id            VARCHAR(50)  PRIMARY KEY,
  name          VARCHAR(255) NOT NULL,
  group_name    VARCHAR(100) NOT NULL REFERENCES groups(name) ON UPDATE CASCADE,
  category_name VARCHAR(50)  NOT NULL REFERENCES task_categories(category_name),
  created_at    TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_tasks_group (group_name)
)

-- Daily progress (deletes cascade when a task is deleted)
progress(
  task_id    VARCHAR(50) REFERENCES tasks(id) ON DELETE CASCADE,
  log_date   DATE        NOT NULL,
  status     INT         NOT NULL DEFAULT 0,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (task_id, log_date),
  INDEX idx_progress_date (log_date)
)
```

> **Note:** `task_categories` must be pre-seeded with valid values (e.g. `checklist`, `counter`) before tasks can be created. The backend does not create categories automatically.

### Database Configuration (environment variables)

| Variable | Default |
|----------|---------|
| `DB_HOST_IP` | `localhost` |
| `DB_PORT` | `5432` |
| `DB_NAME` | `postgres` |
| `DB_USER` | `postgres` |
| `DB_PASS` | `password` |

### Local Development

Run the backend:
```bash
cd src/backend/TaskTrackerServer2
dotnet run
# Listens on http://localhost:5056
```

Build only:
```bash
dotnet build src/backend/TaskTrackerServer2/TaskTrackerServer2.csproj
```

## Frontend

**Stack:** Vanilla JS, Tailwind CSS (CDN), Lucide icons (CDN) — all in one file: `src/html/TaskTracker2.html`.

### API Base URL

`API_BASE` is always `window.location.origin + '/api'` — no hardcoded hostnames. Works for any subdomain because each environment gets its own subdomain (see `docs/ARCHITECTURE.md`).

### Key Frontend Behaviours

- **Task cells:** Left-click to toggle/increment. Right-click on a counter cell to decrement.
- **Groups:** Tasks are filtered by the selected group tab. New groups can be created locally (they are persisted to the DB only when a task is first added to them).
- **Move task:** Hover a task row to reveal the move and delete buttons.
- **Virtual keyboard:** Optional on-screen keyboard for touch devices, toggled via the keyboard icon button.

## Docker

### Production Build & Push

```bash
./build_docker.sh
# Builds image: bartje2/tasktracker:v2.0
# Pushes to Docker Hub
# (script cd's to parent dir DotNetSolutions/ for build context)
```

Production uses Alpine, installs nginx + Tailscale, serves HTML from `/app/html`, backend on port 5000.

### Dev Docker

```bash
# From repo root:
docker build -f infrastructure/DevDockerfile -t tasktracker-dev .
docker run -d --network host --name tasktracker-dev tasktracker-dev
```

Dev uses Debian, nginx only (no Tailscale), HTML served from `/var/www/html`, backend on port 8080.

> **Security warning:** `infrastructure/DevDockerfile` contains hardcoded database credentials. Do not commit real credentials — use a `.env` file or Docker secrets instead.

## Key Notes & Gotchas

- **No tests** exist in this project.
- **CORS is wide open** (`AllowAnyOrigin`) — acceptable for a personal/home tool.
- **Swashbuckle** packages are referenced in the `.csproj` but Swagger middleware is **not wired up** in `Program.cs`.
- `src/x` appears to be a scratch/temp file and should probably be deleted or gitignored.
- The `DevDockerfile` hardcodes a Tailscale IP (`100.100.104.47`) as DB host — this is a personal dev machine setup.
- Production Dockerfile uses Alpine nginx (`/etc/nginx/http.d/default.conf`); DevDockerfile uses Debian nginx (`/etc/nginx/sites-available/default`).

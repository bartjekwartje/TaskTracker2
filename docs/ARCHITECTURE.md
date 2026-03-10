# Deployment & Debugging Architecture

## Purpose
This document describes the deployment and network architecture of a containerised .NET web application. It is intended as a reference for scaffolding new projects or adapting existing ones.

---

## Project Structure

```
repo/
├── src/
│   ├── backend/          # ASP.NET Core project
│   │   └── ProjectName/
│   │       └── Program.cs        # All endpoints (minimal API, no controllers)
│   └── html/             # Frontend (vanilla JS, served by nginx)
│       ├── App.html
│       ├── Login.html
│       ├── css/
│       └── js/
│           └── Api.js            # All fetch wrappers; uses window.location.origin as base URL
├── infrastructure/
│   ├── Dockerfile            # Production image
│   ├── DevDockerfile         # Development image
│   ├── nginx-prod.conf       # Container nginx config (production)
│   ├── nginx-dev.conf        # Container nginx config (development)
│   ├── start.sh              # Production startup (tailscaled + dotnet + nginx)
│   └── start-dev.sh          # Dev startup (dotnet + nginx only)
└── docs/
    └── ARCHITECTURE.md
```

---

## Backend

- **Framework:** ASP.NET Core minimal APIs (.NET 10), all endpoints in `Program.cs`
- **ORM:** Dapper with raw SQL against PostgreSQL via Npgsql
- **Auth:** JWT (8-hour lifetime), stored in `localStorage` as `jwt_token`
  - Roles: `ADMIN` / `REGULAR`; per-user feature flags in `user_config` table
- **Soft deletes:** `retired = true` instead of row deletion (e.g. drivers table)
- **Listens on:** `http://+:5000` (configured via `ASPNETCORE_URLS` env var)

### Environment Variables

| Variable | Description |
|---|---|
| `DB_HOST_IP` | PostgreSQL host |
| `DB_PORT` | PostgreSQL port (5432) |
| `DB_NAME` | Database name |
| `DB_USER` | DB username |
| `DB_PASS` | DB password |
| `JWT_SECRET` | JWT signing secret (32+ chars) |
| `JWT_ISSUER` | Defaults to `RosterAPI` |
| `JWT_AUDIENCE` | Defaults to `RosterClient` |
| `ASPNETCORE_URLS` | e.g. `http://+:5000` |
| `TAILSCALE_AUTHKEY` | Tailscale reusable+ephemeral auth key (production only) |

---

## Frontend

- **Vanilla JS** (ES6 modules), no framework
- **API base URL:** always `window.location.origin` — no hardcoded hostnames
- **Routing between pages:** absolute paths (e.g. `window.location.href = 'Login.html'`)
- **Third-party libs:** loaded from CDN (jsDelivr, unpkg) — no node_modules in repo
- **Served by:** nginx inside the Docker container from `/app/html`

---

## Docker Images

### Production (`Dockerfile`)

- **Build context:** repo root
- **Multi-stage:** SDK build stage → ASP.NET runtime stage
- **Installs:** nginx, Tailscale (via `apk add tailscale` — do **not** use the install script, it calls `rc-update` which doesn't exist in Docker)
- **HTML files:** copied to `/app/html` with `--chown=www-data:www-data` so nginx can serve them
- **Startup:** `start.sh` — starts `tailscaled`, runs `tailscale up`, then dotnet, then nginx
- **Exposes:** port 80 (nginx)

**Build command (from build context root):**
```bash
docker build -t org/appname:v1.x -f path/to/repo/infrastructure/Dockerfile .
```

**Run command:**
```bash
docker run -d --restart unless-stopped --name appname-v1 \
  -e TZ='Europe/Amsterdam' \
  --add-host=host.docker.internal:host-gateway \
  -e DB_HOST_IP='host.docker.internal' \
  -e DB_PORT='5432' \
  -e DB_USER='postgres' \
  -e DB_PASS='<db-password>' \
  -e DB_NAME='<db-name>' \
  -e ASPNETCORE_URLS='http://+:5000' \
  -e TAILSCALE_AUTHKEY='<reusable-ephemeral-auth-key>' \
  -p 808x:80 \
  --cap-add NET_ADMIN --cap-add NET_RAW \
  --device /dev/net/tun \
  org/appname:v1.x
```
> Port `808x` is unique per container on the production server (e.g. `8080`, `8081`, `8082`, ...).

### Development (`DevDockerfile`)

- **Build context:** repo root
- **Network:** `--network host` (shares host network, reaching DB via host Tailscale)
- **No Tailscale** inside the container — relies on host machine's Tailscale
- **Startup:** `start-dev.sh` — starts dotnet and nginx only
- **HTML files:** copied to `/var/www/html` with `--chown=www-data:www-data` so nginx can serve them
- **DB credentials:** hardcoded as `ENV` in the Dockerfile (dev only)

---

## Server Layout

There are two physical servers:

| Server | Role | Tailscale IP |
|---|---|---|
| **Production server** | Runs host nginx + all production containers | `100.100.104.47` |
| **Development server** | Runs development containers | `100.86.198.113` |

PostgreSQL runs on the **production server**.

---

## Network Architecture

```
Internet
   │
   ▼
Production server — Host nginx (port 443, SSL termination)
   │
   ├── server_name: appname.domain.com
   │     proxy_pass http://localhost:808x  ──▶  Production container (same server)
   │                                               │
   │                                    Container nginx (port 80)
   │                                      ├── /api/  →  Kestrel :5000
   │                                      └── /      →  /app/html
   │                                               │
   │                                    Kestrel connects to PostgreSQL
   │                                    via host.docker.internal:5432
   │                                    (172.17.0.1 = production server)
   │
   └── server_name: appname-dev.domain.com
         proxy_pass http://<dev-server-ip>:80  ──▶  Development server
                                                       │
                                            Dev container (--network host)
                                            Container nginx (port 80)
                                              ├── /api/  →  Kestrel :8080
                                              └── /      →  /var/www/html
                                                       │
                                            Kestrel connects to PostgreSQL
                                            via Tailscale (100.100.104.47:5432)
```

### Subdomain Routing

Each application gets its own subdomain — no path-based prefixes:

| Subdomain | Routes to | How |
|---|---|---|
| `appname.domain.com` | Production container on prod server | `proxy_pass http://localhost:808x` |
| `appname-dev.domain.com` | Dev container on dev server | `proxy_pass http://<dev-server-ip>:80` |

Production containers each get a unique port starting from 8080 (`-p 8080:80`, `-p 8081:80`, etc.).

---

## SSL (Let's Encrypt)

```bash
certbot --nginx -d appname.domain.com -d appname-dev.domain.com \
  --non-interactive --agree-tos -m admin@domain.com
```

- Certbot auto-configures nginx and sets up auto-renewal
- One cert can cover multiple subdomains (SAN cert)
- Use individual certs per subdomain if wildcard is unavailable

---

## PostgreSQL Access

### From production container (bridge network)
- Container connects via `host.docker.internal` → resolves to `172.17.0.1`
- Requires `--add-host=host.docker.internal:host-gateway` in `docker run`
- `postgresql.conf`: `listen_addresses = '*'`
- `pg_hba.conf`: `host all all 172.16.0.0/12 scram-sha-256`
- UFW: `ufw allow from 172.16.0.0/12 to any port 5432`

### From dev server (via Tailscale on host)
- Dev container uses `--network host`, inherits host Tailscale routes
- DB host IP is the Tailscale IP of the DB server
- `pg_hba.conf`: `host all all 100.64.0.0/10 scram-sha-256`
- UFW on DB server: `ufw allow from 100.64.0.0/10 to any port 5432`

---

## Tailscale (Production Container)

- Installed inside the production Docker container
- Auth key: **reusable + ephemeral** (generate once in Tailscale admin, node auto-removes on stop)
- Requires: `--cap-add NET_ADMIN --cap-add NET_RAW --device /dev/net/tun`
- All Tailscale nodes get IPs in `100.64.0.0/10` (IANA CGNAT range, stable)

---

## Dockerfile HTML Copy Pattern

nginx runs as `www-data` inside the container. Always use `--chown` when copying HTML files, or nginx will get permission errors:

```dockerfile
# Production (html at /app/html)
COPY --chown=www-data:www-data src/html /app/html

# Development (html at /var/www/html)
COPY --chown=www-data:www-data src/html /var/www/html
```

---

## Container nginx Config Pattern

```nginx
server {
    listen 80;

    access_log /var/log/nginx/access.log;
    error_log  /var/log/nginx/error.log;

    location /api/ {
        proxy_pass http://localhost:5000/api/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        root /app/html;
        index App.html;
        try_files $uri $uri/ =404;
    }
}
```

## Host nginx Config Pattern

```nginx
# Production container (same server, unique port per app)
server {
    server_name appname.domain.com;

    listen 443 ssl;
    ssl_certificate /etc/letsencrypt/live/appname.domain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/appname.domain.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    add_header Strict-Transport-Security "max-age=63072000" always;

    location / {
        proxy_pass http://localhost:8080;   # increment per app: 8081, 8082, ...
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# Development container (on separate dev server)
server {
    server_name appname-dev.domain.com;

    listen 443 ssl;
    ssl_certificate /etc/letsencrypt/live/appname.domain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/appname.domain.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    add_header Strict-Transport-Security "max-age=63072000" always;

    location / {
        proxy_pass http://<dev-server-ip>:80;  # dev container uses --network host
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# HTTP → HTTPS redirect (all domains in one block)
server {
    listen 80;
    server_name appname.domain.com appname-dev.domain.com;
    return 301 https://$host$request_uri;
}
```

# Environment and configuration

Everything you need to set to run RelayForge from a fresh clone. There are three places to configure: the database, the backend (.NET API) and the frontend (Next.js). No real secrets are committed to this repo.

## What you need installed

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 8 | Backend API |
| Node.js | 18 or newer (20+ recommended) | Frontend |
| Docker | any recent | Local SQL Server (on macOS and Linux; skip if you have another SQL Server) |

## Quick start (three terminals)

1. **Database:** start SQL Server and set the connection string (below).
2. **Backend:** `cd backend && dotnet tool restore && dotnet tool run dotnet-ef database update -p src/RelayForge.Infrastructure -s src/RelayForge.Api && dotnet run --project src/RelayForge.Api`
3. **Frontend:** `cd frontend && cp .env.local.example .env.local && npm install && npm run dev`, then open http://localhost:3000

Check the API is up: `http://localhost:5284/health` should return `{"status":"ok"}`.

## 1. Database (SQL Server)

SQL Server has no native macOS build, so run it in Docker:

```bash
docker run -d --name relayforge-sqlserver \
  -e "ACCEPT_EULA=Y" \
  -e "MSSQL_SA_PASSWORD=<choose-a-strong-password>" \
  -p 1434:1433 \
  mcr.microsoft.com/mssql/server:latest
```

- The password needs 8+ characters with upper case, lower case, a digit and a symbol, or the container exits immediately.
- Port `1434` on your machine maps to `1433` in the container, so it does not clash with another SQL Server you may already run.
- Any reachable SQL Server works instead (Azure SQL, Windows, a remote host). You only need its connection string.

## 2. Backend settings (`backend/src/RelayForge.Api`)

| Setting | Required | Example value | Notes |
|---|---|---|---|
| `ConnectionStrings:Default` | **Yes** | `Server=localhost,1434;Database=RelayForge;User Id=sa;Password=<your-password>;TrustServerCertificate=True;Encrypt=False;` | Use the same password as the Docker command. Never put the real value in a committed file. |
| `WorkerPool:Concurrency` | No (default `4`) | `4` | How many steps run at the same time. |
| `Reliability:BaseBackoffMs` | No (default `500`) | `500` | First retry delay. It doubles each retry. |
| `Reliability:MaxBackoffMs` | No (default `8000`) | `8000` | Longest retry delay. |
| `Reliability:DefaultMaxAttempts` | No (default `4`) | `4` | Tries per step when a step does not set its own. |
| `Reliability:LeaseBufferMs` | No (default `15000`) | `15000` | How long a worker may hold a step before it is treated as crashed. |
| `Reliability:SweepIntervalMs` | No (default `1000`) | `1000` | How often the system looks for crashed steps. |
| `ASPNETCORE_ENVIRONMENT` | No | `Development` | Set automatically by `dotnet run` through `launchSettings.json`. |

**How to set the connection string (pick one):**

- **User secrets (recommended for local use, stored outside the repo):**
  ```bash
  cd backend
  dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1434;Database=RelayForge;User Id=sa;Password=<your-password>;TrustServerCertificate=True;Encrypt=False;" --project src/RelayForge.Api
  ```
- **Environment variable** (also how you would set it on a server; double underscore replaces the colon):
  ```bash
  export ConnectionStrings__Default="Server=localhost,1434;Database=RelayForge;User Id=sa;Password=<your-password>;TrustServerCertificate=True;Encrypt=False;"
  ```
  Any setting in the table can be set this way, for example `Reliability__DefaultMaxAttempts=6`.

`appsettings.Development.json` only contains a placeholder (`REPLACE-ME`). Do not put your real password in it.

**Azure SQL instead of Docker:** `Server=tcp:<name>.database.windows.net,1433;Database=RelayForge;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;`

**Create the tables** (run once, and again after pulling new migrations):
```bash
cd backend
dotnet tool restore
dotnet tool run dotnet-ef database update -p src/RelayForge.Infrastructure -s src/RelayForge.Api
```

**Ports:** the API listens on `http://localhost:5284` (and `https://localhost:7054` with the https profile).

## 3. Frontend settings (`frontend/.env.local`)

Copy the example: `cp .env.local.example .env.local`.

| Variable | Required | Example value | Notes |
|---|---|---|---|
| `NEXT_PUBLIC_API_URL` | Yes | `http://localhost:5284` | Where the browser finds the API. It is read when the app starts, so restart `npm run dev` after changing it. |

`NEXT_PUBLIC_` variables are visible to anyone using the site, so never put a secret in one.

## Things that commonly go wrong

- **"Could not reach the API" in the browser:** the backend is not running, or `NEXT_PUBLIC_API_URL` points at the wrong port. Use `http://localhost:5284` unless you started the https profile.
- **Works on `localhost:3000` but not from your phone or another machine:** the backend only accepts requests from `http://localhost:3000` (CORS, in `backend/src/RelayForge.Api/Program.cs`). Add your origin there to use another address. Next.js also needs that address listed under `allowedDevOrigins` in `frontend/next.config.ts`.
- **API fails at startup with a database error:** the connection string is empty or wrong, the container is not running (`docker ps`), or the migration has not been applied.
- **Docker container exits straight away:** the SQL Server password does not meet the complexity rules.
- **A run stays "Running" forever after you stopped the API:** the worker queue is in memory. Delete that run, or restart and start a new one.
- **Port 1434 or 3000 already in use:** change the left side of `-p 1434:1433`, or start Next.js with `npm run dev -- -p 3001` (then the CORS origin above must change too).

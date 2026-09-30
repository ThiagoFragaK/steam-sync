# steam-sync

Standalone **.NET 10** background worker that syncs Steam libraries and achievements into the shared [AchievHub](../achiev-hub) PostgreSQL database.

Jobs are published by the AchievHub API and consumed from RabbitMQ (`steam_sync_jobs`) via MassTransit.

## What it does

- Pulls owned / recently played games from the Steam Web API
- Upserts games, playtime, and user–game links in Postgres
- Syncs achievement schemas and unlock progress per game
- Optionally enriches game metadata from the Steam Store (capped per job)
- Tracks per-user progress in `user_sync_status` (`Pending` → `Syncing` → `Complete` / `Failed`)
- Runs a nightly cron that fans out `recent_activity_only` (+ crawl) jobs for active users

## Architecture

```
achiev-hub API  --publish UserSyncJob-->  RabbitMQ  -->  steam-sync Worker (×N)
                         |                                    |
                         +-------- shared Postgres -----------+
                                   user_sync_status
```

| JobType | Behavior |
|---------|----------|
| `full_library_resync` | Full owned library + priority achievement sync + crawl |
| `recent_activity_only` | Recent games + priority sync (+ crawl when `IncludeCrawl`) |
| `user_sync` | Single-game achievements when `AppId` is set; otherwise recent activity |

Shared contracts live in `SteamSync.Shared` (`UserSyncJob`, queue names, sync status) and are referenced by both this worker and the AchievHub API.

## Tech stack

| Piece | Choice |
|-------|--------|
| Runtime | .NET 10 Worker |
| Messaging | MassTransit + RabbitMQ |
| Database | EF Core + Npgsql (shared AchievHub schema) |
| HTTP / resilience | `HttpClient` + Polly / standard resilience handler |
| Logging | Serilog |
| Metrics | OpenTelemetry (OTLP) |
| Scheduling | Cronos (`NightlyMaintenanceWorker`) |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (RabbitMQ / Postgres / Testcontainers)
- [Steam Web API key](https://steamcommunity.com/dev/apikey)

## Local setup

Both composes share the external Docker network `hub` (`-p hub`).

1. Create the network (once):

```bash
docker network create hub
```

2. Start AchievHub Postgres + API, then RabbitMQ + worker:

```bash
export STEAM_API_KEY=YOUR_KEY

cd ../achiev-hub
docker compose -p hub up -d --build

cd ../steam-sync
docker compose -p hub up -d --build
```

3. Apply AchievHub EF migrations (includes `user_sync_status`).

4. Or run the worker on the host (against published ports):

```bash
# Worker
export SteamApi__ApiKey=YOUR_KEY
export ConnectionStrings__Postgres="Host=localhost;Port=6110;Database=achievhub;Username=postgres;Password=postgres;SSL Mode=Disable"
export RabbitMQ__Host=localhost

cd src/SteamSync.Worker
dotnet run
```

Services: `postgres` + `api` (AchievHub), `rabbitmq` + `steam-sync-worker` (this repo), all on network `hub`.

## Configuration

| Variable | Description |
|----------|-------------|
| `ConnectionStrings__Postgres` | Shared AchievHub DB |
| `RabbitMQ__Host` / `Port` / `Username` / `Password` / `Url` / `VirtualHost` | Broker |
| `SteamApi__ApiKey` | Steam Web API key (**required**) |
| `SyncWorker__Concurrency` | Steam API concurrency gate (default `3`) |
| `SyncWorker__BatchSize` | Achievement crawl batch size (default `25`) |
| `SyncWorker__SchemaTtlDays` | How long game schemas are considered fresh (default `14`) |
| `SyncWorker__NightlyCron` | UTC cron for nightly fan-out (default `0 0 * * *`) |
| `SyncWorker__MaxStoreEnrichPerLibrarySync` | Max Store API enrich calls per library sync (default `5`) |
| `SyncWorker__MaxAttempts` | Sync attempt budgeting (default `5`) |

## Queue monitoring

1. Open http://localhost:15672 (default `guest` / `guest`).
2. Queues → `steam_sync_jobs` — watch Ready / Unacked depth.
3. After MassTransit’s immediate retries (3), faults publish to the error / dead-letter topology (`steam_sync_dlx` in contracts).
4. Alert guidance: queue depth &gt; 100 or failure rate &gt; 5% (pair with OpenTelemetry `jobs_failed_total` / `jobs_processed_total`).

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| Jobs stuck in `Pending` | Worker not running / wrong RabbitMQ host / queue name mismatch |
| Status `Failed` + Steam errors | Invalid API key, private Steam profile, or rate limits |
| User stays Provisioning | Full sync never reached `Complete` (`LastFullSync` null) |
| API register 500 on enqueue | RabbitMQ unreachable from the API container |
| Duplicate progress worries | Multiple workers are fine; each message is processed once (MassTransit ack) |

## Tests

```bash
dotnet test

# Unit (Moq + EF InMemory)
dotnet test tests/SteamSync.Worker.Tests

# Integration (Docker required for Testcontainers)
dotnet test tests/SteamSync.IntegrationTests
```

## Project layout

```
src/SteamSync.Shared/     # UserSyncJob contract + sync status DTOs (API + worker)
src/SteamSync.Worker/     # Hosted worker, MassTransit consumer, EF writes, nightly cron
tests/                    # Unit + integration
Dockerfile
docker-compose.yml
SteamSync.slnx
```

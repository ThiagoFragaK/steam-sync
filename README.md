# steam-sync

Standalone **.NET 10** background worker that syncs Steam libraries and achievements into the shared [AchievHub](../achiev-hub) PostgreSQL database.

Jobs are published as plain JSON `UserSyncJob` messages on RabbitMQ (`steam_sync_jobs`) and consumed by the worker via RabbitMQ.Client.

## What it does

- Pulls owned / recently played games from the Steam Web API
- Upserts games, playtime, and user–game links in Postgres
- Syncs achievement schemas and unlock progress per game
- Optionally enriches game metadata from the Steam Store (capped per job)
- Tracks per-user progress in `user_sync_status` (`Pending` → `Syncing` → `Complete` / `Failed`)
- Runs a daily cron that fans out `recent_activity_only` (+ crawl) jobs for active users

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

Message contracts live in `SteamSync.Shared` (`UserSyncJob`, queue names, sync status). Publishers keep their own copy of the queue names and message shapes; nothing outside this repo references these projects.

## Tech stack

| Piece | Choice |
|-------|--------|
| Runtime | .NET 10 Worker |
| Messaging | RabbitMQ.Client (JSON jobs) |
| Database | EF Core + Npgsql (shared AchievHub schema) |
| HTTP / resilience | `HttpClient` + Polly / standard resilience handler |
| Logging | Serilog |
| Metrics | OpenTelemetry (OTLP) |
| Scheduling | Cronos (`DailyUpdateWorker`) |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (RabbitMQ / Postgres / Testcontainers)
- [Steam Web API key](https://steamcommunity.com/dev/apikey)

## Local setup

The compose file runs only the worker. It does not start RabbitMQ or Postgres; it connects to an existing broker and
database through configuration (by default, the ones published on the Docker host).

```bash
export STEAM_API_KEY=YOUR_KEY
docker compose up -d --build
```

| Variable | Default in compose |
|----------|--------------------|
| `ConnectionStrings__Postgres` | Postgres on the Docker host, port `6110` (`host.docker.internal`) |
| `RabbitMQ__Host` / `Port` / `Username` / `Password` | RabbitMQ on the Docker host, port `5672`, `guest` / `guest` |

The broker must already be running. If it is not reachable, the startup check retries and then exits with code `1`.

The database schema (including `user_sync_status`) must already exist; this worker does not run migrations.

Or run the worker on the host:

```bash
export SteamApi__ApiKey=YOUR_KEY
export ConnectionStrings__Postgres="Host=localhost;Port=6110;Database=achievhub;Username=postgres;Password=postgres;SSL Mode=Disable"
export RabbitMQ__Host=localhost

cd src/SteamSync.Worker
dotnet run
```

## Startup checks and queues

The worker **never declares queues or exchanges**. It assumes the queues it consumes from and publishes to (see
`SyncQueueNames`) are created by whoever provisions the broker.

Before any consumer starts, the worker runs its startup checks in order:

1. **Database**: opens a connection to `ConnectionStrings__Postgres`.
2. **RabbitMQ**: opens a connection and channel to the broker.

Each check logs `OK` or the specific error. A failing check is retried (`StartupHealthChecks__MaxAttempts`, default `5`,
every `StartupHealthChecks__DelaySeconds`, default `5`). If it still fails, the worker logs the error and exits with code `1`.

If a queue does not exist yet, its consumer logs `Queue ... does not exist yet; retrying in 5s` and starts consuming as
soon as the queue appears. Note that publishing to a queue that does not exist silently drops the message.

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
| `StartupHealthChecks__MaxAttempts` / `DelaySeconds` / `TimeoutSeconds` | Startup check retries (defaults `5` / `5` / `10`) |

## Queue monitoring

1. Open http://localhost:15672 (default `guest` / `guest`).
2. Queues → `steam_sync_jobs` — watch Ready / Unacked depth.
3. On failure the consumer retries up to `SyncWorker__MaxAttempts`, then publishes to the dead-letter topology (`steam_sync_dlx` / `steam_sync_jobs_dlq`).
4. Alert guidance: queue depth &gt; 100 or failure rate &gt; 5% (pair with OpenTelemetry `jobs_failed_total` / `jobs_processed_total`).

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| Jobs stuck in `Pending` | Worker not running / wrong RabbitMQ host / queue name mismatch |
| Status `Failed` + Steam errors | Invalid API key, private Steam profile, or rate limits |
| User stays Provisioning | Full sync never reached `Complete` (`LastFullSync` null) |
| API register 500 on enqueue | RabbitMQ unreachable from the API container |
| Duplicate progress worries | Multiple workers are fine; each message is acked once after success or final failure |

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
src/SteamSync.Worker/     # Hosted worker, SteamSyncConsumer, EF writes, DailyUpdateWorker
tests/                    # Unit + integration
Dockerfile
docker-compose.yml
SteamSync.slnx
```

# steam-sync

Standalone .NET 10 worker that syncs Steam libraries and achievements into the shared AchievHub PostgreSQL database. Jobs are consumed from RabbitMQ (`steam_sync_jobs`).

## Architecture

```
achiev-hub API  --publish UserSyncJob-->  RabbitMQ  -->  steam-sync Worker (x2)
                         |                                    |
                         +-------- shared Postgres -----------+
                                   user_sync_status
```

| JobType | Behavior |
|---------|----------|
| `full_library_resync` | Full owned library + priority + crawl |
| `recent_activity_only` | Recent games + priority (+ crawl if `IncludeCrawl`) |
| `user_sync` | Single-game achievements when `AppId` is set |

## Prerequisites

- .NET 10 SDK
- Docker (for RabbitMQ / Postgres / Testcontainers)
- Steam Web API key

## Local setup

1. Start Postgres + RabbitMQ (from achiev-hub compose or steam-sync compose).
2. Apply achiev-hub EF migrations (includes `user_sync_status`).
3. Configure secrets:

```bash
# Worker
export SteamApi__ApiKey=YOUR_KEY
export ConnectionStrings__Postgres="Host=localhost;Port=6110;Database=achievhub;Username=postgres;Password=postgres;SSL Mode=Disable"
export RabbitMQ__Host=localhost

# API (achiev-hub)
export SteamApi__ApiKey=YOUR_KEY
export Jwt__Key=your-32-char-minimum-secret-key!!
export RabbitMQ__Host=localhost
```

4. Run worker:

```bash
cd src/SteamSync.Worker
dotnet run
```

5. Full stack (from `achiev-hub`):

```bash
cd ../achiev-hub
# context is parent MyApps folder
docker compose up --build
```

Services: `api`, `steam-sync-worker` (2 replicas), `rabbitmq`, `postgres`.

## Queue monitoring (RabbitMQ Management UI)

1. Open http://localhost:15672 (guest/guest by default).
2. Queues → `steam_sync_jobs` — watch Ready / Unacked depth.
3. After 3 failed MassTransit retries, faults land on the error / dead-letter topology (`steam_sync_dlx` naming in contracts).
4. Alert guidance: queue depth > 100 or failure rate > 5% (pair with OpenTelemetry `jobs_failed_total` / `jobs_processed_total`).

## Configuration

| Variable | Description |
|----------|-------------|
| `ConnectionStrings__Postgres` | Shared AchievHub DB |
| `RabbitMQ__Host` / `Port` / `Username` / `Password` / `Url` | Broker |
| `SteamApi__ApiKey` | Steam Web API key |
| `SyncWorker__Concurrency` | Steam API gate (default 3) |
| `SyncWorker__BatchSize` | Crawl batch size |
| `SyncWorker__NightlyCron` | Cron for fan-out (default `0 0 * * *`) |

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| Jobs stuck in Pending | Worker not running / wrong RabbitMQ host / queue name mismatch |
| Status Failed + Steam errors | Invalid API key, private Steam profile, or rate limits |
| User stays Provisioning | Full sync never reached Complete (`LastFullSync` null) |
| API register 500 on enqueue | RabbitMQ unreachable from API container |
| Duplicate progress | Multiple workers OK; one message is processed once (MassTransit ack) |

## Tests

```bash
dotnet test
# Unit tests (Moq + EF InMemory)
dotnet test tests/SteamSync.Worker.Tests
# Integration (requires Docker for Testcontainers)
dotnet test tests/SteamSync.IntegrationTests
```

## Project layout

```
src/SteamSync.Shared/     # UserSyncJob contract (referenced by API)
src/SteamSync.Worker/     # Hosted worker, MassTransit consumer, EF writes
tests/                    # Unit + integration
Dockerfile
docker-compose.yml
```

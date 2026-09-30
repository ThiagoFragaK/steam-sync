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

## Deploy on Railway

`docker-compose.yml` runs the worker and RabbitMQ. Postgres is the existing AchievHub database. The worker does not apply migrations, so that schema has to exist already.

Local `docker compose up` keeps the defaults (`guest` / `guest`, broker host `rabbitmq`, Postgres on `host.docker.internal:6110`). On Railway, override them:

| Variable | Value |
|----------|--------|
| `DOTNET_ENVIRONMENT` | `Production` |
| `RABBITMQ_USER` | `steam` (do not use `guest`; it cannot connect from another container) |
| `RABBITMQ_PASSWORD` | a long random password |
| `RABBITMQ_HOST` | `${{rabbitmq.RAILWAY_PRIVATE_DOMAIN}}` |
| `STEAM_API_KEY` | Steam Web API key |
| `ConnectionStrings__Postgres` | Npgsql connection string for the AchievHub database, with `SSL Mode=Require` |

`DATABASE_URL` (`postgresql://...`) is not accepted. Use a semicolon-separated Npgsql string:

```text
Host=<host>;Port=<port>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require
```

The achiev-hub API has to publish to this same broker. In the same Railway project, set the API to:

```text
RabbitMQ__Host=${{rabbitmq.RAILWAY_PRIVATE_DOMAIN}}
RabbitMQ__Port=5672
RabbitMQ__Username=<same as RABBITMQ_USER>
RabbitMQ__Password=<same as RABBITMQ_PASSWORD>
RabbitMQ__VirtualHost=/
```

If the API runs outside this project, expose RabbitMQ port `5672` with a TCP proxy and point the API at that host. Leave the worker private.

Run **one** worker instance. Each instance includes the nightly fan-out, so a second replica enqueues that pass twice. Set the replica count to 1 in the Railway service settings.

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
docker-compose.yml        # worker + RabbitMQ (local defaults, Railway via env)
```

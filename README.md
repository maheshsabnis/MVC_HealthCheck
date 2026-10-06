# Order Processing with Health Checks

ASP.NET Core 10 MVC application demonstrating `AspNetCore.Diagnostics.HealthChecks` (Xabaril)
against four real dependencies, wrapped around a working order processing pipeline.

## Pipeline

```
Create Order (MVC)
      |
      v
  SQL Server            Orders / OrderDetails / Customers / Products
      |
      v
  RabbitMQ              order-processing-queue (durable)
      |
      v
  Background worker     advance >= 50% of total ? Accepted : Rejected
      |
      +--> PostgreSQL   processed_orders (the decision record)
      +--> Redis        received / processed activity lists and counters
```

## Health checks

Registered in `Program.cs` via `AddHealthChecks()`:

| Name | Package | Failure status | Tags |
|------|---------|----------------|------|
| `sql-server` | `AspNetCore.HealthChecks.SqlServer` | Unhealthy | db, sql, sqlserver, ready |
| `postgresql` | `AspNetCore.HealthChecks.NpgSql` | Unhealthy | db, sql, postgres, ready |
| `redis` | `AspNetCore.HealthChecks.Redis` | Degraded | cache, redis, ready |
| `rabbitmq` | `AspNetCore.HealthChecks.Rabbitmq` | Unhealthy | broker, rabbitmq, ready |

Endpoints:

| Route | Purpose |
|-------|---------|
| `/health` | All checks, custom indented JSON (`HealthChecks/HealthCheckResponseWriter.cs`) |
| `/health/ready` | Readiness: only checks tagged `ready` |
| `/health/live` | Liveness: no dependency is probed, 200 if the process is up |
| `/health/ui-data` | The `UIResponseWriter` payload the UI polls |
| `/health-ui` | The polling dashboard, refreshed every 15s |

The dashboard at `/` also renders the same report in-process via `HealthCheckService`.

## Screens

| Route | Screen |
|-------|--------|
| `/` | Dashboard: dependency health, Redis counters, received and processed activity |
| `/Orders/Create` | Create an order, with a live advance-percentage meter |
| `/Orders/Status` | Order status from SQL Server, filterable by status |
| `/Orders/Details/{id}` | One order: lines, advance, and the PostgreSQL outcome |
| `/Orders/Processed` | The `processed_orders` table from PostgreSQL |
| `/Orders/Decisions` | Accepted and Rejected side by side, filterable |

## Prerequisites

SQL Server and PostgreSQL run locally; Redis and RabbitMQ run in Docker:

```bash
docker run -d --name hc-redis -p 6379:6379 redis:7-alpine
docker run -d --name hc-rabbitmq -p 5672:5672 -p 15672:15672 \
  -e RABBITMQ_DEFAULT_USER=guest -e RABBITMQ_DEFAULT_PASS=guest rabbitmq:3-management
```

RabbitMQ management UI: http://localhost:15672 (guest/guest).

## Configuration

`appsettings.json`:

```json
"ConnectionStrings": {
  "SqlServer": "Server=.;Database=OrderProcessingDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True",
  "PostgreSql": "Host=localhost;Port=5432;Database=orderprocessing;Username=postgres;Password=...",
  "Redis": "localhost:6379"
}
```

The accept/reject threshold is configurable:

```json
"OrderProcessing": {
  "MinimumAdvancePercentage": 50,
  "SimulatedWorkMilliseconds": 750,
  "CacheListLength": 50
}
```

Both schemas are created on startup by `Data/DatabaseInitializer.cs` (`EnsureCreated`), which also
seeds five customers and eight products. Initialisation failures are logged rather than thrown, so
a database outage shows up on `/health` instead of blocking startup.

## Run

```bash
dotnet run --project MVC_HealthCheck --urls http://localhost:5103
```

A local `NuGet.config` pins restore to nuget.org, because the organisation's private Azure DevOps
feed returns 401 in this environment.

## Design notes

- **Pricing is recomputed server side** in `OrderService.CreateOrderAsync` from the catalogue, so a
  tampered form cannot change the accept/reject outcome.
- **The worker is idempotent**: a redelivered message updates the existing `processed_orders` row
  (unique index on `order_id`) rather than inserting a duplicate.
- **Poison messages are not requeued.** A message that throws is nacked with `requeue: false` and
  the order is left in the `Failed` state, visible on the Order Status screen.
- **Redis is treated as optional.** Every cache call swallows connection errors and degrades to an
  empty result, so a Redis outage shows on `/health` without taking the UI down.
- **The RabbitMQ connection is re-established on demand.** The health check uses the async factory
  overload, so the provider gets a chance to reconnect on every probe instead of pinning a dead
  connection.

## Known issues

- `AspNetCore.HealthChecks.UI` 9.0.0 pulls in `KubernetesClient` 15.0.1, which carries a moderate
  severity advisory (GHSA-w7r3-mgwf-4mqq). It surfaces as NU1902 on every build. Removing the UI
  packages drops the warning; the `/health` endpoints do not depend on them.
- `AspNetCore.HealthChecks.UI.InMemory.Storage` 9.0.0 resolves EF Core 8's InMemory provider, which
  calls a method removed in EF Core 10 and crashes at startup. Fixed by referencing
  `Microsoft.EntityFrameworkCore.InMemory` 10.0.12 explicitly.

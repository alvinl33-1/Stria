---
name: ef-core
description: EF Core performance and correctness practices for Stria's Npgsql/PostgreSQL data layer. Use when writing or reviewing queries, DbContext lifetime, migrations, or tracking behaviour in Stria.API/Data.
---

# EF Core for Stria

Stria uses EF Core 10 with Npgsql and registers `AddDbContextFactory<StriaDbContext>` (not a scoped
DbContext) so the singleton `DatabaseChaosOrchestrator` and hosted services create one short-lived
context per operation. Keep that pattern.

## Rules for this repo

- Create contexts with `await using var db = await factory.CreateDbContextAsync(ct);` — never hold
  one in a field of a singleton.
- Read-only queries use `AsNoTracking()`; project to DTOs with `Select` instead of loading entities.
- Pass `CancellationToken` through every async EF call.
- Migrations live in `Stria.API/Data/Migrations`. Add with
  `dotnet ef migrations add <Name> --project Stria.API`. Never edit an applied migration.
- `Database:MigrateOnStartup` is a local/Compose convenience only. For Azure, ship an EF migration
  bundle (`dotnet ef migrations bundle`) and run it as a release step — see the
  `compose-to-container-apps` and `database-migrations-sql-migrations` skills.
- Integration tests use Testcontainers PostgreSQL (`Stria.Tests/TestSupport/PostgresFixture.cs`);
  add a test there for any query whose behaviour depends on PostgreSQL semantics.

## Reference

Detailed patterns (query optimisation, batching, compiled queries, concurrency):
[references/ef-core-best-practices.md](references/ef-core-best-practices.md).

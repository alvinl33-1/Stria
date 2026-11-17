---
name: clean-architecture
description: Dependency-direction and layering review based on Clean Architecture. Use when adding a new service, moving code between Stria.API Domain/Services/Data/DTOs, or reviewing whether a change leaks infrastructure into domain code.
---

# Clean Architecture review

Stria's layout maps roughly to:

| Layer | Stria location |
|---|---|
| Entities / domain rules | `Stria.API/Domain` (`ChaosExperiment`, `ChaosRules`) |
| Use cases / orchestration | `Stria.API/Services` (`IChaosOrchestrator` and implementations) |
| Adapters | `Stria.API/Data` (EF Core), `Stria.API/DTOs`, minimal API endpoints in `Program.cs`, Blazor components |
| Frameworks / hosting | ASP.NET Core host, Npgsql, Azure SDKs |

## Checklist

1. Domain types do not reference EF Core, ASP.NET Core, HTTP, or Azure SDK types.
2. Services depend on interfaces they own; concrete adapters are registered in `Program.cs`.
3. Validation that expresses a business rule lives in `ChaosRules`, not in endpoint lambdas.
4. Cloud concerns (Key Vault, managed identity, telemetry) are configured at the composition root,
   never inside domain or service classes.
5. Prefer the smallest change that keeps dependencies pointing inward; do not introduce extra
   projects or layers without a concrete reason.

## Reference

Full criteria and examples: [references/clean-architecture.md](references/clean-architecture.md).

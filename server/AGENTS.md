# NovaGame Server Instructions

These instructions extend the repository-level `AGENTS.md` for files under `server/`.

## Required Reading

Before server work, read:

- `docs/architecture.md`;
- `docs/workflows/development.md`;
- `docs/goals/m0.5.md` when working on the active milestone.

## Technology Baseline

- .NET 8
- NovaCore packages produced from the configured local NovaCore source
- Entity Framework Core 8
- Pomelo Entity Framework Core provider
- MySQL 8.4

SQLite is not a supported M0.5 runtime or test target.

## Layer Structure

The business architecture uses six standard layer projects. The server also contains two non-domain operational projects:

```text
src/
  NovaGame.Domain.Shared
  NovaGame.Domain
  NovaGame.Application.Contracts
  NovaGame.Application
  NovaGame.Host.EntityFrameworkCore
  NovaGame.Deployment
  NovaGame.DbMigrator
  NovaGame.Api
```

`NovaGame.Deployment` centralizes deployment-time configuration validation. `NovaGame.DbMigrator` is the migration and data-seeding composition root. Neither project is a business-domain module.

Organize every layer by the owning business domain. Do not create one physical project or top-level entity module per Player, Inventory, Equipment, Mail, Ranking, or other catalog entry.

Allowed dependency direction:

- `Domain.Shared` depends only on stable NovaCore building blocks and base libraries.
- `Domain` depends on `Domain.Shared`.
- `Application.Contracts` depends on `Domain.Shared` and contains public DTOs and service contracts.
- `Application` depends on `Application.Contracts` and `Domain`.
- `Host.EntityFrameworkCore` depends on `Domain` and owns EF Core configuration, repositories, migrations, and provider setup.
- `Deployment` owns shared operational configuration validation and has no business-layer dependency.
- `DbMigrator` may reference Application, Deployment, and persistence to apply migrations and run configured seed contributors.
- `Api` is the HTTP composition root and may reference Application, Deployment, and persistence composition projects.

Domain and Application code must not depend on EF Core, controllers, HTTP context, or MySQL-specific APIs.

## Domain And Application Rules

- Keep aggregate invariants inside the aggregate.
- Use value objects for meaningful validated concepts.
- Use domain services for rules spanning multiple domain objects.
- Application services coordinate transactions, authorization, repositories, and external boundaries without reimplementing domain rules.
- Repositories persist aggregates and do not contain business decisions.
- Reference another domain's aggregate by identifier instead of sharing mutable entity instances.

## Persistence

- Use MySQL 8.4 through Pomelo for local development, integration tests, and production-compatible migrations.
- Keep connection strings and credentials outside committed configuration.
- Use explicit migrations; do not mutate production schemas automatically during API startup.
- Keep migrations owned by the DbContext that owns the tables.
- Detect optimistic concurrency conflicts instead of silently overwriting data.
- Use transactions for operations that change multiple authoritative records.
- Design retryable commands, reward grants, purchases, mail claims, and similar mutations to be idempotent.

## Time And Numeric Rules

- Persist timestamps in UTC.
- Use an injectable clock for business time.
- Do not call `DateTime.Now` in domain logic.
- Do not use `float` or `double` for currency, prices, authoritative rewards, or values that require exact deterministic behavior.

## Authentication And Authorization

- Use authenticated server identity as the source of user and administrator identity.
- Protect mutating and privileged endpoints with explicit permissions.
- Store passwords only through a reviewed password hasher.
- Store refresh tokens as non-reversible hashes and rotate them on use.
- Keep JWT keys and seed credentials outside the repository.
- Audit administrator and economy-affecting mutations.

## HTTP APIs

- Controllers receive requests, invoke application contracts, and map responses.
- Do not expose EF Core entities or domain aggregates.
- Use stable error codes and consistent validation responses.
- Prefer additive contract changes; version breaking public changes.
- Propagate cancellation tokens.
- Keep OpenAPI accurate so client contracts can be generated.

## Generator Restrictions

Use `NovaCore.Cli new app` for the baseline when required by the active goal. Do not use `generate entity` or `generate module` if it would violate domain-first organization or introduce an unvalidated module.

Keep the generated `NovaGame.Host.EntityFrameworkCore` name for generator compatibility.

## Verification

Run the narrowest applicable verification scope while iterating, then the cumulative scope required by the active goal.

Never report a MySQL integration path as verified when it was tested only against an in-memory provider.

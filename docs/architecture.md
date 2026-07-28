# NovaGame Architecture

## Project Goal

NovaGame is a modular game-development framework built on NovaCore. It provides reusable online-game backend architecture, client architecture, configuration-driven development, live-operations support, and extension points.

NovaGame is not a game. SwordMaster is the first validation consumer, not part of the framework.

## Dependency Direction

```text
NovaCore
  -> NovaGame
    -> Specific Game
       Example: SwordMaster
```

- NovaGame may depend on NovaCore.
- A specific game may depend on NovaGame.
- NovaGame must never reference a specific game.
- Samples and validation projects may exercise public NovaGame contracts but are never framework dependencies.
- Admin and client projects consume versioned public contracts; they do not reference server implementation projects.

## Server Structure

The M0.5 server uses six standard business-layer projects plus two operational composition projects:

```text
server/src/
  NovaGame.Domain.Shared/
  NovaGame.Domain/
  NovaGame.Application.Contracts/
  NovaGame.Application/
  NovaGame.Host.EntityFrameworkCore/
  NovaGame.Deployment/
  NovaGame.DbMigrator/
  NovaGame.Api/
```

`NovaGame.Host.EntityFrameworkCore` is the effective persistence project. Its generated name is preserved for NovaCore generator and validation compatibility.

`NovaGame.Deployment` centralizes deployment-time configuration validation. `NovaGame.DbMigrator` applies migrations and runs configured data seed contributors. They are not business-domain modules and do not change the six-layer ownership model.

Every project is organized internally by business domain. Layers represent technical responsibility; business-domain folders represent ownership.

## Layer Responsibilities

| Layer | Responsibility |
| --- | --- |
| Domain.Shared | Stable constants, metadata, shared value definitions, and domain-facing abstractions that do not depend on Domain. |
| Domain | Aggregates, entities, value objects, domain services, repository contracts, invariants, and domain events. |
| Application.Contracts | Public use-case contracts, request and response DTOs, and stable error contracts. |
| Application | Use-case coordination, authorization checks, transaction boundaries, and external-port orchestration. |
| Host.EntityFrameworkCore | MySQL provider configuration, DbContexts, mappings, repository implementations, and migrations. |
| Deployment | Shared deployment-time validation for production database, authentication, and host configuration. |
| DbMigrator | Operational composition root that applies migrations and then runs configured data seed contributors. |
| Api | HTTP controllers, authentication composition, middleware, OpenAPI, health checks, and application startup. |

Controllers remain thin. Application coordinates use cases. Domain owns business rules. Repositories persist aggregates.

## Business Domain Catalog

The catalog describes vocabulary and likely ownership boundaries. It is not a backlog and does not authorize empty scaffolding.

```text
PlayerAssets/
  Players
  Inventory
  Equipment
  Currency
  Rewards

Gameplay/
  Stats
  Skills
  Battle
  Drops
  Quests

Content/
  GameConfig
  Maps
  Monsters
  Items

Operations/
  Mail
  Shop
  Ranking
  Activity
```

Names may evolve when concrete use cases reveal better boundaries. Avoid the name `Attribute` for the stats domain because it conflicts conceptually with .NET attributes.

## Domain Ownership

Each aggregate belongs to exactly one domain. Another domain may reference it by identifier but must not mutate it through a shared repository or mutable entity reference.

Application coordinates cross-domain use cases. Domain events communicate completed domain facts and enable reactions without circular dependencies. Use direct calls when they are simpler and remain within a clear application-level orchestration boundary.

## Physical Module Rule

A domain is not automatically a physical NovaCore module. M0.5 built-in domains remain in the six standard business-layer projects; Deployment and DbMigrator remain operational composition projects.

A physical module is justified only after validation demonstrates a need for an independent package, dependency graph, persistence boundary, feature lifecycle, installation, or removal lifecycle. One entity is never sufficient justification.

NovaCore generators are optional scaffolding. Do not use entity or module generation when its output would violate domain-first organization.

## Configuration Boundary

Configuration represents content, references, and tunable parameters. Domain invariants remain code. Configuration must not grow into an arbitrary scripting language for game policy.

Configuration is versioned. Published versions are immutable and move through the lifecycle defined in [Configuration Release](workflows/configuration-release.md). Clients consume validated exported snapshots or manifests and never read configuration database tables directly.

### GameConfig Snapshot Contract

M0.5 snapshots are deterministic UTF-8 JSON documents with fixed metadata and canonical content. The top-level contract contains `formatVersion`, `schemaVersion`, `configurationSet`, `configurationVersion`, `createdAtUtc`, `integrity`, and `content`. `integrity` records the `SHA-256` algorithm, the canonicalization profile, and the lowercase hexadecimal SHA-256 hash of the exact canonical `content` JSON bytes.

The published version also stores a lowercase hexadecimal SHA-256 hash of the complete snapshot bytes. This snapshot hash is intentionally outside the JSON document to avoid a recursive hash and is the strong HTTP ETag. A version-specific snapshot is immutable and cacheable for one year; an active-environment snapshot uses the same bytes and ETag but requires revalidation because activation or rollback can change the environment pointer.

The server verifies both the complete snapshot hash and the embedded canonical-content hash before serving stored bytes. Clients must verify schema compatibility and both integrity boundaries before atomically replacing their active snapshot.

## Runtime Boundaries

### Server

The server is authoritative for identity, ownership, progression, rewards, prices, permissions, time-sensitive decisions, and economy changes.

Use UTC, an injectable clock, exact numeric types for authoritative values, explicit transactions, idempotency for retryable mutations, optimistic concurrency where conflicting writes are possible, and audit records for privileged or economy-affecting changes.

EF provider execution-strategy retries are disabled in M0.5. NovaCore currently begins explicit transactions outside an execution-strategy replay delegate, so an automatic retry could not replay the complete application operation safely. Transient database failures propagate to the caller; commands that may be retried by an API client or job runner must be idempotent.

### Admin

The Vben `web-ele` admin is an operational API client. It contains presentation and interaction behavior, not domain policy. It uses server permission codes for navigation and action visibility while the server remains the authorization authority.

### Client

Engine-independent TypeScript packages own reusable contracts and snapshot behavior. Cocos adapters own engine integration. A specific game consumes both and adds its own gameplay and content.

Future client-engine support must preserve server contracts and server-side business logic, but each engine may have a separate adapter and implementation language.

## API Evolution

Do not expose EF Core entities or mutable domain aggregates. Public APIs use explicit DTOs, stable error codes, and documented semantics.

M0.5 abstractions remain experimental until validated. After promotion, prefer additive changes and version breaking changes. SwordMaster must continue to pass as a contract consumer after extraction or public API evolution.

## M0.5 Decisions

- .NET 8 and NovaCore server baseline.
- MySQL 8.4 through Pomelo; no SQLite runtime path.
- Vben Admin 5 `web-ele`, Vue 3, and Element Plus.
- Administrator authentication and permissions are real server capabilities, not a mock-only frontend flow.
- Versioned GameConfig is the first complete infrastructure vertical slice.
- Cocos and SwordMaster validate configuration snapshot consumption only.
- Mail, Ranking, Shop, Activity, and gameplay systems remain out of scope.

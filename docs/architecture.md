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

## Specific Game Backend Composition

NovaGame is a framework dependency, not one shared business backend for every game. Each specific game owns and deploys its own backend composition, database schema and migrations, HTTP APIs, game administration extensions, and game client behavior.

```text
NovaCore
  -> NovaGame packages and contracts
    -> SwordMaster backend, admin extensions, and client
    -> AnotherGame backend, admin extensions, and client
```

A game backend may follow the same DDD layers when useful, but it remains a separate solution and dependency consumer. NovaGame must never reference, host, or conditionally branch on a specific game's business system. The completed M1 record documents SwordMaster's concrete backend layout; another game must establish its own layout from an approved use case rather than copying empty projects.

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

## Ownership Catalog

Ownership starts in the specific game. A capability moves into NovaGame only when a concrete consumer proves a reusable mechanism and the extraction workflow is separately authorized.

### NovaGame-Owned Baseline

The verified NovaGame-owned business capabilities are currently limited to:

```text
Operations/
  AdminAccess

Content/
  GameConfig
```

Cross-cutting framework infrastructure includes authentication composition, permissions, auditing, configuration release, MySQL integration, API conventions, typed client contracts, and engine adapters. Infrastructure supports game backends but does not own their game rules.

`Content/GameConfig` also owns the extracted neutral snapshot-validation mechanism. The concrete
`GameConfigSnapshotValidator` and immutable `ValidatedGameConfigSnapshot` are reusable protocol
services inside the existing Domain project; they do not create another business domain or physical
module. Their C# API remains experimental after M2 even though the extraction behavior is verified.

### Candidate Common Mechanisms

The following are possible reusable mechanisms, not current NovaGame modules or implementation commitments:

- deterministic stat or attribute values and modifiers;
- item identity, configuration references, and ownership primitives;
- inventory placement and equipment-assignment state;
- reusable transaction, idempotency, audit, and configuration mechanisms used by a game-owned capability.

These candidates must first work in a specific game. A future authorized extraction may move only the smallest independently useful mechanism supported by evidence. In C# namespaces and folders, prefer `Stats` over the bare name `Attribute` to avoid confusion with .NET attributes.

M3-07 recorded `request-audit-context-consumer-contract` as evidence vocabulary for the
consumability and layer placement of NovaGame's existing request-audit context. Extraction remains
unauthorized and unimplemented: the record adds no public API, business domain, physical module, or
parallel provider. The complete SwordMaster equipment-grant and rollback lifecycle, persistence,
administration workflow, and game policy remain owned by SwordMaster.

### Specific Game-Owned By Default

Complete business systems and their policies remain in each game's backend by default, including:

- character definitions, progression, and game-specific stat formulas;
- combat, skills, effects, targeting, and battle sequencing;
- equipment restrictions, enhancement, affixes, set bonuses, durability, and crafting;
- drops, rewards, probability, pity rules, quests, maps, monsters, and content orchestration;
- shops, pricing, currencies, purchase limits, economy, and monetization policy;
- mail, ranking, activity, and other live-operations behavior when their semantics depend on the game.

A generic name does not make a full system reusable. NovaGame may later own a proven mechanism used by one of these systems, but the game retains its terminology, policy, configuration meaning, orchestration, persistence extensions, APIs, and operator workflows.

## Domain Ownership

Each aggregate belongs to exactly one domain. Another domain may reference it by identifier but must not mutate it through a shared repository or mutable entity reference.

Application coordinates cross-domain use cases. Domain events communicate completed domain facts and enable reactions without circular dependencies. Use direct calls when they are simpler and remain within a clear application-level orchestration boundary.

## Physical Module Rule

A domain is not automatically a physical NovaCore module. The M0.5 framework-owned `Operations/AdminAccess` and `Content/GameConfig` capabilities remain in the six standard business-layer projects; Deployment and DbMigrator remain operational composition projects.

A physical module is justified only after validation demonstrates a need for an independent package, dependency graph, persistence boundary, feature lifecycle, installation, or removal lifecycle. One entity is never sufficient justification.

NovaCore generators are optional scaffolding. Do not use entity or module generation when its output would violate domain-first organization.

## Configuration Boundary

Configuration represents content, references, and tunable parameters. Domain invariants remain code. Configuration must not grow into an arbitrary scripting language for game policy.

Configuration is versioned. Published versions are immutable and move through the lifecycle defined in [Configuration Release](workflows/configuration-release.md). Clients consume validated exported snapshots or manifests and never read configuration database tables directly.

### GameConfig Snapshot Contract

M0.5 snapshots are deterministic UTF-8 JSON documents with fixed metadata and canonical content. The top-level contract contains `formatVersion`, `schemaVersion`, `configurationSet`, `configurationVersion`, `createdAtUtc`, `integrity`, and `content`. `integrity` records the `SHA-256` algorithm, the canonicalization profile, and the lowercase hexadecimal SHA-256 hash of the exact canonical `content` JSON bytes.

The published version also stores a lowercase hexadecimal SHA-256 hash of the complete snapshot bytes. This snapshot hash is intentionally outside the JSON document to avoid a recursive hash and is the strong HTTP ETag. A version-specific snapshot is immutable and cacheable for one year; an active-environment snapshot uses the same bytes and ETag but requires revalidation because activation or rollback can change the environment pointer.

The server verifies both the complete snapshot hash and the embedded canonical-content hash before serving stored bytes. Clients must verify schema compatibility and both integrity boundaries before atomically replacing their active snapshot.

The extracted `GameConfigSnapshotValidator` applies the same neutral producer contract to exact
snapshot bytes for .NET consumers and returns immutable verified metadata plus canonical content.
It is synchronous, stateless, and independent of transport, persistence, HTTP, OpenAPI, caching,
and game content policy. The engine-independent TypeScript client enforces the corresponding wire
rules for its supported schema. This cross-runtime behavior evidence does not by itself make the C#
constructor, method signature, result shape, dependency-injection lifetime, or failure model stable.

## Runtime Boundaries

### Server

Each concrete game server is authoritative for identity, ownership, progression, rewards, prices, permissions, time-sensitive decisions, and economy changes. NovaGame provides reusable infrastructure and promoted mechanisms; the game backend owns its game decisions.

Use UTC, an injectable clock, exact numeric types for authoritative values, explicit transactions, idempotency for retryable mutations, optimistic concurrency where conflicting writes are possible, and audit records for privileged or economy-affecting changes.

EF provider execution-strategy retries are disabled in M0.5. NovaCore currently begins explicit transactions outside an execution-strategy replay delegate, so an automatic retry could not replay the complete application operation safely. Transient database failures propagate to the caller; commands that may be retried by an API client or job runner must be idempotent.

### Admin

The Vben `web-ele` NovaGame admin is an operational client for framework-owned APIs such as administrator access and GameConfig. It contains presentation and interaction behavior, not domain policy. Each game owns its game-specific operator pages and consumes its own backend APIs, while it may reuse the NovaGame shell, authentication flow, components, and conventions. Server permission codes control navigation and action visibility; the owning server remains the authorization authority.

The completed M3.5 `Operations/AdminAccess` management capability remains in the existing standard
server layers and generic Vben admin. Its 17 HTTP operations manage administrators, custom roles
and registered permissions, session-family revocation and redacted audit queries. Server and client
checks require Access plus the relevant leaf permission. Aggregate policies, strong revisions,
transactional audit and idempotent receipts remain server-authoritative; built-in-role and
self-lockout guards are domain rules. OpenAPI-generated TypeScript and real MySQL/browser evidence
validate the existing contract. This completion adds no game policy, physical module, admin-shell
package, extraction or stable promotion of the experimental snapshot validator.

### Client

Engine-independent TypeScript packages own promoted reusable contracts and snapshot behavior. Cocos adapters own engine integration. A specific game consumes both and adds its own game systems, presentation, and content; those game packages do not move into the NovaGame client framework merely because another game might need similar behavior.

Future client-engine support must preserve server contracts and server-side business logic, but each engine may have a separate adapter and implementation language.

## API Evolution

Do not expose EF Core entities or mutable domain aggregates. Public APIs use explicit DTOs, stable error codes, and documented semantics.

An extracted contract remains experimental until an explicit maturity decision promotes it. Extraction validates ownership and reuse; stable promotion additionally requires evidence for the public API shape and compatibility boundary. After promotion, prefer additive changes and version breaking changes. SwordMaster must continue to pass as a contract consumer after extraction or public API evolution.

## M0.5 Decisions

- .NET 8 and NovaCore server baseline.
- MySQL 8.4 through Pomelo; no SQLite runtime path.
- Vben Admin 5 `web-ele`, Vue 3, and Element Plus.
- Administrator authentication and permissions are real server capabilities, not a mock-only frontend flow.
- Versioned GameConfig is the first complete infrastructure vertical slice.
- Cocos and SwordMaster validate configuration snapshot consumption only.
- Complete game-owned systems such as Battle, Skills, Enhancement, Set Bonuses, Drops, Shop, Mail, Ranking, and Activity remain outside the NovaGame baseline unless a later evidence-backed extraction promotes only a reusable mechanism.

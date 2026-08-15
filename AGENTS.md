# NovaGame Repository Instructions

## Purpose

NovaGame is reusable infrastructure for developing online games. It is not a game and must not contain game-specific gameplay, terminology, assets, or policy.

The dependency direction is strict:

```text
NovaCore -> NovaGame -> Specific Game
```

NovaGame may depend on NovaCore. Specific games may depend on NovaGame. NovaGame must never reference SwordMaster or any other specific game. Samples validate NovaGame but never become NovaGame dependencies.

Each specific game owns its own backend composition, persistence extensions and migrations, APIs, administration extensions, and game client behavior. NovaGame is a reusable dependency of those backends; it is not a shared host for every game's business systems.

## Instruction Scope

This file applies to the entire repository. Read the nearest scoped instruction file before editing a subtree:

- `server/AGENTS.md` for .NET server work.
- `admin/AGENTS.md` for the operations admin.
- `clients/cocos/AGENTS.md` for the Cocos client framework.

Also read the workflow relevant to the task under `docs/workflows/`. Milestone status is defined in `docs/roadmap.md`. The completed M0.5, M1, M2, and M3 records are `docs/goals/m0.5.md`, `docs/goals/m1.md`, `docs/goals/m2.md`, and `docs/goals/m3.md`. M3.5 is active at `M35-05` in `docs/goals/m3.5.md` for the separately authorized NovaGame-owned `administrator-management` capability. Creating or reading a goal definition does not authorize work beyond an explicitly activated stage and task.

Roadmap items provide context, not authorization. Do not implement a future milestone or an unrequested extraction candidate.

## Completed Baseline

M0.5 is complete and defines the verified reusable baseline for bootstrapping SwordMaster as a consumer:

- repository composition and repeatable verification;
- NovaCore server integration;
- MySQL persistence;
- administrator authentication and permissions;
- versioned configuration management and snapshot export;
- Vben Admin integration;
- an engine-independent TypeScript configuration client;
- a SwordMaster configuration-loading smoke test without gameplay.

Preserve this baseline unless a user explicitly requests a maintenance change. The user approved the game-owned SwordMaster `equipment-attribute-effect` capability on 2026-08-01 and reconfirmed it on 2026-08-02. M1 and every M1 stage and task are complete. M2 completed extraction of only the narrower `game-config-snapshot-consumer-validation` mechanism after the user separately authorized that exact candidate on 2026-08-10. That one-time authorization was consumed by the completed M2 runbook and grants no continuing authorization for extraction. The complete `equipment-attribute-effect` capability remains SwordMaster-owned, and the extracted C# `GameConfigSnapshotValidator` contract remains experimental.

The user explicitly approved the SwordMaster-owned `player-equipment-grant` capability on
2026-08-10. M3 is complete. The complete player-equipment-grant capability remains SwordMaster-owned.
M3 grants no continuing authorization for extraction, another capability, M4, or NovaCore changes.

M3-06-T01 froze the requirement-to-evidence matrix and exact non-mock acceptance contract.
M3-06-T02 completed the real composed API and dual-MySQL acceptance path, and M3-06-T03 completed
the real browser acceptance path. M3-06-T04 completed cumulative verification and the cleanup audit,
so M3-06 is complete. M3-07-T01 froze, independently reconstructed, and verified the reproducible
completed-M3-06 baseline. M3-07-T02 completed the evidence-only reuse-pressure audit, and
M3-07-T03 recorded `request-audit-context-consumer-contract` as a possible later extraction
candidate without authorizing extraction, and M3-07-T04 completed the post-baseline path,
documentation, framework-boundary, and cumulative SwordMaster audit. M3-07 is complete. M3-08-T01
completed the exact ordered 28-command repository acceptance set, M3-08-T02 completed the final
dependency, contract, migration, secret, NovaCore, and disposable-resource audit, and M3-08-T03
completed documentation synchronization, local-evidence cleanup, and deactivation. The recorded
`request-audit-context-consumer-contract` remains a possible later candidate only; it is not
implemented or authorized for extraction.

The user separately authorized the NovaGame-owned `administrator-management` capability on
2026-08-13. M3.5 completed its evidence-backed baseline audit, frozen-contract stage, and M35-03
Domain/Application implementation. The domain, security, Application, HTTP, MySQL, Vben, browser,
and verification contracts remain frozen and verified. M35-04-T01 completed its persistence
implementation, independent review fixes, and real-MySQL verification; M35-04-T02 completed its
management permission registration and idempotent DbMigrator seed synchronization; M35-04-T03
completed the frozen thin management HTTP layer and real-MySQL API/OpenAPI verification; and
M35-04-T04 completed the frozen real-MySQL integration evidence for migrations, seed,
permissions, management queries, mutations, locking, replay, and atomic audit behavior.
M35-04 is complete after final sequential server, database, authentication, management-contract,
documentation, repository, and whitespace verification. M35-05 is active only at `M35-05-T01`.
Follow `docs/goals/m3.5.md`. The active task authorizes only typed management transport, permission
constants, static routes, request/error handling, and focused transport/route testing. It does not
authorize M35-05-T02 or later work, SwordMaster changes, an admin-shell extraction, another
capability, M4, or NovaCore changes. M4 remains `Unscheduled`.

Complete systems such as Battle, Skills, Enhancement, Set Bonuses, Drops, Shop, Mail, Ranking, and Activity are specific-game-owned by default. They are not automatic NovaGame deliverables in any milestone. Only a separately authorized, evidence-backed reusable mechanism may be extracted later.

## Core Design Rules

### Configuration Driven

Prefer configuration for content, references, and tunable parameters. Configuration is not a replacement for domain invariants and must not become an application-specific scripting language.

Published configuration versions are immutable. Changes follow the release workflow in `docs/workflows/configuration-release.md`.

### Business Domain First

Organize code by business domain inside each architectural layer. Do not organize the repository around one module, service, or top-level folder per entity.

The ownership catalog in `docs/architecture.md` is not an implementation backlog. The verified framework-owned business capabilities are `Operations/AdminAccess` and `Content/GameConfig`. Every other system starts in the specific game unless an independently useful mechanism completes the extraction workflow. Do not create empty folders, projects, entities, services, or modules for unvalidated NovaGame mechanisms or game-owned systems.

### Rich Domain, Thin API

Entities and value objects own business invariants. Domain services own rules that do not naturally belong to one entity. Application services coordinate use cases. Controllers translate HTTP requests and responses only.

Do not place game rules in controllers, persistence repositories, or UI clients.

### Domain Ownership

Every aggregate has one owning domain. A domain must not mutate another domain's aggregate through its repository. Reference aggregates in another domain by identifier, coordinate cross-domain use cases in Application, and use domain events for reactions and side effects when appropriate.

Avoid circular dependencies between domains.

## Extraction Rule

Do not build a reusable gameplay or business framework before a concrete use case validates it.

Implement the capability in SwordMaster first, validate the behavior, separate game policy from reusable mechanism, and extract only the reusable part through `docs/workflows/extraction.md`.

M0.5 has a narrow bootstrap exception for infrastructure required to create and run the validation consumer. This exception does not authorize speculative gameplay abstractions.

When reuse is uncertain, leave the capability in the specific game and record it as an extraction candidate.

Do not extract a complete game system merely because several games use a similarly named concept. Combat, skills, enhancement, set bonuses, drops, shops, progression, economy, and live-operations policy remain game-owned; only independently useful mechanisms with concrete evidence are eligible.

## Physical Module Rule

A business domain is an organizational boundary, not automatically a physical NovaCore module. The M0.5 framework-owned `Operations/AdminAccess` and `Content/GameConfig` capabilities remain inside the standard server layers.

Create a separate physical module only when a validated capability requires an independent package, dependency, persistence, feature, installation, or removal lifecycle. Never create one merely because an entity was added.

## API Stability

Unvalidated abstractions remain experimental. Completing extraction and passing one consumer do not automatically promote an API to stable; promotion requires a separate evidence-based maturity decision. Do not promise backward compatibility before concrete consumers validate both behavior and the public contract shape.

After explicit promotion to a stable public NovaGame contract:

- prefer additive changes;
- version breaking changes;
- provide migration notes;
- keep public error codes stable;
- keep SwordMaster passing as a contract consumer.

Never expose persistence entities through public APIs. Use explicit request and response contracts. Generate TypeScript contracts from OpenAPI where practical instead of manually maintaining duplicate models.

## Coding Style

- Use modern C# and TypeScript supported by the pinned toolchains.
- Enable nullable reference types in C# and strict type checking in TypeScript.
- Use async APIs for I/O and propagate cancellation where supported.
- Prefer constructor injection.
- Avoid static service classes.
- Do not create `Helper`, `Utils`, or `Common` dumping grounds.
- Prefer value objects when they enforce meaningful invariants.
- Add abstractions only for a concrete boundary, variation, or testing need.
- Keep code and documentation in English unless user-facing localization requires otherwise.

## Security And Operations

The owning concrete game server is authoritative. Never trust client-provided rewards, prices, ownership, progression, permissions, or time.

Use UTC for persisted timestamps and an injectable clock for business decisions. Economy-affecting and administrative mutations must be transactional, idempotent where retries are possible, authorized, and auditable. Do not use floating-point values for authoritative currency calculations.

Use structured logging and correlation IDs. Never log passwords, access tokens, refresh tokens, connection strings, player private data, or configuration secrets. Do not commit secrets or production credentials.

## Code Generation

NovaCore generators are scaffolding tools, not architecture authorities. Generated output must still follow NovaGame ownership and folder rules.

Do not run `generate entity` or `generate module` when the output would organize code by entity or create an unvalidated physical module. Never preserve a generated layout merely because it was generated.

Preserve the generated `NovaGame.Host.EntityFrameworkCore` project name unless an explicit migration also updates the generator and all validation tooling.

## Definition Of Done

A change is complete only when:

- dependency and ownership rules still hold;
- relevant builds, tests, linters, and type checks pass;
- behavior changes have focused tests;
- persistence changes include reviewed migrations;
- public contract changes update generated clients and documentation;
- required validation commands were actually run.

Do not claim completion for checks that were not run. Record blockers and failed verification in the current executable goal when one exists instead of weakening acceptance criteria.

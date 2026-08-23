# Development Workflow

Use this workflow for repository changes unless a more specific workflow applies. It turns a concrete request into the smallest complete, verified vertical slice.

## 1. Establish Scope

Read the root `AGENTS.md`, the nearest scoped `AGENTS.md`, the active goal, and the relevant architecture or workflow document.

Write down the concrete outcome, consumer, owning business domain, in-scope behavior, non-goals, and required verification before editing. Confirm that the work belongs to the active milestone or is explicitly requested by the user.

Roadmap entries alone do not authorize work.

## 2. Identify Ownership And Layer

First decide whether the capability belongs to NovaGame or to a specific game. NovaGame ownership is valid only for the verified framework baseline or for a mechanism that completed the extraction workflow. Complete gameplay, progression, economy, shop, drop, combat, skill, enhancement, set, and game-specific operations systems default to the specific game backend.

Assign every changed concept to one owning business domain. Select the layer by responsibility:

- Domain.Shared for stable shared domain metadata and definitions.
- Domain for invariants, aggregates, value objects, domain services, repository contracts, and domain events.
- Application.Contracts for public use-case contracts and DTOs.
- Application for use-case coordination.
- Host.EntityFrameworkCore for MySQL mappings, repositories, DbContexts, and migrations.
- Api for HTTP translation and composition.
- Admin or client packages for presentation, interaction, transport, and engine integration only.

A specific game backend may mirror these responsibilities in its own solution, projects, migrations, APIs, admin extension, and client packages. Do not place game-owned code under NovaGame framework projects merely to reuse the existing host.

Do not create a new physical module, generic abstraction, provider, factory, or shared folder without a concrete ownership or variation requirement.

## 3. Inspect Existing Behavior

Search the repository before adding a new concept. Reuse established contracts and patterns when they satisfy the use case.

When using NovaCore generation, inspect the planned output first. Do not run a generator that would create entity-first folders or an unvalidated physical module. Generated code must be reorganized when it conflicts with NovaGame ownership rules.

## 4. Implement A Complete Vertical Slice

Implement the smallest slice in the owning specific game that proves the behavior end to end. A server slice normally includes the game-owned domain rule, application contract, application orchestration, persistence when required, HTTP exposure when required, and focused tests. NovaGame changes during this step are limited to separately authorized maintenance of its existing baseline.

Do not create empty placeholders for game-owned systems or unvalidated framework mechanisms. Do not broaden the slice to adjacent roadmap work.

Keep controllers thin, domain models authoritative, application services orchestration-focused, repositories persistence-only, and clients free of server business policy.

## 5. Handle Data And Failure Explicitly

Define validation, authorization, concurrency, idempotency, transaction, and failure behavior where they are relevant to the use case.

Use UTC and an injectable clock for business time. Keep secrets out of source and logs. Never trust client-provided authoritative state.

For persistence changes, create and review an explicit MySQL migration. Do not substitute an in-memory database for required provider behavior.

## 6. Add Verification With The Change

Tests should prove behavior rather than implementation detail. Scale coverage with risk:

- Domain tests for invariants and state transitions.
- Application tests for use-case coordination, authorization, and failure mapping.
- MySQL integration tests for mappings, queries, transactions, concurrency, and migrations.
- API tests for contracts, authentication, permissions, status codes, and error codes.
- Admin and client tests for typed adapters, state handling, and user-visible workflows.
- End-to-end tests for cross-system acceptance paths.

Do not mark tests as passing unless they were run successfully.

## 7. Run Required Checks

Run the narrow checks while iterating, then the root verification command when it exists. At minimum, run every build, test, lint, type-check, migration, architecture, and smoke command affected by the change.

If a required check cannot run, record the exact command and blocker in the active goal. Do not remove or weaken the acceptance criterion.

## 8. Update Contracts And Documentation

Regenerate clients when OpenAPI contracts change. Update architecture, workflow, roadmap, goal, migration, or user documentation when the change alters public behavior or a durable decision.

Breaking public changes require an explicit version and migration notes after an API has been promoted as stable.

## 9. Record And Commit Goal Progress

When an executable goal is active, do not begin a successor task until the completed task's verified
result is recorded in the active goal, the status documents that name its current state are
synchronized, and its scoped changes are committed. Do not begin a successor stage until the same
recording, synchronization, and dedicated commit have occurred after every task and cumulative
stage check pass.

Each goal record must identify the commands run, their result, the completed task or stage, and any
relevant cleanup or residual risk. A failed or blocked task or stage must record its evidence and
blocker, but must not be represented as complete or unlock its successor. Keep the commit scoped to
the verified work; do not include unrelated user changes or disposable evidence.

## Completion Check

A change is complete only when ownership is clear, layer rules hold, the vertical slice works,
relevant tests pass, generated contracts are current, documentation is accurate, and the active
goal records the verified result. A completed executable-goal task or stage also requires its
dedicated, scoped commit before its successor becomes active.

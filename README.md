# NovaGame

NovaGame is a modular game-development framework built on top of NovaCore. It is not a game. It provides reusable server, client, configuration, and live-operations infrastructure for specific games.

The first validation consumer is SwordMaster:

```text
NovaCore
  -> NovaGame
    -> SwordMaster
```

NovaGame must never depend on SwordMaster or contain SwordMaster-specific concepts.

Each specific game owns and deploys its own backend, schema and migrations, APIs, admin extensions, and client behavior. NovaGame is a shared dependency that supplies framework infrastructure and evidence-backed reusable mechanisms; it is not one business backend shared by every game.

## Targets

- Server: .NET 8 and NovaCore
- Database: MySQL 8.4
- Client: Cocos Creator and TypeScript
- Admin: Vben Admin 5 `web-ele`, Vue 3, and Element Plus

## Current Status

M0.5, [M1](docs/goals/m1.md), and [M2](docs/goals/m2.md) are complete. Repository bootstrap, the pinned NovaCore 0.2.0 server baseline, MySQL 8.4, administrator access, versioned GameConfig, the real-API Vben Admin 5 `web-ele` workspace, the reusable Cocos TypeScript configuration client, the SwordMaster configuration-loading consumer, and the approved SwordMaster-owned `equipment-attribute-effect` vertical slice have passed their acceptance paths. M2 extracted only the narrower `game-config-snapshot-consumer-validation` mechanism, and SwordMaster consumes the generalized validator while retaining all equipment and PlayerAssets policy. The extracted C# `GameConfigSnapshotValidator` remains experimental. Its one-time extraction authorization was consumed and grants no continuing authorization.

M3 is complete. Its [completed goal record](docs/goals/m3.md) verifies Domain, Application, MySQL, administrator API, independent
Vben admin, real dual-MySQL API, desktop/mobile browser, exact 28-command cumulative acceptance,
boundary, reuse-evidence, secret, NovaCore, and cleanup paths are verified. The complete
player-equipment-grant capability remains SwordMaster-owned.
`request-audit-context-consumer-contract` remains a possible later candidate only and is neither
implemented nor authorized for extraction. M3 grants no continuing authorization for extraction,
another capability, M4, or NovaCore changes.

[M3.5](docs/goals/m3.5.md) is the only active repository milestone. M35-07 completed on
2026-10-01. The separately authorized `M35-08-T01` completed its frozen ten-command acceptance
set on 2026-10-01, including 210 real-MySQL tests, 147 focused management tests, checked contracts,
190 focused frontend tests, and full Admin with 520 unit tests plus five zero-retry browser tests.
Accepted evidence and failed-attempt history remain recorded. The user authorized `M35-08-T02`
and remaining M3.5 closure on 2026-10-01; only T02 is executable, with T03 pending its committed
result. The separately authorized T04 passed cumulative Admin with 520 unit tests,
exactly five zero-retry real browser tests, all 20 manual screenshot reviews, committed evidence,
verified disposal, and final governance checks. M35-05 completed
the NovaGame-owned `Operations/AdminAccess`
`administrator-management` workspace across .NET, MySQL 8.4, and the generic Vben Admin 5
`web-ele` application, including its final management-admin, checked-contract, generic Admin,
Documentation, Repository, and whitespace verification. M35-06-T01 froze and verified the exact
real-server requirement-to-evidence matrix, ordered acceptance commands, T02 boundary, and
premature-source guard. M35-06 then completed the real composed API, frozen security, serial
cumulative verification, and zero-residue cleanup paths. M35-07-T01 completed the frozen real
browser foundation through the composed Vben/API/DbMigrator/MySQL acceptance path, and M35-07-T02
completed its route/action permission and authoritative `401`/`403` acceptance after focused,
frontend, and serial real-browser verification. T03 completed on 2026-09-30 with real unknown-ID
API `404`/stable-code evidence, focused defensive detail coverage, 190 passing frontend tests,
serial five-test real-browser acceptance, and manual review of all 20 retained desktop/mobile
screenshots. T03 added no removal lifecycle. Neither its consumed authority nor the completed
T04 authorization permits M35-08 execution, SwordMaster, extraction, M4, or NovaCore changes.
M4 remains `Unscheduled`.

## Prerequisites

- .NET 8 SDK selected by `global.json`;
- Docker Desktop with Docker Compose;
- Node.js 24.14.0 and pnpm 10.33.4, resolved by the repository scripts;
- NovaCore source from `NOVACORE_ROOT`, defaulting to `D:\Programs\nova-core`;
- the fixed Vben Admin 5 template from `VBEN_TEMPLATE_ROOT`, or the ignored local template cache prepared for this workspace;
- Microsoft Edge, or a Playwright browser channel selected through `NOVAGAME_E2E_BROWSER_CHANNEL`.

Bootstrap reads NovaCore through a Git snapshot and does not modify its source worktree. Existing ignored credentials in `server/.env.local` and `deploy/.env` are preserved.

## Bootstrap And Verification

Run the complete repository bootstrap and cumulative acceptance suite from the repository root:

```powershell
pwsh -NoProfile -File scripts/bootstrap.ps1
pwsh -NoProfile -File scripts/verify.ps1
```

Both commands default to the cumulative `All` scope. Narrow iteration scopes remain available, including `Repository`, `Server`, `Database`, `Admin`, `Client`, and `SwordMaster` where supported.

## Database And Migrations

Start MySQL 8.4 without the application:

```powershell
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Database
```

Start MySQL, apply all EF Core migrations, run seed contributors, and return:

```powershell
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Migrations
```

The migration path uses `NovaGame.DbMigrator`; the API never mutates the schema during startup.

## Local Development

Start MySQL, apply migrations, run the API, and launch the Vben `web-ele` admin:

```powershell
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope All
```

The default endpoints are `http://127.0.0.1:5000` for the API and `https://localhost:5777` for the admin. The ignored `server/.env.local` file contains the local administrator bootstrap values. Use `Ctrl+C` to stop the foreground development processes.

Focused startup paths are also available:

```powershell
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Server
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Admin
```

The verified SwordMaster smoke consumer is documented in [samples/SwordMaster](samples/SwordMaster/README.md).

## Documentation

- [Documentation index](docs/README.md)
- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
- [Development workflow](docs/workflows/development.md)
- [Extraction workflow](docs/workflows/extraction.md)
- [Configuration release workflow](docs/workflows/configuration-release.md)
- [M0.5 completed goal record](docs/goals/m0.5.md)
- [M1 completed goal record](docs/goals/m1.md)
- [M2 completed goal record](docs/goals/m2.md)
- [M3 completed goal record](docs/goals/m3.md)
- [M3.5 active Administrator Management goal](docs/goals/m3.5.md)

Codex and other coding agents must read [AGENTS.md](AGENTS.md) before changing this repository and the nearest scoped `AGENTS.md` before changing a subtree.

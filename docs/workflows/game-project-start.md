# Starting A Specific Game With NovaGame

This guide is the handoff entry point for a coding agent starting a real game
project. NovaGame supplies reusable infrastructure; the game owns a separate
application that consumes it. Use the existing composition as a starting
reference, then implement and verify the game's own first complete use case.
The game is not required to use Cocos Creator. Choose its engine or client
technology from its requirements. There is no verified one-command generator
for a new game.

## Baseline And Authority

As of 2026-10-01, M0.5, M1, M2, M3, and M3.5 are complete. No NovaGame milestone
is active; M4 is `Unscheduled`. The completed framework baseline is commit
`32ef6f15bf4cd1584a91ec12d1d15f581e3e5744` on `master` in
[NovaGame](https://github.com/xf417xf/NovaGame). Pin a verified framework commit
for each game and record deliberate upgrades rather than following a moving
branch silently. This guide is documentation, not activation of a milestone or
authorization to create a game.

The last verified local checkouts were `D:\MyPrograms\NovaGame` and
`D:\MyPrograms\nova-core`. These are machine-specific locations, not required
consumer paths. The M3.5 external NovaCore checkout was verified at
`ef8db55173ff8f0286bb06abe753861c8a7d2d80`. That provenance check is distinct from
the NovaCore source revision used to build the locked packages. Use the
repository's dependency lock and bootstrap scripts for package selection; do
not replace the package lock with the external checkout HEAD.

Historical task approvals have been consumed. A new game's current user request
defines its implementation scope. Framework maintenance, extraction, NovaCore
changes, and future NovaGame milestones require their own applicable authority.

## What To Read First

1. Read the new game's root `AGENTS.md` and current task or goal, if they exist.
2. Read NovaGame [AGENTS.md](../../AGENTS.md), this guide,
   [architecture](../architecture.md), and [development workflow](development.md).
3. Read the [roadmap](../roadmap.md) and
   [M3.5 completed record](../goals/m3.5.md) for current state and evidence.
   Earlier completed goals explain historical decisions; do not resume them.
4. Read the relevant subtree instructions before changing NovaGame:
   [server](../../server/AGENTS.md), [admin](../../admin/AGENTS.md), or
   [client workspace](../../clients/cocos/AGENTS.md). The client workspace's
   directory name does not require a game to use Cocos.
5. Inspect [server setup](../../server/README.md),
   [client setup](../../clients/cocos/README.md), and the
   [SwordMaster consumer](../../samples/SwordMaster/README.md) for the surfaces
   the game will use. Follow the
   [configuration release workflow](configuration-release.md) for content.

Inspect Git status, current branches, pinned revisions, existing task records,
and any uncommitted changes before implementing. Existing user changes must be
preserved. Read instructions in the actual game repository; NovaGame's files do
not automatically install instructions or tooling into a separate checkout.

## Ownership And Reuse

```text
NovaCore -> NovaGame -> Specific Game
```

Specific games depend on NovaGame, which may depend on NovaCore.
NovaGame must never reference the game.
Games can share framework source without sharing one business server or runtime
database. Each game owns and deploys its backend composition.

| Concern | Reuse from NovaGame | Specific game responsibility |
| --- | --- | --- |
| Server infrastructure | NovaCore integration, standard server layers and deployment validation | Executable host, module composition, deployment settings |
| Administrator access | Authentication, registered permissions, administrators, roles, session revocation and audit | Game permission definitions, game operations, authorization in game use cases |
| Configuration | Drafts, publication, deterministic snapshot export, activation and rollback | Content schema, semantic game rules, releases and compatibility policy |
| Persistence | Framework-owned EF model and migrations | Game DbContext, mappings, migrations, connection settings and migrator composition |
| Admin UI | Verified generic Vben operations and configuration screens as a reference | Independent workspace composition, game pages and connection to the game API |
| Client | Engine-independent TypeScript configuration client; optional Cocos adapter | Chosen engine or client application, assets, player transport and interaction behavior |
| Gameplay | No ready-made gameplay system | Players, combat, skills, equipment, economy, drops, shop and other game systems |

The framework-owned business capabilities are `Operations/AdminAccess` and
`Content/GameConfig`. Administrator authentication is not player authentication;
design the game's player identity and security boundary for its concrete use case.
SwordMaster's equipment rules and grant operations are game-owned examples, not
NovaGame services to copy into the framework or assume fit another game.

The C# `GameConfigSnapshotValidator` remains experimental. Passing NovaGame and
SwordMaster acceptance does not establish stable API compatibility or a verified
second consumer. Admin shell extraction is also not complete. Consult the
[extraction workflow](extraction.md) only when separately authorized evidence
supports extracting a narrower reusable mechanism.

## Choose The First Game Task

Before creating files, establish the following from the current request or ask
for the missing choices that affect implementation:

- Game name, namespace, local path, repository and intended branch.
- Client technology or engine, exact version, target platforms and required
  build tools; editor availability only when the chosen technology needs it.
- First complete use case and its acceptance behavior, including failure paths.
- Backend, persistence, admin and client portions included in that use case.
- Framework commit and dependency locations; whether any framework maintenance
  is explicitly included.
- Required real database, API, browser and engine checks; deployment target if
  production work is requested.

Implement one complete game-owned use case before adding adjacent systems or
generic abstractions. A request to start a game does not require creating empty
folders for every potential gameplay domain.

## Repository And Dependency Setup

A recommended layout is three independent repositories under a development
directory: `nova-core`, `NovaGame`, and `<GameName>`. This is a recommendation,
not a generated or verified new game. The game may organize `server/`, `admin/`,
`client/`, `docs/`, and `scripts/` as needed for the approved first use case.

The validated server consumer uses source `ProjectReference` dependencies.
Do not assume NovaGame NuGet packages or its TypeScript packages have a verified
public release. For an external checkout:

1. Pin the NovaGame revision and make its root an explicit build input, such as
   an MSBuild `NovaGameRepositoryRoot` property. Inspect
   [SwordMaster's build properties](../../samples/SwordMaster/server/Directory.Build.props)
   for external-reference configuration handling. Its three-parent relative
   path is specific to its location inside this repository.
2. Bootstrap the locked NovaCore packages through NovaGame. Adapt the game's
   NuGet source configuration using [NovaGame's configuration](../../server/NuGet.Config)
   as a reference. Its `../.novacore/packages` path is relative to `server/`;
   resolve the actual feed location for an external game and preserve the
   `NovaCore.*` source mapping.
3. Ensure the game's SDK selection, restore paths, package versions and build
   configuration work from a fresh checkout. Do not rely on undeclared packages
   in a developer's global cache.
4. Record framework revisions, setup commands and external path inputs in the
   game's documentation and verification script.

The local TypeScript packages are private, version `0.0.0`:
`@novagame/game-config-client` and `@novagame/cocos-creator-game-config`. Define
and verify the consumer's local package linking and build order. A public npm
installation command is not an established distribution path.

## Backend Composition

Use SwordMaster to understand composition, not to rename its entire business
implementation. Relevant references are its
[API project](../../samples/SwordMaster/server/src/SwordMaster.Api/SwordMaster.Api.csproj),
[module](../../samples/SwordMaster/server/src/SwordMaster.Api/SwordMasterApiModule.cs),
and [entry point](../../samples/SwordMaster/server/src/SwordMaster.Api/Program.cs).

The game owns an executable API that composes `NovaGameApiModule` with its own
Application and persistence modules using NovaCore modularity. Register game
HTTP contracts in the game's API. Preserve applicable NovaGame production
configuration validation and add game-owned configuration validation.

Organize business domains inside layers: Domain.Shared, Domain,
Application.Contracts, Application, persistence and API. Domain owns invariants;
Application coordinates use cases; controllers translate HTTP. Add physical
modules only for a concrete independent lifecycle requirement.

The game owns a DbMigrator that applies the framework migrations and its own
reviewed migrations, then runs appropriate seed contributors. APIs must not
mutate schemas on startup. The SwordMaster example uses separate named
connections and two distinct MySQL databases; inspect its DbMigrator and database
validator before choosing and verifying the game's topology. Do not copy
SwordMaster development seed data, credentials, database names or game permission
codes. Keep game tables and migrations out of NovaGame's DbContext.

Game operations must enforce player or administrator authorization on the
server. Use transactional mutations, concurrency handling, idempotency where
retries are possible, UTC and audit records as the use case requires. Keep
credentials and tokens out of source, command output and logs. Do not silently
enable provider retries against the current explicit-transaction policy.

## Admin And Client Integration

The generic admin already manages framework administrator access and GameConfig.
A game must compose its own admin against its own API for game operations.
Do not place game business pages in NovaGame's generic admin.

[SwordMaster's admin package](../../samples/SwordMaster/admin/package.json)
depends on the Vben workspace through `workspace:*` and catalog dependencies.
Copying one application directory does not produce an independent working admin.
Choose and verify a complete workspace/dependency arrangement in the game
repository, then adapt its routes, API base URL, generated OpenAPI contracts,
permissions and login/session behavior. This setup is game-owned work; it does
not require extracting an admin shell into NovaGame.

For a TypeScript-compatible client, integrate the public configuration package
surfaces independently of Cocos. Select the transport, storage and lifecycle
adapters supported by the actual runtime; engine independence does not establish
compatibility with every JavaScript runtime.

For Unity, Unreal or another non-TypeScript client, consume the game API and
published snapshot wire format through game-owned language/runtime adapters.
The existing TypeScript package is not a native Unity or Unreal SDK, and the
experimental C# Domain validator is not a verified Unity client library.
Verify the chosen client's parsing, canonicalization, integrity checks, caching,
transport and cancellation behavior against producer fixtures and the real API.
Do not implement another language SDK or extract an engine adapter until that
work is included in the approved game task.

Supply a trusted snapshot hash through the export/release channel; do not
calculate the expected hash from the untrusted download being checked. Validate
metadata and both integrity boundaries before activation, and retain the last
valid snapshot on failure. For cross-origin Web clients, allow `If-None-Match`
and expose `ETag` through the server CORS policy.

If Cocos is selected, its existing adapter boundary deliberately has no pinned
Creator release. Headless TypeScript checks are not editor, scene, native build
or device acceptance. For any client technology, select an exact toolchain in
the game, supply its runtime adapters, and verify the actual target. Client
configuration and UI never replace authoritative server rules.

## Prepare And Run The Framework Locally

The following commands run from the NovaGame root and prepare or demonstrate
the framework baseline. They do not create or launch an arbitrary new game.
Use PowerShell 7, the SDK selected by `global.json`, Docker Desktop/Compose,
MySQL 8.4, Node.js 24.14.0 and pnpm 10.33.4. Bootstrap resolves the pinned admin
toolchain and Vben template; see the root [README](../../README.md) prerequisites.

For the last verified machine, configure the NovaCore source location:

```powershell
Set-Location -LiteralPath 'D:\MyPrograms\NovaGame'
$env:NOVACORE_ROOT = 'D:/MyPrograms/nova-core'
```

If this machine uses the repository-local SDK instead of a system installation,
set its executable location in the current shell before running scripts:

```powershell
$env:DOTNET_ROOT = 'D:/MyPrograms/NovaGame/.local/dotnet-sdk'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
dotnet --version
```

Adapt these locations on another machine. Prepare dependencies, then inspect
ignored local environment files without printing their contents into logs:

```powershell
pwsh -NoProfile -File scripts/bootstrap.ps1
```

`server/.env.local` holds local administrator/JWT configuration, and
`deploy/.env` holds database configuration. Configure both administrator seed
name and password to create the first administrator; leaving both empty disables
seeding. Existing administrators are not overwritten by seed changes. Changing
the database environment file does not reset passwords in an existing MySQL
volume. Preserve existing secrets and data; never reset a volume as an implicit
setup step.

Run these phases as needed:

```powershell
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Database
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Migrations
pwsh -NoProfile -File scripts/run-dev.ps1 -Scope All
```

`All` also starts the database and runs the migrator. The default API is
`http://127.0.0.1:5000`, and the admin is `https://localhost:5777`. Readiness is
available at `/health/ready`. Stop foreground development processes with
`Ctrl+C`; this does not imply deletion of the database volume.

For a new game, write and verify its own setup, migration, development and
acceptance commands, with distinct ports/resource names when running beside
NovaGame. The framework's `run-dev.ps1` is not the game's runtime launcher.

## Verification And Readiness

NovaGame's completed tests prove the recorded framework and SwordMaster paths,
not a new game's composition. Run the relevant framework scopes for any
authorized framework change. For documentation-only work:

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
git diff --check
```

The full `scripts/verify.ps1` defaults to `All` and includes costly integration
and browser work. Choose required scopes from the change, and run cumulative
acceptance when required by its task; do not claim unexecuted checks passed.

A new game needs its own evidence: fresh restore/build, focused business tests,
real MySQL migrations and transactions, real composed API authorization and
contracts, real browser admin flows if included, and actual engine/platform
checks if included. Verify framework endpoints inside the game-owned host as
well as its game endpoints. Generate clients from that host's OpenAPI and check
for stale output. Record failed commands and blockers alongside successful runs.

Local development readiness means the approved use case runs with reproducible
setup and its required acceptance passes. Production readiness additionally
requires evidence for the actual environment: secret provision, TLS and CORS,
authentication, database migration/backup/restore, monitoring, configuration
release/rollback, deployment rollback and target-client compatibility. Local
development credentials and passing headless checks are not production evidence.

## Handoff In The Game Repository

Create a durable game-owned task record when starting the authorized work. Keep
it current with scope, owning domains, pinned dependencies, active task, branch
and commit, exact commands/results, blockers and next actions. Store secrets
separately. If an executable goal is used, record and commit each verified task
before starting its successor. Keep these records in the game repository rather
than reactivating completed NovaGame goals.

A starter instruction file for the game can contain the following text. Replace
placeholders and add the concrete game rules before using it; this is a template,
not an instruction to create every listed system:

```text
# <GameName> Repository Instructions

This repository owns <GameName>'s backend composition, game domains, persistence
extensions and migrations, APIs, admin, and client. It consumes NovaGame at
<verified commit> from <source location> and never becomes its dependency.

Read <NovaGame source location>/AGENTS.md and
docs/workflows/game-project-start.md within that NovaGame checkout first.
Read this game's current task record at <record path> before implementation.
The approved task is <first complete use case>; its required checks are
<exact commands and acceptance behavior>.

Keep gameplay policy in this game. NovaGame supplies AdminAccess and GameConfig;
do not add game business code to framework projects. Follow rich Domain,
Application orchestration, thin HTTP, explicit migrations and server authority.
Do not change NovaGame or NovaCore outside separately authorized scope.
Preserve user changes and secrets. Record actual verification and unresolved
blockers, and leave a precise next action for the next agent.
```

Use this prompt to start the next Codex session in the actual game repository,
replacing every placeholder:

```text
Start <GameName> in <game repository path>, using NovaGame at
<NovaGame checkout path> pinned to <verified commit>.
First read the game's AGENTS.md and current task record, then NovaGame's
AGENTS.md and docs/workflows/game-project-start.md.
Client technology or engine/version/platform: <exact choice; Cocos optional>.
Authorized first use case: <behavior and backend/admin/client scope>.
Required acceptance: <real database/API/browser/engine expectations>.
Framework changes allowed: <none, or exact maintenance scope>.
Inspect existing files and Git state, implement the authorized complete slice,
run its required checks, and maintain the game-owned handoff record.
```

On resumption, inspect the actual recorded branch and commit rather than
assuming `master`. Continue the current authorized game task; ask for missing
material choices without treating completed NovaGame milestones as new work.

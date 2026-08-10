# SwordMaster Equipment Attribute Effect

**Milestone:** M1

**Stage:** M1-02 - Establish Ownership And Policy

**Status:** Approved

**Approval reference:** `user-approval-2026-08-01-equipment-attribute-effect`

**M1-06 evidence:** [PlayerAssets Acceptance Evidence](player-assets-m1-06-evidence.md)

## Approved Outcome

A player-owned validation character can equip an owned configured item. The equipped item contributes deterministic modifiers to the character's effective attributes. Unequipping it removes exactly the modifiers captured by that equipment assignment.

This is a SwordMaster capability. It does not create NovaGame-owned Character, Item, Equipment, Inventory, or Stats systems.

## Physical Backend Layout

M1 uses a separate backend under `samples/SwordMaster/server/`. The NovaGame root and server solutions remain framework-only.

```text
samples/SwordMaster/server/
  SwordMaster.Server.sln
  Directory.Build.props
  NuGet.Config
  src/
    SwordMaster.Domain/
    SwordMaster.Application.Contracts/
    SwordMaster.Application/
    SwordMaster.Host.EntityFrameworkCore/
    SwordMaster.Api/
    SwordMaster.DbMigrator/
  tests/
    SwordMaster.Domain.Tests/
    SwordMaster.Application.Tests/
    SwordMaster.Integration.Tests/
```

`Domain.Shared` is not created because this slice has no stable definitions that must be shared independently of Domain or Application Contracts. `Deployment` is not created because the API and DbMigrator composition roots can own the small set of SwordMaster-specific option checks required by this slice.

M1-03 added Domain, Application.Contracts, Application, Domain.Tests, and Application.Tests to the standalone solution. M1-04 then added Host.EntityFrameworkCore, Api, DbMigrator, and Integration.Tests when their integration work became active; they were not created as empty placeholders during M1-03.

Projects are organized internally by the `PlayerAssets` business domain. Item, Equipment, Character, and Stats do not become physical modules or top-level entity projects.

## Dependency Direction

M1 uses same-repository source references because NovaGame does not yet provide a versioned local .NET package feed and `NovaGame.Api` is currently a non-packable Web project. These references are transitional validation dependencies, not a stable NuGet contract.

| SwordMaster project | Allowed dependencies |
| --- | --- |
| Domain | NovaCore building blocks only. |
| Application.Contracts | NovaCore shared contract primitives only. |
| Application | SwordMaster Application.Contracts and Domain; NovaGame Application.Contracts for transport and NovaGame Domain for the extracted GameConfig validator; Microsoft options configuration support for game-owned settings. |
| Host.EntityFrameworkCore | SwordMaster Application ports and Domain, NovaCore EntityFrameworkCore, Pomelo, and MySQL connector packages. |
| Api | SwordMaster Application and Host.EntityFrameworkCore; transitional NovaGame.Api and NovaGame.Deployment composition references. |
| DbMigrator | SwordMaster Application and Host.EntityFrameworkCore; transitional NovaGame.Application, NovaGame.Host.EntityFrameworkCore, and NovaGame.Deployment composition references. |

The game projects must not use NovaGame repositories, EF configurations, `NovaGameDbContext`, or other infrastructure implementation classes. SwordMaster Application may consume only the separately authorized NovaGame Domain validator in addition to public Application.Contracts; composition roots retain the existing module references. NovaGame projects must never reference `SwordMaster.*` projects, namespaces, files, or contracts.

`SwordMaster.Host.EntityFrameworkCore` depends on `SwordMaster.Application` only to implement the game-owned PlayerAssets transaction port. This outer-to-inner dependency avoids enlisting `NovaGameDbContext` in equipment commands and does not allow Application to reference EF Core.

`samples/SwordMaster/server/Directory.Build.props` exposes a normalized `NovaGameRepositoryRoot` property so project files do not repeat fragile deep relative paths. `samples/SwordMaster/server/NuGet.Config` consumes the repository-local NovaCore 0.2.0 feed through a relative path and does not publish or consume accidental NovaGame `1.0.0` packages.

## Runtime Composition

The executable `SwordMaster.Api` host composes `SwordMasterApiModule` through NovaCore's module lifecycle. `SwordMasterApiModule` depends on `NovaGameApiModule`, `SwordMasterApplicationModule`, and `SwordMasterEntityFrameworkCoreModule`, and adds the NovaGame API assembly as an MVC application part so the existing AdminAccess and GameConfig controllers remain available without copying them.

The executable `SwordMaster.DbMigrator` host composes `SwordMasterDbMigratorModule`, which depends on `NovaGameApplicationModule`, `NovaGameEntityFrameworkCoreModule`, `SwordMasterApplicationModule`, and `SwordMasterEntityFrameworkCoreModule`. It applies NovaGame migrations before SwordMaster migrations, always runs the configured NovaGame administrator seed, and runs the validation PlayerAssets seed only in Development or Testing. Administrator and PlayerAssets seeds use separate database-specific transactions. The project does not reference the `NovaGame.DbMigrator` executable.

Both hosts require the exact `NovaGame` and `SwordMaster` connection names, validate MySQL 8.4, and reject equal database names so the framework and game migration histories cannot share one physical database. The API additionally validates its HTTP and JWT settings; the migrator validates only database and seed settings.

This source-composition boundary is experimental during M1. A future pure-NuGet boundary requires separate authorization to extract a packable NovaGame ASP.NET Core module, define explicit package versions, and add package provenance. M1 does not perform that maintenance.

## Persistence Boundary

SwordMaster owns `SwordMasterDbContext`, its mappings, repositories, migrations, and `sm_*` tables. Game entities and migrations never enter `NovaGameDbContext`.

The M1 deployment uses one MySQL 8.4 server with two databases and two configured connection strings:

- `NovaGame`: framework-owned administrator and GameConfig data;
- `SwordMaster`: game-owned player assets and command receipts.

Separate databases keep each DbContext's default EF migration history independent. `SwordMaster.DbMigrator` applies both migration sets. Equipment commands modify only the SwordMaster database, so they never require a distributed transaction with GameConfig.

## Domain Ownership

The owning SwordMaster business domain is `PlayerAssets`. M1 does not create separate Character, Inventory, Equipment, Item, or Stats domains.

`PlayerAssetsProfile` is the aggregate root keyed by `PlayerId`. For the approved validation slice it owns:

- one validation `PlayerCharacter` entity with base attributes;
- player-owned `OwnedEquipmentItem` instances that reference configured item identifiers;
- equipment assignments from a character slot to one owned item;
- a monotonic aggregate `Revision` used for optimistic concurrency.

The player owns the item; the character equips it. An owned item may be assigned to at most one slot. An occupied slot must be explicitly unequipped before another item can use it. These are SwordMaster policies for M1, not NovaGame contracts.

Meaningful value objects include `PlayerId`, `CharacterId`, `OwnedItemId`, `EquipmentSlot`, `StatCode`, `StatValue`, and an immutable `EquipmentModifierSnapshot`. Effective attributes are calculated from character base values plus the modifier snapshots on current equipment assignments and are not persisted as a second authoritative total.

`EquipmentSlot` is a path-safe game-owned identifier. After trimming, it must be non-empty, contain no whitespace or control characters, must not equal `.` or `..`, and must contain neither `/` nor `\`. The Domain value object, game-owned GameConfig parser, and HTTP transport enforce the same invariant so every accepted configured slot is addressable through the `{slot}` route segment.

The aggregate repository loads and persists only `PlayerAssetsProfile`. Application orchestration obtains a validated game-owned `EquipmentDefinition` from the configuration boundary and passes that immutable definition into the aggregate. The Domain project does not call GameConfig, HTTP, EF Core, or NovaGame implementation services.

No cross-domain aggregate reference is required for M1. A future inventory or multi-character requirement must be validated before splitting this aggregate or adding another domain.

## Configuration Policy

The M1 acceptance configuration uses:

- configuration set `swordmaster`;
- environment `development`;
- schema version `swordmaster.player-assets.v1`;
- collection `equipment-items`.

Each equipment definition contains a stable configured item identifier, one compatible slot, and a non-empty list of integer modifiers identified by stat code. SwordMaster owns this content schema and validates it after reading the immutable GameConfig snapshot envelope. NovaGame remains unaware of equipment fields or semantics.

Equip resolves the currently active immutable configuration version, validates the selected definition, and stores the configuration version identifier plus an immutable copy of its resolved modifiers on the equipment assignment. Later GameConfig activation does not silently change already equipped attributes. A player must explicitly unequip and equip again to adopt a newer definition. This pinned policy guarantees that unequip removes exactly the modifiers originally applied.

Missing definitions, unsupported schema versions, invalid slots, duplicate stat codes, non-integer values, or modifier overflow fail before the aggregate is saved.

## Player Identity And Authorization

Administrator tokens are not player credentials. SwordMaster adds a named `SwordMasterPlayer` JWT bearer scheme with its own issuer, audience, signing key, and validation options.

Player endpoints require an authenticated token with:

- a `sub` claim containing the authoritative `PlayerId`;
- a `token_use` claim equal to `player`;
- the SwordMaster player issuer and audience.

The player identifier never comes from route, query, or request-body input. Administrator tokens and tokens without the player-use claim are rejected.

M1 does not implement registration, account recovery, or a production login service. Integration tests mint short-lived player tokens through the test host. A manual development token may be supplied through ignored local configuration. This validates the authorization boundary without expanding the slice into an account system.

## HTTP Contracts

The game-owned API surface is:

```text
PUT    /api/player-assets/characters/{characterId}/equipment/{slot}
DELETE /api/player-assets/characters/{characterId}/equipment/{slot}
GET    /api/player-assets/characters/{characterId}/attributes
```

The PUT body contains only `ownedItemId`. Mutating requests require a GUID `Idempotency-Key` header and a strong `If-Match` ETag representing the expected aggregate revision. Responses return the new revision and ETag.

The attribute response contains base, equipment, and effective integer values keyed by stat code, plus current equipment assignments and their pinned configuration version identifiers. It does not expose EF entities or mutable domain objects.

Stable game-owned error codes include:

- `SwordMaster.PlayerAssets.InvalidRequest`;
- `SwordMaster.PlayerAssets.CharacterNotFound`;
- `SwordMaster.PlayerAssets.OwnedItemNotFound`;
- `SwordMaster.PlayerAssets.ItemNotEquipable`;
- `SwordMaster.PlayerAssets.SlotMismatch`;
- `SwordMaster.PlayerAssets.ItemAlreadyEquipped`;
- `SwordMaster.PlayerAssets.SlotOccupied`;
- `SwordMaster.PlayerAssets.SlotNotEquipped`;
- `SwordMaster.PlayerAssets.InvalidModifier`;
- `SwordMaster.PlayerAssets.ConcurrencyConflict`;
- `SwordMaster.PlayerAssets.IdempotencyConflict`;
- `SwordMaster.PlayerAssets.UnauthorizedPlayer`.

Validation failures return no partial mutation. A missing or unowned resource is not distinguished in a way that leaks another player's assets.

## Transactions, Concurrency, And Retry Safety

Every equip or unequip command uses one SwordMaster database transaction. The transaction persists the aggregate change and a `PlayerAssetCommandReceipt` identified by `(PlayerId, IdempotencyKey)`.

`PlayerAssetsAppService` uses `IPlayerAssetsTransactionManager`, implemented by `SwordMaster.Host.EntityFrameworkCore` over only `SwordMasterDbContext`. It must not use NovaCore's generic `IUnitOfWorkManager` in the combined host because that manager enlists every registered DbContext and cannot provide an atomic cross-database commit. The future PlayerAssets HTTP controller disables the global MVC unit-of-work filter before invoking this game-owned transaction boundary.

Repeating the same key with the same request fingerprint returns the recorded result. Reusing a key with a different fingerprint returns `IdempotencyConflict`. `PlayerAssetsProfile.Revision` is an EF optimistic-concurrency token; a stale ETag returns `ConcurrencyConflict` without overwriting state.

The application resolves and validates an immutable GameConfig version before starting the SwordMaster mutation transaction. The persisted modifier snapshot removes any need for a cross-database transaction.

Receipt and audit timestamps use UTC through `TimeProvider`. Structured logs include correlation ID, player ID, character ID, command type, result code, and revision, but never include tokens, signing keys, complete configuration payloads, or private player data.

M1-06-T02 implements this operation evidence in the SwordMaster API boundary. Successful GET and mutation responses log the authoritative result revision; handled failures log the stable result code and a revision only when the request contains a valid canonical strong ETag. Client-caused 4xx failures do not attach exception details. `PlayerAssetsObservabilityTests` verifies the fixed field set and secret exclusions through the real Program and MySQL, while the real MySQL API test reloads the HTTP-created command receipt and verifies its exact identity, fingerprint, resulting revision, and UTC completion time.

## Player Client Boundary

The SwordMaster client slice has three game-owned layers: generated PlayerAssets transport types plus the authenticated HTTP transport, the headless interaction model, and `SwordMasterPlayerAssetsCocosComponent` as a thin Cocos-compatible lifecycle wrapper. The wrapper delegates state and commands without calculating attributes, interpreting configuration, changing equipment optimistically, constructing transport or authentication, generating idempotency keys, or owning retry policy.

Each wrapper exclusively owns a fresh interaction model for its entire lifecycle. One abort signal is shared across `start`, `equip`, `unequip`, `retry`, and `recover`; `onDestroy()` aborts in-flight work, rejects later entries with `PlayerAssetsCocosComponentError` code `destroyed`, suppresses stale completion, and permanently prevents model reuse. The wrapper has no direct `cc` dependency and no production scene or prefab. A concrete Cocos forwarder must consume every returned promise and may ignore only the expected `destroyed` rejection after its own destruction; every other failure remains observable.

## NovaGame Boundary Review

M1 reuses only existing NovaGame infrastructure:

- AdminAccess for framework administrator operations;
- GameConfig draft, publication, activation, rollback, and immutable snapshot delivery;
- the current NovaGame ASP.NET Core and EF module composition points;
- the existing TypeScript GameConfig client and Cocos adapter.

M1-04-T03 includes one framework-only baseline correction required by the approved schema contract: remove GameConfig's contradictory hardcoded equality check that accepts only schema `1` after its public validation has already accepted safe consumer schema identifiers. Generic validation and snapshot behavior remain unchanged, and NovaGame tests use neutral schema examples rather than SwordMaster terminology.

SwordMaster owns all `PlayerAssets` domain code, configuration schema interpretation, player authentication, application contracts, APIs, DbContext, migrations, command receipts, error codes, tests, and client interaction for this capability.

The temporary source references do not promote any API to stable package status. M1 does not add SwordMaster terminology, equipment schemas, repositories, controllers, mappings, configuration validators, or extension flags to NovaGame. It does not modify NovaCore.

Any later extraction candidate must be recorded as evidence during M1-07 and separately authorized through M2. The complete PlayerAssets or equipment system is never an extraction candidate.

## Exact Source References

The following transitional source references are allowed:

| Consumer project | Referenced NovaGame project |
| --- | --- |
| `SwordMaster.Application` | `server/src/NovaGame.Application.Contracts/NovaGame.Application.Contracts.csproj` |
| `SwordMaster.Application` | `server/src/NovaGame.Domain/NovaGame.Domain.csproj` |
| `SwordMaster.Api` | `server/src/NovaGame.Api/NovaGame.Api.csproj` |
| `SwordMaster.Api` | `server/src/NovaGame.Deployment/NovaGame.Deployment.csproj` |
| `SwordMaster.DbMigrator` | `server/src/NovaGame.Application/NovaGame.Application.csproj` |
| `SwordMaster.DbMigrator` | `server/src/NovaGame.Host.EntityFrameworkCore/NovaGame.Host.EntityFrameworkCore.csproj` |
| `SwordMaster.DbMigrator` | `server/src/NovaGame.Deployment/NovaGame.Deployment.csproj` |

No other SwordMaster source project may reference a NovaGame implementation project. Test projects may reference their corresponding SwordMaster project and the API or DbMigrator only where integration composition requires it.

## Verification Commands

M1-03 and later stages must make these exact commands pass:

```powershell
$novaCoreLock = Get-Content server/novacore.lock.json -Raw | ConvertFrom-Json
$env:NUGET_PACKAGES = Join-Path (Resolve-Path .local) "nuget/$($novaCoreLock.commit)"
dotnet restore samples/SwordMaster/server/SwordMaster.Server.sln --configfile samples/SwordMaster/server/NuGet.Config --force-evaluate
dotnet format samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --verify-no-changes
dotnet build samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --configuration Release -maxcpucount:1 -warnaserror
dotnet test samples/SwordMaster/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssets
dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssets
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssetsApiTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterRuntimeCompositionTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterDbMigratorConfigurationTests
pnpm --dir samples/SwordMaster test:player-assets-interaction
pnpm --dir samples/SwordMaster test:player-assets-transport
pnpm --dir samples/SwordMaster test:player-assets-cocos
pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
git diff --check
```

`scripts/verify.ps1 -Scope SwordMaster` retains the existing TypeScript checks, restores and verifies `SwordMaster.Server.sln`, and runs the focused Domain, Application, player API, runtime-composition, and DbMigrator configuration tests. It requires all three dedicated PlayerAssets suites to report exact Vitest counts through JSON reporters: 12 transport tests, 12 interaction tests, and 7 Cocos-boundary tests. The transport suite covers the approved GET, PUT, and DELETE requests, player authentication, cancellation, strong ETags, canonical idempotency keys, stable validation and authorization failures, concurrency and idempotency conflicts, and invalid success contracts. The interaction suite covers loading, authoritative effective-attribute display, equip, unequip, exact ambiguous retries, token and authorization states, post-success GET-only retry, stale-state refresh with a fresh command key, validation, deterministic conflicts, recovery, cancellation, and overlapping-operation guards. The Cocos-boundary suite covers exact lifecycle and command delegation, authoritative state exposure, one shared abort signal, post-destroy guards, permanent model ownership, and suppression of late mutation success or failure without refresh, replay, recovery, or compensation. None of the suites adds client-owned game rules. The full offline SwordMaster TypeScript suite contains 38 tests. The scope then provisions one disposable MySQL 8.4 server with separate NovaGame and SwordMaster databases, runs the combined migrator twice, verifies migration and seed order, and runs runtime-database, real-Program PlayerAssets HTTP, and persistence tests. The HTTP test publishes and activates two GameConfig versions through the administrator API, uses the named player JWT scheme, deterministically coordinates concurrent retries at the real transaction boundary, verifies stale and conflicting mutations, proves pinned configuration behavior, and reloads the persisted assignment. Destructive test reset requires an explicit disposable marker and the exact test database names. The script removes the container, network, and volume in cleanup.

The generated HTTP contract is verified cumulatively: `PlayerAssetsApiTests` checks the generated PlayerAssets request body, headers, ETags, response codes, DTO schema, and player security, while `SwordMasterRuntimeCompositionTests` proves the framework and game routes plus their distinct security schemes are generated by the real `SwordMaster.Api.Program` host. The tracked PlayerAssets-only contract generates transport types, and the SwordMaster-owned HTTP transport consumes those types without introducing a generated runtime client.

Repository architecture checks must assert that both NovaGame solutions remain framework-only, the SwordMaster solution contains exactly the approved game projects, inner SwordMaster layers use only the exact authorized NovaGame references, and no NovaGame project references SwordMaster.

## Resolved M1-02 Decisions

All physical layout, ownership, policy, contract, and verification decisions required before M1-03 were resolved and approved in M1-02.

## Explicit Boundaries

- No combat, skills, enhancement, affixes, set bonuses, drops, crafting, durability, shop, economy, or progression behavior.
- No inventory-management API, character-creation API, account system, or production player-login workflow.
- No SwordMaster admin pages during this slice; operators use the existing generic GameConfig workspace for configuration content.
- No NovaGame business-domain additions or extraction during M1.
- No changes to NovaCore.

# SwordMaster Player Equipment Grant

## Approval And Scope

The user explicitly approved the SwordMaster-owned `player-equipment-grant` capability on
2026-08-10. The authorization reference is
`user-approval-2026-08-10-player-equipment-grant`.

This document freezes the M3-02 implementation contract. M3-03 and later stages may implement only
the behavior, paths, and verification defined here. The capability remains entirely in the existing
SwordMaster `PlayerAssets` domain. It does not create a NovaGame feature, a separate SwordMaster
business domain, a physical module, or another server project.

The approved operator outcome is:

1. An authenticated administrator with one game-owned permission enters an exact player identifier.
2. The administrator reviews that player's equipment-grant history and current PlayerAssets revision.
3. The administrator grants exactly one active configured equipment item with a required reason.
4. The administrator sees the durable grant result, actor, reason, configuration version, and time.
5. The administrator may roll back that grant only while its exact issued item remains owned and
   unequipped.

The server is authoritative. The request never supplies an owned-item identifier, grant identifier
for creation, configuration-version identifier, modifier, attribute value, quantity, price, or
timestamp.

## Physical Boundary

M3 uses the existing SwordMaster server solution and project set:

```text
samples/SwordMaster/server/
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

No source or test project is added. All capability namespaces and folders remain under
`PlayerAssets`. NovaGame `server/src`, `server/tests`, `clients/cocos`, and generic
`admin/apps/web-ele/src` receive no SwordMaster feature code.

The independently deployable game admin is:

```text
samples/SwordMaster/admin/
```

Its package name is `@swordmaster/admin-web-ele`. The pinned `admin/pnpm-workspace.yaml` may list
`../samples/SwordMaster/admin` so the game app can resolve the existing local `@vben/*` packages.
That workspace membership is build composition only. The generic `@vben/web-ele` package must not
depend on, import, route to, or display the SwordMaster app.

Do not create `samples/SwordMaster/AGENTS.md`.

## Domain Ownership

`PlayerAssetsProfile` remains the aggregate root authoritative for:

- equipment ownership;
- equipment assignments;
- whether an owned item is currently equipped;
- the PlayerAssets revision used for optimistic concurrency.

`PlayerEquipmentGrant` is a second aggregate root in the same SwordMaster `PlayerAssets` domain. It
owns:

- the immutable original grant result;
- the administrator and audit context for that result;
- the grant idempotency receipt and request fingerprint;
- active or rolled-back lifecycle state;
- the optional immutable one-time rollback result;
- the rollback idempotency receipt and request fingerprint.

Application coordinates the two aggregates. Neither aggregate repository mutates the other
aggregate. Both are committed by one SwordMaster transaction because both persist through
`SwordMasterDbContext`.

### PlayerAssetsProfile Transitions

M3 adds two explicit domain transitions:

```csharp
OwnedEquipmentItem GrantEquipment(
    long expectedRevision,
    OwnedItemId ownedItemId,
    string configuredItemId);

OwnedEquipmentItem RollbackGrantedEquipment(
    long expectedRevision,
    OwnedItemId ownedItemId);
```

`GrantEquipment` requires a current positive revision, a non-empty server-generated owned-item ID,
a normalized configured-item ID, and no existing owned item with that ID. It creates one unequipped
`OwnedEquipmentItem` and advances `PlayerAssetsProfile.Revision` exactly once.

`RollbackGrantedEquipment` requires a current positive revision and the exact owned-item ID held by
the selected grant. The item must still exist and no `EquipmentAssignment` may reference it. It
removes that item and advances the revision exactly once. It does not inspect another grant, remove
another item, auto-unequip, restore an older revision, or modify an equipment assignment.

The Application service obtains the owned-item ID only from the selected `PlayerEquipmentGrant`.
This prevents rollback from becoming an arbitrary inventory-removal operation.

### PlayerEquipmentGrant Lifecycle

A grant is created once in `Active` state. Its original fields never change:

- `GrantId`;
- `PlayerId`;
- `OwnedItemId`;
- `ConfiguredItemId`;
- `ConfigurationVersionId`;
- `GrantedByAdministratorId`;
- `GrantedAtUtc`;
- `GrantReason`;
- `GrantIdempotencyKey`;
- `GrantRequestFingerprint`;
- `GrantResultingRevision`;
- original correlation ID, IP address, and user agent.

The only state transition is `Active -> RolledBack`. It records exactly once:

- `RolledBackByAdministratorId`;
- `RolledBackAtUtc`;
- `RollbackReason`;
- `RollbackIdempotencyKey`;
- `RollbackRequestFingerprint`;
- `RollbackResultingRevision`;
- rollback correlation ID, IP address, and user agent.

A rolled-back grant cannot become active again. Its owned item cannot be re-created through rollback.
A second rollback with another idempotency key fails as already rolled back. An exact replay of the
first rollback key and fingerprint returns the original rollback result.

### Value Rules

- `GrantId`, `PlayerId`, `OwnedItemId`, administrator IDs, configuration-version IDs, and idempotency
  keys are non-empty GUIDs.
- The server generates `GrantId` and `OwnedItemId` through the configured GUID generator.
- `ConfiguredItemId` follows the existing `EquipmentDefinition` normalization and maximum length.
- Grant and rollback reasons are trimmed, contain 1 to 500 non-control characters, and are persisted
  exactly in normalized form.
- All persisted operation timestamps use UTC from the injected `TimeProvider`.
- A rollback timestamp cannot precede the grant timestamp.
- Request fingerprints are uppercase 64-character SHA-256 hexadecimal strings.
- Audit metadata uses the existing limits: IP address 64, user agent 512, and correlation ID 128
  characters. Empty metadata normalizes to `null`.

## Application Contract

The contract surface remains under
`SwordMaster.Application.Contracts.PlayerAssets`:

```csharp
public interface IPlayerEquipmentGrantAppService
{
    Task<PlayerEquipmentGrantPageDto> GetPageAsync(
        Guid playerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PlayerEquipmentGrantMutationResultDto> GrantAsync(
        Guid playerId,
        GrantPlayerEquipmentRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default);

    Task<PlayerEquipmentGrantMutationResultDto> RollbackAsync(
        Guid playerId,
        Guid grantId,
        RollbackPlayerEquipmentGrantRequest request,
        PlayerEquipmentGrantMutationContext context,
        CancellationToken cancellationToken = default);
}
```

The request and mutation context are fixed as:

```csharp
public sealed record GrantPlayerEquipmentRequest(
    string ConfiguredItemId,
    string Reason);

public sealed record RollbackPlayerEquipmentGrantRequest(string Reason);

public sealed record PlayerEquipmentGrantMutationContext(
    long ExpectedRevision,
    Guid IdempotencyKey);
```

The mutation result contains:

- grant ID;
- player ID;
- owned-item ID;
- configured-item ID;
- configuration-version ID;
- `Active` or `RolledBack` status;
- resulting PlayerAssets revision;
- original grant actor, normalized reason, and UTC time;
- optional rollback actor, normalized reason, and UTC time.

The page result contains the exact player ID, current PlayerAssets revision, one-based page,
page size, total count, and the newest-first grant DTOs. Page defaults to 1. Page size defaults to 20
and must be from 1 through 100. Ordering is `GrantedAtUtc DESC, GrantId DESC` so equal timestamps are
deterministic.

IP address, user agent, correlation ID, request fingerprints, and idempotency keys are persisted
evidence but are not returned by the HTTP DTOs.

## Authorization

There is one game-owned permission:

```text
SwordMaster.PlayerAssets.EquipmentGrants.Manage
```

`PlayerEquipmentGrantPermissions` and its definition provider belong to
`SwordMaster.Application.Contracts/PlayerAssets`. The provider is registered by
`SwordMasterApplicationContractsModule`.

All three endpoints use the default NovaGame administrator `Bearer` authentication scheme and
`[NovaAuthorize(PlayerEquipmentGrantPermissions.Manage)]`. They never accept the
`SwordMasterPlayer` scheme. Application repeats the permission check through `IPermissionChecker`
and resolves the current administrator ID from a game-owned current-administrator port. Both checks
occur before player or grant existence is revealed.

The SwordMaster DbMigrator runs a game-owned permission seed after
`AdministratorSeedContributor`. The seed:

1. verifies the permission definition exists;
2. loads the built-in `Administrators` role from NovaGame persistence;
3. calls that NovaGame aggregate's idempotent `GrantPermission` behavior;
4. writes one NovaGame `PermissionGranted` audit only when the assignment is new;
5. commits through a NovaGame database transaction before any SwordMaster development seed.

Running the migrator twice leaves one role permission and one corresponding framework audit. This
composition write is separate from grant execution. Grant and rollback never write NovaGame
persistence and M3 introduces no distributed transaction.

## Configuration Boundary

Grant resolves `ConfiguredItemId` through the existing game-owned active
`IEquipmentDefinitionProvider` before opening the SwordMaster mutation transaction. The active
published GameConfig snapshot is authoritative.

A found, valid `EquipmentDefinition` supplies the exact normalized configured-item ID and
`ConfigurationVersionId` recorded by the grant. Missing configuration fails as
`ConfigurationNotFound`; invalid snapshot or definition data fails as `ConfigurationInvalid`.
Neither failure creates an owned item, grant, revision change, or audit record.

Exact replay is checked before resolving current configuration. A completed grant therefore remains
replayable after the active configuration changes or disappears and returns its originally recorded
configuration version and result.

M3 does not add an equipment catalog endpoint. The admin accepts an exact configured-item ID and
shows server validation feedback.

## Idempotency And Transaction Order

Command identity is scoped by:

```text
PlayerId + CommandKind + IdempotencyKey
```

`CommandKind` is exactly `Grant` or `Rollback`. Reusing the same GUID once for a grant and once for a
rollback is allowed because the command kinds are distinct. Reusing a key for another request of the
same kind and player is not allowed.

The fingerprint input is deterministic JSON written with `Utf8JsonWriter` in the listed property
order and hashed as exact UTF-8 bytes with SHA-256:

```json
{"command":"grant","playerId":"D-guid","expectedRevision":1,"configuredItemId":"item-id","reason":"normalized reason"}
```

```json
{"command":"rollback","playerId":"D-guid","grantId":"D-guid","expectedRevision":2,"reason":"normalized reason"}
```

GUID text uses lowercase canonical `D` form. Revisions use invariant JSON integers. JSON string
escaping is performed by `Utf8JsonWriter`; string concatenation is not used. The idempotency key and
administrator ID are not fingerprint fields because the key selects the receipt and command scope is
player plus command kind.

For either command, an existing receipt with the same fingerprint returns its original result with
`200 OK`, the original ETag, no new timestamp, and no write. A different fingerprint returns
`IdempotencyConflict` with no write.

### Grant Order

1. Validate route, headers, body, normalized reason, administrator identity, and permission.
2. Look up an existing grant receipt without a lock; return or conflict when found.
3. Resolve the active equipment definition.
4. Begin one SwordMaster database transaction.
5. Lock `PlayerAssetsProfile` by player ID with `FOR UPDATE`.
6. Look up the grant receipt again inside the transaction; return or conflict when found.
7. Enforce the expected PlayerAssets revision.
8. Generate grant and owned-item IDs.
9. Call `PlayerAssetsProfile.GrantEquipment`.
10. Create `PlayerEquipmentGrant` with the resulting revision and audit context.
11. Save both aggregates and commit once.

The second receipt lookup makes concurrent same-key requests converge on one result. Concurrent
different-key requests using the same expected revision serialize on the profile; only the first may
advance it.

### Rollback Order

1. Validate route, headers, body, normalized reason, administrator identity, and permission.
2. Look up an existing rollback receipt by player and key without a lock; return or conflict when
   found.
3. Begin one SwordMaster database transaction.
4. Lock `PlayerAssetsProfile` by player ID with `FOR UPDATE`.
5. Lock the selected `PlayerEquipmentGrant` with `FOR UPDATE`.
6. Look up the rollback receipt again; return or conflict when found.
7. Confirm the grant belongs to the route player and remains active.
8. Enforce the expected PlayerAssets revision.
9. Call `PlayerAssetsProfile.RollbackGrantedEquipment` with the grant's exact owned-item ID.
10. Mark the grant rolled back with the resulting revision and audit context.
11. Save both aggregates and commit once.

The lock order is always profile then grant. No code path may lock a grant and then a profile. A
transaction failure rolls back the profile change and grant evidence together. EF execution-strategy
retries remain disabled; callers retry through the explicit idempotency contract.

## Rollback Semantics

Rollback is a compensating game command. It is not:

- a database rollback;
- an optimistic-concurrency revision rollback;
- a GameConfig rollback;
- deletion of grant history;
- automatic unequip;
- arbitrary inventory removal.

Rollback succeeds only when the selected active grant exists for the route player, its exact issued
item is still owned, and that item is not referenced by an equipment assignment. Success removes the
item, advances the PlayerAssets revision once, and permanently records the rollback result.

If the item is equipped, rollback returns `RollbackBlockedItemEquipped`. The operator cannot force
the action. The player may later unequip through the existing player API, after which the operator
must refresh history, use the new ETag and a new idempotency key, and explicitly retry rollback.

If the item is missing while the grant is active, rollback returns `GrantedItemMissing`. It does not
silently repair history or mark the grant rolled back.

## HTTP Contract

The controller route is:

```text
api/swordmaster/operations/player-assets/{playerId}/equipment-grants
```

The controller binds `playerId` and `grantId` as `Guid` values without ASP.NET route constraints.
This keeps the public path unchanged while ensuring malformed identifiers reach the endpoint's
authenticated model-validation boundary and return the stable `InvalidRequest` response instead of
an unwrapped routing 404.

All responses use the existing `ApiResponse<T>` envelope. Every successful response supplies the
strong ETag `"<positive-decimal-player-assets-revision>"`.

### Get Grant History

```http
GET /api/swordmaster/operations/player-assets/{playerId}/equipment-grants?page=1&pageSize=20
Authorization: Bearer <administrator-access-token>
```

Success is `200 OK` with `PlayerEquipmentGrantPageDto`. It is exact-player lookup only. There is no
name, account, partial, fuzzy, or global search.

### Grant Equipment

```http
POST /api/swordmaster/operations/player-assets/{playerId}/equipment-grants
Authorization: Bearer <administrator-access-token>
Idempotency-Key: 11111111-1111-1111-1111-111111111111
If-Match: "7"
Content-Type: application/json

{"configuredItemId":"starter-sword","reason":"Customer support correction"}
```

Success and exact replay are `200 OK` with `PlayerEquipmentGrantMutationResultDto` and the resulting
revision ETag.

### Roll Back Grant

```http
POST /api/swordmaster/operations/player-assets/{playerId}/equipment-grants/{grantId}/rollback
Authorization: Bearer <administrator-access-token>
Idempotency-Key: 22222222-2222-2222-2222-222222222222
If-Match: "8"
Content-Type: application/json

{"reason":"Correction approved by support lead"}
```

Success and exact replay are `200 OK` with the same mutation-result contract and resulting revision
ETag.

Mutations require exactly one non-empty canonical GUID `Idempotency-Key` in `D` format and exactly
one strong quoted positive decimal `If-Match` ETag. Weak tags, `*`, duplicate values, zero,
non-canonical values, and missing headers return `400 Bad Request`.

## Stable Failures

The M3 administrator contract uses these exact codes:

| Code | HTTP | Meaning |
| --- | --- | --- |
| `SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest` | 400 | Invalid route, paging, header, body, configured-item ID, or reason. |
| `SwordMaster.PlayerAssets.EquipmentGrants.UnauthorizedAdministrator` | 401 | No valid default administrator credential. |
| `SwordMaster.PlayerAssets.EquipmentGrants.Forbidden` | 403 | Administrator lacks the Manage permission. |
| `SwordMaster.PlayerAssets.EquipmentGrants.PlayerNotFound` | 404 | The exact PlayerAssets profile does not exist. |
| `SwordMaster.PlayerAssets.EquipmentGrants.GrantNotFound` | 404 | The route grant does not exist for that player. |
| `SwordMaster.PlayerAssets.EquipmentGrants.ConfigurationNotFound` | 422 | The active configuration has no requested equipment definition. |
| `SwordMaster.PlayerAssets.EquipmentGrants.ConfigurationInvalid` | 422 | Active snapshot or equipment-definition data is invalid. |
| `SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict` | 409 | The supplied PlayerAssets revision is stale. |
| `SwordMaster.PlayerAssets.EquipmentGrants.IdempotencyConflict` | 409 | The command key was used for another fingerprint in the same scope. |
| `SwordMaster.PlayerAssets.EquipmentGrants.AlreadyRolledBack` | 409 | Another command already rolled back the grant. |
| `SwordMaster.PlayerAssets.EquipmentGrants.RollbackBlockedItemEquipped` | 409 | The issued item is currently equipped. |
| `SwordMaster.PlayerAssets.EquipmentGrants.GrantedItemMissing` | 409 | The active grant's exact issued item is no longer owned. |

Authentication middleware produces 401 before the controller. `NovaAuthorize` produces 403 before
the action. Application returns the same semantic failures when invoked without HTTP. Unknown
exceptions remain 500 and do not expose internal details.

## Persistence Contract

M3 adds one SwordMaster table through an explicit `SwordMasterDbContext` migration named
`AddPlayerEquipmentGrants`:

```text
sm_player_equipment_grants
```

It contains:

- non-null grant identity, player, owned item, configured item, configuration version, status,
  original administrator, time, reason, key, fingerprint, resulting revision, and audit metadata;
- nullable rollback administrator, time, reason, key, fingerprint, resulting revision, and audit
  metadata, all populated together only after rollback.

Identifiers use canonical `char(36)` storage. UTC values use `datetime(6)`. Fingerprints use
`char(64)` with ASCII binary comparison. Configured-item IDs use the existing binary `utf8mb4`
collation. Status is a bounded numeric enum. Reasons and audit strings use the limits defined above.

Required keys and indexes are:

```text
PK (GrantId)
FK PlayerId -> sm_player_assets_profiles.PlayerId ON DELETE RESTRICT
UNIQUE (PlayerId, OwnedItemId)
UNIQUE (PlayerId, GrantIdempotencyKey)
UNIQUE (PlayerId, RollbackIdempotencyKey)
INDEX (PlayerId, GrantedAtUtc, GrantId)
```

MySQL permits multiple `NULL` values in the nullable rollback-key unique index. Once set, the index
prevents one rollback key from selecting two grants for the same player. Grant and rollback keys use
separate indexes, preserving command-kind scope.

There is deliberately no foreign key from the history row to `sm_owned_equipment_items`: successful
rollback removes the owned row while retaining grant history. There is no cross-database foreign key
to NovaGame administrators or GameConfig versions.

`IPlayerEquipmentGrantRepository` provides no-tracking page and receipt lookups plus transactional
`FOR UPDATE` lookup by grant ID. Every locking method rejects calls without an active
`SwordMasterDbContext` transaction.

The migration must be reviewed, its designer and model snapshot must be current, it must apply to
MySQL 8.4, and the combined migrator must remain idempotent when executed twice.

## Audit And Observability

Successful grant and rollback evidence is part of `PlayerEquipmentGrant` and commits atomically with
the PlayerAssets mutation. It records authenticated administrator ID, normalized reason, UTC time,
configuration version for the grant, resulting revision, fingerprint, correlation ID, IP address,
and user agent.

Exact replays do not append evidence, change timestamps, or advance revisions. Failed and denied
attempts do not create a separate SwordMaster database transaction. They emit structured logs with
correlation ID, administrator ID when available, player ID, grant ID when applicable, command kind,
stable result code, and resulting revision when available.

Logs never contain reasons, request bodies, access or refresh tokens, signing keys, connection
strings, complete configuration payloads, IP addresses, user agents, or private player data beyond
the exact operational identifiers required for correlation.

## Administrator Application Boundary

The SwordMaster admin uses Vben Admin 5 `web-ele`, Vue 3, Element Plus, strict TypeScript, and the
pinned admin Node and pnpm toolchain. It uses the composed SwordMaster API's existing administrator
login, refresh, logout, permission-code, and current-user endpoints. It does not create another token
scheme or persist refresh tokens in JavaScript-readable storage.

The app uses a distinct `swordmaster-admin` storage namespace and default authenticated route:

```text
/player-equipment-grants
```

Only `SwordMaster.PlayerAssets.EquipmentGrants.Manage` exposes the route and enables grant or
rollback actions. Client filtering never replaces server authorization.

The workspace contains:

```text
samples/SwordMaster/admin/
  package.json
  index.html
  openapi-ts.config.mjs
  contracts/player-equipment-grants.openapi.json
  src/api/
  src/features/player-equipment-grants/
  src/generated/player-equipment-grants/types.gen.ts
  src/router/
  src/store/
  src/views/player-equipment-grants/
  tests/e2e/player-equipment-grants.spec.ts
  vite.config.mts
```

The first app composition may copy only the Vben bootstrap, adapters, layouts, authentication,
request-envelope handling, preferences, localization, and core fallback routes needed to run an
independent app. The M3 verifier records an allowlist and hashes unchanged protocol copies against
their generic-admin source at M3-05. SwordMaster feature API, state, route, view, tests, environment,
namespace, and branding remain game-owned and must differ from the generic app where required.

The operations workspace provides:

- exact GUID player input and explicit lookup;
- current revision and latest history after a successful lookup;
- configured-item ID and required reason inputs;
- confirmation before grant;
- newest-first paged history with status, item, configuration version, actors, reasons, and UTC times;
- rollback confirmation with a separate required reason;
- one in-flight mutation at a time;
- exact uncertain retry using the same body, ETag, and idempotency key;
- authoritative GET refresh after confirmed mutation;
- conflict refresh before any new explicit command;
- loading, empty, validation, unauthenticated, forbidden, not-found, conflict, ambiguous, server-error,
  retry, and cancellation states.

The UI performs no ownership, equipment, rollback, configuration, revision, or audit decision. It
does not derive inventory state or optimistically add or remove items.

## Generated Administrator Contract

The operator contract is separate from the existing player contract:

```text
samples/SwordMaster/admin/contracts/player-equipment-grants.openapi.json
samples/SwordMaster/admin/src/generated/player-equipment-grants/types.gen.ts
scripts/update-swordmaster-player-equipment-grant-contract.ps1
```

The OpenAPI artifact is captured from the real `SwordMaster.Api.Program` host and contains exactly
the three approved administrator paths, their transitive schemas, `ApiResponse` envelopes, headers,
status responses, and default `Bearer` security. It contains no player-only endpoint and no
`SwordMasterPlayer` security requirement.

`samples/SwordMaster/contracts/player-assets.openapi.json` remains byte-identical and continues to
contain only the existing player GET, PUT, and DELETE operations. TypeScript generation is type-only;
transport, retry, state, and UI behavior remain handwritten and tested.

## Planned Source Files

M3-03 and M3-04 use these existing-project paths. File grouping may combine closely related records,
but no behavior may move outside the named layer or domain:

```text
samples/SwordMaster/server/src/SwordMaster.Domain/PlayerAssets/
  PlayerAssetsProfile.cs
  PlayerAssetsEntities.cs
  PlayerAssetsErrors.cs
  PlayerEquipmentGrant.cs
  IPlayerEquipmentGrantRepository.cs

samples/SwordMaster/server/src/SwordMaster.Application.Contracts/PlayerAssets/
  IPlayerEquipmentGrantAppService.cs
  PlayerEquipmentGrantDtos.cs
  PlayerEquipmentGrantErrorCodes.cs
  PlayerEquipmentGrantException.cs
  PlayerEquipmentGrantPermissions.cs
  PlayerEquipmentGrantPermissionDefinitionProvider.cs
  PlayerEquipmentGrantRequests.cs

samples/SwordMaster/server/src/SwordMaster.Application/PlayerAssets/
  PlayerEquipmentGrantAppService.cs
  PlayerEquipmentGrantApplicationPorts.cs
  PlayerEquipmentGrantRequestFingerprint.cs

samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/
  PlayerEquipmentGrantConfiguration.cs
  PlayerEquipmentGrantRepository.cs

samples/SwordMaster/server/src/SwordMaster.Api/PlayerAssets/
  PlayerEquipmentGrantsController.cs
  PlayerEquipmentGrantExceptionFilter.cs
  PlayerEquipmentGrantOperationLogger.cs
  PlayerEquipmentGrantOpenApiOperationFilter.cs
  HttpCurrentSwordMasterAdministrator.cs
  HttpPlayerEquipmentGrantAuditContextAccessor.cs

samples/SwordMaster/server/src/SwordMaster.DbMigrator/PlayerAssets/
  SwordMasterPlayerEquipmentGrantPermissionSeedRunner.cs
```

The existing module, service-registration, DbContext, DbMigrator, configuration, migration snapshot,
and test-composition files receive only the registrations required by these concrete types.

## Focused Test Contract

The required focused .NET filters are:

- `FullyQualifiedName~PlayerEquipmentGrant` in Domain tests;
- `FullyQualifiedName~PlayerEquipmentGrant` in Application tests;
- `FullyQualifiedName~PlayerEquipmentGrantApiTests` in Integration tests;
- `FullyQualifiedName~PlayerEquipmentGrantOpenApiContractTests` in Integration tests;
- `FullyQualifiedName~PlayerEquipmentGrantPersistenceTests` in Integration tests;
- `FullyQualifiedName~PlayerEquipmentGrantMySqlApplicationTests` in Integration tests;
- `FullyQualifiedName~PlayerEquipmentGrantPermissionSeedTests` in Integration tests;
- the existing `SwordMasterRuntimeCompositionTests` and
  `SwordMasterDbMigratorConfigurationTests` filters.

Every filter must execute at least one test and have zero failed or skipped tests. Coverage must map
all approved happy paths, stable failures, exact replays, changed fingerprints, concurrent same-key
requests, concurrent different-key stale revisions, transaction rollback, lock order, audit fields,
migration, permission-seed idempotency, API headers, status mapping, and OpenAPI isolation.

The game admin later freezes exact Vitest counts before M3-05 completes. Its minimum focused suites
are 12 transport tests, 14 interaction-state tests, and 10 view/permission tests. The real Playwright
suite runs one complete desktop path and one complete mobile path against the non-mock SwordMaster
API.

## Exact Verification Commands

Run from the repository root.

M3-02 design acceptance:

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
dotnet format samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --verify-no-changes --verbosity minimal
dotnet build samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --configuration Release -maxcpucount:1 -warnaserror
git diff --check
```

M3-03 Domain and Application:

```powershell
dotnet restore samples/SwordMaster/server/SwordMaster.Server.sln --configfile samples/SwordMaster/server/NuGet.Config --force-evaluate
dotnet format samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --verify-no-changes --verbosity minimal
dotnet build samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --configuration Release -maxcpucount:1 -warnaserror
dotnet test samples/SwordMaster/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrant
dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrant
pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
git diff --check
```

M3-04 server integration:

```powershell
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantApiTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantOpenApiContractTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantPersistenceTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantMySqlApplicationTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantPermissionSeedTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterRuntimeCompositionTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterDbMigratorConfigurationTests
pwsh -NoProfile -File scripts/update-swordmaster-player-equipment-grant-contract.ps1
pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
git diff --check
```

M3-05 game admin:

```powershell
pnpm --dir admin --filter @swordmaster/admin-web-ele run format:check
pnpm --dir admin --filter @swordmaster/admin-web-ele run lint
pnpm --dir admin --filter @swordmaster/admin-web-ele run typecheck
pnpm --dir admin --filter @swordmaster/admin-web-ele run test:equipment-grant-transport
pnpm --dir admin --filter @swordmaster/admin-web-ele run test:equipment-grant-model
pnpm --dir admin --filter @swordmaster/admin-web-ele run test:equipment-grant-view
pnpm --dir admin --filter @swordmaster/admin-web-ele run build
pwsh -NoProfile -File scripts/verify.ps1 -Scope Admin
pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
git diff --check
```

M3-06 through M3-08 add the real API, dual-MySQL, browser, cleanup, bootstrap, and cumulative commands
specified by the active Goal. No stage may report a command as passed unless that exact command was
run successfully.

## Non-Goals

- Bulk, quantity, scheduled, campaign, currency, mail, shop, ranking, or generic reward grants.
- Player, account, name, or fuzzy search.
- An equipment catalog API.
- Auto-equip, auto-unequip, item transfer, item consumption, expiry, or approval chains.
- Arbitrary inventory removal or rollback of an item not created by the selected grant.
- Reversing database, PlayerAssets revision, GameConfig, or history.
- A NovaGame business capability, contract, physical module, generic rewards API, or admin-shell
  extraction.
- Adding SwordMaster behavior to generic NovaGame server, client, or admin source.
- Promoting the experimental GameConfig validator, starting M4, or modifying NovaCore.

## M3-02 Completion Contract

M3-02 is complete only when this document exists, its exact boundaries pass conditional repository
guards, the current SwordMaster solution remains formatted and builds in Release with warnings as
errors, Documentation and Repository verification pass, `git diff --check` passes, and the Goal
activates only M3-03 without adding capability implementation.

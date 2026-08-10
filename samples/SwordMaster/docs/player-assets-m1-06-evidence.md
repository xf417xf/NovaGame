# SwordMaster PlayerAssets M1-06 Acceptance Evidence

**Milestone:** M1

**Stage:** M1-06 - Verify Behavior End To End

**Capability:** `equipment-attribute-effect`

**Approval reference:** `user-approval-2026-08-01-equipment-attribute-effect`

**Status:** Frozen

**Frozen at:** 2026-08-02

**Completed at:** 2026-08-02

## Scope And Authority

This document freezes the requirement-to-evidence map for M1-06-T01. It is subordinate to the
[M1 goal](../../../docs/goals/m1.md) and the approved
[equipment attribute effect design](equipment-attribute-effect.md).

The capability remains entirely SwordMaster-owned. NovaGame receives no new business capability during M1-06. The matrix authorized only evidence work required to prove the already approved
behavior; it did not authorize another gameplay feature, a generic equipment framework, or an
extraction.

## Evidence Rules

- **Proven** means a named current test exercises the required boundary and its exact command passes.
- **Split** means separate tests prove adjacent layers, but no current test proves their composition.
- **Missing** means the approved requirement has no sufficient current test at the required boundary.
- **Not applicable** means the approved capability record explicitly excludes the surface.
- No mock may substitute for a missing cross-boundary acceptance path.
- A broad green command does not close a named gap unless the command executes the evidence named in
  this matrix.
- Existing focused tests remain authoritative where they already prove the required boundary. M1-06
  did not duplicate every domain or contract case in a live acceptance test.
- The matrix explicitly covers happy path, authorization, invalid input, concurrency conflict,
  idempotency conflict, retry and recovery, persisted state, command receipts, audit evidence,
  pinned GameConfig version, and client-visible results.

## Requirement-To-Evidence Matrix

| ID | Acceptance Requirement | Existing Evidence | Verification Command | Gap | Owning Task |
| --- | --- | --- | --- | --- | --- |
| AE-01 | The authoritative happy path loads base attributes, equips an owned configured item, returns the new revision, refreshes effective attributes, unequips, and returns to base attributes. | `PlayerAssetsMySqlApiTests.RealProgramUsesPublishedConfigurationAndPersistsIdempotentEquipmentEffects` proves the real Program and MySQL path. [player-assets-real-api.acceptance.ts](../tests/player-assets-real-api.acceptance.ts) now composes the public Cocos-compatible wrapper, interaction model, typed HTTP transport, live API, and MySQL for the same approved lifecycle. | `pnpm --dir samples/SwordMaster test:player-assets-real-api`; `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven:** the live client path covers load, equip, authoritative refresh, unequip, and final base attributes. | none |
| AE-02 | Equipment assignments expose authoritative base, equipment, and effective values, strong ETags, revisions, and the pinned GameConfig version; a later activation does not change an existing assignment, while explicit re-equip adopts the new version. | The real MySQL API test verifies version-one assignment, activation of version two, pinned results, unequip, re-equip, and version-two adoption. `GameConfigEquipmentDefinitionProviderTests.ResolvedDefinitionRemainsPinnedWhenActiveSnapshotChanges` verifies provider semantics. The live client acceptance verifies revision and ETag identity plus the pinned version through a fresh wrapper lifecycle. | `pnpm --dir samples/SwordMaster test:player-assets-real-api`; `dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssetsMySqlApiTests` | **Proven cumulatively:** server evidence covers later activation and re-equip, while the live client verifies fresh-consumer persistence of the pinned assignment. | none |
| AE-03 | Every stable game-owned failure is deterministic and makes no partial mutation: character not found, owned item not found, item not equipable, slot mismatch, item already equipped, slot occupied, slot not equipped, invalid modifier, concurrency conflict, idempotency conflict, and unauthorized player. | [PlayerAssetsProfileTests.cs](../server/tests/SwordMaster.Domain.Tests/PlayerAssets/PlayerAssetsProfileTests.cs) proves aggregate invariants. `PlayerAssetsAppServiceTests.DomainFailuresMapToStableApplicationErrors` and no-commit tests in [PlayerAssetsAppServiceTests.cs](../server/tests/SwordMaster.Application.Tests/PlayerAssets/PlayerAssetsAppServiceTests.cs) prove orchestration. `PlayerAssetsApiTests.PlayerAssetsFailureKindsMapToStableHttpResponses` in [PlayerAssetsApiTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsApiTests.cs) proves HTTP mapping. [PlayerAssetsPersistenceTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsPersistenceTests.cs) proves rollback, while the real MySQL API test adds authorization, concurrency, and idempotency evidence. | `dotnet test samples/SwordMaster/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssets`; `dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssets`; `dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssetsApiTests`; `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven cumulatively:** M1-06 did not duplicate one live test per stable failure. | none |
| AE-04 | Authorization accepts only the named SwordMaster player scheme and rejects anonymous callers, administrator tokens, invalid player claims, and player access to administrator operations. | `PlayerAssetsApiTests.AnonymousAndAdministratorTokensCannotCallPlayerEndpoints`, `InvalidOrMultiplePlayerClaimsAreRejected`, and `ValidPlayerTokenPopulatesCurrentPlayerButCannotCallAdministratorEndpoint` prove the HTTP boundary. The real MySQL API test rejects anonymous and administrator tokens. The transport suite preserves the stable authorization code without exposing tokens. | `dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssetsApiTests`; `pnpm --dir samples/SwordMaster test:player-assets-transport`; `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven cumulatively.** | none |
| AE-05 | `SwordMaster.PlayerAssets.InvalidRequest`, malformed request bodies, missing media types, invalid identifiers or slots, invalid mutation headers, weak ETags, noncanonical idempotency keys, and malformed success contracts fail without invoking authoritative mutation behavior. | [PlayerAssetsApiTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsApiTests.cs) proves controller and HTTP validation. [player-assets-http-transport.test.ts](../tests/player-assets-http-transport.test.ts) proves client-side metadata and response validation. The generated OpenAPI artifact fixes the public shape. | `dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssetsApiTests`; `pnpm --dir samples/SwordMaster test:player-assets-transport` | **Proven:** these are contract-edge requirements and do not require duplicate live-process cases. | none |
| AE-06 | A concurrency conflict preserves authoritative state; an idempotency conflict rejects key reuse for a different request; same-key same-request replay returns one result without duplicate mutation. | The real MySQL API test proves deterministic concurrent same-key replay, stale ETag rejection, conflicting key reuse, one resulting revision, and one receipt. Application and persistence tests prove transaction rechecks and receipt uniqueness. Transport and interaction tests preserve the distinct conflict contracts. | `pnpm --dir samples/SwordMaster test:player-assets-transport`; `pnpm --dir samples/SwordMaster test:player-assets-interaction`; `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven cumulatively.** | none |
| AE-07 | Retry and recovery preserve one logical mutation after an ambiguous failure, retry only GET after a confirmed mutation, refresh stale state, and require a fresh idempotency key only for an explicitly retried command. | [player-assets-interaction-model.test.ts](../tests/player-assets-interaction-model.test.ts) proves exact retry and recovery state transitions. Application receipt replay tests and the real MySQL concurrent replay prove the authoritative server half. | `pnpm --dir samples/SwordMaster test:player-assets-interaction`; `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven cumulatively:** a live fault-injection duplicate is not required. | none |
| AE-08 | Persisted state, assignments, modifier snapshots, revisions, row locks, optimistic concurrency, receipt uniqueness, and transaction rollback behave correctly on MySQL 8.4. | [PlayerAssetsPersistenceTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsPersistenceTests.cs) contains eight real MySQL tests. The real MySQL API test reloads the aggregate after HTTP mutations. | `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven** for the server persistence boundary. Fresh client-visible reload remains AE-16. | none |
| AE-09 | Successful mutations persist command receipts with the player, key, request fingerprint, resulting revision, and UTC completion time; conflicting or failed mutations do not create extra receipts. | [PlayerAssetCommandReceiptTests.cs](../server/tests/SwordMaster.Domain.Tests/PlayerAssets/PlayerAssetCommandReceiptTests.cs), `PlayerAssetsAppServiceTests.EquipCoordinatesConfigurationTransactionAndReceipt`, and persistence receipt tests prove the model and storage. `PlayerAssetsMySqlApiTests.RealProgramUsesPublishedConfigurationAndPersistsIdempotentEquipmentEffects` now reloads the HTTP-created receipt by player and idempotency key and verifies its exact SHA-256 fingerprint, resulting revision, fixed UTC completion time, and unchanged receipt counts across replay and failures. | `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven.** | none |
| AE-10 | Audit evidence and structured logs include correlation ID, player ID, character ID, command type, result code, and revision without logging tokens, signing keys, connection strings, full configuration payloads, or private player data. | [PlayerAssetsOperationLogger.cs](../server/src/SwordMaster.Api/PlayerAssets/PlayerAssetsOperationLogger.cs) emits one fixed structured field set for success and handled failure results. `PlayerAssetsObservabilityTests.RealProgramEmitsStructuredSuccessAndFailureOperationLogs` verifies those fields through the real Program and MySQL, including safe 4xx logging without exception details or secret values. | `dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --configuration Release --filter FullyQualifiedName~PlayerAssetsObservabilityTests`; `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven.** | none |
| AE-11 | GameConfig publication and activation use the real administrator path, while PlayerAssets resolves the active immutable snapshot and persists a pinned modifier copy. | `GameConfigEquipmentDefinitionProviderTests` proves validation and pinning. The real MySQL API test authenticates an administrator, publishes and activates two versions, and exercises player behavior against both. | `dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssets`; `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven.** | none |
| AE-12 | The generated PlayerAssets TypeScript types match the real `SwordMaster.Api.Program` OpenAPI contract and contain no generated runtime client. | `PlayerAssetsOpenApiContractTests.RealProgramPlayerAssetsContractMatchesTrackedArtifact` in [PlayerAssetsOpenApiContractTests.cs](../server/tests/SwordMaster.Integration.Tests/ClientContracts/PlayerAssetsOpenApiContractTests.cs) and `Test-SwordMasterPlayerAssetsGeneratedTypes` in [verify.ps1](../../../scripts/verify.ps1) perform byte comparisons. | `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven.** | none |
| AE-13 | The typed HTTP transport constructs the approved GET, PUT, and DELETE requests, uses player bearer authentication, handles cancellation, validates strong ETags and revisions, and preserves stable failures. | [player-assets-http-transport.test.ts](../tests/player-assets-http-transport.test.ts) contains 12 focused tests. The live acceptance uses the same public transport with Node fetch against the real API. | `pnpm --dir samples/SwordMaster test:player-assets-transport`; `pnpm --dir samples/SwordMaster test:player-assets-real-api` | **Proven cumulatively:** focused tests retain edge and failure coverage, and the one live test proves runtime request and response compatibility. | none |
| AE-14 | The headless interaction model exposes authoritative client-visible results and owns loading, equip, unequip, retry, stale refresh, recovery, cancellation, and overlap states without server rules. | [player-assets-interaction-model.test.ts](../tests/player-assets-interaction-model.test.ts) contains 12 focused tests. The live acceptance uses the public model for load, mutation refresh, fresh lifecycle reload, and final refresh. | `pnpm --dir samples/SwordMaster test:player-assets-interaction`; `pnpm --dir samples/SwordMaster test:player-assets-real-api` | **Proven cumulatively:** focused tests retain retry and recovery evidence, while the live path proves authoritative state publication. | none |
| AE-15 | The Cocos-compatible wrapper delegates exact state and commands, owns one fresh model lifecycle, shares one abort signal, rejects reuse after destruction, and suppresses late completion without optimistic changes. | [player-assets-cocos-component.test.ts](../tests/player-assets-cocos-component.test.ts) contains 7 focused tests. The live acceptance creates and destroys two independent public wrapper and model lifecycles. | `pnpm --dir samples/SwordMaster test:player-assets-cocos`; `pnpm --dir samples/SwordMaster test:player-assets-real-api` | **Proven cumulatively:** wrapper lifecycle semantics remain focused evidence, and the real composition uses no stub model or transport. A production Cocos scene remains unnecessary. | none |
| AE-16 | A fresh client lifecycle can load persisted state after a previous lifecycle equips an item, and all client-visible results remain authoritative after reload and unequip. | [player-assets-real-api.acceptance.ts](../tests/player-assets-real-api.acceptance.ts) destroys the first wrapper after equip, constructs a fresh transport, model, and wrapper, reloads revision 2 with the pinned assignment, then unequips and refreshes revision 3. | `pnpm --dir samples/SwordMaster test:player-assets-real-api` | **Proven:** the verifier enforces exactly one executed live acceptance test. | none |
| AE-17 | Acceptance infrastructure uses separate disposable NovaGame and SwordMaster MySQL databases, a loopback real API, short-lived credentials, deterministic cleanup, and no secret logging. | `Test-SwordMasterDatabase` in [verify.ps1](../../../scripts/verify.ps1) creates guarded disposable dual databases, runs the combined migrator, starts the built API on loopback, publishes and activates configuration through the real administrator API, signs a five-minute player JWT, executes Vitest in an isolated environment, checks revision 3 plus zero assignments and two receipts, and cleans every process and temporary resource in `finally`. | `pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster` | **Proven:** only the API URL, player token, and validation identifiers enter the isolated client process; administrator and database credentials, signing keys, connection strings, and tokens are not printed. | none |
| AE-18 | NovaCore to NovaGame to SwordMaster dependency direction remains valid, NovaGame contains no SwordMaster code, and no new NovaGame business contract is introduced. | Repository architecture checks inspect both framework solutions, the SwordMaster solution and project references, source references, namespaces, and the no-nested-AGENTS rule. | `pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository` | **Proven.** | none |
| AE-19 | Browser automation, a SwordMaster admin UI, and production Cocos scenes are not required for this approved capability. | The Capability Decision Record and M1 non-goals explicitly exclude these surfaces. | `pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation` | **Not applicable.** | none |

## Recorded Failure Coverage

| Public Code | Current Evidence | Status |
| --- | --- | --- |
| `SwordMaster.PlayerAssets.InvalidRequest` | Application invalid-mutation tests, API body and header validation, and transport metadata validation. | Proven |
| `SwordMaster.PlayerAssets.CharacterNotFound` | Domain failed-equip and attribute lookup tests, Application stable-error mapping, and API response mapping. | Proven cumulatively |
| `SwordMaster.PlayerAssets.OwnedItemNotFound` | Domain ownership lookup tests, Application stable-error mapping, and API response mapping. | Proven cumulatively |
| `SwordMaster.PlayerAssets.ItemNotEquipable` | Domain definition validation, missing active definition before transaction, and API response mapping. | Proven cumulatively |
| `SwordMaster.PlayerAssets.SlotMismatch` | Domain slot compatibility, Application mapping, API mapping, transport decoding, and interaction recovery. | Proven cumulatively |
| `SwordMaster.PlayerAssets.ItemAlreadyEquipped` | Domain single-assignment invariant plus Application and API mapping. | Proven cumulatively |
| `SwordMaster.PlayerAssets.SlotOccupied` | Domain occupied-slot invariant plus Application, API, and interaction evidence. | Proven cumulatively |
| `SwordMaster.PlayerAssets.SlotNotEquipped` | Domain unequip invariant plus Application and API not-found mapping. | Proven cumulatively |
| `SwordMaster.PlayerAssets.InvalidModifier` | Domain overflow and modifier validation, GameConfig parser validation, Application mapping, and API mapping. | Proven cumulatively |
| `SwordMaster.PlayerAssets.ConcurrencyConflict` | Application no-commit behavior, real Program and MySQL stale ETag rejection, transport decoding, and interaction stale refresh. | Proven |
| `SwordMaster.PlayerAssets.IdempotencyConflict` | Application key-fingerprint conflict, real Program and MySQL conflicting replay, and transport and interaction handling. | Proven |
| `SwordMaster.PlayerAssets.UnauthorizedPlayer` | Application pre-repository rejection, API scheme isolation, real Program rejection, transport decoding, and interaction state. | Proven |

These failures remain covered by their focused and cumulative tests. T02 and T03 did not add one
live acceptance case per error code. A future matrix revision would require concrete contradictory
evidence and a separate Goal record.

## Named Gaps

### G1 - Structured Operation And Receipt Evidence - Closed

M1-06-T02 added a SwordMaster API operation logger with the exact approved fields and focused real-
Program/MySQL evidence for one success and one stable failure. The existing real MySQL API flow now
reloads the successful HTTP-created receipt and verifies its identity, fingerprint, resulting
revision, and UTC completion time. No audit aggregate, table, public contract, business domain, or
NovaGame mechanism was added.

### G2 - Live Client Composition - Closed

[player-assets-real-api.acceptance.ts](../tests/player-assets-real-api.acceptance.ts) now composes:

```text
SwordMasterPlayerAssetsCocosComponent
  -> SwordMasterPlayerAssetsInteractionModel
    -> SwordMasterPlayerAssetsHttpTransport
      -> live SwordMaster.Api
        -> MySQL 8.4
          -> authoritative GET result
```

M1-06-T03 added exactly one live test without a stub model, transport, API, or database. It covers
load, equip, authoritative refresh, fresh-model reload, unequip, and final refresh. Existing focused
and real-MySQL tests remain authoritative for failures, ambiguous retry, and stale-state recovery.

### G3 - Live Harness Cleanup And Secrets - Closed

The harness reuses the guarded disposable MySQL lifecycle, runs the built real API on loopback, and
cleans up the API process, container, network, volume, result files, environment file, and logs in
`finally` paths. The TypeScript process receives only the API URL, a short-lived player token, and
validation identifiers through an isolated environment. Administrator credentials, database
credentials, signing keys, connection strings, and tokens do not appear in command output or logs.

## Authorized Follow-Up Work

M1-06-T02 closed only G1 and is complete. Its implementation remains limited to the SwordMaster API
logging boundary, focused observability evidence, and exact assertions over the existing command-
receipt table.

M1-06-T03 closed G2 and G3 and is complete. It constructs a fresh interaction model and wrapper for
persisted reload because a wrapper permanently owns its model lifecycle. It added no production
scene, prefab, presentation UI, client-owned game rule, NovaGame business contract, or extraction.

M1-06-T04 completed the frozen matrix, capability, cleanup, and cumulative repository-governance
commands without weakening an acceptance criterion. All M1-06 tasks and named gaps are complete.

At M1 completion, every M1 stage and task was complete. The evidence validated the narrower
`game-config-snapshot-consumer-validation` candidate, while the complete capability remained
SwordMaster-owned. At that time no repository milestone was active, and extraction and M2 remained
inactive. The later M2 authorization and completed extraction do not change this historical record.

## Exact Verification Commands

The existing capability baseline remains:

```powershell
$novaCoreLock = Get-Content server/novacore.lock.json -Raw | ConvertFrom-Json
$env:NUGET_PACKAGES = Join-Path (Resolve-Path .local) "nuget/$($novaCoreLock.commit)"
dotnet restore samples/SwordMaster/server/SwordMaster.Server.sln --configfile samples/SwordMaster/server/NuGet.Config
dotnet format samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --verify-no-changes
dotnet build samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --configuration Release -maxcpucount:1 -warnaserror
dotnet test samples/SwordMaster/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssets
dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssets
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerAssetsApiTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterRuntimeCompositionTests
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterDbMigratorConfigurationTests
pnpm --dir samples/SwordMaster test:player-assets-transport
pnpm --dir samples/SwordMaster test:player-assets-interaction
pnpm --dir samples/SwordMaster test:player-assets-cocos
pwsh -NoProfile -File scripts/verify.ps1 -Scope Client
pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster
```

M1-06-T02 added and passed this exact focused command:

```powershell
dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --configuration Release --filter FullyQualifiedName~PlayerAssetsObservabilityTests
```

M1-06-T03 added this exact command, and the repository verifier enforces exactly one executed
acceptance test:

```powershell
pnpm --dir samples/SwordMaster test:player-assets-real-api
```

M1-06-T04 ran the two new commands, every baseline command above, and:

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Scope Server
pwsh -NoProfile -File scripts/verify.ps1
pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
git diff --check
```

No command may be removed or weakened because a named gap is difficult to reproduce.

## Explicit Exclusions

Browser automation, an admin UI, production Cocos scenes, extraction, and new gameplay are excluded.
Combat, skills, enhancement, set bonuses, drops, shops, economy, progression, and live-operations
policy remain specific-game-owned and outside this capability. M1-07 may record and review reuse
evidence only through its ordered tasks; it does not authorize extraction or M2 work.

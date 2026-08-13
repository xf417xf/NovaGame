# SwordMaster Player Equipment Grant M3-07 Reuse Evidence

**Milestone:** M3

**Task:** M3-07-T02 - Audit Concrete Reuse Pressure

**Capability:** `player-equipment-grant`

**Status:** T02 evidence audit complete; frozen before the M3-07-T03 boundary decision

**Frozen baseline:** [Completed M3-06 Baseline Record](../../../docs/goals/m3.md#completed-m3-06-baseline-record), metadata SHA-256 `5c6c67213c76d34efc2feb7ee26ec86eb9a157f7f65552124c850437cf937ebc`

## Scope And Authority

This record applies the [extraction workflow](../../../docs/workflows/extraction.md) to the frozen
SwordMaster capability without performing extraction. It records observed behavior, duplication,
variation, existing reuse, and missing evidence. A similarly named feature or possible future game
is not evidence.

The complete equipment-grant capability remains SwordMaster-owned. This task adds no NovaGame
contract, generalized type, extension point, module, migration, API, admin package, or client
behavior. Extraction remains unauthorized.

At T02 completion this record did not choose a candidate, evaluate sufficiency, select a NovaGame
owner, or authorize extraction. M3-07-T03 was the only task authorized to record one evidence-backed
candidate or `none`; its later decision is recorded in the
[M3 Goal](../../../docs/goals/m3.md#m3-07-t03-boundary-decision).

Each observation is factual input for the T03 boundary decision. It does not establish candidate
identity, independent usefulness, ownership, extraction authorization, or a public contract.

## Evidence Method

The audit inspected the frozen Domain, Application, API, persistence, migration, permission,
generated-contract, independent-admin, player-client, and acceptance-test boundaries. It compared
the M3 administrator workflow with the existing M1 player workflow and compared the independent
SwordMaster admin composition with the generic NovaGame `web-ele` app.

Claims below cite the current implementation and focused tests that were part of the frozen
completed-M3-06 baseline. Read-only repository searches and byte comparisons were used for absence
and exact-copy claims. No hypothetical consumer, unimplemented feature, or roadmap noun is counted
as reuse evidence.

## Frozen Baseline And Search Scope

The baseline contains 25 tracked modifications and 102 selected untracked files across 127
Git-visible paths. The exact patch, physical-byte manifests, archives, exclusion record, and
fresh-clone reconstruction are documented in the [M3 Goal](../../../docs/goals/m3.md#completed-m3-06-baseline-record).
The current repository has one concrete game sample, `samples/SwordMaster/`; it has no second game
backend against which grant policy or a proposed public contract can be compared.

Repository searches found the equipment-grant implementation only in SwordMaster source, tests,
contracts, documentation, and M3 verification composition. The framework boundary verifier checks
that `server/src`, `server/tests`, `clients/cocos`, and `admin/apps/web-ele` have zero M3 feature
changes from commit `ba83e4af0d494da4176ee922c2caaf24f5fce905` in the M3 framework-boundary
logic in [verify.ps1](../../../scripts/verify.ps1).

## Concrete Capability Behavior And Test Evidence

| ID | Frozen behavior | Authoritative implementation | Focused evidence |
| --- | --- | --- | --- |
| M307-BE-01 | Grant creates exactly one server-identified, unequipped owned item and advances the PlayerAssets revision once. Compensation removes only that issued item and refuses an equipped item. | [PlayerAssetsProfile.cs](../server/src/SwordMaster.Domain/PlayerAssets/PlayerAssetsProfile.cs) lines 58-108 | [PlayerEquipmentGrantTests.cs](../server/tests/SwordMaster.Domain.Tests/PlayerAssets/PlayerEquipmentGrantTests.cs) lines 34-148 |
| M307-BE-02 | The lifecycle keeps immutable grant evidence and an optional one-time rollback result with actor, reason, UTC time, configuration version, request identity, revisions, and request metadata. | [PlayerEquipmentGrant.cs](../server/src/SwordMaster.Domain/PlayerAssets/PlayerEquipmentGrant.cs) lines 24-183 | [PlayerEquipmentGrantTests.cs](../server/tests/SwordMaster.Domain.Tests/PlayerAssets/PlayerEquipmentGrantTests.cs) lines 151-315 |
| M307-BE-03 | Application orchestration authorizes first, resolves the active equipment definition, performs replay checks, locks the profile before grant state, commits one SwordMaster transaction, and returns original evidence on exact replay. | [PlayerEquipmentGrantAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantAppService.cs) lines 50-358 | [PlayerEquipmentGrantAppServiceTests.cs](../server/tests/SwordMaster.Application.Tests/PlayerAssets/PlayerEquipmentGrantAppServiceTests.cs) lines 88-748 |
| M307-BE-04 | The MySQL-backed path verifies one atomic profile-and-grant result, same-key convergence, different-key serialization, profile-before-grant lock order, and rollback after failure between flush and commit. These behaviors arise from application orchestration, explicit transaction completion and rollback, `FOR UPDATE` locks, and lifecycle constraints. | [PlayerEquipmentGrantAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantAppService.cs) lines 91-258, [PlayerAssetsTransactionManager.cs](../server/src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsTransactionManager.cs) lines 15-80, [PlayerAssetsProfileRepository.cs](../server/src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsProfileRepository.cs) lines 27-43, [PlayerEquipmentGrantRepository.cs](../server/src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerEquipmentGrantRepository.cs) lines 45-98, and [PlayerEquipmentGrantConfiguration.cs](../server/src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerEquipmentGrantConfiguration.cs) lines 13-155 | [PlayerEquipmentGrantMySqlApplicationTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantMySqlApplicationTests.cs) lines 41-450 |
| M307-BE-05 | The operator API uses NovaGame administrator identity plus a SwordMaster permission, exact-player routes, strong revision ETags, canonical command metadata, stable failures, and safe structured logs. | [PlayerEquipmentGrantsController.cs](../server/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantsController.cs) lines 12-207 | [PlayerEquipmentGrantApiTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantApiTests.cs) lines 47-834 |
| M307-BE-06 | The independent admin performs exact-player lookup, explicit grant and rollback confirmations, authoritative refresh, conflict handling, exact retry after ambiguous failure, permission filtering, and responsive real-browser operation. | [model.ts](../admin/src/features/player-equipment-grants/model.ts) lines 213-438 and [index.vue](../admin/src/views/player-equipment-grants/index.vue) lines 180-687 | [model.test.ts](../admin/src/features/player-equipment-grants/model.test.ts) lines 152-700, [index.test.ts](../admin/src/views/player-equipment-grants/index.test.ts) lines 416-742, and [player-equipment-grants.spec.ts](../admin/e2e/player-equipment-grants.spec.ts) lines 157-367 |

## Observed Duplication And Variation

| ID | Observed surface | Current implementation and test evidence | Concrete duplication or variation | Facts retained for T03 |
| --- | --- | --- | --- | --- |
| M307-RP-01 | Existing game-owned PlayerAssets infrastructure | Both [PlayerAssetsAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerAssetsAppService.cs) lines 11-33 and [PlayerEquipmentGrantAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantAppService.cs) lines 11-47 consume the same profile repository, definition provider, and transaction manager. Root-lock behavior is exercised by [PlayerAssetsPersistenceTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsPersistenceTests.cs) lines 206-226 and grant contention by [PlayerEquipmentGrantMySqlApplicationTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantMySqlApplicationTests.cs) lines 248-408. | M3 reused existing SwordMaster ports and one transaction manager instead of creating parallel inventory persistence. Grant additionally locks and persists its lifecycle aggregate. | Reuse is concrete inside one game and one owning domain; there is no second game consumer or second transaction policy. |
| M307-RP-02 | HTTP mutation envelope | [PlayerAssetsController.cs](../server/src/SwordMaster.Api/PlayerAssets/PlayerAssetsController.cs) lines 112-189 and [PlayerEquipmentGrantsController.cs](../server/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantsController.cs) lines 119-207 each parse `If-Match`, parse `Idempotency-Key`, and emit a strong revision ETag. [PlayerAssetsApiTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsApiTests.cs) lines 248-267 and [PlayerEquipmentGrantApiTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantApiTests.cs) lines 237-334 freeze their inputs. | The revision grammar is equivalent, but the player route accepts a parsed D-format GUID while the operator route additionally requires the exact lowercase canonical text. OpenAPI filters repeat the same surface with that distinction. | There is duplicate parsing and metadata shape, plus a proven compatibility difference that an analysis must not erase. |
| M307-RP-03 | Server replay and idempotency orchestration | Equip/unequip perform preflight lookup, root lock, in-transaction receipt recheck, mutation, receipt persistence, and commit in [PlayerAssetsAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerAssetsAppService.cs) lines 50-193 and 228-274. Grant/rollback repeat the broad order in [PlayerEquipmentGrantAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantAppService.cs) lines 103-258 and 313-358. Focused replay behavior appears in [PlayerAssetsAppServiceTests.cs](../server/tests/SwordMaster.Application.Tests/PlayerAssets/PlayerAssetsAppServiceTests.cs) lines 58-145 and [PlayerEquipmentGrantAppServiceTests.cs](../server/tests/SwordMaster.Application.Tests/PlayerAssets/PlayerEquipmentGrantAppServiceTests.cs) lines 336-393 and 447-590. | The M1 receipt stores key, fingerprint, resulting revision, and time in [PlayerAssetCommandReceipt.cs](../server/src/SwordMaster.Domain/PlayerAssets/PlayerAssetCommandReceipt.cs) lines 12-65. M3 keeps separate grant and rollback keys plus the full original and compensation results on [PlayerEquipmentGrant.cs](../server/src/SwordMaster.Domain/PlayerAssets/PlayerEquipmentGrant.cs) lines 74-132. Pipe-delimited M1 fingerprints and canonical-JSON M3 fingerprints also differ. | The command order repeats, while receipt scope, result shape, fingerprint contract, and replay semantics are demonstrably different. |
| M307-RP-04 | Transaction and locking | Both use [PlayerAssetsTransactionManager.cs](../server/src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsTransactionManager.cs) lines 6-80. Rollback locks the profile before the grant in [PlayerEquipmentGrantAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantAppService.cs) lines 204-257, with equipped-block and rollback evidence in [PlayerEquipmentGrantMySqlApplicationTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantMySqlApplicationTests.cs) lines 122-244 and 411-450. | No second transaction abstraction was added. The extra aggregate lock and compensating result arise only in the administrator grant lifecycle. | Existing reuse removes infrastructure duplication; remaining lock variation is tied to current game policy. |
| M307-RP-05 | HTTP request audit context | NovaGame defines `IRequestAuditContextAccessor` and the IP/User-Agent/correlation record in [RequestAuditContext.cs](../../../server/src/NovaGame.Application/Auditing/RequestAuditContext.cs) lines 1-11 and supplies HTTP data in [HttpRequestAuditContextAccessor.cs](../../../server/src/NovaGame.Api/Auditing/HttpRequestAuditContextAccessor.cs) lines 6-30. SwordMaster defines the same three-field shape in [PlayerEquipmentGrantApplicationPorts.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantApplicationPorts.cs) lines 10-18 and reads the same HTTP inputs in [HttpPlayerEquipmentGrantAuditContextAccessor.cs](../server/src/SwordMaster.Api/PlayerAssets/HttpPlayerEquipmentGrantAuditContextAccessor.cs) lines 7-32. API forwarding and durable fields are verified by [PlayerEquipmentGrantApiTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantApiTests.cs) lines 96-234 and [PlayerEquipmentGrantPersistenceTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantPersistenceTests.cs) lines 268-346. | The data and HTTP acquisition are duplicated. The SwordMaster Application project references NovaGame contracts and Domain, while the existing framework audit port lives in the NovaGame Application implementation project. | The duplicate has a concrete dependency-location constraint and one game consumer; current evidence does not define a replacement boundary. |
| M307-RP-06 | Structured operation logging | [PlayerAssetsOperationLogger.cs](../server/src/SwordMaster.Api/PlayerAssets/PlayerAssetsOperationLogger.cs) lines 123-160 and [PlayerEquipmentGrantOperationLogger.cs](../server/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantOperationLogger.cs) lines 190-227 duplicate tolerant expected-revision parsing. Safe fields are tested in [PlayerAssetsObservabilityTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsObservabilityTests.cs) lines 34-129 and [PlayerEquipmentGrantApiTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantApiTests.cs) lines 621-834. | The parsing is the same, but the complete log contracts differ: player operations identify player/character, while grant operations identify administrator/player/grant and include authorization denial. | One low-level parser is repeated; the surrounding security and business log semantics are not interchangeable. |
| M307-RP-07 | Client mutation recovery | The player interaction model freezes an ETag and key, repeats the exact request after ambiguous failure, refreshes after success/conflict, and rejects stale refreshes in [player-assets-interaction-model.ts](../src/player-assets-interaction-model.ts) lines 271-473. The operator model does the same broad work in [model.ts](../admin/src/features/player-equipment-grants/model.ts) lines 213-438. [player-assets-interaction-model.test.ts](../tests/player-assets-interaction-model.test.ts) lines 81-418 and [model.test.ts](../admin/src/features/player-equipment-grants/model.test.ts) lines 385-700 prove both paths. | These are two concrete UI consumers in the same game. Player conflict recovery may issue a new equip/unequip command with a new key; the operator flow refreshes history and preserves the original grant conflict. Their result snapshots, authentication, commands, and failure codes differ. | There is repeated recovery structure and proven behavioral variation, not one byte-identical state machine. |
| M307-RP-08 | Bounded admin composition | The verifier pins eleven byte-identical game-owned copies against generic `web-ele` in its bounded-composition hash checks in [verify.ps1](../../../scripts/verify.ps1). A read-only same-path hash comparison found 29 overlapping source files: 12 identical and 17 different, with 21 additional SwordMaster-only files. Representative identical files are [auth.ts](../admin/src/api/core/auth.ts) lines 18-34 and [basic.vue](../admin/src/layouts/basic.vue) lines 1-48. | `router/routes/core.ts` is the twelfth identical file but is absent from the eleven-file equality list; it is path-allowed by the SwordMaster admin production-path guard in [verify.ps1](../../../scripts/verify.ps1). The private app consumes shared `@vben/*` libraries and does not depend on the generic application. | Exact composition copying creates maintenance pressure. Existing divergence and the explicit independent-deployment boundary prevent treating the whole app as identical. The guard coverage gap is recorded, not repaired by T02. |
| M307-RP-09 | Administrator authentication composition | Both apps use the same four administrator protocol calls in generic [auth.ts](../../../admin/apps/web-ele/src/api/core/auth.ts) lines 18-34 and SwordMaster [auth.ts](../admin/src/api/core/auth.ts) lines 18-34. SwordMaster adds a coalescing one-retry interceptor in [administrator-auth-response-interceptor.ts](../admin/src/api/administrator-auth-response-interceptor.ts) lines 46-92 and pending-logout/session cleanup in [store/auth.ts](../admin/src/store/auth.ts) lines 30-212. [request.test.ts](../admin/src/api/request.test.ts) lines 209-313, [store/auth.test.ts](../admin/src/store/auth.test.ts) lines 255-355, and [auth-routing.test.ts](../admin/src/router/auth-routing.test.ts) lines 272-301 verify those additions. | Protocol adapters and composition are duplicated, but only the game app has the stricter concurrent-refresh, partial-session cleanup, persisted uncertain-logout, route-reset, and isolated-namespace behavior. | The two current apps prove both reuse pressure and material lifecycle variation; they do not prove one current implementation satisfies both. |
| M307-RP-10 | Game operator transport, interaction, and view validation | Reason and identifier constraints are checked for operator feedback in [index.vue](../admin/src/views/player-equipment-grants/index.vue) lines 162-177 and 248-282, for state integrity in [model.ts](../admin/src/features/player-equipment-grants/model.ts) lines 624-684, and before HTTP in [player-equipment-grants.ts](../admin/src/api/player-equipment-grants.ts) lines 706-814. The boundaries are independently exercised by [index.test.ts](../admin/src/views/player-equipment-grants/index.test.ts) lines 433-440, [model.test.ts](../admin/src/features/player-equipment-grants/model.test.ts) lines 230-305, and [player-equipment-grants.test.ts](../admin/src/api/player-equipment-grants.test.ts) lines 190-300. | Some constraints repeat within one capability, but each layer has a distinct feedback or defense responsibility. The route, permission, history evidence, grant, and compensation concepts are SwordMaster-specific. | This is game-local synchronization pressure with no cross-game or generic-admin implementation. |
| M307-RP-11 | Configuration and permission composition | Grant reuses the existing equipment-definition provider in [PlayerEquipmentGrantAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantAppService.cs) lines 119-121 and records configured ID/version, while equip interprets slot/modifier policy in [PlayerAssetsAppService.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerAssetsAppService.cs) lines 68-93. The provider keeps neutral snapshot validation separate from SwordMaster content parsing in [PlayerAssetsGameConfigSnapshotParser.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerAssetsGameConfigSnapshotParser.cs) lines 21-125. The permission runner composes NovaGame role/audit repositories in [SwordMasterPlayerEquipmentGrantPermissionSeedRunner.cs](../server/src/SwordMaster.DbMigrator/PlayerAssets/SwordMasterPlayerEquipmentGrantPermissionSeedRunner.cs) lines 34-80, and [PlayerEquipmentGrantPermissionSeedTests.cs](../server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantPermissionSeedTests.cs) lines 27-104 prove idempotency and preconditions. SwordMaster declares a game-owned current-administrator port in [PlayerEquipmentGrantApplicationPorts.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantApplicationPorts.cs) lines 3-8, adapts NovaCore `ICurrentUser` in [HttpCurrentSwordMasterAdministrator.cs](../server/src/SwordMaster.Api/PlayerAssets/HttpCurrentSwordMasterAdministrator.cs) lines 7-49, and registers that adapter in [SwordMasterPlayerAuthentication.cs](../server/src/SwordMaster.Api/PlayerAssets/SwordMasterPlayerAuthentication.cs) lines 80-85. | M3 consumes existing neutral GameConfig validation and NovaGame administrator primitives. Equipment meaning, the one game permission, and the current-administrator adapter remain local. | Existing framework reuse is explicit. SwordMaster adds an application port and HTTP adapter over the existing authenticated principal; it does not introduce a second credential issuer, authentication protocol, or administrator persistence system. |

## Absent Or Insufficient Evidence

The following evidence is absent from the frozen repository. This section records missing inputs; it
does not decide the threshold or result that M3-07-T03 must apply.

- There is no second game backend, second game-owned equipment-grant implementation, or independent
  domain aggregate using the grant lifecycle.
- There is no current bulk, scheduled, quantity, currency, mail, reward, shop, or inventory grant
  behavior with which to compare command scope, audit requirements, persistence, or compensation.
- Generic `admin/apps/web-ele` contains no SwordMaster grant route, permission, transport, state
  model, view, generated contract, or feature test.
- There is no independently packaged admin shell, server mutation protocol, receipt mechanism, or
  audit-context contract consumed by both current apps or by multiple games.
- No evidence establishes an independent install/removal lifecycle or physical-module boundary for
  the complete capability or any observed repeated code.
- Similar words such as grant, rollback, equipment, audit, ETag, or idempotency do not establish
  identical invariants, result shapes, retry behavior, or ownership.

## Game Policy And Ownership Retained

SwordMaster continues to own all current policy and complete-system behavior, including:

- the exact player and equipment ownership aggregates, configured item meaning, and revision;
- creation of one unequipped item and removal of that exact item only while unequipped;
- the one-way grant-to-rolled-back lifecycle and its immutable original and compensation evidence;
- administrator permission, exact-player routes, error codes, request fingerprints, lock order,
  MySQL schema, migration, and repository queries;
- operator reasons, history paging, confirmations, conflicts, retries, presentation, branding,
  storage namespace, and game-admin deployment;
- all equipment, inventory, attribute, character, reward, economy, and gameplay policy.

## T03 Handoff

At T02 completion, M3-07-T03 still had to answer the boundary decision using only current evidence.
The frozen handoff questions were:

1. Is any smallest observed repeated behavior separable from SwordMaster policy and independently
   useful without naming a hypothetical future consumer?
2. Do the current second-use-case facts and semantic variations establish one coherent neutral
   responsibility, or do they show only similar orchestration with incompatible contracts?
3. Does an existing NovaGame primitive already satisfy the pressure, leaving only composition or
   game-local duplication?
4. If no observation crosses the required evidence threshold, must the decision record `none`?

T02 supplied facts and questions only. It provided no answer to them.

## Non-Decisions

- No candidate identifier was selected or proposed by T02.
- No NovaGame owner, layer, package, module, contract, API, adapter, or extension point was selected
  by T02.
- No extraction, migration, duplicate removal, public-contract promotion, or compatibility plan was
  designed or authorized by T02.
- The complete capability, admin app, PlayerAssets domain, equipment model, and gameplay policy are
  not eligible merely because internal patterns repeat.
- M3-07-T03 was the only task allowed to record one evidence-backed candidate or `none`; this
  evidence file remains the frozen T02 input rather than the later decision record.

## Verification

```powershell
rg -n "PlayerEquipmentGrant|player-equipment-grant" samples/SwordMaster server/src clients/cocos admin/apps/web-ele
rg -n "Idempotency-Key|If-Match|ETag|retry|conflict" samples/SwordMaster/src samples/SwordMaster/admin/src samples/SwordMaster/server/src
pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster
pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
git diff --check
```

The same-path admin count is reproduced by hashing every SwordMaster `admin/src` file that has a
generic `admin/apps/web-ele/src` counterpart. The boundary audit compares tracked and untracked
framework paths with the frozen base commit and requires zero feature changes.

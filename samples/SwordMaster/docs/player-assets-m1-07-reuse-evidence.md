# SwordMaster PlayerAssets M1-07 Reuse Evidence

**Milestone:** M1

**Stage:** M1-07 - Record Reuse Evidence

**Capability:** `equipment-attribute-effect`

**Status:** Complete; candidate validated; extraction not authorized

**Baseline:** `.local/m1-07-baseline/baseline.json`, SHA-256 `7913d9930aa156aac3fc4cb60edd87945b401f9eeca7fb055507aa9edc1bae58`

## Scope And Authority

This record applies the [extraction workflow](../../../docs/workflows/extraction.md) to the completed
SwordMaster capability without performing extraction. It records concrete duplication, variation,
portability pressure, and second-consumer evidence. A possible future need is not evidence.

The complete PlayerAssets or equipment system remains SwordMaster-owned. This stage does not add a
NovaGame contract, generalized type, extension point, module, migration, API, or client behavior.
The completed M1 record supplies the evidence prerequisite. Any validated candidate still requires
separate user authorization before M2 may begin.

M1-07 does not authorize extraction.

## Post-M1 Authorization

This completed M1 record remains historically unchanged: M1-07 itself did not authorize extraction.
On 2026-08-10 the user separately authorized M2 to extract exactly
`game-config-snapshot-consumer-validation`. The completed M2 record owns that authorization and the
resulting extraction decision. M2 extracted the neutral validator, and SwordMaster consumes it while
the C# `GameConfigSnapshotValidator` contract remains experimental. That one-time authorization is
exhausted and grants no continuing authorization. The complete `equipment-attribute-effect`
capability and every PlayerAssets, equipment, attribute, or gameplay policy remain SwordMaster-owned.

## Evidence Method

The audit inspected the completed Domain, Application, API, persistence, GameConfig, TypeScript, and
Cocos boundaries and searched the repository for independent consumers or duplicate implementations.
The repository currently contains one concrete game sample, SwordMaster. Therefore similarly named
game concepts do not provide second-game evidence.

Evidence is sufficient only when an independently useful mechanism is separated from SwordMaster
policy and has concrete behavior and tests outside one hypothetical future consumer.

## Concrete Reuse Evidence

All source paths and line references in this evidence section describe the frozen M1 baseline named
above. They are historical evidence, not claims that the post-M2 parser or builder still contains
the extracted validation implementation.

| Area | Concrete evidence | Reuse-pressure result |
| --- | --- | --- |
| GameConfig snapshot consumer validation | [PlayerAssetsGameConfigSnapshotParser.cs](../server/src/SwordMaster.Application/PlayerAssets/PlayerAssetsGameConfigSnapshotParser.cs) lines 44-107 validate the complete downloaded-byte hash, strict envelope, expected format, configuration set and schema, non-empty version, UTC timestamp, integrity metadata, and embedded raw-content hash. The equipment collection and modifier interpretation begin separately at line 109. [GameConfigEquipmentDefinitionProviderTests.cs](../server/tests/SwordMaster.Application.Tests/PlayerAssets/GameConfigEquipmentDefinitionProviderTests.cs) lines 56-122 isolate the generic envelope and integrity behavior from equipment-policy tests beginning at line 124. | **Concrete duplication and portability variation.** NovaGame's engine-independent TypeScript consumer validates the same neutral snapshot contract in [snapshot.ts](../../../clients/cocos/packages/game-config-client/src/snapshot.ts) lines 89-215, while the producer verifies related integrity behavior in [GameConfigSnapshotBuilder.cs](../../../server/src/NovaGame.Domain/Content/GameConfig/GameConfigSnapshotBuilder.cs) lines 102-139. The TypeScript HTTP consumer receives a strong ETag; the .NET in-process consumer receives `GameConfigSnapshotDownload.SnapshotHash`. |
| Deterministic integer stat accumulation | [PlayerAssetsProfile.cs](../server/src/SwordMaster.Domain/PlayerAssets/PlayerAssetsProfile.cs) lines 200-245 contain one private additive `string`-code to `int` accumulator with SwordMaster error mapping and overflow policy. Its focused tests remain inside the same aggregate. | **Insufficient.** There is no second consumer, alternate stat formula, or independent implementation. Extracting it would promote SwordMaster's additive integer policy before variation is known. |
| Equipment ownership and assignment | The PlayerAssets aggregate owns item identity, slot compatibility, one-item and one-slot assignment rules, pinned modifier snapshots, and equip/unequip sequencing. | **Game policy.** Similar nouns do not establish a reusable mechanism, and the complete equipment system is explicitly ineligible. |
| Idempotent command receipts | `PlayerAssetCommandReceipt` and `PlayerAssetsAppService` implement player-scoped GUID keys, SHA-256 request fingerprints, revision-only results, row locking, and one SwordMaster transaction for equip and unequip. | **Insufficient.** Equip and unequip are operations within one capability, not independent consumers. No other repository system implements the same receipt contract. |
| Revision ETags | PlayerAssets uses a quoted positive decimal aggregate revision for `If-Match`; GameConfig uses a quoted lowercase SHA-256 snapshot hash for cache validation. | **Meaningful semantic difference, not reusable duplication.** A shared type would hide different concurrency and caching contracts. Local request parsing and tolerant log parsing also intentionally have different failure behavior. |
| Cocos lifecycle wrapper | `SwordMasterPlayerAssetsCocosComponent` owns one model, an `AbortController`, destruction guards, and late-completion suppression. NovaGame's `GameConfigBootstrap` accepts a caller-owned signal but does not own or destroy a model lifecycle. | **Insufficient.** Only the SwordMaster wrapper needs these ownership semantics; no duplicate lifecycle mechanism exists. |
| Game-owned EF transaction boundary | `PlayerAssetsTransactionManager` commits one `SwordMasterDbContext` because the combined host's generic unit of work would enlist unrelated DbContexts. | **Insufficient.** This is a concrete composition constraint but has no second game, second DbContext policy, or reusable public contract. |
| Player authentication, errors, and operation logs | The named player JWT scheme, PlayerAssets error codes, HTTP mapping, and structured fields describe this game's player contract. | **Game policy.** No independent consumer proves a neutral mechanism. |

## T02 Candidate For Decision

T02 found enough evidence for T03 to evaluate one narrow identifier:

`game-config-snapshot-consumer-validation`

The possible mechanism is limited to validating neutral GameConfig snapshot bytes and metadata for a
consumer and returning the verified envelope plus raw canonical content. Concrete behavior includes:

- verifying the trusted complete-snapshot SHA-256 value against the exact downloaded bytes;
- parsing strict JSON with the exact snapshot and integrity fields;
- validating the expected format, configuration set, schema, optional version, and UTC timestamp;
- validating the supported hash algorithm and canonicalization profile;
- verifying the embedded hash against the exact raw `content` bytes;
- returning neutral metadata and raw content without interpreting a game collection.

The frozen M1 baseline exposed a real runtime variation that a later design had to resolve: the
TypeScript consumer received the trusted snapshot hash through an HTTP ETag and performed lossless
canonical JSON checks, while the .NET in-process consumer received `SnapshotHash` in the application
contract and validated the raw content hash through `JsonElement.GetRawText()`.

The following remain SwordMaster policy and are outside the candidate:

- the `swordmaster.player-assets.v1` schema identifier;
- the `equipment-items` collection and its record shape;
- item identifiers, slots, modifiers, stat codes, integer value policy, and duplicate handling;
- conversion to `EquipmentDefinition` and pinned modifier snapshots;
- environment choice, provider orchestration, PlayerAssets errors, and logging;
- equipment, inventory, character, persistence, API, and client behavior.

## T02 Conclusion

The audit found one evidence-backed mechanism for T03 consideration and rejected every broader or
gameplay-oriented candidate. During T02 the Goal metadata remained
`validated_extraction_candidate: none`; T03 owns the formal boundary decision below.

## T03 Decision

M1 validates `game-config-snapshot-consumer-validation` as the only candidate supported by the
completed evidence. This records a possible future extraction input; it does not authorize M2 or
define a public NovaGame API.

| Field | Decision |
| --- | --- |
| Candidate status | Validated for possible future M2 consideration |
| Stable identifier | `game-config-snapshot-consumer-validation` |
| Evidence reference | This document and the source and test links above |
| Possible NovaGame owner | `Content/GameConfig`, subject to M2 ownership and dependency review |
| Extraction authorization | Not granted |
| Extraction implementation | Not started |

### Independently Useful Mechanism

The candidate validates one neutral GameConfig snapshot for a consumer before game code interprets
its content. Its evidence-backed responsibility is limited to:

- accepting the exact snapshot bytes and a trusted complete-snapshot SHA-256 value;
- validating the exact neutral envelope and integrity fields;
- checking expected format, configuration set, schema, optional configuration version, and UTC time;
- checking the supported integrity algorithm and canonicalization profile;
- validating the complete bytes and embedded raw-content hashes;
- returning verified neutral metadata and raw content for a game-owned interpreter.

The mechanism is independently useful to a .NET game server that consumes NovaGame GameConfig and
to the existing engine-independent TypeScript client contract. It does not require an equipment,
character, inventory, or player-assets concept to explain its behavior.

### Proven Variation To Preserve

The shared contract has concrete runtime variation rather than an imagined extension point:

- the TypeScript HTTP client obtains the trusted snapshot hash from a quoted strong ETag;
- the .NET in-process consumer obtains it from `GameConfigSnapshotDownload.SnapshotHash`;
- the TypeScript consumer performs lossless UTF-8 and canonical JSON checks;
- the frozen M1 .NET consumer used strict `JsonDocument` parsing and hashed `content.GetRawText()`.

At M1-07 completion, a future M2 still had to define the exact common guarantee and runtime-specific
adapters before moving code. This historical requirement prohibited copying the M1 parser wholesale
or silently weakening the stronger TypeScript validation.

### SwordMaster Policy Retained

SwordMaster continues to own every concern after neutral content is returned, including:

- configuration-set and schema choices for PlayerAssets;
- collection names and record shapes;
- equipment identifiers, slots, modifiers, stat codes, integer rules, and duplicate policy;
- `EquipmentDefinition` construction and modifier pinning;
- provider orchestration, environments, errors, logs, persistence, APIs, and clients;
- the complete PlayerAssets, equipment, attribute, inventory, and character systems.

Attribute accumulation, equipment assignment, command receipts, revision ETags, Cocos lifecycle
ownership, transaction management, authentication, and operation logging remain rejected candidates
because no independent evidence supports extracting them.

### Authorization Boundary

The candidate record and completed M1 acceptance satisfy only the evidence prerequisite.

At M1-07 completion, M2 remained drafting and inactive, and extraction still required explicit user
approval of this exact candidate. Capability approval for `equipment-attribute-effect` was not
extraction approval for `game-config-snapshot-consumer-validation`. The later authorization and
completed extraction are recorded separately above and do not change this historical boundary.

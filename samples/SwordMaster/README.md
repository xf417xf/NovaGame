# SwordMaster Validation Consumer

This sample validates SwordMaster as a consumer of NovaGame infrastructure. It contains the M0.5
configuration smoke consumer and the approved M1 game-owned `equipment-attribute-effect` backend.
The backend remains separate from NovaGame, and the sample contains no copies of NovaGame framework
code or unrelated gameplay systems.

The committed assets are:

- `config/game-config.snapshot.json`, a compact UTF-8 snapshot exported in the NovaGame format;
- `config/game-config.snapshot.manifest.json`, a separately supplied sidecar containing the trusted
  complete `snapshotHash`.

The sidecar hash must come from the trusted export or release channel. The sample never derives the
expected hash from the snapshot bytes it is about to verify.

## Package Boundary

The sample links to the public packages `@novagame/game-config-client` and
`@novagame/cocos-creator-game-config`. `SwordMasterGameConfigBootstrapComponent` constructs the
public `GameConfigClient` and `GameConfigBootstrap`, activates a snapshot, and queries a generic
record through `GameConfigSnapshot.getRecord`. Its snapshot source is either a
`GameConfigAssetLoader` for bundled files or an injected public `GameConfigTransport` for another
delivery channel.

`GameConfigAssetLoader` accepts an optional abort signal and returns exact text or bytes, which
keeps the bundled path headless and lets a Cocos project supply its own asset-manager bridge without
a hard dependency on `cc`. The live acceptance path injects the public `FetchSnapshotTransport`
without changing the bootstrap component.

## Verification

Use Node.js 24.14.0 and pnpm 10.33.4. Build the linked NovaGame client packages before building this
independent package.

```powershell
pnpm install --frozen-lockfile
pnpm format:check
pnpm lint
pnpm typecheck
pnpm test:player-assets-interaction
pnpm test:player-assets-transport
pnpm test
pnpm build
```

The smoke tests cover a valid load and query, a wrong trusted hash, an unsupported schema, and
missing manifest or snapshot assets. They run without Cocos Creator.

## Real API Acceptance

The dedicated live suite consumes the active `development` snapshot for the `default`
configuration set. It expects the disposable operations workflow to finish with the first version
active after rollback: `items/starter-item` is present and `items/second-item` is absent. The first
request must activate the snapshot, and the second must revalidate it through the strong ETag and a
`304 Not Modified` response.

Only the anonymous client snapshot URL is required; do not provide administrator or database
credentials to the sample process.

```powershell
$env:NOVAGAME_ACCEPTANCE_API_URL = "http://127.0.0.1:5000"
pnpm test:real-api
```

The repository acceptance script owns the disposable MySQL, API, and admin lifecycle and invokes
this command before cleanup. The live suite is isolated from `pnpm test`, so normal sample checks
remain deterministic and offline.

## PlayerAssets Client Contract

The focused contract at `contracts/player-assets.openapi.json` is captured from the real
`SwordMaster.Api.Program` host. It contains only the approved PlayerAssets GET, PUT, and DELETE
operations, their transitive schemas, and the `SwordMasterPlayer` security scheme. The exact
`@hey-api/openapi-ts` version generates type-only output at
`src/generated/player-assets/types.gen.ts`; transport and interaction behavior are not generated.

From the repository root, regenerate both artifacts with:

```powershell
pwsh -NoProfile -File scripts/update-swordmaster-player-assets-contract.ps1
```

The SwordMaster verification scope compares a fresh real-Program capture with the tracked JSON and
regenerates the TypeScript file into a temporary directory for byte-level stale-output detection.

## PlayerAssets HTTP Transport

M1-05-T02 implemented the game-specific transport at `src/player-assets-http-transport.ts`. It consumes
the generated types without generating a runtime client and is limited to the three approved
PlayerAssets operations. The transport supplies the player bearer token, propagates cancellation,
handles strong ETags, validates and forwards caller-owned canonical idempotency keys for mutations,
and decodes stable server errors. It does not generate idempotency keys or own retries, interaction
state, presentation, equipment rules, or attribute calculation.

The dedicated `test:player-assets-transport` command must execute exactly 12 focused tests. The
repository verifier reads Vitest JSON output so this coverage cannot disappear inside the broader
offline suite. The focused cases cover GET, PUT, and DELETE request construction and success,
authorization and validation failures, both conflict codes, cancellation, idempotency-key
validation, ETag validation, and malformed success payloads.

## PlayerAssets Interaction Model

M1-05-T03 implemented the engine-independent interaction model at
`src/player-assets-interaction-model.ts`. It owns only client interaction state and coordinates the
accepted transport for load, equip, unequip, explicit retry, and authoritative recovery. Displayed
attributes and equipment assignments always come from GET responses; the model does not calculate
attributes, interpret equipment configuration, optimistically change assignments, refresh tokens,
or own server policy.

One logical mutation receives one canonical idempotency key. An uncertain mutation retry reuses the
exact command, ETag, and key. A concurrency conflict refreshes the authoritative snapshot before an
explicit retry creates a new key against the refreshed ETag. A confirmed mutation is always followed
by GET, and a failed post-mutation refresh retries only GET. Published snapshots and error details are
defensively frozen so presentation code cannot corrupt retained interaction state.

The dedicated `test:player-assets-interaction` command must execute exactly 12 focused tests. They
cover loading, authoritative display, equip, unequip, ambiguous retry, token and authorization
failure, post-success refresh, stale-state refresh, validation, deterministic conflicts, recovery,
cancellation, and overlapping-operation guards. The full offline SwordMaster suite contains 38
tests after the Cocos-boundary task.

## PlayerAssets Cocos-Compatible Boundary

M1-05-T04 implemented `src/player-assets-cocos-component.ts` as a thin lifecycle wrapper around the
headless interaction model. `start()` delegates to `load()`, while `equip`, `unequip`, `retry`, and
`recover` forward the caller's inputs with one shared lifetime signal. The exposed `state` is the
exact authoritative model state; the wrapper does not derive attributes or edit assignments.

Each wrapper permanently owns one fresh interaction model. `onDestroy()` aborts in-flight work,
rejects all later state access and commands with `PlayerAssetsCocosComponentError` code `destroyed`,
and prevents the model from being attached to another wrapper. Late success or failure is consumed
without refresh, replay, recovery, or compensation. The boundary has no direct `cc` dependency,
scene or prefab, transport or authentication construction, idempotency-key generation, optimistic
equipment updates, retry policy, or server-rule duplication.

A concrete Cocos `Component` forwarder must consume every returned promise, especially `start()`.
After its own destruction it may ignore only `PlayerAssetsCocosComponentError` with code `destroyed`;
all other failures remain observable. Each new component lifecycle must construct a fresh interaction
model and wrapper, and a destroyed model must never be reused. The dedicated
`test:player-assets-cocos` command executes exactly 7 focused tests for these boundaries.

## Optional Cocos Integration

No Cocos Creator version is pinned yet, so M0.5 does not require an editor check. After a Creator
version is selected, an optional scene check can:

1. Import both files under `config/` as raw assets without changing the snapshot bytes.
2. Implement `GameConfigAssetLoader` by wrapping the selected Creator asset-manager API.
3. Supply `LocalStorageSnapshotCache` from `@novagame/cocos-creator-game-config` if persistence is
   needed; the headless sample uses the public in-memory cache by default.
4. Construct `SwordMasterGameConfigBootstrapComponent` with the loader and the generic
   `bootstrap-records/startup` query.
5. Invoke its asynchronous `start()` method from a thin Creator component and treat
   `lifecycle.status === "ready"` as successful activation.

An eventual Creator-facing GameConfig wrapper should contain lifecycle wiring only. Snapshot verification,
activation, cache policy, and lookups remain in the NovaGame packages.

## M1 Backend Design

The approved SwordMaster-owned equipment-to-attribute capability and its separate backend boundary
are documented in [Equipment Attribute Effect](docs/equipment-attribute-effect.md). Its Domain,
Application, MySQL persistence, GameConfig integration, player API, runtime composition, generated
client contracts, authenticated HTTP transport, headless interaction model, thin Cocos-compatible
wrapper, structured operation evidence, and live wrapper-to-API/MySQL acceptance path are verified.
M1-06 and its cumulative repository governance are complete.

The completed [M1-06 acceptance evidence matrix](docs/player-assets-m1-06-evidence.md) maps every
approved requirement to passing evidence. M1 and every M1 stage and task are complete. The evidence
validated the narrower `game-config-snapshot-consumer-validation` candidate, while the complete
capability remains entirely SwordMaster-owned. SwordMaster now consumes the extracted neutral
validator without moving equipment or PlayerAssets policy into NovaGame. The extracted C#
`GameConfigSnapshotValidator` remains experimental. The one-time authorization was consumed and
grants no continuing authorization. M2 is complete.

M3 is complete. The completed [Player Equipment Grant design](docs/player-equipment-grant.md),
server, independent SwordMaster Vben admin, real dual-MySQL API, desktop/mobile browser, exact
28-command cumulative acceptance, boundary, reuse-evidence, secret, NovaCore, and cleanup paths are
verified. The complete player-equipment-grant capability remains SwordMaster-owned.
`request-audit-context-consumer-contract` remains a possible later candidate only and is neither
implemented nor authorized for extraction. M3 grants no continuing authorization for extraction,
another capability, M4, or NovaCore changes. No repository milestone is active.

The current [M1-07 reuse evidence](docs/player-assets-m1-07-reuse-evidence.md) records the concrete
audit and keeps every broader PlayerAssets, equipment, attribute, and gameplay candidate game-owned.

# SwordMaster Configuration Smoke Consumer

This sample validates SwordMaster as a consumer of NovaGame's public configuration packages. It
contains no gameplay, game-specific server behavior, or copies of NovaGame framework code.

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

The eventual Creator wrapper should contain lifecycle wiring only. Snapshot verification,
activation, cache policy, and lookups remain in the NovaGame packages.

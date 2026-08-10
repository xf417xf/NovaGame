# NovaGame Cocos Client

This workspace contains the engine-independent NovaGame configuration client and
the Cocos Creator integration boundary.

- `packages/game-config-client` validates, caches, activates, and queries
  published GameConfig snapshots without depending on a game engine.
- `adapters/cocos-creator` supplies HTTP, local-storage, and lifecycle adapters
  without taking a hard dependency on an unpinned Cocos Creator release.

The server remains authoritative. A client snapshot is activated only after its
complete response hash, embedded content hash, contract metadata, and lookup
indexes have all been validated.

The TypeScript client aligns with the producer and .NET validator for canonical
UTF-8 encoding, byte and depth limits, nonzero version identifiers, exact and
resolved references, and both integrity boundaries within its supported schema.
This wire-format parity does not promote the separate C# API to stable status.

`LocalStorageSnapshotCache` uses two slots and switches the active pointer only
after a candidate is written, read back, and hash-verified. Browser and native
storage quotas are platform-specific. Large snapshots should use a custom
`GameConfigSnapshotCache` backed by storage with sufficient capacity; a storage
failure leaves the previous valid snapshot active.

For cross-origin Cocos Web deployments, configure the server CORS policy to
allow the `If-None-Match` request header and expose the `ETag` response header.
Native transports are not subject to browser CORS enforcement.

Run the complete client checks from the repository root:

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Scope Client
```

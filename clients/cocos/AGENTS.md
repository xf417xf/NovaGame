# NovaGame Cocos Client Instructions

These instructions extend the repository-level `AGENTS.md` for files under `clients/cocos/`.

## Required Reading

Before client work, read:

- `docs/architecture.md`;
- `docs/workflows/development.md`;
- `docs/workflows/configuration-release.md` for configuration consumption;
- the current executable goal identified by `docs/roadmap.md` when working on milestone scope.

## Boundary

The Cocos client framework is reusable infrastructure. It must not contain SwordMaster gameplay, assets, names, progression rules, or content policy.

Keep engine-independent TypeScript concepts separate from Cocos Creator adapters:

- framework packages own transport-neutral contracts, snapshot validation, lookup APIs, cache policy, and deterministic data handling;
- adapters own Cocos Creator lifecycle, asset integration, platform APIs, and engine-specific bootstrapping;
- samples consume framework and adapter packages but are never imported by them.

Game-specific packages own combat presentation, skills, progression, equipment policy, economy flows, content, and other game behavior. Similar concepts in multiple games do not justify moving those packages into `clients/cocos/`; only a validated engine or transport mechanism may be extracted.

Future Unity or Godot support must not require server-side business-logic changes. It does not require sharing the same client implementation language or engine adapter.

## Contracts And Configuration

- Prefer generated TypeScript contracts from the server OpenAPI document.
- Do not manually duplicate server DTOs when generation is available.
- Validate snapshot schema, version, and integrity before activation.
- Keep the previous valid snapshot available when a new snapshot fails validation or activation.
- Do not read server database tables or admin storage formats directly.
- Treat client configuration and calculations as presentation or prediction data; the server remains authoritative.

## TypeScript Rules

- Enable strict type checking.
- Avoid `any` at public boundaries.
- Prefer explicit result and error types for transport and snapshot loading.
- Keep I/O asynchronous.
- Do not create `Utils`, `Helpers`, or `Common` dumping grounds.
- Add abstractions only for a concrete engine boundary or validated variation.

## M0.5 Scope

M0.5 covers an engine-independent configuration client, a Cocos adapter, and a SwordMaster smoke consumer that loads an exported snapshot. It does not include gameplay systems.

## Verification

Run formatting, linting, type checking, unit tests, package builds, and the SwordMaster snapshot-loading smoke test once their commands exist. Verify failure behavior for invalid, incompatible, missing, and stale snapshots.

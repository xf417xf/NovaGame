# NovaGame Documentation

This directory separates architectural context, repeatable workflows, roadmap context, and executable milestone state.

## Authority Map

- Repository and subtree `AGENTS.md` files contain durable coding-agent instructions.
- [Architecture](architecture.md) defines system boundaries and long-lived design decisions.
- Files under [workflows](workflows/) define repeatable ways of working.
- [Roadmap](roadmap.md) describes milestone direction and exit criteria. It is not authorization to implement future work.
- Files under [goals](goals/) are executable milestone runbooks. A goal is active only when its status is `in_progress` or `blocked` and `execution_authorized` is `true`. `ready` and `drafting` definitions do not authorize work; a completed goal is a verification record, not continuing authorization.
- The current user request remains the authority for the specific task being performed.

M0.5, M1, and M2 are complete. For the approved SwordMaster-owned `equipment-attribute-effect` capability, generated PlayerAssets contracts, the authenticated HTTP transport, the headless interaction model, the thin Cocos-compatible wrapper, structured operation logs, exact UTC receipt evidence, the live wrapper-to-API/MySQL path, exact capability commands, cumulative repository verification, cleanup, reuse evidence, and final boundary audit are verified. M2 extracted only the separately authorized `game-config-snapshot-consumer-validation` mechanism; SwordMaster consumes it while the complete capability remains game-owned. The extracted C# `GameConfigSnapshotValidator` remains experimental, and the consumed one-time authorization grants no continuing authorization.

M3 is complete. Its server, independent-admin, real dual-MySQL API, desktop/mobile browser, exact
28-command cumulative acceptance, boundary, reuse-evidence, secret, NovaCore, and cleanup paths are
verified. The complete player-equipment-grant capability remains SwordMaster-owned.
`request-audit-context-consumer-contract` remains a possible later candidate only and is neither
implemented nor authorized for extraction. M3 grants no continuing authorization for extraction,
another capability, M4, or NovaCore changes.

[M3.5 Administrator Management](goals/m3.5.md) is the only active repository milestone at
`M35-07`, and only `M35-07-T01` is executable. M35-05 completed the authorized
`administrator-management` capability through administrator, role and permission, session, audit,
and generic Vben `web-ele` operations, including final management-admin, checked-contract,
generic Admin, Documentation, Repository, and whitespace verification. M35-06-T01 froze and
verified the exact real-server requirement-to-evidence matrix, ordered acceptance commands, T02
boundary, and premature-source guard. M35-06 then completed the real composed API, frozen
security, serial cumulative verification, and zero-residue cleanup paths. The active task
authorizes only the frozen real-browser acceptance foundation. It does not authorize M35-07-T02 or
later work, SwordMaster changes, extraction, M4, or NovaCore changes. M4 remains `Unscheduled`.

When sources appear to conflict, stop and resolve the conflict before implementation. Do not silently weaken a repository rule or an active goal acceptance criterion.

## Core Documents

- [Architecture](architecture.md)
- [Development workflow](workflows/development.md)
- [Extraction workflow](workflows/extraction.md)
- [Configuration release workflow](workflows/configuration-release.md)
- [Roadmap](roadmap.md)
- [M0.5 Framework Bootstrap completed goal record](goals/m0.5.md)
- [M1 First SwordMaster Domain Slice completed goal record](goals/m1.md)
- [M2 First Validated Extraction completed goal record](goals/m2.md)
- [M3 SwordMaster Player Equipment Grant completed goal record](goals/m3.md)
- [M3.5 Administrator Management active goal](goals/m3.5.md)

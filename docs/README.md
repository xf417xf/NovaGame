# NovaGame Documentation

This directory separates architectural context, repeatable workflows, roadmap context, and executable milestone state.

## Authority Map

- Repository and subtree `AGENTS.md` files contain durable coding-agent instructions.
- [Architecture](architecture.md) defines system boundaries and long-lived design decisions.
- Files under [workflows](workflows/) define repeatable ways of working.
- [Roadmap](roadmap.md) describes milestone direction and exit criteria. It is not authorization to implement future work.
- Files under [goals](goals/) are executable milestone runbooks. A goal is active only when its status is `in_progress` or `blocked` and `execution_authorized` is `true`. `ready` and `drafting` definitions do not authorize work; a completed goal is a verification record, not continuing authorization.
- The current user request remains the authority for the specific task being performed.

M0.5, M1, and M2 are complete. For the approved SwordMaster-owned `equipment-attribute-effect` capability, generated PlayerAssets contracts, the authenticated HTTP transport, the headless interaction model, the thin Cocos-compatible wrapper, structured operation logs, exact UTC receipt evidence, the live wrapper-to-API/MySQL path, exact capability commands, cumulative repository verification, cleanup, reuse evidence, and final boundary audit are verified. M2 extracted only the separately authorized `game-config-snapshot-consumer-validation` mechanism; SwordMaster consumes it while the complete capability remains game-owned. The extracted C# `GameConfigSnapshotValidator` remains experimental, and the consumed one-time authorization grants no continuing authorization. M2 is complete and no repository milestone is active.

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

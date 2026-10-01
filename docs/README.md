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

[M3.5 Administrator Management](goals/m3.5.md) is complete and deactivated. No repository milestone is active.
M35-07 completed on 2026-10-01. The separately authorized `M35-08-T01` completed the frozen
ten-command acceptance set, including 210 real-MySQL tests, 147 focused management tests, checked
contracts, 190 focused frontend tests, and full Admin with 520 unit tests plus five zero-retry
browser tests. Accepted evidence and failed-attempt history remain recorded. The user authorized
`M35-08-T02` and remaining M3.5 closure on 2026-10-01. T02 final audit passed with no unresolved
finding and was committed before `M35-08-T03` verified evidence disposal, documentation synchronization
and final governance checks. All 37 tasks and eight stages are complete. T04 passed cumulative Admin with 520 unit tests, exactly five
zero-retry real browser tests, all 20 manual screenshot reviews, committed evidence, verified
disposal, and final governance checks. Its consumed authority does not authorize M35-08. M35-05
completed the authorized
`administrator-management` capability through administrator, role and permission, session, audit,
and generic Vben `web-ele` operations, including final management-admin, checked-contract,
generic Admin, Documentation, Repository, and whitespace verification. M35-06-T01 froze and
verified the exact real-server requirement-to-evidence matrix, ordered acceptance commands, T02
boundary, and premature-source guard. M35-06 then completed the real composed API, frozen
security, serial cumulative verification, and zero-residue cleanup paths. M35-07-T01 completed the
frozen real-browser acceptance foundation, and M35-07-T02 completed route/action permission plus
authoritative `401`/`403` acceptance after focused, frontend, and serial real-browser verification.
M35-07-T03 completed on 2026-09-30 with real unknown-ID API `404`/stable-code evidence,
focused defensive detail coverage, 190 passing frontend tests, serial five-test real-browser
acceptance, and manual review of all 20 retained desktop/mobile screenshots. It added no removal
lifecycle and does not authorize later work,
SwordMaster changes, extraction, M4, or NovaCore changes. M4 remains `Unscheduled`.

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

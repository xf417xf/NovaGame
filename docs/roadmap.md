# NovaGame Roadmap

The roadmap describes intended validation outcomes. It is not authorization to implement future work. A future milestone requires an explicit user request and activation of its repository goal.

No dates are promised here. A milestone advances only when its exit criteria are verified.

At most one milestone may be active. An executable goal is active only while its status is `in_progress` or `blocked` and `execution_authorized` is `true`. A `ready` or `drafting` goal remains planned and inactive.

## Status Legend

- **Active**: the only milestone currently executable through a repository goal.
- **Complete**: verified baseline retained as historical context and maintenance scope.
- **Planned**: direction or an inactive goal definition for later validation; implementation is not authorized.
- **Unscheduled**: retained as context without committed scope.

## M0.5 - Framework Bootstrap

**Status:** Complete

**Completed goal record:** [M0.5 Framework Bootstrap](goals/m0.5.md)

**Concrete consumer:** SwordMaster configuration-loading smoke project

### Goal

Create the minimum reusable NovaGame kernel required to bootstrap a specific game and prove a versioned configuration path from server operations to a Cocos-compatible consumer.

### Deliverables

- repeatable monorepo bootstrap and verification;
- NovaCore-based six-layer .NET server with dedicated Deployment and DbMigrator composition projects;
- Docker MySQL 8.4 persistence and migrations;
- real administrator authentication, permissions, and auditing;
- versioned GameConfig workflow and snapshot export;
- Vben Admin 5 `web-ele` operations UI;
- engine-independent TypeScript configuration client and Cocos adapter;
- SwordMaster snapshot-loading smoke validation without gameplay.

### Exit Criteria

- A clean environment can follow the documented bootstrap path.
- Required server, admin, and client projects build and their tests pass.
- MySQL migrations apply to MySQL 8.4 and database health is reported.
- An administrator can authenticate and complete the configuration release workflow.
- A published snapshot can be activated, rolled back, and loaded by SwordMaster.
- The root verification command passes and the M0.5 goal records the result.

### Non-Goals

- SwordMaster gameplay;
- complete game-owned gameplay, economy, or operations systems;
- speculative NovaGame mechanisms without consumer evidence;
- empty domain or module scaffolding;
- Unity or Godot client adapters;
- distributed deployment, brokers, or microservice decomposition.

## M1 - First SwordMaster Domain Slice

**Status:** Complete

**Completed goal record:** [M1 First SwordMaster Domain Slice](goals/m1.md)

**Execution state:** Complete. The user approved the separate-game-backend ownership boundary and the SwordMaster-owned `equipment-attribute-effect` behavior. Every M1 stage and task passed, including the live wrapper-to-model-to-transport-to-SwordMaster-API-to-MySQL path, exact capability commands, cumulative verification, deterministic cleanup, reuse-evidence review, and final boundary audit. At M1 completion the evidence validated the narrower `game-config-snapshot-consumer-validation` candidate without authorizing extraction, while the complete capability remained SwordMaster-owned. The later 2026-08-10 authorization and extraction belong to the completed M2 record and do not alter this completed M1 record.

### Goal

Implement one concrete domain capability in a separate SwordMaster backend using NovaGame M0.5 infrastructure. The first proposal validates an owned equipment item changing effective character attributes without placing SwordMaster business code in NovaGame.

M1-01 is the capability-selection gate. It may begin only after an explicit user activation request, and no implementation may begin until the selected behavior is recorded and approved.

### Exit Criteria

- The approved domain capability works in SwordMaster.
- Its game-specific rules and content are explicit.
- Tests describe its concrete behavior.
- Reuse pressure, variations, and extraction candidates are recorded without extracting them prematurely.

### Non-Goals

- advancing beyond the approved capability without completing the ordered M1 stage gates;
- implementing game-specific gameplay in NovaGame;
- moving Battle, Skills, Enhancement, Set Bonuses, Drops, Shop, economy, progression, or live-operations systems into NovaGame as complete framework-owned systems;
- extracting a framework API during the initial implementation;
- implementing unrelated game-owned systems or unvalidated NovaGame mechanisms.

## M2 - First Validated Extraction

**Status:** Complete

**Completed goal record:** [M2 First Validated Extraction](goals/m2.md)

**Execution state:** Complete. The user explicitly authorized extraction of only `game-config-snapshot-consumer-validation` on 2026-08-10. M2 extracted the generalized `Content/GameConfig` validator, migrated SwordMaster without changing its equipment or PlayerAssets policy, verified behavior and boundaries, kept the C# API experimental because only one external .NET game consumer and no versioned package boundary are proven, and completed repository acceptance. The one-time authorization was consumed by this extraction and grants no continuing authorization. M2 closed with no active repository milestone; M3 was authorized later and does not alter the completed M2 record.

### Goal

Use the extraction workflow to promote the reusable mechanism from a proven SwordMaster capability into NovaGame while retaining game policy in SwordMaster.

M2 never starts automatically after M1. It may extract only the explicitly authorized candidate recorded by the completed M1 runbook.

The complete SwordMaster domain, backend schema, APIs, admin workflows, and client policy remain game-owned after extraction.

### Exit Criteria

- NovaGame owns only generalized mechanism and contracts.
- SwordMaster consumes the extracted API and retains its policy layer.
- NovaGame and SwordMaster tests pass without duplicate authoritative implementations.
- Compatibility expectations and extension points are documented.

## M3 - Game Operations Validation

**Status:** Complete

**Completed goal record:** [M3 SwordMaster Player Equipment Grant](goals/m3.md)

**Execution state:** M3 is complete. Its server, independent Vben admin, real dual-MySQL API,
desktop/mobile browser, exact 28-command cumulative acceptance, boundary, reuse-evidence, secret,
NovaCore, and cleanup paths are verified. The complete player-equipment-grant capability remains
SwordMaster-owned. `request-audit-context-consumer-contract` remains a possible later candidate only
and is neither implemented nor authorized for extraction. M3 grants no continuing authorization for
extraction, another capability, M4, or NovaCore changes.

### Goal

Implement the approved single-equipment grant capability in the SwordMaster backend and its
independent game-admin extension. Shop, Mail, Ranking, Activity, generic rewards, and similar
domains remain game-owned and outside this milestone; M3 does not make them NovaGame modules.

### Exit Criteria

- The selected capability solves a real operational workflow.
- Authorization, audit, idempotency, and rollback behavior are verified.
- Any later extraction promotes only an independently useful infrastructure mechanism and follows the same consumer-first rule.

## M3.5 - Administrator Management

**Status:** Active

**Active goal:** [M3.5 Administrator Management](goals/m3.5.md)

**Execution state:** M35-01 completed its evidence-backed current-state and boundary audit, M35-02
froze and verified its domain, security, Application, HTTP, MySQL, Vben, browser, and verification
contracts, and M35-03 completed its approved Domain/Application implementation and focused-test
matrix. M35-04-T01 completed its approved persistence implementation, independent review fixes,
and real-MySQL verification; M35-04-T02 completed management permission registration and
idempotent DbMigrator seed synchronization; M35-04-T03 completed the frozen thin management HTTP
layer and real-MySQL API/OpenAPI verification; M35-04-T04 completed the frozen real-MySQL
integration evidence for migrations, seed, permissions, account/role/session/audit queries,
mutations, locking, replay, and atomic audit behavior; and M35-04-T05 completed real OpenAPI
capture and checked TypeScript contract generation. M3.5 is active at `M35-04`, with only
`M35-04-T06` executable. The user separately authorized the NovaGame-owned
`administrator-management` capability on 2026-08-13. M35-04-T06 authorizes only the cumulative
M35-04 governance verification and state transition. This authorization does not come from M3 and
does not authorize M35-05 or later work, Vben management UI implementation, SwordMaster changes,
extraction, M4, or NovaCore changes.

### Goal

Complete the existing framework-owned `Operations/AdminAccess` capability so authorized operators
can manage administrator accounts, custom roles and registered permissions, inspect and revoke
administrator sessions, and query administrator-access audit history through the generic Vben
Admin 5 `web-ele` application.

### Exit Criteria

- Administrator, role, permission, session, and audit behavior is server-authoritative and verified.
- Privileged mutations are authorized, concurrency-safe, transactional, and auditable in MySQL 8.4.
- The generic Vben admin passes focused and real desktop/mobile browser acceptance.
- OpenAPI and TypeScript contracts remain synchronized without game-owned navigation or policy.
- NovaCore and SwordMaster remain unchanged, M4 remains `Unscheduled`, and repository verification passes.

### Non-Goals

- game-owned player, gameplay, economy, reward, or live-operations management;
- multi-factor authentication, external identity providers, SSO, or tenant management;
- extracting an admin shell or creating a new physical module or package;
- M4 portability work or NovaCore changes.

## M4 - Portability And Reuse Proof

**Status:** Unscheduled

### Goal

Validate that server contracts and reusable concepts can support another client engine or another specific game without changing server-side business logic for engine concerns.

### Exit Criteria

- A second concrete consumer exercises public NovaGame contracts.
- Engine-specific behavior remains in adapters.
- Compatibility gaps are resolved through versioned contracts rather than game-specific branches in NovaGame.

## Extraction Candidates

Completed M2 extraction: `game-config-snapshot-consumer-validation`, supported by the completed M1 evidence and the explicit 2026-08-10 extraction approval. That one-time authorization was consumed by M2 and grants no continuing authorization. The extracted C# contract remains experimental, and the complete `equipment-attribute-effect` capability remains SwordMaster-owned rather than becoming a NovaGame extraction candidate.

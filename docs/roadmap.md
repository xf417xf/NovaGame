# NovaGame Roadmap

The roadmap describes intended validation outcomes. It is not authorization to implement future work. A future milestone requires an explicit user request and a new active executable goal.

No dates are promised here. A milestone advances only when its exit criteria are verified.

## Status Legend

- **Active**: the only milestone currently executable through a repository goal.
- **Complete**: verified baseline retained as historical context and maintenance scope.
- **Planned**: direction for later validation; implementation is not authorized.
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
- reusable gameplay systems;
- Mail, Ranking, Shop, or Activity implementation;
- empty domain or module scaffolding;
- Unity or Godot client adapters;
- distributed deployment, brokers, or microservice decomposition.

## M1 - First SwordMaster Vertical Slice

**Status:** Planned

### Goal

Implement one concrete, playable SwordMaster capability inside SwordMaster using NovaGame M0.5 infrastructure. Select the capability from an actual game requirement at that time.

### Exit Criteria

- The capability works in SwordMaster.
- Its game-specific rules and content are explicit.
- Tests describe its concrete behavior.
- Reuse pressure, variations, and extraction candidates are recorded without extracting them prematurely.

### Non-Goals

- choosing the capability before SwordMaster requirements exist;
- extracting a framework API during the initial implementation;
- implementing unrelated domain catalog entries.

## M2 - First Validated Extraction

**Status:** Planned

### Goal

Use the extraction workflow to promote the reusable mechanism from a proven SwordMaster capability into NovaGame while retaining game policy in SwordMaster.

### Exit Criteria

- NovaGame owns only generalized mechanism and contracts.
- SwordMaster consumes the extracted API and retains its policy layer.
- NovaGame and SwordMaster tests pass without duplicate authoritative implementations.
- Compatibility expectations and extension points are documented.

## M3 - Operations Validation

**Status:** Unscheduled

### Goal

Add an operations capability only in response to a concrete SwordMaster live-operations need. Mail, Ranking, Shop, and Activity are candidates, not promised deliverables.

### Exit Criteria

- The selected capability solves a real operational workflow.
- Authorization, audit, idempotency, and rollback behavior are verified.
- Any extraction follows the same consumer-first rule.

## M4 - Portability And Reuse Proof

**Status:** Unscheduled

### Goal

Validate that server contracts and reusable concepts can support another client engine or another specific game without changing server-side business logic for engine concerns.

### Exit Criteria

- A second concrete consumer exercises public NovaGame contracts.
- Engine-specific behavior remains in adapters.
- Compatibility gaps are resolved through versioned contracts rather than game-specific branches in NovaGame.

## Extraction Candidates

There are currently no approved extraction candidates. Add a candidate only after a concrete consumer implementation produces evidence under the extraction workflow.

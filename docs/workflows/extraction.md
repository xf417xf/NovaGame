# Extraction Workflow

Use this workflow to promote a capability from a specific game into NovaGame. Do not use it to justify speculative framework design.

## Preconditions

Extraction may start only when:

- the capability works in SwordMaster or another concrete game;
- at least one real use case exercises it;
- tests describe the current behavior;
- there is evidence of reusable mechanism distinct from game policy;
- the active user request authorizes extraction.

A second use case, meaningful variation, repeated implementation pressure, or a clear portability requirement provides stronger evidence than a single hypothetical future need.

The M0.5 infrastructure bootstrap exception does not make gameplay capabilities eligible for extraction.

## 1. Describe The Concrete Capability

Document the current consumer, behavior, inputs, outputs, invariants, failure modes, dependencies, and tests. Identify every game-specific name, rule, data shape, balance assumption, and lifecycle decision.

If the current behavior is not understood or tested, improve it in the game before extracting.

## 2. Separate Policy From Mechanism

Keep game-specific policy, content, balancing, terminology, presentation, and orchestration in the game.

Candidate reusable mechanisms must be independently useful and must not contain SwordMaster concepts such as Forge, SwordSoul, Sect, or Jianghu. Prefer composition and explicit extension points over flags for known games.

Do not generalize names while retaining hidden game assumptions.

## 3. Define The NovaGame Ownership Boundary

Choose the owning NovaGame business domain and required layers. Keep the capability in the six standard business-layer projects unless validation proves that it needs an independent physical-module lifecycle. Deployment and DbMigrator remain operational composition projects, not extraction targets.

Define the smallest public contract required by the game. Mark the contract experimental until the extraction is complete and the consumer passes.

## 4. Move Behavior And Generalize Tests

Move reusable invariants, contracts, and infrastructure into NovaGame. Move or recreate tests so they describe general behavior rather than SwordMaster examples.

Keep game policy and game fixtures in SwordMaster. Do not move content merely to make the framework test pass.

## 5. Migrate The Consumer

Replace the SwordMaster implementation with consumption of the extracted NovaGame API. Keep a thin game-owned adapter or policy layer when required.

Do not leave two authoritative implementations. Remove obsolete duplicated mechanism only after the migrated consumer passes.

## 6. Verify No Behavioral Regression

Run NovaGame domain, application, persistence, API, client, and contract tests relevant to the extraction. Run the SwordMaster consumer tests and its concrete acceptance path.

The extraction fails if SwordMaster behavior changes unintentionally, game-specific terminology leaks into NovaGame, or the game still needs to bypass the extracted contract.

## 7. Promote Or Keep Experimental

Promote the API only when:

- the generalized tests pass;
- SwordMaster passes as a consumer;
- ownership and extension points are documented;
- public names contain no game-specific concepts;
- compatibility expectations are explicit.

After promotion, prefer additive evolution and version breaking changes. If the evidence remains weak, keep the capability game-owned and record it as an extraction candidate.

## Do Not Extract When

- the only argument is that another game might need it;
- the abstraction mainly renames SwordMaster concepts;
- configuration flags are replacing game policy;
- no consumer can exercise the proposed contract;
- the extraction creates circular domain dependencies;
- the active milestone does not include extraction.

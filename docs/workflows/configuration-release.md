# Configuration Release Workflow

This workflow governs game-data configuration from editing through client consumption. Configuration contains content, references, and tunable parameters; executable domain invariants remain code.

## Lifecycle

```text
Draft
  -> Validate
  -> Review
  -> Publish Immutable Version
  -> Export Snapshot
  -> Activate
  -> Monitor
  -> Rollback when necessary
```

Publication and activation are separate. A published version may be inspected and exported before it becomes active.

## 1. Draft

A draft is mutable and not available to production clients. Edits create or update draft data only.

Each draft records its configuration set, base version when applicable, author, creation time in UTC, and current revision used for optimistic concurrency.

## 2. Validate

Validation has two levels:

- Schema validation checks required fields, types, formats, ranges, and structural constraints.
- Semantic validation checks identifiers, references, uniqueness, cross-record rules, and domain-owned invariants that can be evaluated before publication.

Validation returns stable machine-readable error codes, locations, and operator-readable messages. A failed draft remains editable and cannot advance to publication.

Client-side admin validation is advisory. Server validation is authoritative.

## 3. Review

Review presents the validated revision, differences from the base or active version, validation results, author, and release notes.

If the draft changes after review, its validation and review result become stale and must be repeated.

## 4. Publish Immutable Version

Publishing creates a new immutable configuration version. Never edit a published version in place.

Publication must be transactional: either the complete version and its metadata exist, or neither exists. Record the publisher, UTC timestamp, source draft revision, and audit event.

## 5. Export Snapshot

Export a deterministic UTF-8 JSON snapshot from a published version. The fixed top-level fields are `formatVersion`, `schemaVersion`, `configurationSet`, `configurationVersion`, `createdAtUtc`, `integrity`, and `content`. The `content` value is the canonical JSON validated for publication.

`integrity.algorithm` is `SHA-256`, `integrity.canonicalization` identifies the canonicalization profile, and `integrity.contentHash` is the lowercase hexadecimal SHA-256 hash of the exact canonical `content` bytes. Store a separate lowercase hexadecimal SHA-256 hash of the complete snapshot bytes as the snapshot hash; do not embed that hash inside the document.

The same published version must return byte-for-byte identical content. Use the snapshot hash as a strong ETag. Version-specific downloads are immutable and may use long-lived caching; active-environment downloads must revalidate because activation or rollback can change the pointer. Clients never read draft storage or database tables directly.

## 6. Activate

Activation atomically changes the environment's active-version pointer after the snapshot is available and validated. Record environment, previous version, new version, operator, UTC time, and audit event.

Do not partially activate configuration categories. If partial rollout becomes a validated requirement later, design it as an explicit versioned capability rather than an implicit exception.

## 7. Monitor

Observe activation success, snapshot retrieval, client compatibility, validation failures, and relevant operational errors. Correlate release and activation events without logging secrets or sensitive configuration values.

Monitoring does not mutate the published version.

## 8. Rollback

Rollback atomically reactivates a previously published compatible version. It does not edit or delete the failed version.

Record the source version, target version, reason, operator, UTC time, and audit event. Re-export only when the stored snapshot is missing or integrity verification fails.

## Client Activation Rules

Clients validate format and schema compatibility, version metadata, the complete-snapshot hash or ETag, and the embedded canonical-content hash before replacing the current snapshot. Activation is all-or-nothing. Keep the last valid snapshot when download, validation, or activation fails.

The server remains authoritative even when the client has a valid configuration snapshot.

## M0.5 Acceptance

M0.5 must prove draft editing, authoritative validation, immutable publication, deterministic export, environment activation, rollback, audit metadata, version conflict handling, and SwordMaster smoke consumption of an exported snapshot.

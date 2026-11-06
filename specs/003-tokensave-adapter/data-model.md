# Data Model: Read-Only, Version-Checked TokenSave Adapter

All types live in `src/Polifemo.Platform/TokenSave/` unless noted. Records are immutable; lists are ordered deterministically (paths and names ordinal, then line, then id) so two reads of one revision are equal.

| Entity | Fields | Rules |
|---|---|---|
| `SnapshotReadOptions` | `LockTimeout` (TimeSpan, default 5 s), `VerifySource` (bool, default true) | `MaxRetries` is the constant 3 from the approved spec, not an option. |
| `ResolvedDatabase` (existing, extended) | `DatabasePath`, `Resolution` (`branch-meta` or `default`), `MetadataAbsent` (bool) | `default` only when metadata is absent and exactly `tokensave.db` exists (R8). |
| `GraphSnapshot` | `Revision` (GraphRevision), `Graph` (TokenSaveGraph), `DocAssociations` (list), `Integrity` (IntegrityReport), `Provenance` (TokenSaveProvenance) | Constructed only after every check, read and verification succeeded; has no date/time member (R6). |
| `GraphRevision` | `Value` (string hash) | `Hashing.Hash("gr/v1", …)` over the canonical graph rows (files, symbols, edges, unresolved, ambiguous, doc associations); `SourceFingerprint` (`"sf/v1"`) over `files` (R4). Equal for equal commit states. |
| `TokenSaveGraph` (existing) | `Symbols`, `Edges`, `Unresolved`, `Ambiguous`, `Files`, `DatabasePath` | Row container; `LoadAsync` removed. |
| `FileRow` (existing) | `Path`, `ContentHash` (sha256 hex), `Kind` (`code`/`artifact`), `NodeCount` | No `modified_at`/`indexed_at`. |
| `SymbolRow` (existing) | `Id`, `Kind`, `Name`, `QualifiedName`, `FilePath`, `StartLine`, `EndLine` (0-based, provenance only), `Signature`, `Docstring`, `ParentId` | `Id` and lines are never used as identity by consumers (Constitution IV). |
| `EdgeRow` (existing) | `SourceId`, `TargetId`, `Kind`, `Line`, `ResolvedBy` | `ResolvedBy` kept as the extractor's confidence hint; never upgraded to "exact". |
| `UnresolvedRow` (existing) | `FromNodeId`, `ReferenceName`, `ReferenceKind`, `Line`, `FilePath` | Preserved as unresolved. |
| `AmbiguousRow` (existing) | `FromNodeId`, `ReferenceName`, `FilePath`, `Line`, `CandidateIds` | `CandidateIds` parsed from JSON; unparsable → `TS_DATA_INCOMPLETE`. |
| `DocAssociationRow` (new) | `DocPath`, `DocNodeId`, `TargetPath`, `TargetNodeId`, `Kind` (`sidecar` when the doc ends with `.readme.md`, else `central`) | Derived inside the adapter from `nodes.kind = 'doc'` and `edges.kind = 'documents'`; file-level, as TokenSave defines it. Stale `applies_to` entries are not in the graph and belong to F03-02. |
| `IntegrityReport` (new) | `DanglingEdges`, `DanglingUnresolved`, `DanglingAmbiguousSources`, `UnknownCandidateIds` (ints) | Counts of rows whose node ids are unknown; informational (R3). |
| `TokenSaveProvenance` (new) | `DatabasePath` (worktree-relative), `Resolution`, `MetadataAbsent`, `IndexVersion`, `BinaryVersion`, `Attempts` (1..4), `SourceVerified` (bool) | Attempts > 1 means a retry happened. |
| `TokenSaveSnapshotException` (new, `Polifemo.Contracts.Errors`) | `Code`, `Message`, `Attempts` | Codes: `TS_DB_LOCKED`, `TS_DB_CORRUPT`, `TS_DATA_INCOMPLETE`, `TS_REVISION_UNSTABLE`, `TS_SOURCE_MISMATCH`, `TS_META_ABSENT_AMBIGUOUS` (the last thrown as `ActiveDatabaseResolutionException`). Compatibility failures keep `TokenSaveCompatibilityException`. |

## State of one read attempt

```text
resolve ─► pre-open file snapshot ─► open (ReadOnly, busy timeout) ─► BEGIN + first SELECT
   │                                      │ NOTADB/CORRUPT → TS_DB_CORRUPT          │ BUSY → TS_DB_LOCKED
   ▼                                      ▼                                          ▼
compatibility check (all mismatches) ─► read files/nodes/edges/unresolved/ambiguous/docs ─► ROLLBACK
   │ mismatches → TokenSaveCompatibilityException     │ bad row → TS_DATA_INCOMPLETE
   ▼
post file snapshot == pre?  ──no──► revision changed ─► retry (≤3) ─► TS_REVISION_UNSTABLE
   │ yes
   ▼
verify source (VerifySource) ──mismatch──► retry (≤3) ─► TS_SOURCE_MISMATCH
   │ ok
   ▼
GraphSnapshot (Revision, Graph, DocAssociations, Integrity, Provenance)
```

A failure at any step throws before a `GraphSnapshot` exists, so callers never see partial data (FR-006, SC-001).

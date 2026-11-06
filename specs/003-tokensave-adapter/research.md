# Research: Read-Only, Version-Checked TokenSave Adapter

Observed on 2026-11-01 inside the devcontainer with `tokensave 7.16.0` on this repository's own `.tokensave/` (WAL mode, 78 files, 596 nodes) and on the F01 records in `docs/platform-compatibility.md`.

## R1. What F01 already provides and what this feature changes

- **Decision:** build on `ActiveDatabaseResolver`, `CompatibilityChecker`, `ExpectedSchema` and `TokenSaveReader`; replace `TokenSaveGraph.LoadAsync` with `TokenSaveAdapter.ReadAsync`, which returns a `GraphSnapshot`. `TokenSaveGraph` stays as the row container. The four call sites (`CisharpaiCorpus`, `ExpectationTests`, `WorktreeIsolationTests`, `ExpectationObserver`) move to the adapter.
- **Rationale:** FR-001 asks for one component that knows the layout. Today the row queries, the check and the resolver are three public classes; one entry point with `TokenSaveReader` kept `internal` makes the boundary enforceable, and existing tests (resolver, checker, read-only guarantee) remain valid.
- **Alternatives considered:** keeping `LoadAsync` as a wrapper (two public doors for the same data, rejected); a new project (`Polifemo.TokenSave`) is unjustified at this size (Constitution VII).

## R2. Snapshot and locking strategy

- **Facts:** TokenSave 7.16.0 leaves its database in `journal_mode=wal`. A `Mode=ReadOnly` connection that runs `BEGIN` followed by a first `SELECT` holds one WAL read snapshot until `COMMIT`/`ROLLBACK`: every later query in that transaction sees the same committed state, and a concurrent `tokensave sync` neither blocks nor is blocked (it appends to the WAL; the checkpoint cannot overwrite pages the reader still needs). If a database is in rollback-journal mode instead, the same `BEGIN` + `SELECT` takes a SHARED lock, which makes a concurrent writer wait or fail with `SQLITE_BUSY`; the reader still sees one state. Opening a WAL database read-only creates empty `-wal`/`-shm` files when they are absent (F01 observation); they hold no data.
- **Decision:** one connection, one deferred read transaction for the whole read: compatibility check, metadata, `files`, `nodes`, `edges`, `unresolved_refs`, `ambiguous_calls`, doc associations. The transaction ends with `ROLLBACK` (never `COMMIT`, so no write intent even by accident). `SqliteConnection.DefaultTimeout` (SQLite busy handler) is set from `SnapshotReadOptions.LockTimeout`, default 5 s; `SQLITE_BUSY`/`SQLITE_LOCKED` after that becomes `TS_DB_LOCKED` ("a writer holds the database; wait for `tokensave sync` to finish and retry"). A size/mtime snapshot of the database and a non-empty `-wal` is taken before open and after the transaction; a difference means a sync committed meanwhile and is a **revision change** (not a failure by itself, see R5 for the retry).
- **Guarantees:** all rows of one `GraphSnapshot` come from one SQLite commit state (FR-008). **Limits:** a read cannot see rows a running sync has not committed yet, so a snapshot may lag the working tree (detected by R5); the `.tokensave/` directory must allow the empty sidecars to be created (owner-only permissions are enough); a database replaced on disk between resolve and open is detected by the pre-open/post-open size/mtime comparison and handled as a revision change.
- **Alternatives considered:** `PRAGMA query_only` alone (does not give a stable snapshot); copying the database first (hides the "never write" guarantee, slow on large indexes, and the copy can itself be mid-checkpoint); file locks on `.tokensave/` (would block TokenSave, violates "never interfere").

## R3. Corrupt, incomplete and unsupported data

- **Decision:** map failures to stable codes with an action in the message:
  - open or first statement fails with `SQLITE_NOTADB`/`SQLITE_CORRUPT`/`SQLITE_IOERR` → `TS_DB_CORRUPT` ("run `tokensave sync -f` to rebuild the index, or restore the worktree");
  - required table/column missing or changed, version mismatch → existing `TS_TABLE_MISSING`, `TS_COLUMN_MISSING`, `TS_COLUMN_CHANGED`, `TS_VERSION_MISMATCH`, `TS_INDEX_VERSION_MISMATCH`, all reported together as today (`TokenSaveCompatibilityException`), message names found and supported versions;
  - a row that cannot be represented (`ambiguous_calls.candidate_node_ids` not a JSON string array, a NOT NULL column read as NULL) → `TS_DATA_INCOMPLETE` naming table and row key; the read returns nothing (FR-006);
  - an edge, unresolved reference or ambiguous call whose node id is unknown is **kept** and counted in `IntegrityReport.DanglingEdges` etc. The schema declares `ON DELETE CASCADE`, so this indicates a TokenSave defect, not a broken file; the counts make it visible without blocking real corpora.
- **Rationale:** FR-005, FR-006 and SC-001: every failure mode has one distinct code and no partial result leaves the adapter (the exception is thrown before the `GraphSnapshot` is constructed).
- **Alternatives considered:** `PRAGMA quick_check` on every read (seconds on large databases for a condition SQLite reports anyway when it occurs); treating dangling references as corruption (would fail cisharpai on a vendor bug the exporter can tolerate).

## R4. Graph revision identity

- **Fact:** `files.content_hash` is the lowercase hex SHA-256 of the file's raw bytes (verified on three files of this repository). `nodes.id` values are hashes that differ across runs (F01), so they cannot identify a revision.
- **Decision:** two identities, both computed with `Polifemo.Contracts.Hashing.Hashing.Hash` (SHA-256 over the length-prefixed encoding; `Hashing.Encode` alone is not a hash). `SourceFingerprint = Hash("sf/v1", indexVersion, path₁, hash₁, …)` over `files` sorted by path (ordinal) identifies the source inputs only. `GraphRevision = Hash("gr/v1", indexVersion, row₁, row₂, …)` identifies the graph content: the canonical rows of `files`, symbols, edges, unresolved references, ambiguous calls and doc associations, each as a stable string, sorted ordinal. Node ids are replaced by stable keys (kind, qualified name, file path and, for symbols, start line) because ids differ across runs (F01 fact). Any change of graph content changes the revision, including doc associations changed by `docs_dir` while the source bytes stay the same. Two reads of the same commit state give the same revision. Pairing (R5) uses both identities.
- **Rationale:** FR-008 and FR-009 need a revision identity that is stable, cheap and independent of TokenSave timestamps (FR-010) and node ids (FR-011).
- **Alternatives considered:** a source-only hash (rejected: it cannot see extractor or doc-association changes, so distinct graphs could share a revision); hashing raw node ids (rejected: unstable across runs); `metadata.last_sync_at` (forbidden).

## R5. Source/graph pairing and the approved bounded retry

- **Decision:** after the rows are read, `SourceVerifier` checks the worktree against `files` with the three-step protocol of contract step 7: (1) stat every indexed file (size, mtime); (2) read and SHA-256 every file and compare with `content_hash`, where a missing file or a different hash is a **source mismatch**; (3) stat every file again, where any stat change is a source mismatch. One read attempt is: resolve → pre-open file snapshot → open → `BEGIN` → check → read rows → `ROLLBACK` → post file snapshot → verify source. The attempt is **retried from scratch** when (a) the database snapshot changed between pre and post (a sync committed) or (b) the source verification fails (a sync is likely in flight, or the tree was edited). At most `MaxRetries = 3` retries (4 attempts). After the limit: `TS_REVISION_UNSTABLE` when the database kept changing, `TS_SOURCE_MISMATCH` (with the number of mismatched files and up to 5 relative paths) when the tree and the graph still disagree. Each attempt waits briefly (250 ms, 500 ms, 1 s) before retrying. `GraphSnapshot.Provenance.Attempts` records how many attempts were needed. `SnapshotReadOptions.VerifySource = false` skips (b) for callers that only need the graph (tests of the graph itself); the default is `true`.
- **Rationale:** the approved Clarify answer (option B). Within one SQLite transaction the rows are already consistent (R2), so the retry only exists to pair the graph with the right source bytes and to avoid returning a snapshot that a committed sync has just superseded.
- **Limit (recorded):** only graph → disk is verified, and the worktree is not locked, so the files are not read as one frozen image. An edit that is reverted within one verification pass with the same size and mtime is not detected. A file present on disk but absent from `files` is not detected either, because deciding whether TokenSave should have indexed it means reproducing its exclude rules; F03-02 owns canonical file inventories. The first gap is closed by verified consumption instead of a frozen copy: every consumer that reads source bytes hashes exactly those bytes and compares them with the snapshot's `content_hash` for that path, discarding its work and re-reading the snapshot on a difference (contract, "Consumption rule"). A frozen copy of the worktree was rejected: it doubles disk use per read and is itself copied file by file, so it has the same race.
- **Alternatives considered:** failing immediately (option A) and waiting on a lock (option C) were offered and not chosen by the approver.

## R6. No TokenSave timestamp is read

- **Decision:** the adapter's SQL never selects `files.modified_at`, `files.indexed_at`, `nodes.updated_at`, `metadata.last_sync_at`, `metadata.last_full_sync_at`, `metadata.last_sync_duration_ms`, nor reads `branch-meta.json`'s `last_synced_at`/`created_at`. `GraphSnapshot` has no date or time member. A test (`NoTimestampReadTests`) asserts both: the SQL constants exposed to tests via an `internal` list contain none of these names, and reflection over `GraphSnapshot` finds no `DateTime`/`DateTimeOffset`/`*_at` member.
- **Rationale:** FR-010 and Constitution IV ("TokenSave timestamps are not a change cursor"). A test makes the rule survive refactors.

## R7. Error text and privacy

- **Decision:** messages contain code, worktree-relative paths, table/column names, counts and versions. Never file contents, signatures, docstrings or qualified names. New codes are regular `PolifemoException` messages, so `SafeMessage` scrubs them with the standard rules; none needs an opaque replacement because none wraps subprocess output.
- **Rationale:** FR-012 and Constitution I. Paths are needed to act (which file disagrees); names of symbols are not.

## R8. Absent branch metadata

- **Fact:** `tokensave init` always writes `branch-meta.json`, so absence means a hand-made or very old store.
- **Decision:** when `branch-meta.json` is absent, list `*.db` directly under `.tokensave/`. Exactly one file, and it is `tokensave.db` → use it, `Resolution = "default"`, `MetadataAbsent = true` in the provenance. Zero files → `TS_DB_MISSING`. More than one, or one that is not `tokensave.db` → `TS_META_ABSENT_AMBIGUOUS` ("run `tokensave sync` to recreate branch-meta.json or remove the extra databases"). The existing branch-meta rules (`TS_META_MALFORMED`, `TS_META_NO_DB`, path traversal rejection, `TS_DB_MISSING` without fallback) are unchanged.
- **Rationale:** FR-003 and the assumption recorded in the approved spec.

## R9. CLI surface

- **Decision:** add `polifemo platform snapshot --worktree <dir> [--no-verify-source] [--lock-timeout <seconds>]`. It prints JSON with the revision, counts per table, doc association count, integrity counts, provenance (database path, resolution, metadata absent, index version, binary version, attempts) and the existing `SnapshotProvenance`. Exit codes follow F01: 0 ok, 1 a typed TokenSave failure (`TS_*`), 2 usage, 3 environment. No row content is printed.
- **Rationale:** gives operators the "actionable error" of User Story 2 without a test harness and makes quickstart validation runnable. It is one subcommand on an existing command (Constitution VII).

# Feature Specification: Read-Only, Version-Checked TokenSave Adapter

**Feature Branch**: `003-tokensave-adapter`

**Created**: 2026-10-27

**Status**: Draft

**Input**: GitHub issue #10 (F03-01, child of story #13; depends on #2, #3, #4): isolate all TokenSave table knowledge in one adapter that resolves branch metadata, opens the TokenSave database read-only and extracts files, nodes, edges, doc associations and unresolved/ambiguous relationships from a consistent snapshot.

## Clarifications

### Session 2026-11-01

- Q: When the adapter detects that TokenSave changed during a read (a concurrent sync, or the graph no longer matching the source files), should it retry automatically or fail immediately? → A: Retry the whole read from a fresh snapshot up to 3 times, then fail with an explicit error.

## Constitution Check

Assessed against `.specify/memory/constitution.md` Principles I-VII at specification level; the plan repeats the check against the design.

| Principle | Assessment |
|---|---|
| I Local-Only (NON-NEGOTIABLE) | Pass. The adapter reads local files only; no network, model or external service. Errors and logs carry codes, relative paths, table names, counts and versions, never source text (FR-012). Tests use synthetic fixtures and the public `testrepo/` corpus locally. |
| II Evidence | Pass. No ranking, weight or heuristic is introduced. The only numeric choice, the retry limit of 3, was fixed by the approver in Clarify. |
| III Pinned runs | Pass. Only a pinned, supported TokenSave version and schema are read (FR-005); anything else fails explicitly. |
| IV Identity and publication | Pass. Identity never depends on TokenSave ids or line numbers (FR-011); TokenSave's last-sync time is never a cursor (FR-010); one result comes from one snapshot or fails (FR-008, FR-009). Of the required sync coverage this feature owns **concurrent sync** (US3, SC-003); additions, edits, deletions, renames, line shifts, docs-only changes, excludes, profile versions and publication crashes belong to F03-02 and F04 and are not applicable here. |
| V Isolated contracts | Pass. The adapter is the only component that knows TokenSave's layout (FR-001); consumers use its typed output. |
| VI Scope-correct, honest retrieval | Pass. Unresolved and ambiguous relationships are returned as such, with their confidence (FR-007), never silently resolved or dropped. The adapter serves no queries, so scope filtering does not apply. |
| VII Staged simplicity | Pass. No new project, package or service is required by the spec. |

Correctness gates: **no half-published snapshots** and **deterministic recovery** are met at this layer by "a read returns one complete revision or an explicit error, never partial data" (FR-006, FR-008, SC-001, SC-003). **Zero cross-scope leakage** does not apply: the adapter publishes and serves nothing.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the active graph from the correct database (Priority: P1)

An indexing operator points the exporter at an indexed worktree. The adapter reads the TokenSave branch metadata, selects the active database it names, and returns the files, nodes, edges, doc associations and unresolved/ambiguous relationships recorded there, without changing anything in TokenSave.

**Why this priority**: Every downstream stage (chunking, publication, search) depends on getting the right graph. This is the MVP.

**Independent Test**: Run the adapter against a fixture TokenSave store with valid branch metadata. The result contains the expected records, and the store is byte-identical afterwards.

**Acceptance Scenarios**:

1. **Given** branch metadata naming an existing database file, **When** the adapter reads it, **Then** it reads that file and no other.
2. **Given** branch metadata absent but a single default database present, **When** the adapter reads it, **Then** it uses the default database and reports that metadata was absent.
3. **Given** a successful read, **When** the store is compared before and after, **Then** no file, journal or metadata is created or modified.
4. **Given** ambiguous or unresolved relationships in the graph, **When** the adapter reads them, **Then** they are returned with their ambiguity/confidence information instead of being dropped or resolved.

---

### User Story 2 - Fail explicitly on bad metadata, missing targets and unsupported versions (Priority: P1)

When the branch metadata is malformed, names a missing database, the schema/version is unsupported, or the data is corrupt or incomplete, the operator gets a specific, actionable error rather than an empty or partial result or a read from a different database.

**Why this priority**: Silent fallback would publish a wrong graph; explicit failure is a constitution requirement.

**Independent Test**: Feed fixtures for each failure mode and assert a distinct error that names the problem and the corrective action.

**Acceptance Scenarios**:

1. **Given** malformed branch metadata, **When** the adapter reads it, **Then** it fails with an error that identifies the metadata file and what is wrong.
2. **Given** metadata naming a database that does not exist, **When** the adapter reads it, **Then** it fails naming the missing path, and does not fall back to another database.
3. **Given** a database whose schema or TokenSave version is not supported, **When** the adapter reads it, **Then** it fails stating the found and supported versions and what to do.
4. **Given** a database that is corrupt or lacks required tables/columns, **When** the adapter reads it, **Then** it fails naming what is missing or damaged, and returns no partial data.

---

### User Story 3 - Consistent snapshot under concurrent TokenSave sync (Priority: P2)

While the exporter reads, TokenSave may run a sync. The adapter guarantees that all records it returns come from one revision of the graph, and that the graph revision matches the source files it is paired with, so output never mixes revisions.

**Why this priority**: Required for atomic publication, but only matters once reads work.

**Independent Test**: Run a simulated concurrent sync during a read; the read either returns one complete revision or fails/retries explicitly, never a mixture.

**Acceptance Scenarios**:

1. **Given** a sync commits during a read, **When** the read completes, **Then** every returned record belongs to the same revision.
2. **Given** the source files changed relative to the graph revision, **When** the adapter reports the snapshot, **Then** the mismatch is detected and reported rather than silently paired.
3. **Given** the adapter's snapshot/locking strategy, **When** a maintainer reads the documentation, **Then** the strategy and its guarantees and limits are described.

---

### Edge Cases

- Database locked by a writer for longer than the read can wait: fail with a clear timeout error, never read partially.
- Branch metadata names a path outside the project's TokenSave folder (e.g. traversal): rejected.
- Empty graph (valid schema, zero rows): returned as a valid empty snapshot.
- Database file replaced or deleted between metadata resolution and open.
- TokenSave's last-sync time is present: it MUST NOT be used as a publication or change cursor.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The adapter MUST be the only component that knows TokenSave's storage layout; all other components consume its typed output.
- **FR-002**: The adapter MUST resolve the active database from the branch metadata and MUST NOT read any other database when metadata is malformed or its target is missing.
- **FR-003**: The adapter MUST define and document behavior when branch metadata is absent.
- **FR-004**: The adapter MUST open the database strictly read-only. It MUST NOT modify, checkpoint or write the database, its journal, or any other file in the TokenSave store, and MUST NOT store exporter state there. The only permitted creation is SQLite's own sidecars, which SQLite creates when it opens a WAL-mode database read-only: a zero-length `-wal` and the `-shm` WAL index (32 KiB, observed during F03-01 implementation) whose header records zero WAL frames. They carry no database content, and the tests assert that (research R2).
- **FR-005**: The adapter MUST verify the schema/version against a pinned supported set before extraction and fail with an actionable error naming found versus supported versions.
- **FR-006**: The adapter MUST detect corrupt or incomplete data (missing tables/columns, unreadable database) and fail with an actionable error, returning no partial result.
- **FR-007**: The adapter MUST extract files, nodes, edges, doc associations and unresolved/ambiguous relationships, preserving ambiguity and confidence.
- **FR-008**: All records in one result MUST come from a single consistent snapshot of the graph, documented with its locking/snapshot strategy. If a revision change is detected during a read, the adapter MUST discard the partial read and retry the whole read from a fresh snapshot, at most 3 retries, then fail with an explicit "revision kept changing" error; it MUST never return mixed-revision data.
- **FR-009**: The adapter MUST detect a mismatch between the graph revision and the source files it is paired with, apply the same bounded retry as FR-008, and report the mismatch explicitly if it persists after the retry limit.
- **FR-010**: The adapter MUST NOT use TokenSave's last-sync time as a downstream publication cursor or change cursor.
- **FR-011**: Application identity MUST NOT depend on TokenSave internal ids or line numbers; they are exposed as provenance only.
- **FR-012**: Errors and logs MUST NOT include raw source text or confidential paths beyond what is needed to act.

### Key Entities

- **Branch metadata**: TokenSave's record of the active branch and its database file.
- **Graph snapshot**: One consistent revision of files, nodes, edges, doc associations and relationships, with its revision identity and supported-version stamp.
- **Relationship (resolved/unresolved/ambiguous)**: A link between symbols with its resolution state and confidence.
- **Adapter error**: A categorized failure (metadata, missing target, unsupported version, corruption, lock timeout, revision mismatch) with a corrective action.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every tested failure mode (absent, malformed, missing target, unsupported version, corrupt/incomplete), 100% of cases yield a distinct error stating the cause and the corrective action, and zero partial results.
- **SC-002**: Across all read tests, the TokenSave store is unchanged except for SQLite sidecars that carry no data (empty `-wal`, `-shm` WAL index with zero frames): no file is created or modified apart from them, and no byte of the database, journal or metadata changes.
- **SC-003**: In a concurrent-sync test run repeatedly, 0 outputs mix records from different revisions; each run ends in one complete revision (possibly after up to 3 retries) or an explicit retry-exhausted error.
- **SC-004**: Tests cover absent branch metadata, a supported schema and at least one version-mismatch case, and the full suite passes with zero warnings and zero Sonar findings.

## Assumptions

- The pinned TokenSave version and its schema are determined during planning; only versions in the pinned set are supported.
- Absent branch metadata falls back to TokenSave's default database only if exactly one candidate exists; otherwise it fails.
- Tests use plain-file fixtures and the public corpus under `testrepo/`; no private sample content is used or transmitted (local-only).
- Depends on F01-01..F01-03 (#2, #3, #4) for solution, contracts and platform foundations.
- Out of scope: chunking, publication, change detection against exporter manifests, and running `tokensave sync`.

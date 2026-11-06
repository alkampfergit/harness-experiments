# Tasks: Read-Only, Version-Checked TokenSave Adapter

**Input**: Design documents from `/specs/003-tokensave-adapter/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R9), data-model.md, contracts/tokensave-adapter.md, quickstart.md

**Tests**: Required. SC-004 asks for tests of absent metadata, a supported schema and a version mismatch; SC-001..SC-003 are only provable by tests; the constitution (IV, "Required test coverage") mandates the concurrent-sync case this feature owns. Test tasks are listed before the implementation they prove and are expected to fail until it lands.

**Organization**: Tasks are grouped by user story. US1 (read the active graph) is the MVP; US2 (explicit failures) and US3 (consistent snapshot under concurrent sync) extend the same `TokenSaveAdapter.ReadAsync` entry point.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on an unfinished task)
- **[Story]**: User story the task belongs to (US1, US2, US3)
- Every task names the file it touches; paths are repository-relative

## Path Conventions

Single .NET solution `Polifemo.slnx`. Library code under `src/Polifemo.Platform/TokenSave/`, shared error types under `src/Polifemo.Contracts/Errors/`, CLI under `src/Polifemo.Cli/Commands/`, tests under `src/Polifemo.Tests/` (xUnit v3, `TestContext.Current.CancellationToken`), documentation under `docs/`. No new project, package or folder (plan, Constitution VII).

---

## Phase 1: Setup (Test Support)

**Purpose**: Extend the existing synthetic TokenSave builder so every story can be tested without a private sample.

- [x] T001 Add row builders to `src/Polifemo.Tests/Support/SyntheticTokenSave.cs`: fluent `AddFile(path, contentHash, kind = "code", nodeCount)`, `AddNode(id, kind, name, qualifiedName, filePath, startLine, endLine, signature = null, docstring = null, parentId = null)`, `AddEdge(sourceId, targetId, kind, line = null, resolvedBy = null)`, `AddUnresolved(fromNodeId, referenceName, referenceKind, line, filePath)`, `AddAmbiguous(fromNodeId, referenceName, filePath, line, candidateIdsJson)` (raw JSON string so a test can store an invalid value) and `AddDoc(docPath, docNodeId, targetPath, targetNodeId)` (inserts a `doc` node plus a `documents` edge). Rows are inserted by `Build(root)` after the schema; keep the existing `DropTable`/`DropColumn`/`ChangeColumn`/`AddColumn`/`BranchMetaJson`/`WalMode` options unchanged.
- [x] T002 [P] Add `WriteSourceFiles(root, IReadOnlyDictionary<string, byte[]>)` and `ContentHashOf(byte[])` (lowercase hex SHA-256, the `files.content_hash` convention from research R4) to `src/Polifemo.Tests/Support/SyntheticTokenSave.cs`, so a fixture can write worktree files whose hashes match the `files` rows it inserts.
- [x] T003 [P] Add `StoreDigest(string tokensaveDir)` to `src/Polifemo.Tests/Support/TestWorkspace.cs`: a sorted `(relativePath, length, sha256)` list of every file under `.tokensave/`. Store-unchanged tests (SC-002) compare the digest of every entry except the zero-length `*-wal`/`*-shm` sidecars SQLite may create on open (research R2, FR-004 as reconciled), and additionally assert that every such sidecar is zero-length, so a sidecar that carries data fails the test.
- [x] T004 [P] Add `CorruptDatabase(path)` (overwrite the first 100 bytes with non-SQLite bytes) and `HoldWriteLock(path)` (returns an `IDisposable` holding an open `BEGIN EXCLUSIVE` transaction on a separate read-write connection to a **rollback-journal** database; `BEGIN IMMEDIATE` only takes a RESERVED lock, which still lets the adapter's deferred read through, and in WAL mode no writer lock blocks readers) to `src/Polifemo.Tests/Support/SyntheticTokenSave.cs` for the `TS_DB_CORRUPT` and `TS_DB_LOCKED` tests.

**Checkpoint**: `dotnet build Polifemo.slnx` passes with 0 warnings; no behavior changed yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The typed output and error types every story returns or throws. No story can be implemented or tested before these exist.

**⚠️ CRITICAL**: Complete before any user story.

- [ ] T005 Add `TokenSaveSnapshotException(string code, string message, int attempts)` (sealed, derives from `PolifemoException`, exposes `Attempts`) to `src/Polifemo.Contracts/Errors/PolifemoException.cs`. Codes it carries: `TS_DB_LOCKED`, `TS_DB_CORRUPT`, `TS_DATA_INCOMPLETE`, `TS_REVISION_UNSTABLE`, `TS_SOURCE_MISMATCH` (contract table). `TS_META_ABSENT_AMBIGUOUS` stays an `ActiveDatabaseResolutionException`.
- [ ] T006 [P] Create `src/Polifemo.Platform/TokenSave/GraphSnapshot.cs` with the data-model records: `SnapshotReadOptions` (`LockTimeout` TimeSpan default 5 s; `VerifySource` bool default true), `GraphRevision(string Value)`, `DocAssociationRow(DocPath, DocNodeId, TargetPath, TargetNodeId, Kind)` where `Kind` is `"sidecar"` when `DocPath` ends with `.readme.md` (ordinal, case-insensitive) else `"central"`, `IntegrityReport(DanglingEdges, DanglingUnresolved, DanglingAmbiguousSources, UnknownCandidateIds)` (ints), `TokenSaveProvenance(DatabasePath, Resolution, MetadataAbsent, IndexVersion, BinaryVersion, Attempts, SourceVerified)` with `DatabasePath` worktree-relative, and `GraphSnapshot` (`Revision`, `Graph`, `DocAssociations`, `Integrity`, `Provenance`; all `required init`). The file MUST contain no `DateTime`/`DateTimeOffset` member and no `*At` property (research R6).
- [ ] T007 [P] Extend `ResolvedDatabase` in `src/Polifemo.Platform/TokenSave/ActiveDatabaseResolver.cs` with `bool MetadataAbsent` (default `false`); set it to `true` on the `default` resolution path. Keep `DatabasePath` and `Resolution` values unchanged so `CompatibilityChecker` and its tests still pass.
- [ ] T008 Add `GraphRevision.Compute(string indexVersion, GraphRows rows)` and `SourceFingerprint.Compute(string indexVersion, IReadOnlyList<FileRow> files)` to `src/Polifemo.Platform/TokenSave/GraphSnapshot.cs`, following research R4 exactly: canonical rows sorted ordinal, node ids replaced by stable keys, both values from `Polifemo.Contracts.Hashing.Hashing.Hash` (not `Encode`). `GraphRows` is the row container (`TokenSaveGraph` rows plus `DocAssociations`). Depends on T006.
- [ ] T009 Create `src/Polifemo.Platform/TokenSave/SourceVerifier.cs` (`internal static`): `Verify(string worktree, IReadOnlyList<FileRow> files, CancellationToken ct)` returns `SourceVerification(IReadOnlyList<string> MissingPaths, IReadOnlyList<string> ChangedPaths, bool StatChanged)`, implementing the stat/hash/stat protocol of research R5 with worktree-relative paths; the file bytes are never kept or logged (FR-012). Graph → disk only (plan Complexity Tracking).
- [ ] T010 Extend `src/Polifemo.Platform/TokenSave/TokenSaveReader.cs` (stays `internal`): (a) `OpenReadOnly(string databasePath, TimeSpan lockTimeout)` sets `SqliteConnection.DefaultTimeout` to the whole seconds of `lockTimeout` (minimum 1); (b) `DocAssociations()` query returning `DocAssociationRow` from `nodes` with `kind = 'doc'` joined to `edges` with `kind = 'documents'` and the target node's `file_path`, ordered by doc path, target path, ids; (c) `EndSnapshot()` executes `ROLLBACK` (never `COMMIT`, research R2); (d) a static `internal IReadOnlyList<string> SqlStatements` listing every SQL text the reader executes (for `NoTimestampReadTests`); (e) `Ambiguous()` throws `TokenSaveSnapshotException("TS_DATA_INCOMPLETE", …)` naming table `ambiguous_calls` and the `(from_node_id, line)` key, with no row content, when `candidate_node_ids` is not a JSON array of strings; the same code for a NULL read from a NOT NULL column of `files`, `nodes`, `edges`, `unresolved_refs`.
- [ ] T011 Add `internal static TokenSaveSnapshotException MapSqlite(SqliteException ex, string relativeDbPath, TimeSpan lockTimeout, int attempt)` to `src/Polifemo.Platform/TokenSave/TokenSaveReader.cs`: `SqliteErrorCode` 5 (`SQLITE_BUSY`) or 6 (`SQLITE_LOCKED`) → `TS_DB_LOCKED` with the timeout and the text "wait for `tokensave sync` to finish"; 26 (`SQLITE_NOTADB`), 11 (`SQLITE_CORRUPT`) or 10 (`SQLITE_IOERR`) → `TS_DB_CORRUPT` with the relative database path and the text "`tokensave sync -f`"; anything else is rethrown unchanged. Messages carry no SQL text (FR-012).

**Checkpoint**: Build green with 0 warnings; existing `CompatibilityCheckerTests`, `ReadOnlyGuaranteeTests`, `ActiveDatabaseResolverTests` still pass (`dotnet test Polifemo.slnx --filter "FullyQualifiedName~Platform.TokenSave"`).

---

## Phase 3: User Story 1 - Read the active graph from the correct database (Priority: P1) 🎯 MVP

**Goal**: `TokenSaveAdapter.ReadAsync(worktree)` returns a `GraphSnapshot` (files, nodes, edges, unresolved, ambiguous, doc associations, integrity counts, revision, provenance) from the database that branch metadata names, inside one read transaction, without touching the store, and becomes the only door to TokenSave data.

**Independent Test**: Build a synthetic store with `SyntheticTokenSave` (valid `branch-meta.json`, a few rows of each kind, one sidecar and one central doc, one unresolved and one ambiguous reference), call `ReadAsync`, assert the rows, the doc associations with their kinds, the ambiguity preserved, `Provenance.Attempts == 1`, and `StoreDigest` equal before and after.

### Tests for User Story 1

- [ ] T012 [P] [US1] Create `src/Polifemo.Tests/Platform/TokenSave/TokenSaveAdapterTests.cs` (class fixture: `TestWorkspace`, resolver seam `new ActiveDatabaseResolver((_, _) => Task.FromResult<string?>("main"))`, checker seam `_ => Task.FromResult(PlatformPins.TokenSave)`) with: `Reads_the_database_named_by_branch_meta` (two `*.db` files, metadata names the second, only its rows come back), `Empty_graph_is_a_valid_snapshot` (zero rows, counts 0, `Revision.Value` non-empty), `Doc_associations_are_typed_sidecar_or_central`, `Unresolved_and_ambiguous_rows_are_preserved_with_candidates`, `Dangling_references_are_counted_not_rejected` (`Integrity.DanglingEdges == 1`), `Store_is_byte_identical_after_read` (`StoreDigest` before == after, WAL and rollback-journal variants via `[Theory]`), `Two_reads_of_one_revision_are_equal` (same `Revision.Value`, same ordered rows), `Provenance_has_relative_path_versions_and_attempts_1`, `Node_ids_and_lines_are_provenance_only` (asserts `GraphSnapshot` exposes them only under `Graph.Symbols`/`Edges`, never in `Revision`).
- [ ] T013 [P] [US1] Add to `src/Polifemo.Tests/Platform/TokenSave/ActiveDatabaseResolverTests.cs`: `Absent_metadata_with_only_tokensave_db_resolves_default_and_flags_MetadataAbsent`, `Absent_metadata_with_no_database_is_TS_DB_MISSING`, `Absent_metadata_with_several_databases_is_TS_META_ABSENT_AMBIGUOUS` (message names the candidate file names and "run `tokensave sync`"), `Absent_metadata_with_one_database_not_named_tokensave_db_is_TS_META_ABSENT_AMBIGUOUS` (research R8).
- [ ] T014 [P] [US1] Create `src/Polifemo.Tests/Platform/TokenSave/NoTimestampReadTests.cs`: (a) every entry of `TokenSaveReader.SqlStatements` matches none of `modified_at`, `indexed_at`, `updated_at`, `last_sync_at`, `last_full_sync_at`, `last_sync_duration_ms`, `created_at`, `last_synced_at`; (b) reflection over `GraphSnapshot`, `TokenSaveProvenance`, `GraphRevision`, `IntegrityReport`, `DocAssociationRow` and the `TokenSaveGraph` row records finds no property of type `DateTime`/`DateTimeOffset` and no name ending in `At` (research R6, FR-010).
- [ ] T015 [P] [US1] Create `src/Polifemo.Tests/Platform/TokenSave/AdapterBoundaryTests.cs`: scan every `*.cs` under `src/` for the regexes `FROM\s+(nodes|edges|files|unresolved_refs|ambiguous_calls|metadata)\b` and `\bTokenSaveReader\b`, and assert no match (contract "Public surface", FR-001). Exempt only the adapter itself and its own white-box tests: `src/Polifemo.Platform/TokenSave/`, `src/Polifemo.Tests/Platform/TokenSave/` (the adapter-facing tests such as `ReadOnlyGuaranteeTests` (T022), `NoTimestampReadTests` (T014) and this test, which contains the patterns) and `src/Polifemo.Tests/Support/SyntheticTokenSave.cs` (the fixture builder writes TokenSave tables). Every other consumer, including the CLI, `Corpus/`, `Platform/Fixtures/` and the rest of `Support/`, stays scanned. Use `TestWorkspace.RepoRoot()`.

### Implementation for User Story 1

- [ ] T016 [US1] Implement absent-metadata resolution in `src/Polifemo.Platform/TokenSave/ActiveDatabaseResolver.cs` (research R8): when `branch-meta.json` is absent, list `*.db` directly under `.tokensave/`; exactly one file named `tokensave.db` → `ResolvedDatabase(path, "default", MetadataAbsent: true)`; zero files → existing `TS_DB_MISSING`; otherwise `ActiveDatabaseResolutionException("TS_META_ABSENT_AMBIGUOUS", …)` listing the candidate file names and "run `tokensave sync` to recreate branch-meta.json or remove the extra databases". Keep the branch-meta rules (`TS_META_MALFORMED`, `TS_META_NO_DB`, traversal rejection, `TS_DB_MISSING` without fallback) unchanged.
- [ ] T017 [US1] Create `src/Polifemo.Platform/TokenSave/TokenSaveAdapter.cs` (`public sealed class`, `public const int MaxRetries = 3`, constructors `()` and `(CompatibilityChecker checker)`, plus an `internal` constructor adding `Func<int, CancellationToken, Task>? afterRowsRead` as the retry test seam) with a single-attempt `ReadAsync(string worktree, SnapshotReadOptions? options = null, CancellationToken ct = default)`: resolve (let `ActiveDatabaseResolutionException` propagate unchanged), open read-only with `options.LockTimeout`, `BeginSnapshot`, `checker.CheckOpenAsync` (throw `TokenSaveCompatibilityException` on any mismatch), read `Files`, `Symbols`, `Edges`, `Unresolved`, `Ambiguous`, `DocAssociations`, `Metadata("last_full_index_version")`, `EndSnapshot` (`ROLLBACK`), compute `IntegrityReport` (ids not in the symbol set), `GraphRevision.Compute`, and return `GraphSnapshot` with `Provenance` (relative db path, resolution, `MetadataAbsent`, index version, `checker` binary version, `Attempts = 1`, `SourceVerified = false` for now). Map `SqliteException` through `TokenSaveReader.MapSqlite`. The retry loop and source verification are added in US3 (T031). Depends on T005-T011, T016.
- [ ] T018 [US1] Remove `TokenSaveGraph.LoadAsync` from `src/Polifemo.Platform/TokenSave/TokenSaveGraph.cs` (research R1); keep the row records, `TokenSaveGraph` container and `QualifiedNames`.
- [ ] T019 [P] [US1] Replace `TokenSaveGraph.LoadAsync(Worktree)` in `src/Polifemo.Tests/Corpus/CisharpaiCorpus.cs` with `new TokenSaveAdapter().ReadAsync(Worktree)`; keep `Graph` (`snapshot.Graph`) and add `Snapshot` (`GraphSnapshot`) properties. Depends on T017, T018.
- [ ] T020 [P] [US1] Replace `TokenSaveGraph.LoadAsync` calls in `src/Polifemo.Tests/Platform/Fixtures/ExpectationTests.cs` and `src/Polifemo.Tests/Platform/Fixtures/WorktreeIsolationTests.cs` with `TokenSaveAdapter.ReadAsync(…, ct: TestContext.Current.CancellationToken)` and `.Graph`. Depends on T017, T018.
- [ ] T021 Change `ExpectationObserver.Observe` in `src/Polifemo.Tests/Support/ExpectationObserver.cs` to take the `GraphSnapshot` (not a bare `TokenSaveGraph`): `Extract(snapshot.Graph)` keeps producing symbols and relationships, and the doc expectations come from `snapshot.DocAssociations` instead of the private `DocAssociations(graph, worktree)` query. Keep the stale-entry detection against the previous expectations. Thread the snapshot through its callers (`CisharpaiCorpus`, `ExpectationTests`). Expectation files under `src/Polifemo.Tests/Corpus/Expectations/` MUST remain unchanged (same observed output). Depends on T019, T020.
- [ ] T022 [US1] Make `TokenSaveReader` usage in `src/Polifemo.Tests/Platform/TokenSave/ReadOnlyGuaranteeTests.cs` compile against the new `OpenReadOnly(path, lockTimeout)` overload (keep the no-timeout overload delegating with 5 s) and confirm T015 still passes (`Platform/TokenSave/` tests are the only users outside the adapter, by `InternalsVisibleTo`, and T015 exempts exactly them).

**Checkpoint**: T012-T015 pass; `dotnet test Polifemo.slnx` fully green; `Corpus` tests unchanged when `testrepo/cisharpai` is present.

---

## Phase 4: User Story 2 - Fail explicitly on bad metadata, missing targets and unsupported versions (Priority: P1)

**Goal**: Every failure mode ends in one distinct `TS_*` code with a corrective action and no partial snapshot, both from the API and from a new `polifemo platform snapshot` subcommand.

**Independent Test**: Feed one fixture per code (malformed metadata, missing target, version mismatch, dropped table, corrupt file, locked database, bad candidate JSON) to `ReadAsync` and to the CLI; assert the code, the action text in the message, and that no `GraphSnapshot` was produced / exit code 1 with `{ ok:false, code, message }`.

### Tests for User Story 2

- [ ] T023 [P] [US2] Add to `src/Polifemo.Tests/Platform/TokenSave/TokenSaveAdapterTests.cs`: `Malformed_metadata_is_TS_META_MALFORMED_and_names_the_file`, `Missing_target_is_TS_DB_MISSING_and_does_not_fall_back` (a `tokensave.db` exists but metadata names `other.db`; assert the message names `other.db` and no snapshot is returned), `Unsupported_index_version_is_TS_INDEX_VERSION_MISMATCH_naming_found_and_supported`, `Wrong_binary_version_is_TS_VERSION_MISMATCH`, `Dropped_table_is_TS_TABLE_MISSING_with_no_partial_data`, `Corrupt_file_is_TS_DB_CORRUPT_with_relative_path_and_sync_f_hint`, `Locked_database_is_TS_DB_LOCKED_after_the_timeout` (`HoldWriteLock` on a rollback-journal database, `LockTimeout = 1 s`, assert elapsed ≥ 1 s and the "wait for `tokensave sync`" text), `Invalid_candidate_json_is_TS_DATA_INCOMPLETE_naming_table_and_key_without_content`, `Every_failure_message_is_free_of_row_content` (a sentinel docstring/signature string in the fixture never appears in any thrown message, FR-012).
- [ ] T024 [P] [US2] Add snapshot tests to `src/Polifemo.Tests/Cli/PlatformCommandTests.cs` (same harness as the existing `platform check` tests): success prints `ok:true`, `revision`, `counts` (`files`, `symbols`, `edges`, `unresolved`, `ambiguous`, `docAssociations`), `integrity`, `tokensave` (`databasePath`, `resolution`, `metadataAbsent`, `indexVersion`, `binaryVersion`, `attempts`, `sourceVerified`) and exit 0; malformed metadata prints `{ ok:false, code:"TS_META_MALFORMED", message }` and exits 1; corrupt file exits 1 with `TS_DB_CORRUPT`; a missing `--worktree` exits 2; `--no-verify-source` and `--lock-timeout 1` are accepted; no symbol name, signature or docstring from the fixture appears in stdout/stderr.

### Implementation for User Story 2

- [ ] T025 [US2] In `src/Polifemo.Platform/TokenSave/TokenSaveAdapter.cs` make every failure path throw before any `GraphSnapshot` is constructed and wrap each `SqliteException` from open, `BeginSnapshot`, the compatibility check and every row query through `TokenSaveReader.MapSqlite` with the current attempt number (`TS_DB_LOCKED`, `TS_DB_CORRUPT`); let `TokenSaveSnapshotException("TS_DATA_INCOMPLETE")` from the reader propagate; ensure the reader is disposed (`ROLLBACK` on dispose if a snapshot is open) on every path. Depends on T017.
- [ ] T026 [US2] Add `platform snapshot` to `src/Polifemo.Cli/Commands/PlatformCommands.cs` (research R9): options `--worktree <dir>` (required), `--no-verify-source`, `--lock-timeout <seconds>` (default 5); calls `TokenSaveAdapter.ReadAsync`; on success prints the JSON object of T024 with `provenance` = the existing `SnapshotProvenance` shape used by `platform check`; on `ActiveDatabaseResolutionException`, `TokenSaveCompatibilityException` (first mismatch code plus the full mismatch list) or `TokenSaveSnapshotException` prints `{ ok:false, code, message }` and returns `ExitCodes.CheckFailed` (1); usage errors 2, environment errors 3 (reuse the existing mapping in `Program`). Never print row content. Depends on T025.
- [ ] T027 [P] [US2] Document the subcommand next to `platform check` in `docs/platform-compatibility.md` (section 1 "Pins" or where `platform check` is described) and in `docs/privacy.md` where `platform check` is listed among commands that print no sample content: name, options, the JSON fields, the exit codes and the `TS_*` codes it can print.

**Checkpoint**: T023-T024 pass; every code in the contract table except `TS_REVISION_UNSTABLE` and `TS_SOURCE_MISMATCH` has one passing test; quickstart "Manual failure checks" print the expected codes.

---

## Phase 5: User Story 3 - Consistent snapshot under concurrent TokenSave sync (Priority: P2)

**Goal**: A read returns one complete revision whose graph matches the verified source bytes, retrying from scratch at most 3 times when the database changed during the read or the source disagrees, then fails with `TS_REVISION_UNSTABLE` or `TS_SOURCE_MISMATCH`. The strategy is documented.

**Independent Test**: (a) Seam test: mutate the database or a source file from the `afterRowsRead` hook on attempt 1 only, assert `Provenance.Attempts == 2`; mutate on every attempt, assert the explicit error after 4 attempts. (b) Real test: run `tokensave sync` in a loop against a synthetic fixture whose files are rewritten between syncs while `ReadAsync` repeats; every outcome is a verified single revision or an explicit retry-exhausted error.

### Tests for User Story 3

- [ ] T028 [P] [US3] Add to `src/Polifemo.Tests/Platform/TokenSave/TokenSaveAdapterTests.cs` (using the `internal` seam constructor): `Database_change_during_read_is_retried_and_succeeds` (hook appends a row through a write connection on attempt 1 → `Attempts == 2`, snapshot contains the appended row), `Database_changing_on_every_attempt_is_TS_REVISION_UNSTABLE_after_4_attempts` (`Attempts == 4` on the exception, message states the attempt count), `Source_mismatch_is_retried_then_TS_SOURCE_MISMATCH_with_counts_and_up_to_5_paths` (rewrite a source file so its hash differs; message names the mismatched count and ≤ 5 relative paths, never contents), `Missing_source_file_is_TS_SOURCE_MISMATCH`, `VerifySource_false_skips_source_checks_and_reports_SourceVerified_false`, `Successful_read_reports_SourceVerified_true`, `Retry_waits_are_bounded` (total elapsed for 4 attempts ≥ 250+500+1000 ms and < 10 s), `Cancellation_during_retry_is_OperationCanceledException`.
- [ ] T029 [P] [US3] Create `src/Polifemo.Tests/Platform/TokenSave/SnapshotStrategyTests.cs` (`RequireTokenSave.Ensure()`: fail, never skip, when the pinned binary is missing). Setup: a synthetic source tree under `.index/test/<guid>/` with several indexed files, then `tokensave init` and `tokensave sync`. Generations: generation g rewrites **every** file so that its class names carry the marker `Gen<g>`, records the SHA-256 of each body for g, then runs `tokensave sync`. Race: the writer runs generations 1..20 while the reader calls `ReadAsync` repeatedly, concurrently. For every successful snapshot assert: (a) every `files.content_hash` belongs to one generation g in the writer's recorded map; (b) every symbol in the returned graph carries that same generation g, so graph and paired source match one known generation; (c) `Revision.Value` equals `GraphRevision.Compute` over the snapshot's own rows. Failures allowed: only `TS_REVISION_UNSTABLE` and `TS_SOURCE_MISMATCH`; count them. Assert at least one successful read. Finally assert that `StoreDigest` after the last sync equals a fresh digest taken after it (no exporter artifacts in `.tokensave/`).
- [ ] T030 [P] [US3] Add `Wal_sidecar_growth_during_read_counts_as_a_revision_change` to `src/Polifemo.Tests/Platform/TokenSave/TokenSaveAdapterTests.cs`: on a `WalMode` database, the hook writes through a second connection without checkpointing; assert `Attempts == 2` (research R2: the pre/post comparison includes a non-empty `-wal`).

### Implementation for User Story 3

- [ ] T031 [US3] Add the bounded retry loop to `src/Polifemo.Platform/TokenSave/TokenSaveAdapter.cs` (research R5, contract "Snapshot strategy" steps 1-8): per attempt, after resolve, record `FileSnapshot.Of(db)` and `FileSnapshot.Of(db + "-wal")` (treat a zero-length or absent `-wal` as equal), run the single attempt of T017/T025, invoke the `afterRowsRead` seam, record the post snapshots; a difference → revision change; else if `options.VerifySource`, run `SourceVerifier.Verify` → any missing/changed path is a source mismatch. On revision change or source mismatch, discard everything and retry after `250 ms`, `500 ms`, `1 s` (`Task.Delay(…, ct)`), at most `MaxRetries` (3) retries = 4 attempts. After the limit throw `TokenSaveSnapshotException("TS_REVISION_UNSTABLE", "the database changed during 4 consecutive read attempts; wait for `tokensave sync` to finish and retry", 4)` or `TokenSaveSnapshotException("TS_SOURCE_MISMATCH", "<n> indexed file(s) differ from the worktree (<up to 5 relative paths>); run `tokensave sync` and retry", 4)`. Set `Provenance.Attempts` to the successful attempt number and `SourceVerified = options.VerifySource`. Depends on T017, T025.
- [ ] T032 [US3] Add the section "Snapshot and locking strategy" to `docs/platform-compatibility.md` (after "Branches and worktrees"): one read-only connection, one deferred transaction ended by `ROLLBACK`, WAL snapshot vs SHARED lock in rollback mode, the busy timeout (5 s default, `TS_DB_LOCKED`), pre/post file comparison including a non-empty `-wal`, graph → disk source verification by SHA-256, the 3-retry policy (250 ms / 500 ms / 1 s) and the two terminal codes, the guarantees (one commit state per snapshot; graph paired with verified bytes) and the recorded limits (uncommitted sync work invisible; files TokenSave skipped not verified; empty sidecars may be created in `.tokensave/`; dangling references are counted, not rejected). Satisfies spec US3 scenario 3 and FR-008.

**Checkpoint**: T028-T030 pass locally with the pinned `tokensave`; the full suite is green; SC-003 evidence recorded in the PR body.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Gates required by AGENTS.md and the spec's SC-004, plus artifact consistency.

- [ ] T033 Run `dotnet build Polifemo.slnx` and `dotnet test Polifemo.slnx`; fix every warning at its cause (no `#pragma`, `[SuppressMessage]` or `<NoWarn>`); record the test counts in the PR body.
- [ ] T034 Run `tools/sonar-local.sh` and fix every finding at its cause until it reports `0 finding(s)`.
- [ ] T035 [P] Measure `ReadAsync` on the prepared `cisharpai` worktree (`polifemo platform snapshot --worktree .index/work/cisharpai`) and record wall time with and without `--no-verify-source` in the PR body and in the evidence table of `specs/003-tokensave-adapter/quickstart.md` (plan "Performance Goals": under 2 s); no code change unless the goal is missed, in which case report it rather than tune.
- [ ] T036 [P] Run the quickstart validation in `specs/003-tokensave-adapter/quickstart.md` (build, test, sonar, `platform snapshot` on the corpus, the two manual failure checks) and tick the evidence table; update `specs/003-tokensave-adapter/contracts/tokensave-adapter.md` only if an implemented signature or message differs from the contract, and say so in the PR.
- [ ] T037 Update `docs/backlog/F03-01-tokensave-adapter.md` (status and a pointer to the adapter contract) and the `specs/003-tokensave-adapter/spec.md` status from `Draft` to `Implemented`, and run `git diff --check` before the final commit.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; T001-T004 touch different members and can run in parallel after T001 defines the builder shape.
- **Foundational (Phase 2)**: T005, T006, T007 in parallel; T008 after T006; T009 after T006 (uses `FileRow`, independent of T008); T010 and T011 after T005 and T006. Blocks all stories.
- **US1 (Phase 3)**: after Phase 2. T012-T015 (tests) in parallel and before T016-T022. T016 → T017 → T018 → {T019, T020} → T021; T022 after T010.
- **US2 (Phase 4)**: after US1 (extends `TokenSaveAdapter`). T023, T024 in parallel before T025 → T026; T027 in parallel with T026.
- **US3 (Phase 5)**: after US2 (retry wraps the failure-mapped attempt). T028, T029, T030 in parallel before T031; T032 in parallel with T031.
- **Polish (Phase 6)**: after all stories. T033 → T034; T035, T036 in parallel; T037 last.

### User Story Dependencies

- **US1 (P1)**: independent MVP; delivers the single public door and removes `LoadAsync`.
- **US2 (P1)**: builds on US1's `ReadAsync`; its tests are independent fixtures per failure code.
- **US3 (P2)**: builds on US1 + US2; its seam tests are independent of the real-sync test (T029), which needs the pinned `tokensave` binary.

### Within Each User Story

- Tests are written first and MUST fail before the implementation task that makes them pass.
- Types (Phase 2) → adapter logic → consumers/CLI → documentation.

### Parallel Opportunities

- Phase 1: T002, T003, T004 together (after T001).
- Phase 2: T005 + T006 + T007 together; then T008 + T009 + (T010 → T011).
- US1: T012 + T013 + T014 + T015 together; T019 + T020 together.
- US2: T023 + T024 together; T026 + T027 together.
- US3: T028 + T029 + T030 together; T031 + T032 together.
- Polish: T035 + T036 together.

---

## Parallel Example: User Story 1

```bash
# Tests first, all independent files:
Task: "T012 TokenSaveAdapterTests.cs happy-path/empty/docs/ambiguous/store-unchanged/determinism"
Task: "T013 ActiveDatabaseResolverTests.cs absent-metadata single/none/ambiguous cases"
Task: "T014 NoTimestampReadTests.cs SQL constants and reflection over snapshot types"
Task: "T015 AdapterBoundaryTests.cs no TokenSave table name outside the adapter folder"

# Then the adapter (sequential: T016 → T017 → T018), then the two call-site migrations together:
Task: "T019 CisharpaiCorpus.cs uses TokenSaveAdapter.ReadAsync"
Task: "T020 ExpectationTests.cs and WorktreeIsolationTests.cs use TokenSaveAdapter.ReadAsync"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 (test support) and Phase 2 (types, reader, verifier).
2. Phase 3: `TokenSaveAdapter.ReadAsync` single attempt, consumers migrated, `LoadAsync` removed.
3. **STOP and VALIDATE**: T012-T015 green, store unchanged, corpus expectations unchanged.

### Incremental Delivery

1. US1 → one public door, typed snapshot, boundary and no-timestamp tests (MVP).
2. US2 → every failure code distinct and actionable; `platform snapshot` for operators.
3. US3 → bounded retry, source verification, real concurrent-sync proof, documented strategy.
4. Polish → zero warnings, zero Sonar findings, quickstart evidence, performance note.

### Scope guard

Out of scope and MUST NOT be added while executing these tasks: chunking, publication, exporter manifests/change detection, running `tokensave sync` from the adapter, new packages or projects, any Elasticsearch work (spec Assumptions; Constitution VII).

---

## Notes

- `[P]` tasks touch different files or disjoint members and have no unfinished dependency.
- Every task that writes an error message MUST keep raw source, signatures, docstrings and qualified names out of it (FR-012).
- All tests run on copies under `.index/test/`; nothing is generated under `samples/` or `testrepo/`.
- Commit after each phase checkpoint with a focused message; run `tools/sonar-local.sh` and `dotnet test Polifemo.slnx` before every commit (AGENTS.md).

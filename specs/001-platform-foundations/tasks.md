---

description: "Task list for Verified Platform Foundations"
---

# Tasks: Verified Platform Foundations

**Input**: Design documents from `/specs/001-platform-foundations/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R11), data-model.md, contracts/ (cli.md, tokensave-schema.md, identities-and-hashes.md, boundaries.md, expectation-record.schema.json), quickstart.md

**Tests**: Included. The spec requires verification by tests (SC-001, SC-003, SC-005, SC-006) and the constitution requires identity, isolation and correctness coverage for this feature. Write each test first and confirm it fails before implementing.

**Organization**: Tasks are grouped by user story. Story dependencies: US1 and US2 are independent of each other; US3 needs US2's mutations (identity matrix) and the observed TokenSave kinds from US1, so it runs last.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3. Setup, Foundational and Polish tasks have no label.
- Paths are relative to the repository root `/Users/gianmariaricci/Develop/github/harness-experiments`.

## Path Conventions

Per plan.md: `src/Polifemo.Contracts`, `src/Polifemo.Platform/{TokenSave,Elasticsearch,Fixtures}`, `src/Polifemo.Cli`, `src/Polifemo.Tests` (single test project; first-level folders `Contracts/`, `Platform/`, `Cli/`, `Corpus/`, `Support/` mirror the section tested), `src/fixtures/synthetic-csharp`, `testrepo/`, `docs/`, `.devcontainer/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution skeleton, pins and ignore rules.

- [X] T001 Create `global.json` pinning the .NET 10 SDK (10.0.x, `rollForward: latestPatch`) and `.gitattributes` with `* text=auto eol=lf` and `src/fixtures/** -text`
- [X] T002 Create `Polifemo.slnx` with projects `src/Polifemo.Contracts` (classlib), `src/Polifemo.Platform` (classlib, references Contracts), `src/Polifemo.Cli` (console, assembly name `polifemo`, references Platform and Contracts), and one test project `src/Polifemo.Tests` (xUnit v3, references all three; first-level folders `Contracts/`, `Platform/`, `Cli/`, `Corpus/`, `Support/`)
- [X] T003 [P] Add NuGet packages: `Microsoft.Data.Sqlite` to `src/Polifemo.Platform/Polifemo.Platform.csproj`, `Elastic.Clients.Elasticsearch` 9.x to the same project, `System.CommandLine` to `src/Polifemo.Cli/Polifemo.Cli.csproj`. Record in the plan's dependency list why each is needed (already in plan.md; no extra README needed)
- [X] T004 [P] Update `.gitignore`: ignore `.index/`, `samples/`, `samples.local.md`, `**/.tokensave/`, `*.received`; do NOT ignore `testrepo/` or `src/fixtures/**`
- [X] T005 [P] Add a shared test helper `src/Polifemo.Tests/Support/TestWorkspace.cs` that creates a per-test directory under `.index/test/<guid>/` and cleans it up, plus `Support/RequireTokenSave.cs` that FAILS (does not skip) with a clear message when `tokensave` is missing or not 7.16.0 (research R10)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Typed errors, CLI plumbing and process helpers used by every story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 [P] Implement `PolifemoException(string Code, string Message)` base and the subclasses `TokenSaveCompatibilityException` (carries `Mismatch[]`), `ActiveDatabaseResolutionException`, `SearchStoreException`, `FixturePreparationException`, `EmbeddingProfileMismatchException` in `src/Polifemo.Contracts/Errors/` (research R9, contracts/identities-and-hashes.md "Errors and provenance")
- [X] T007 [P] Implement `SourceScope` record `{ authorizationBoundary, repositoryId, branchRef, worktreeId, snapshotId }` (all non-empty strings, validated in the constructor) and `SnapshotProvenance(SourceScope scope, string snapshotId, string sourceCommit, string extractorVersion)` in `src/Polifemo.Contracts/Provenance/`
- [X] T008 Implement CLI skeleton in `src/Polifemo.Cli/Program.cs`: root command `polifemo`, JSON-to-stdout / diagnostics-to-stderr output helper `CliOutput.cs`, exit-code mapping (0 ok, 1 check failed, 2 invalid args, 3 environment error, 130 cancelled per contracts/cli.md), Ctrl+C wired to a `CancellationToken` that never writes partial success
- [X] T009 [P] Implement `ProcessRunner` in `src/Polifemo.Platform/Processes/ProcessRunner.cs` (async, cancellable, captures stdout/stderr, never logs environment or credentials) used for `git` and `tokensave` calls
- [X] T010 [P] Unit tests for exit-code mapping and for exception → exit-code/JSON-error conversion in `src/Polifemo.Tests/Cli/ExitCodeTests.cs`

**Checkpoint**: Foundation ready; US1 and US2 can start in parallel.

---

## Phase 3: User Story 1 - Verified and pinned platform (Priority: P1) 🎯 MVP

**Goal**: Pinned TokenSave 7.16.0 / Elasticsearch 9.5.5 / .NET 10 / Python 3.12, a compatibility check that stops on any mismatch before import, an authenticated BM25 + knn smoke test, and the compatibility record.

**Independent Test**: `polifemo platform check` passes on a pinned-build worktree (or synthesized DB) and fails with a named code for each mismatch type; `polifemo platform smoke` passes against the local store and fails with `ES_AUTH_FAILED` (no password in output) on wrong credentials.

### Tests for User Story 1 ⚠️ (write first, must fail)

- [X] T011 [P] [US1] Resolver tests in `src/Polifemo.Tests/Platform/TokenSave/ActiveDatabaseResolverTests.cs`: branch-meta names db → used (`resolution: "branch-meta"`); branch without entry → `default_branch` entry; metadata absent → `.tokensave/tokensave.db` (`resolution: "default"`); invalid JSON → `TS_META_MALFORMED`; missing `db_file` / no usable entry → `TS_META_NO_DB`; named file absent → `TS_DB_MISSING` with no silent fallback
- [X] T012 [P] [US1] Compatibility checker tests on synthesized SQLite DBs in `src/Polifemo.Tests/Platform/TokenSave/CompatibilityCheckerTests.cs`: valid DB → `ok: true`; wrong binary version (`--expect-tokensave 0.0.0`) → `TS_VERSION_MISMATCH`; `metadata.last_full_index_version` wrong or missing → `TS_INDEX_VERSION_MISMATCH`; dropped table → `TS_TABLE_MISSING`; dropped/renamed column → `TS_COLUMN_MISSING`; changed declared type or NOT NULL → `TS_COLUMN_CHANGED`; extra table/column → pass with `TS_EXTRA_TABLE`/`TS_EXTRA_COLUMN` warning; several defects at once → every mismatch reported, none short-circuited (SC-001)
- [X] T013 [P] [US1] Read-only guarantee test in `src/Polifemo.Tests/Platform/TokenSave/ReadOnlyGuaranteeTests.cs`: check never changes the DB bytes, mtime or `-wal` file; a simulated concurrent modification during the check yields `TS_DB_MODIFIED`
- [X] T014 [P] [US1] Elasticsearch smoke tests in `src/Polifemo.Tests/Platform/Elasticsearch/SmokeTestTests.cs` gated on `ELASTICSEARCH_URL` (skipped with reason otherwise): success path reports version 9.5.5, license `basic`, `bm25`, `knn`, `cleanup` all ok; wrong password → `ES_AUTH_FAILED` and the captured output/exception text does not contain the password; unreachable URL → `ES_UNREACHABLE`; other version → `ES_VERSION_UNSUPPORTED` naming the version; the temp index `smoke-*` is deleted even when an assertion fails
- [X] T015 [P] [US1] CLI contract tests in `src/Polifemo.Tests/Cli/PlatformCommandTests.cs` for `platform check` JSON shape (`ok, tokensaveBinaryVersion, indexVersion, databasePath, resolution, mismatches[], warnings[]`) and exit codes 0/1/2/3 per contracts/cli.md

### Implementation for User Story 1

- [X] T016 [US1] Generate the embedded resource `src/Polifemo.Platform/TokenSave/expected-schema.v7.16.0.json` from `PRAGMA table_info` of a database created by the pinned binary inside the devcontainer (tables `files`, `nodes`, `edges`, `unresolved_refs`, `ambiguous_calls`, `metadata`; each column with name, declared type, notnull, per contracts/tokensave-schema.md). Replace the observed list in `contracts/tokensave-schema.md` if the devcontainer result differs. Mark the resource `EmbeddedResource` in `Polifemo.Platform.csproj`
- [X] T017 [P] [US1] Implement `Mismatch` / `CompatibilityReport` records (`ok == (mismatches.Count == 0)`; warnings never change `ok`) with the nine mismatch codes from data-model.md in `src/Polifemo.Platform/TokenSave/CompatibilityReport.cs`
- [X] T018 [US1] Implement `ActiveDatabaseResolver` in `src/Polifemo.Platform/TokenSave/ActiveDatabaseResolver.cs` following research R3 (branch of the worktree via `git`, fallback to `default_branch` entry, typed errors, no silent fallback) — depends on T006, T009
- [X] T019 [US1] Implement `TokenSaveReader` (the single read-only adapter: `Mode=ReadOnly`, `PRAGMA table_info`, `metadata` lookup, size/mtime capture of DB and `-wal`) in `src/Polifemo.Platform/TokenSave/TokenSaveReader.cs`; nothing in the code base may read `nodes`/`edges` content before `ok == true`
- [X] T020 [US1] Implement `CompatibilityChecker` in `src/Polifemo.Platform/TokenSave/CompatibilityChecker.cs` applying the rules table of contracts/tokensave-schema.md (binary version via `tokensave --version`, index version, tables, columns, extras as warnings, `TS_DB_MODIFIED` before/after comparison; all mismatches collected) — depends on T016–T019
- [X] T021 [P] [US1] Implement `SmokeTest` in `src/Polifemo.Platform/Elasticsearch/SmokeTest.cs` per research R4: `GET /` version = 9.5.5, `GET _license` = basic, temp index `smoke-<guid>` with `text` + `dense_vector` (dims 4, cosine, indexed), bulk of five synthetic docs with hand-written vectors, `match` query top-hit assertion, top-level `knn` nearest-doc assertion, delete in `finally`; check every bulk item; no `retriever.rrf`, `text_similarity_reranker` or inference endpoints; error messages show URL and username only; password read from `ELASTICSEARCH_PASSWORD` only
- [X] T022 [US1] Wire `polifemo platform check --worktree <dir> [--expect-tokensave <ver>]` and `polifemo platform smoke [--url] [--username]` in `src/Polifemo.Cli/Commands/PlatformCommands.cs` (defaults from `ELASTICSEARCH_URL`/`ELASTICSEARCH_USERNAME`/`ELASTICSEARCH_PASSWORD`; no password argument) — depends on T008, T020, T021
- [X] T023 [P] [US1] Pin TokenSave in `.devcontainer/postcreate.sh`: download the tagged 7.16.0 release asset per architecture, verify the sha256 recorded in the script (fail, never fall back to latest), run `tokensave disable-upload-counter`, never `tokensave upgrade`, set `report_savings: false`, and run `git submodule update --init testrepo/cisharpai` best-effort. Keep `.devcontainer/docker-compose.yml` on Elasticsearch `9.5.5`
- [X] T024 [P] [US1] Confirm in the devcontainer and record the open items from research R4: `knn` works under Basic on 9.5.5, the exact license level of `retriever.rrf` and `text_similarity_reranker` on 9.5.5 (response bodies without credentials). Feeds T025
- [X] T025 [US1] Write the first part of `docs/platform-compatibility.md`: `pins` (TokenSave 7.16.0 + sha256 per arch, Elasticsearch 9.5.5 Basic, .NET SDK from `global.json`, Python 3.12, git, uv), `license` (TokenSave, Elasticsearch, .NET, Python), `network` (what needs network once, what must run offline, upload counter disabled), `unsupported` Elasticsearch features and why (RRF retriever, native reranker), TokenSave language list from `metadata.language_detection` (only C# and Markdown are supported targets), and an explicit statement that no 7.x/8.x Elasticsearch support is implied (FR-010)

**Checkpoint**: US1 is functional on synthesized databases and on the local store; extraction/branch sections of the record are completed in Phase 6 once a fixture worktree exists.

---

## Phase 4: User Story 2 - Reproducible fixtures with known answers (Priority: P2)

**Goal**: Deterministic synthetic C# repository, `fixture build|prepare|mutate|worktree` commands that never touch the source, seven named mutations (+ `rename-and-edit`), JSON expectation records, and the pinned public corpus `testrepo/cisharpai` with its policy amendment.

**Independent Test**: `fixture prepare` with a sample path + commit writes only under `.index/`; the source `git status --porcelain` and HEAD are identical before and after; each mutation applied twice from the same base yields the same tree SHA and commit SHA; expectation tests pass with unresolved/ambiguous cases reported as such.

### Tests for User Story 2 ⚠️ (write first, must fail)

- [X] T026 [P] [US2] Preparation tests in `src/Polifemo.Tests/Platform/Fixtures/SamplePreparerTests.cs`: result only under `.index/work/<name>/`; source status and HEAD unchanged (`PREP_SOURCE_MODIFIED` if not); `.polifemo-prep.json` written; no `.tokensave/` in the source; unknown commit → `PREP_UNKNOWN_COMMIT`, non-repo → `PREP_NOT_A_REPO`, both leave no directory and no `.tmp`; existing target → `PREP_TARGET_EXISTS` unless `--replace`; `--name` that resolves outside `.index/work/` is refused; global git hooks never fire (`core.hooksPath` empty dir)
- [X] T027 [P] [US2] Determinism tests in `src/Polifemo.Tests/Platform/Fixtures/FixtureBuilderTests.cs`: building the synthetic fixture twice gives the same commit SHA and tree SHA, equal to the SHA recorded in `src/fixtures/synthetic-csharp/PROVENANCE.md`; CRLF, BOM and Latin-1 bytes survive byte-exactly
- [X] T028 [P] [US2] Mutation tests in `src/Polifemo.Tests/Platform/Fixtures/MutationApplierTests.cs`: for every mutation folder, applying it twice from the same base (two clones) gives the same tree SHA and commit SHA; wrong base → `MUT_BASE_MISMATCH`; ops that escape the worktree (`..`, absolute path) are rejected; `--no-commit` leaves changes uncommitted
- [X] T029 [P] [US2] Worktree isolation test in `src/Polifemo.Tests/Platform/Fixtures/WorktreeIsolationTests.cs`: second worktree created inside the clone, with dirty edits from `edit-method`; each keeps its own content and its own index; reverting the dirty edits restores the original tree; the source repository is untouched
- [X] T030 [P] [US2] Expectation tests in `src/Polifemo.Tests/Platform/Fixtures/ExpectationTests.cs` (`[Trait("Category","Expectations")]`, needs pinned TokenSave): every expected symbol (path, qualifiedName, kind, extents), relationship and doc association is checked; extents verified by decoding the raw bytes with the detected encoding and checking the range contains the symbol name; `unresolved`/`ambiguous`/`unsupported` cases are reported with their status and never counted as pass or missing; JSON validated against `contracts/expectation-record.schema.json`
- [X] T031 [P] [US2] Corpus tests in `src/Polifemo.Tests/Corpus/CisharpaiTests.cs`: `PinnedCommitMatchesProvenance` (submodule HEAD = `testrepo/corpora.json` commit `8103c0d822211a60bb31815a28d354578be6bd6c`), `CuratedSymbolsExtracted`, `CuratedRelationships`, `LogicalKeysUnique`, `ExtentsWithinFileBounds`, `CountsWithinBands`, `SummaryMatchesSnapshot` (writes `.received` on mismatch, `UPDATE_SNAPSHOTS=1` regenerates); skipped locally with hint `git submodule update --init testrepo/cisharpai` when absent, FAIL when `CI` or `REQUIRE_CORPORA=1`; also assert no `.tokensave/` and no status changes under `testrepo/cisharpai`

### Implementation for User Story 2

- [X] T032 [P] [US2] Author the synthetic repository content in `src/fixtures/synthetic-csharp/src/` (15–25 files, never compiled): overloads, generics and nested generic types with the same simple name, partial classes split across files, extension methods, interfaces and implementations, DI registrations (`AddXxx(this IServiceCollection)`), async methods with `CancellationToken`, cross-file calls, plus an unresolvable extension-method call and a DI-resolved interface call for the unresolved/ambiguous cases; add encoding variants per research R7: UTF-8 no BOM + LF, UTF-8 BOM, UTF-8 + CRLF, Latin-1 with non-ASCII comment, one UTF-16 LE file
- [X] T033 [P] [US2] Author fixture documentation in `src/fixtures/synthetic-csharp/docs/` (central Markdown with explicit `applies_to` associations, one stale document pointing at a removed symbol) and `src/fixtures/synthetic-csharp/tokensave-docs/` (sidecar Markdown), per the TokenSave `docs_dir: "tokensave-docs"` behavior in research baseline
- [X] T034 [US2] Implement `FixtureBuilder` in `src/Polifemo.Platform/Fixtures/FixtureBuilder.cs`: copy files in sorted order into `.index/fixtures/synthetic-csharp.git-src/`, `git init -b main`, commit with fixed author/committer and `GIT_AUTHOR_DATE`/`GIT_COMMITTER_DATE=2026-01-01T00:00:00Z`, return `{commit, treeSha}` (research R6)
- [X] T035 [US2] Record the resulting commit SHA in `src/fixtures/synthetic-csharp/PROVENANCE.md` (source, license, how to rebuild, expected SHA) — depends on T032–T034
- [X] T036 [US2] Implement `SamplePreparer` in `src/Polifemo.Platform/Fixtures/SamplePreparer.cs` following research R5 exactly (validate repo and commit, capture source status + HEAD, `clone --no-hardlinks --no-checkout` to `<name>.tmp` with `core.autocrlf=false` and an empty `core.hooksPath`, detached checkout, rename, recapture and compare, write `.polifemo-prep.json`, delete `.tmp` on failure; `--index` runs the pinned `tokensave init` in the clone only)
- [X] T037 [US2] Implement `MutationApplier` in `src/Polifemo.Platform/Fixtures/MutationApplier.cs` with ops `write`, `delete`, `rename`, `insertLines`, `replaceBytes` (byte-exact, relative paths inside the worktree only, base must equal HEAD, commit with fixed dates, `--no-commit` support) and a second-worktree helper `WorktreeFactory` (`git worktree add` inside the clone, optional dirty-from mutation) in `src/Polifemo.Platform/Fixtures/WorktreeFactory.cs`
- [X] T038 [P] [US2] Author the eight mutations under `src/fixtures/synthetic-csharp/mutations/<name>/mutation.json` (+ payload files): `add-file`, `edit-method`, `delete-file`, `rename-file` (same bytes), `line-shift` (insert lines above a method), `docs-only-edit`, `change-doc-association`, `rename-and-edit`; each declares its `base`
- [X] T039 [US2] Wire `polifemo fixture build|prepare|mutate|worktree` in `src/Polifemo.Cli/Commands/FixtureCommands.cs` with the arguments, outputs and error codes of contracts/cli.md (`PREP_*`, `MUT_BASE_MISMATCH`) — depends on T008, T034, T036, T037
- [X] T040 [US2] Generate the expectation records `src/fixtures/synthetic-csharp/expectations/<state>.json` for the base state and for each mutation state from observed pinned-TokenSave output (symbols with kinds/extents, relationships with `status` resolved|unresolved|ambiguous|unsupported and `candidates`, `docAssociations`, `files` with encoding/lineEnding/`extracted`); record the UTF-16 outcome as observed. Review each entry against the source by hand — depends on T035, T039
- [X] T041 [P] [US2] Add the committed submodule `git submodule add https://github.com/alkampfergit/cisharpai testrepo/cisharpai`, pin to `8103c0d822211a60bb31815a28d354578be6bd6c`, and create `testrepo/corpora.json` with `{name, url, commit, license: "MIT (Directory.Build.props only)", licenseEvidence, approvedOn}` and the noted LICENSE-file gap (research R11). Note: this is the only gitlink allowed; do not use `-f` and do not touch `samples/`
- [X] T042 [US2] Create the cisharpai expectation files in `src/Polifemo.Tests/Corpus/Expectations/cisharpai/`: `curated.json` (20–40 hand-verified symbols/relations covering a partial class, extension method, generic method, verified overload pair, record, primary constructor, call and implements edges, one unresolved and one ambiguous call), `invariants.json` (per-kind count bands, ±2% recorded as a setting), `summary.snapshot.json` (sorted logical keys) — depends on T041, T050 (needs `LogicalKey`)
- [X] T043 [US2] Apply the "Public test corpora" policy amendment in the same change: add the rule from research R11 to `AGENTS.md` (Samples section) and bump `.specify/memory/constitution.md` to v1.1.0 (MINOR) with a Sync Impact Report and updated Principle III/Technology text; add clarifying notes to spec FR-013 and FR-018; AGENTS.md prevails on conflict
- [X] T044 [P] [US2] Add CI/devcontainer plumbing for the corpus: ensure CI checkout uses `submodules: true` (if a workflow exists under `.github/workflows/`; otherwise document in `docs/platform-compatibility.md`), and that `testrepo/` never contains `.tokensave/`

**Checkpoint**: US2 functional: fixtures reproducible, isolation proven, expectations checked.

> T042 needs the identity code from T050; if working strictly in story order, implement T050–T052 (pure code, no fixture dependency) first or generate the snapshot after Phase 5 starts.

---

## Phase 5: User Story 3 - Versioned identities, profiles and boundaries (Priority: P3)

**Goal**: Minimal C# identity/hash/profile types used by tests and the CLI, the profile-mismatch rejection, identity tests for all seven mutations, and boundaries written as contracts only.

**Independent Test**: identity tests assert every cell of the mutation × identity matrix (and that untouched symbols are unchanged); `EnsureSameSpace` throws `EMB_PROFILE_MISMATCH` with no result; boundaries.md review finds one responsibility per boundary and no unused interface in the solution.

### Tests for User Story 3 ⚠️ (write first, must fail)

- [X] T045 [P] [US3] Hashing unit tests in `src/Polifemo.Tests/Contracts/HashingTests.cs`: canonical `Enc` (4-byte big-endian length prefix, UTF-8) prevents concatenation collisions (`("ab","c")` ≠ `("a","bc")`); `H(tag, …)` is lowercase hex SHA-256 and the tag participates; path normalization (relative, `/`, no `./`, case kept, NFC); signature whitespace collapse; known-answer vectors for each tag `lk/v1`, `fh/v1`, `oc/v1`, `ch/v1`, `eh/v1`, `ep/v1`
- [X] T046 [P] [US3] Logical key tests in `src/Polifemo.Tests/Contracts/LogicalKeyTests.cs`: two overloads differ and are stable across runs; line numbers and TokenSave node id never change the key; duplicate (qualifiedName, kind, signature) in one file get `#ordinal` in source order (documents the known trade-off when an earlier duplicate is inserted); partial classes in different files differ by path; nested generic types with the same simple name differ by `qualifiedName`
- [X] T047 [P] [US3] Profile tests in `src/Polifemo.Tests/Contracts/EmbeddingProfileTests.cs`: validation (`dimension > 0`, `maxTokens > 0`, `revision` is an immutable ref and never `main`); `profileId` stable; `EnsureSameSpace` throws `EmbeddingProfileMismatchException` with `Code = "EMB_PROFILE_MISMATCH"`, expected and actual, and no result is produced; `ProfileVersions` independence: changing only one version changes only the hashes marked ✔ in the data-model dependency table
- [X] T048 [P] [US3] Identity matrix tests in `src/Polifemo.Tests/Contracts/IdentityMatrixTests.cs` (`[Trait("Category","Identity")]`, needs the fixture from US2): for each of the seven mutations (+ `rename-and-edit`, + same file in two scopes) assert every cell of the data-model "Mutation × identity matrix" for LogicalKey, FileHash, OccurrenceKey, ChunkHash, EmbeddingInputHash and location, and assert untouched symbols keep every value; line-shift keeps `ChunkHash` because lines are provenance, not inputs; rename keeps `FileHash` and `EmbeddingInputHash`; two scopes share `FileHash` but have different `OccurrenceKey`
- [X] T049 [P] [US3] Boundary hygiene test in `src/Polifemo.Tests/Contracts/BoundaryHygieneTests.cs`: reflection check that `Polifemo.Contracts` and `Polifemo.Platform` define no `IExporter`, `IChunker`, `IStore`, `IEmbedder`, `IRetriever`, `IReranker`, `IContextSelector` or other boundary interface (FR-028), and that every public type in `Polifemo.Contracts` is referenced by a test or the CLI

### Implementation for User Story 3

- [X] T050 [US3] Implement `CanonicalEncoding` and `Hashing` (`Enc`, `H(tag, …)`, path and signature normalizers, `canonicalJson` with ordinal-sorted keys, invariant numbers, UTF-8) in `src/Polifemo.Contracts/Hashing/` per contracts/identities-and-hashes.md
- [X] T051 [P] [US3] Implement `LogicalKey` (with signature/ordinal disambiguation), `FileHash`, `OccurrenceKey`, `ChunkHash`, `EmbeddingInputHash` value types in `src/Polifemo.Contracts/Identity/` — depends on T050
- [X] T052 [P] [US3] Implement `ProfileVersions` (`extractorVersion, chunkerVersion, mappingVersion, embeddingProfileId, inputTemplateVersion`) and `EmbeddingProfile` (`provider, model, revision, documentPrefix, queryPrefix, tokenizer, maxTokens, dimension, pooling, normalize`, `ProfileId = H("ep/v1", …)`, `EnsureSameSpace`) in `src/Polifemo.Contracts/Profiles/` — depends on T006, T050
- [X] T053 [US3] Implement the test-only identity derivation from expectation records/TokenSave symbols (builds `LogicalKey`, `OccurrenceKey`, `ChunkHash`, `EmbeddingInputHash` from a prepared worktree; TokenSave node id and lines kept as provenance only) in `src/Polifemo.Tests/Support/IdentityDeriver.cs`, from the expectation records and the raw fixture bytes, so it needs no TokenSave (TokenSave itself is verified by the expectation tests)
- [X] T054 [US3] Use the identity types in the CLI where the plan requires it: add the `SnapshotProvenance` to the `platform check` and `fixture prepare` JSON outputs in `src/Polifemo.Cli/Commands/` (every result type carries provenance, FR-026)
- [X] T055 [P] [US3] Review and finalize `specs/001-platform-foundations/contracts/boundaries.md` against the implemented types (no interface added, one responsibility each, disabled modes `EMB_DISABLED`, reranker passthrough + degraded-stage diagnostic, store works for Elasticsearch and a later SQLite baseline, provider-specific options only in the owning component); amend the contract if any detail was found wrong while writing tests

**Checkpoint**: All three stories independently verifiable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Finish the compatibility record, validate the quickstart and the success criteria.

- [X] T056 Complete `docs/platform-compatibility.md` with the observed `extraction` (C# node kinds and edge kinds seen on the fixture), companion-doc association behavior (sidecar, `docs_dir` + `applies_to`), `branchBehavior` (branch-meta shape, per-branch DB files, worktree behavior, observed with the dirty and second worktree), and `unsupported`/ambiguous cases (including the UTF-16 outcome and extension/DI calls), with the kinds list used by the `kind` normalization in identities-and-hashes.md — depends on T040, T025
- [X] T057 [P] Add a "Platform foundations" section to `AGENTS.md` repository layout (`src/` incl. test projects and `src/fixtures/`, `testrepo/`, `docs/platform-compatibility.md`) only for folders that now exist, keeping AGENTS.md the single authoritative file
- [X] T058 [P] Create `experiments/` README placeholder only if a deliverable needs it; otherwise record in `docs/platform-compatibility.md` that no experiment folder is created by this feature (Constitution VII: folders only when needed). Done: not needed, recorded in `docs/platform-compatibility.md` section 7
- [ ] T059 Run quickstart steps 1–5 and 7–8 inside the devcontainer; fix any command shape that differs from `contracts/cli.md`. Status: the steps were run on the macOS host (one command shape fixed in step 4); the devcontainer run, with the Compose Elasticsearch that enforces authentication, is still open
- [X] T061 [P] Security/hygiene pass: grep committed files and test output for the Elasticsearch password, private sample URLs and sample content; confirm `git ls-files | grep -E '^(samples/|\.index/)'` prints nothing and the only gitlink is `testrepo/cisharpai`; confirm `.gitmodules` lists only `testrepo/` entries (SC-004, SC-007)
- [X] T062 Map each acceptance row of the plan's "Issue Traceability" table to its evidence (test name or doc section) in `specs/001-platform-foundations/evidence.md` (a new file: `/speckit-implement` treats the checklist files as read-only)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: needs Setup; blocks all stories.
- **US1 (Phase 3)** and **US2 (Phase 4)**: both start after Phase 2, independent of each other.
- **US3 (Phase 5)**: types and unit tests (T050–T052, T045–T047, T049) need only Phase 2. T048 and T053 need US2 mutations (T038, T039, T040) and `TokenSaveReader` (T019).
- **T042** (cisharpai expectations) needs `LogicalKey` (T050/T051) for the snapshot file.
- **Polish (Phase 6)**: needs all stories; T056 needs T025 and T040.

### Within Each Story

- Tests are written first and must fail.
- Models/records before services, services before CLI wiring, CLI before quickstart validation.

### Parallel Opportunities

- Setup: T003, T004, T005 together.
- Foundational: T006, T007, T009, T010 together.
- US1: tests T011–T015 together; T017, T021, T023, T024 together.
- US2: tests T026–T031 together; T032, T033, T038, T041 together.
- US3: T045–T049 together; T051, T052, T055 together after T050.
- US1 and US2 can be developed by different people at the same time.

### Parallel Example: User Story 1

```text
Task: "Resolver tests in src/Polifemo.Tests/Platform/TokenSave/ActiveDatabaseResolverTests.cs"
Task: "Compatibility checker tests in src/Polifemo.Tests/Platform/TokenSave/CompatibilityCheckerTests.cs"
Task: "SmokeTest in src/Polifemo.Platform/Elasticsearch/SmokeTest.cs"
Task: "Pin TokenSave in .devcontainer/postcreate.sh"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup, Phase 2 Foundational.
2. Phase 3 (US1): check + smoke + pins + first part of the compatibility record.
3. Stop and validate: quickstart steps 2 (synthesized failures) and 3.

### Incremental Delivery

1. Setup + Foundational.
2. US1 → pinned, verified platform (MVP).
3. US2 → fixtures, mutations, corpus, policy amendment.
4. US3 → identities, profiles, matrix tests, boundaries.
5. Polish → complete the record, offline timing, hygiene review.

### Parallel Team Strategy

After Phase 2: Developer A takes US1; Developer B takes US2; the identity types of US3 (T050–T052) can be done by either, and the matrix tests wait for US2.

---

## Notes

- [P] tasks touch different files with no dependency on unfinished tasks.
- Every story is independently verifiable by the Independent Test above.
- The cisharpai pin has a known license gap (no LICENSE file); it is recorded, not hidden (T041).
- Never write into `samples/`, `testrepo/` or a TokenSave database; the tests enforce it.
- Commit after each task or logical group.

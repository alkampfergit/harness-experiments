# Research: Verified Platform Foundations

Phase 0 decisions for [plan.md](./plan.md). Read-only observations were made on 2026-10-24 on a macOS host. Facts marked "to confirm" are verified again inside the devcontainer during implementation, and the result goes into `docs/platform-compatibility.md`.

Observed baseline:
- **TokenSave versions:** the binary `tokensave --version` reports 7.16.0, while the database `metadata.last_full_index_version` is 7.15.0.
- **Tables:** `files`, `nodes`, `edges`, `unresolved_refs`, `ambiguous_calls`, `metadata`. There are also FTS, vector, memory, `read_cache`, `node_fingerprints` and `trait_dispatch_callers` tables.
- **Branch metadata:** `.tokensave/branch-meta.json` is `{default_branch, branches{<name>{db_file, created_at, last_synced_at}}}`.
- **Config:** `config.json` has `docs_dir: "tokensave-docs"`, `report_savings: false` and `md` in `artifact_extensions`.
- **Elasticsearch:** `docker-compose.yml` pins `9.5.5` with a Basic license, security on and HTTP without TLS.

## R1. TokenSave pin and version check

- **Decision:** pin TokenSave **7.16.0**. `postcreate.sh` downloads the tagged release asset for the architecture and verifies a sha256 recorded in the script. If the checksum doesn't match, it fails instead of falling back to latest. It runs `tokensave disable-upload-counter` and never `tokensave upgrade`. The compatibility check requires **both** values to equal the pin: `tokensave --version` and `metadata.last_full_index_version`. The two mismatches have separate error codes, and the index-version error tells the user to run a full re-index.
- **Rationale:** the local observation shows the binary and the index version can differ after an upgrade. An index built by an older extractor can differ in kinds and edges even with the same schema.
- **Alternatives considered:**
  - Checking only the binary version: misses stale indexes.
  - Accepting a semver range: unmeasured, and it breaks the reproducibility principle.
  - Vendoring the binary: too large, and the license must be checked first.

## R2. Rule for TokenSave schema differences

- **Decision:** the required schema covers only the tables and columns the adapter reads: `files`, `nodes`, `edges`, `unresolved_refs`, `ambiguous_calls`, `metadata`.
  - **Fail:** a missing required table or column, or a changed declared type or NOT NULL.
  - **Pass with a named warning:** an extra table, or an extra column in a required table. The warning appears in the report and in the check's JSON output.
  - Tables the adapter doesn't read are never inspected beyond being listed.
  - The expectation is an embedded resource, `expected-schema.v7.16.0.json`, generated from `PRAGMA table_info` on a database built by the pinned version.
- **Rationale:** this closes the open item from the checklist. Extra columns cannot change what the adapter reads. Missing or changed ones can. The rule is explicit, so nothing is "ignored without a decision".
- **Alternatives considered:**
  - Exact match of all tables: fails on harmless FTS or memory additions.
  - Ignoring extras silently: the spec forbids it.

## R3. Active database resolution

- **Decision:**
  1. If `.tokensave/branch-meta.json` is absent, use `.tokensave/tokensave.db`.
  2. If it is present, select the entry for the current git branch of the worktree. If that branch has no entry, use the `default_branch` entry.
  3. Each failure is a typed error with no fallback: JSON that doesn't parse (`TS_META_MALFORMED`), no usable entry or a missing `db_file` (`TS_META_NO_DB`), and a file that doesn't exist or can't be read (`TS_DB_MISSING`).

  The database is opened with `Mode=ReadOnly`. The check records the size and mtime of the database (and of the `-wal` file) before and after the run, and fails if they change.
- **Rationale:** FR-004 and FR-005, and Principle V.
- **Alternatives considered:**
  - `Mode=ReadWriteCreate` plus discipline: weaker.
  - Copying the database first: hides the "never write" guarantee, and is slow on large indexes.

## R4. Elasticsearch pin and smoke test

- **Decision:** pin **9.5.5**, Basic license. The smoke test:
  1. checks that `GET /` returns `version.number == 9.5.5`, otherwise it reports `ES_VERSION_UNSUPPORTED` and names the version;
  2. checks that `GET _license` returns type `basic`;
  3. creates a temporary index `smoke-<guid>` with a `text` field and a `dense_vector` (dims 4, cosine, indexed);
  4. bulk indexes five synthetic documents with hand-written vectors and refreshes;
  5. runs a `match` query and asserts the expected top hit (BM25);
  6. runs a top-level `knn` query and asserts the expected nearest document;
  7. deletes the index in a `finally` block.

  No `retriever.rrf`, `text_similarity_reranker` or inference endpoints are used. The compatibility record lists them as unsupported because they need a license or are not adopted. Authentication failures (401) and an unreachable store produce `ES_AUTH_FAILED` and `ES_UNREACHABLE`. Messages show the URL and username, never the password.
- **Rationale:** FR-006, FR-007 and FR-010. Synthetic vectors avoid any model dependency, which keeps the smoke test offline and fast.
- **To confirm:** that the 9.5.5 `knn` search works under Basic, and the exact license level of the RRF retriever on 9.5.5. The confirmed facts are recorded with the response bodies minus credentials.
- **Alternatives considered:** Testcontainers. It duplicates the Compose service and needs Docker-in-Docker inside the devcontainer.

## R5. Preparing a sample without touching it

- **Decision:** `polifemo fixture prepare --sample <path> --commit <sha> --name <name>` works in these steps:
  1. Validate that `<path>` is a git repository and that `git cat-file -e <sha>^{commit}` succeeds. If either check fails, nothing is created (`PREP_NOT_A_REPO`, `PREP_UNKNOWN_COMMIT`).
  2. Capture `git status --porcelain=v1 --ignored` and HEAD of the source.
  3. Run `git -c core.autocrlf=false -c core.hooksPath=<empty dir> clone --no-hardlinks --no-checkout <path> .index/work/<name>.tmp`.
  4. Run `checkout --detach <sha>` and set `core.hooksPath` locally to an empty directory, so the global TokenSave hooks never fire.
  5. Rename `.tmp` to its final name. On any failure, delete the `.tmp` directory.
  6. Recapture the source status and HEAD, and fail if they changed (`PREP_SOURCE_MODIFIED`).
  7. Write `.index/work/<name>/.polifemo-prep.json` with the source path, commit, tool version and UTC time.

  `tokensave init` then runs in the clone. A second worktree with dirty edits is made with `git worktree add` **inside the clone**, never on the source.
- **Rationale:** `git clone` of a local path only reads the source. `git worktree add` on the source writes `.git/worktrees/`, which conflicts with SC-004.
- **Alternatives considered:**
  - `git archive | tar`: no git history, so TokenSave branch behavior can't be observed.
  - `--shared` (alternates): the clone depends on the source object store.

## R6. Synthetic fixture determinism and mutations

- **Decision:** the fixture is stored as plain files in `src/fixtures/synthetic-csharp/`. `.gitattributes` marks `src/fixtures/** -text` so CRLF, BOM and Latin-1 bytes survive git. `polifemo fixture build` copies the files into `.index/fixtures/synthetic-csharp.git-src/`, runs `git init -b main` and commits. Author, committer and both dates are fixed (`GIT_AUTHOR_DATE=2026-01-01T00:00:00Z`) and the file order is sorted, so the commit SHA is deterministic. The expected SHA is recorded in `PROVENANCE.md` and asserted by a test.

  Each mutation is a folder `mutations/<name>/` with a `mutation.json` and payload files:
  - The supported ops are `write`, `delete`, `rename`, `insertLines` and `replaceBytes`.
  - The seven mutations are `add-file`, `edit-method`, `delete-file`, `rename-file`, `line-shift`, `docs-only-edit` and `change-doc-association`. There is also an extra `rename-and-edit` for the edge case.
  - `fixture mutate --worktree <dir> --mutation <name>` applies the ops byte-exactly and commits with fixed dates. It asserts that the starting HEAD equals the mutation's declared `base`.
- **Rationale:** FR-015 and SC-003, which require byte-identical results. Declarative mutations are reviewable.
- **Alternatives considered:**
  - A nested `.git` in the repository: not possible to commit.
  - Patch files: fragile with CRLF and BOM.
  - Mutations coded in C#: less reviewable.

## R7. Encodings and line endings

- **Decision:** the fixture includes files in UTF-8 without BOM and LF, UTF-8 with BOM, UTF-8 with CRLF, Latin-1 (ISO-8859-1) with non-ASCII in a comment, and one UTF-16 LE file. The UTF-16 file is used to **observe** TokenSave behavior. Its outcome (extracted, skipped or garbled) is recorded as an expectation with `status: unsupported` when applicable, and in the compatibility record. Extent tests read the raw bytes, decode them with the detected encoding and check that the `start_line`/`end_line` ranges contain the symbol name.
- **Rationale:** these are the spec edge cases. Line ranges must be correct for byte-exact hashing later.

## R8. Identity and hash scheme

- **Decision:** the encoding is **canonical**: each field is written as a 4-byte big-endian length followed by its UTF-8 bytes, and the result is hashed with SHA-256 and written as lowercase hex.
  - **Logical key** = H(`"lk/v1"`, repoId, normalizedPath, qualifiedName, kind, disambiguator).
    - `normalizedPath` uses forward slashes and is relative to the repository root, with case kept.
    - `disambiguator` is the normalized signature (whitespace collapsed, parameter list kept). If two symbols in one file share qualifiedName, kind and signature (for example partial fragments or local functions), an ordinal `#n` in source order is appended.
  - **Content revision** = file hash = H(`"fh/v1"`, raw bytes).
  - **Occurrence key** = H(`"oc/v1"`, logicalKey, authorizationBoundary, repoId, branchRef, worktreeId, snapshotId).
  - **Chunk hash** = H(`"ch/v1"`, chunkerVersion, mappingVersion, logicalKey, canonical JSON of the search fields and metadata).
  - **Embedding-input hash** = H(`"eh/v1"`, embeddingProfileId, inputTemplateVersion, inputRole, prepared input bytes). The path and scope are excluded, so a rename keeps the hash. The cache key adds the authorization boundary separately (Principle IV) and is not defined here.

  The full rules are in [contracts/identities-and-hashes.md](./contracts/identities-and-hashes.md).
- **Rationale:** FR-020 to FR-023. Length prefixes prevent concatenation collisions, and version tags let each scheme change independently.
- **Known trade-off:** the ordinal fallback changes when an earlier duplicate is inserted. The identity tests document this.
- **Alternatives considered:**
  - Readable composite IDs: they exceed the 512-byte limit for Elasticsearch IDs on generics.
  - TokenSave node IDs: forbidden.

## R9. Errors, cancellation and provenance

- **Decision:** every operation is `async` and takes a `CancellationToken`. Cancellation surfaces as `OperationCanceledException` and never as an empty result. Failures derive from `PolifemoException(string Code, string Message)`. The concrete types are only the ones the check, the CLI and the tests raise:
  - `TokenSaveCompatibilityException`, which carries a list of mismatches
  - `ActiveDatabaseResolutionException`
  - `SearchStoreException`
  - `FixturePreparationException`
  - `EmbeddingProfileMismatchException`

  Each result record carries `SnapshotProvenance(scope, snapshotId, sourceCommit, extractorVersion)`. CLI exit codes: 0 means ok, 1 means a check failed (mismatches found), 2 means a usage error, 3 means an environment error.
- **Rationale:** FR-026 and FR-027.

## R10. Test framework and environment gating

- **Decision:**
  - xUnit (v3), with `[Fact]`/`[Theory]` plus a `SkippableFact` pattern.
  - Elasticsearch tests run only when `ELASTICSEARCH_URL` is set, and are skipped with the reason otherwise.
  - TokenSave-dependent tests require the pinned binary on PATH and **fail** with a clear message when it is missing or the wrong version. They are not skipped silently.
  - Generated data goes to a per-test directory under `.index/test/`.
- **Alternatives considered:** NUnit or MSTest. They are equally capable, and xUnit is the .NET default template.

## R11. Real-code public corpus: `testrepo/cisharpai`

- **Decision:** add a **committed** git submodule `testrepo/cisharpai` → `https://github.com/alkampfergit/cisharpai`, pinned at `8103c0d822211a60bb31815a28d354578be6bd6c`, the tip of `develop` on 2026-10-19. Provenance goes in `testrepo/corpora.json` as `{name, url, commit, license, licenseEvidence, approvedOn}`.
- **Facts verified from the GitHub API and a scratch clone:**
  - The repository is public and has 445 `.cs` files, about 64.8k LOC across 13 projects (net8.0/net10.0).
  - It exercises async/CancellationToken, `IAsyncEnumerable`, DI `*ServiceCollectionExtensions`, partial classes with source-generator attributes, generic methods, records, primary constructors, about 30 interfaces and repeated method names.
  - `wiki/` has 20 Markdown topic pages to use as companion docs. `specs/`, `.claude/`, `.specify/`, `llm/` and `memories/` are agent and process files, and are excluded from the docs corpus.
- **License gap:** the repository has no LICENSE file. Only `Directory.Build.props` declares `PackageLicenseExpression` MIT.
  - **Recommended:** the owner adds an MIT `LICENSE` upstream, and the pin moves to the first commit that has it.
  - Until then, `corpora.json` records `license: "MIT (Directory.Build.props only)"` and the gap.
- **Policy:** AGENTS.md and the constitution only define private, git-ignored `samples/`. A new rule is added **in the same change** as the code:

  > **Public test corpora.** `testrepo/<name>` MAY hold committed git submodules of public repositories whose license permits redistribution and local analysis, used only by automated tests. Each is pinned to a commit; its URL, commit, license and approval date are recorded in `testrepo/corpora.json`. Changing the pin is a reviewed change that updates the expectation files in the same commit. The `samples/` rules for read-only sources and no generated artifacts apply equally: tests index a copy under `.index/work/<name>/`. Private repositories MUST NOT be added under `testrepo/`.

  The constitution moves to v1.1.0 (MINOR: guidance expanded) with a Sync Impact Report. Clarifying notes are added to spec FR-013 and FR-018.
- **Test design:**
  - Preparation uses R5 with the submodule as the source, which works offline.
  - `PinnedCommitMatchesProvenance` asserts that the submodule HEAD equals `corpora.json`.
  - Expectations are in `src/Polifemo.Tests/Corpus/Expectations/cisharpai/`:
    - `curated.json`: about 20–40 hand-verified symbols and relations with extents. They cover a partial class, an extension method, a generic method, a verified same-type overload pair, a record, a primary constructor, call and implements edges, and one unresolved and one ambiguous call.
    - `invariants.json`: per-kind count bands. The band width starts at ±2% and is recorded as a setting, not a quality threshold.
    - `summary.snapshot.json`: sorted logical keys only.
  - Tests:
    - `CuratedSymbolsExtracted`
    - `CuratedRelationships`
    - `LogicalKeysUnique`
    - `ExtentsWithinFileBounds`
    - `CountsWithinBands`
    - `SummaryMatchesSnapshot`: on a mismatch it writes `.received`, and `UPDATE_SNAPSHOTS=1` regenerates.
  - If the submodule is not initialized, the tests are skipped locally with the hint `git submodule update --init testrepo/cisharpai`. They **fail** when `CI` or `REQUIRE_CORPORA=1` is set.
- **Pitfalls handled:**
  - Line endings: clone with `core.autocrlf=false`, and add `* text=auto eol=lf` in our `.gitattributes`.
  - Hooks: `core.hooksPath` points to an empty directory.
  - Agent config in the corpus: its `AGENTS.md`, `CLAUDE.md`, `.claude/` and `.mcp.json` are never used as instructions, and harnesses run from our repository root.
  - Devcontainer: postcreate runs `git submodule update --init testrepo/cisharpai` best-effort.
  - CI: checkout uses `submodules: true`.
  - `.tokensave/` must never appear under `testrepo/`.
- **Alternatives considered:**
  - Fetching by URL and SHA at prepare time: it needs custom download code and network on the first run. It is kept as the fallback.
  - Adding cisharpai under git-ignored `samples/`: not reproducible for other maintainers or CI.

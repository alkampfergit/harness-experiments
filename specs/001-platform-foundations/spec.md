# Feature Specification: Verified Platform Foundations

**Feature Branch**: `001-platform-foundations`

**Created**: 2026-10-24

**Status**: Draft

**Input**: User description: "specify issues 5 with all the children". GitHub issue #5 [F01] "Establish verified platform foundations" (milestone M0) and its children #2 [F01-01] "Verify and pin TokenSave, Elasticsearch and runtimes", #3 [F01-02] "Prepare reproducible C# and documentation fixtures" and #4 [F01-03] "Define versioned identities, profiles and application boundaries".

## Overview

Every later experiment in this repository (ingestion, BM25 search, embeddings, reranking, context assembly) relies on three things that are currently only assumed: that the pinned tools behave as the research plan claims, that there is test data with known answers, and that all components agree on how things are identified and versioned. This feature replaces those assumptions with checked evidence, repeatable fixtures and written, tested contracts.

The users are the experiment maintainers and the coding agents working in this repository. Neither can trust a comparison between techniques unless the platform under it is pinned, the test data is reproducible and identities are stable.

**Out of scope**: production search, any ingestion or publication pipeline, hosted providers of any kind, and choosing a winning embedding or reranking model. No component implementations are delivered beyond what is needed to verify compatibility and to test the contracts.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verified and pinned platform (Priority: P1)

An experiment maintainer wants to know exactly which TokenSave build, Elasticsearch version and language runtimes the experiments support, and wants any mismatch reported before data is imported. Today TokenSave is installed as "latest release" and its database layout is undocumented, so a silent upgrade could change results or break ingestion without warning.

**Why this priority**: every other piece of work depends on it. Fixture expectations and contracts are only meaningful once the extractor's real behavior and the store's real capabilities are known. Child issue F01-03 explicitly depends on F01-01.

**Independent Test**: on a clean development environment, run the compatibility check against a TokenSave-indexed worktree and the local search store. The check reports the detected versions and schema, passes on the pinned configuration, and fails with a clear message when a different version or an unexpected schema is presented.

**Acceptance Scenarios**:

1. **Given** a worktree indexed by the pinned TokenSave build, **When** the compatibility check runs, **Then** it reports the detected TokenSave version, the database file in use and the schema, and confirms they match the pinned expectations.
2. **Given** a TokenSave database whose version or schema differs from the pinned expectation (missing table, missing or renamed column, different version marker), **When** the compatibility check runs, **Then** it stops before any import and names each mismatch.
3. **Given** a worktree with branch metadata naming the active database, **When** the active database is resolved, **Then** the database named in the metadata is used; **Given** branch metadata is absent, **Then** the documented default database is used; **Given** the metadata names a database that does not exist, **Then** resolution fails explicitly and does not fall back silently.
4. **Given** the local search store running with authentication on, **When** the smoke test runs, **Then** it indexes a few synthetic documents, gets results from authenticated lexical (BM25) search and from the supported vector search, and confirms that no licensed-only native ranking feature (native rank fusion, native reranker) is used.
5. **Given** the search store is unreachable or rejects the credentials, **When** the smoke test runs, **Then** it fails with an explicit error that identifies the cause and does not print the password.
6. **Given** the verification is complete, **When** a maintainer reads the platform compatibility record, **Then** it lists the pinned versions of TokenSave, Elasticsearch, .NET, Python and the toolchain; the C# symbol kinds TokenSave extracts; how companion documentation is associated; how branches and worktrees behave; the unsupported or ambiguous cases observed; the license assumptions; and the offline and network assumptions.

---

### User Story 2 - Reproducible fixtures with known answers (Priority: P2)

A maintainer or agent writing a test or an evaluation needs a small corpus whose expected symbols, source extents, relationships and documentation links are known in advance. They also need deterministic changes to that corpus so incremental behavior can be tested, and a procedure to prepare approved public corpora without touching the read-only sample checkouts.

**Why this priority**: tests for extraction, chunking, incremental sync and identity (F03, F04 and the contracts in Story 3) all need these fixtures. It does not depend on Story 1 and can be built in parallel, but its expected values are confirmed against the pinned extractor.

**Independent Test**: run the preparation procedure with a sample path and commit. It creates a worktree under `.index/`, leaves the sample checkout unchanged, applies each named mutation deterministically, and a test confirms that the extracted symbols and relationships match the recorded expectations, including the cases marked as unresolved or ambiguous.

**Acceptance Scenarios**:

1. **Given** a sample path and commit, **When** preparation runs, **Then** worktrees and indexes are written only under `.index/`, the sample checkout under `samples/` has no new, changed or deleted files (including no `.tokensave/` folder), and the commit used is recorded.
2. **Given** a commit that does not exist in the sample, or a path that is not a repository, **When** preparation runs, **Then** it fails explicitly without creating a partial worktree.
3. **Given** the synthetic fixture repository, **When** it is inspected, **Then** it contains overloads, generics, partial classes, extension methods, interfaces, dependency-injection registrations, async methods with cancellation, relationships across files, sidecar Markdown, central Markdown with explicit associations, a stale document, and files with different encodings and line endings.
4. **Given** the fixture, **When** each named mutation is applied (add, edit, delete, rename, line shift, documentation-only edit, changed documentation association), **Then** running it twice from the same starting commit produces byte-identical results.
5. **Given** two worktrees of the same fixture, one of them with uncommitted (dirty) edits, **When** both are prepared, **Then** each keeps its own content and index and neither changes the other.
6. **Given** the expectation records, **When** a test compares them with what the pinned extractor produces, **Then** every expected symbol, extent and relationship is checked, and each known unresolved or ambiguous case is recorded as such instead of being counted as a pass or silently dropped.
7. **Given** a commit to this repository, **When** its contents are reviewed, **Then** it contains no private sample URLs, sample content, local sample inventory, gitlink or generated data, while the provenance of the synthetic and public fixtures (source, commit, license) is recorded so they can be rebuilt.

---

### User Story 3 - Versioned identities, profiles and boundaries (Priority: P3)

A maintainer designing the exporter, the store writer and the retrieval stages needs one agreed definition of how a file, a chunk, an embedding profile, a publication batch and a search result are identified and versioned, and where each component's responsibility starts and ends. Without this, a rename, a line shift or a model change could silently corrupt incremental updates or mix incompatible vectors.

**Why this priority**: it is needed before any pipeline is built (F03 onward), but it depends on Story 1 for the real extractor kinds and identifiers, and it is tested with the fixtures from Story 2.

**Independent Test**: identity tests run on the fixture mutations from Story 2 and show which identities stay the same and which change for each mutation. A review confirms that each boundary has one stated responsibility, that no provider-specific setting appears in the chunking or orchestration contracts, and that no unused abstraction was introduced.

**Acceptance Scenarios**:

1. **Given** two overloads of the same method in one file, **When** their logical keys are derived, **Then** the keys are different and stable across runs, and are built from repository/sample, path, qualified name, kind and a signature or ordinal disambiguator, never from line numbers or TokenSave node IDs.
2. **Given** a method that only moves to different lines in the same file, **When** identities are derived again, **Then** its logical key stays the same and only its location provenance (lines) changes.
3. **Given** a file that is renamed with unchanged content, **When** identities are derived again, **Then** the documented rename behavior applies: a new path-based logical identity, while the embedding-input hash is unchanged so the existing vector can be reused.
4. **Given** the same file content in two source scopes (for example two worktrees or two authorization boundaries), **When** occurrences are derived, **Then** they share one content revision but are separate occurrences, each with its own scope, path, lines and snapshot.
5. **Given** a change to searchable metadata only, **When** the three hashes are computed, **Then** the chunk hash changes while the file hash and the embedding-input hash stay the same; each hash has a separately documented list of inputs.
6. **Given** a new version of the extractor, chunker, store mapping, embedding model or input template, **When** only that version changes, **Then** only the identities and hashes that depend on it change, and the others stay valid.
7. **Given** a query embedded with one embedding profile and an index built with another, **When** a vector search is requested, **Then** the contract rejects the request with an explicit profile-mismatch error instead of returning results.
8. **Given** any boundary operation, **When** it is cancelled or fails, **Then** the contract states that it ends with an explicit cancellation or a typed error, never with an empty "successful" result, and every result carries snapshot provenance.

### Edge Cases

- The installed TokenSave differs from the pinned build (for example after the devcontainer reinstalls "latest"): the check must fail before import and name the mismatch.
- The TokenSave schema has extra tables or columns that are not used: the documented rule decides whether this passes with a warning or fails; it is never ignored without a decision.
- Branch metadata exists but names a missing or unreadable database file.
- Branch metadata is malformed (invalid JSON, missing `db_file`).
- The search store runs a different 9.x version from the pinned one, or a 7.x/8.x version: the check reports it as unsupported and does not imply support.
- The search store is reachable but vector search is not available for the configured mapping.
- Network access is unavailable: verification and fixture preparation must still run offline once the pinned assets are present locally.
- A fixture file has a byte-order mark, CRLF line endings or a non-UTF-8 encoding, so line ranges and hashes must still be correct.
- A symbol cannot be resolved by the extractor (for example an extension method call or a DI-resolved interface): it is recorded as unresolved or ambiguous, not as a missing or wrong result.
- Partial classes spread across files and nested generic types with the same simple name must not collide in their logical keys.
- A rename combined with an edit in the same mutation.
- A documentation-only change, or a changed `applies_to` association, with no change to source bytes.
- A dirty worktree whose uncommitted edits are later reverted.

## Requirements *(mandatory)*

### Functional Requirements

**Platform verification and pinning (F01-01)**

- **FR-001**: The project MUST pin one specific TokenSave version or build, one specific Elasticsearch 9 version, and specific .NET, Python and toolchain versions, and MUST record them in one platform compatibility record.
- **FR-002**: The development environment MUST install the pinned TokenSave version instead of the latest release.
- **FR-003**: A compatibility check MUST detect the TokenSave version and the database schema actually present (tables, columns and any version marker) and compare them with the pinned expectations before any import.
- **FR-004**: The compatibility check MUST stop with an explicit, named error for every version or schema mismatch, and MUST NOT write anything into the TokenSave database.
- **FR-005**: Active-database resolution MUST use the database named in the branch metadata when present, MUST use the documented default database only when branch metadata is absent, and MUST fail explicitly when the metadata is malformed or names a missing database.
- **FR-006**: A local smoke test MUST exercise authenticated lexical (BM25) search and the supported vector search on the pinned search store using only synthetic documents.
- **FR-007**: The smoke test MUST NOT depend on licensed-only native ranking features (native rank fusion, native rerankers), and the compatibility record MUST list these as unsupported.
- **FR-008**: The compatibility record MUST document the C# symbol kinds and relationships extracted, companion-documentation association behavior, branch and worktree behavior, and the unsupported or ambiguous cases observed during verification.
- **FR-009**: The compatibility record MUST state license assumptions for TokenSave, the search store and the runtimes, and the offline and network assumptions (what needs network once, what must run offline, and how optional TokenSave uploads are disabled).
- **FR-010**: Documentation MUST NOT imply support for Elasticsearch versions other than the pinned one, and in particular no production support for 7.x or 8.x.

**Reproducible fixtures (F01-02)**

- **FR-011**: The project MUST provide a small synthetic C# test repository covering overloads, generics, partial classes, extension methods, interfaces, dependency-injection registration, async methods with cancellation, and relationships across files.
- **FR-012**: The synthetic repository MUST include sidecar Markdown, central Markdown with explicit file associations, at least one stale document, and files with different encodings and line endings.
- **FR-013**: A preparation procedure MUST take a sample path and a commit as inputs, MUST write all worktrees and indexes under `.index/`, and MUST leave the sample checkout unchanged. This applies equally to the committed public corpora under `testrepo/`.
- **FR-014**: The preparation procedure MUST support approved, pinned public corpora as well as the synthetic repository, and MUST record the source, commit and license of each fixture so it can be rebuilt.
- **FR-015**: The project MUST provide deterministic, named mutations for add, edit, delete, rename, line shift, documentation-only edit and changed documentation association. Applying a mutation to the same starting commit MUST always give the same result.
- **FR-016**: The fixtures MUST support at least two worktrees of the same repository, at least one of them with uncommitted edits, kept isolated from each other.
- **FR-017**: Each fixture MUST have recorded expectations for symbols, source extents and relationships, including the cases expected to be unresolved or ambiguous.
- **FR-018**: Private sample URLs, sample content, local sample inventories, gitlinks and generated data MUST NOT be committed. The only committed corpora are the approved public ones under `testrepo/` (AGENTS.md, "Public test corpora").

**Versioned contracts and boundaries (F01-03)**

- **FR-019**: The project MUST define typed contracts for source scope, immutable file revision, chunk occurrence, embedding profile, publication batch and cited search result.
- **FR-020**: Logical keys MUST be derived from repository/sample, path, qualified name and kind, with signature or ordinal disambiguation for overloads and fragments. Line numbers and TokenSave identifiers MUST be kept only as provenance.
- **FR-021**: Content revisions and scope-specific occurrences MUST be distinct, and the behavior on rename and on line movement MUST be documented and covered by identity tests using the fixture mutations.
- **FR-022**: File hash, chunk hash and embedding-input hash MUST each have a separately specified list of inputs.
- **FR-023**: Extractor, chunker, store mapping, embedding model and embedding input-template versions MUST be recorded separately so that each can change without invalidating identities that do not depend on it.
- **FR-024**: The project MUST define responsibility boundaries for exporter, chunker, store, embedder, retriever, fusion, reranker and context selector, each with a stated single responsibility and an explicit disabled mode for optional inference stages.
- **FR-025**: The store boundary MUST support Elasticsearch now and a later small SQLite baseline without changes to callers. Provider-specific options MUST NOT appear in the chunking or orchestration contracts.
- **FR-026**: Every contract operation MUST support cancellation, report failures as explicit typed errors, and carry snapshot provenance in its results.
- **FR-027**: A vector search MUST be rejected with an explicit error when the query's embedding profile does not match the profile of the index being searched.
- **FR-028**: Boundaries MUST be defined without component implementations or abstractions that nothing uses yet, apart from what the compatibility check and the identity tests need.

### Key Entities

- **Platform compatibility record**: the pinned versions of each tool and runtime, the expected TokenSave schema, observed extraction capabilities and limits, unsupported features, license assumptions and offline/network assumptions.
- **Fixture**: a synthetic or approved public repository at a pinned commit, with its provenance (source, commit, license), its named mutations and its expectation records.
- **Expectation record**: for one fixture state, the expected symbols with kinds and source extents, relationships, documentation associations and the cases known to be unresolved or ambiguous.
- **Source scope**: authorization boundary, repository ID, branch ref, worktree ID and snapshot ID.
- **File revision**: an immutable version of one file, identified by its raw-content hash, with path, language, symbol outline, documentation associations and extractor profile.
- **Chunk occurrence**: one searchable unit with a stable logical symbol identity, kind, ordinal/extent, its file revision, scope and snapshot, location provenance and documentation links.
- **Embedding profile**: provider, model, immutable revision, query and document formatting, tokenizer and maximum length, dimension, pooling and normalization.
- **Publication batch**: the set of changes published together for one scope and snapshot, with the versions of every profile involved.
- **Cited search result**: occurrence ID, authorized snapshot, path and lines, snippet, source revision, and the contributing strategies with their scores.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the deliberately introduced version and schema mismatches (at least one per mismatch type: wrong version, missing table, missing column, malformed metadata, missing database file) are detected before any import, each with a message that names the mismatch.
- **SC-002**: On a clean environment, a maintainer can run the full platform verification and the smoke test in under 10 minutes, without network access once the pinned assets are present locally.
- **SC-003**: Preparing a fixture twice from the same sample path and commit, and applying the same mutation twice, gives identical results in 100% of runs.
- **SC-004**: After any preparation or mutation run, the sample checkout has zero added, changed or deleted files.
- **SC-005**: 100% of the expected symbols, extents and relationships in the expectation records are checked by tests, and every known unresolved or ambiguous case is reported as such.
- **SC-006**: Identity tests cover all seven mutation types and show the expected keep/change outcome for logical key, content revision, occurrence and each of the three hashes, with no unexpected change.
- **SC-007**: A review of the repository history finds zero committed private sample URLs, sample content, inventories, gitlinks or generated data.
- **SC-008**: A new maintainer can determine the supported version of every pinned component, and why each unsupported feature is unsupported, from the compatibility record alone in under 5 minutes.

## Constitution Check

| Principle | How this feature complies |
|---|---|
| I. Local-only sample data | Smoke tests use synthetic documents on the local, localhost-bound store. Verification must run offline after the one-time pinned download, and optional TokenSave uploads are documented as disabled (FR-009). No sample content is committed (FR-018). |
| II. Evidence before adoption | The feature replaces plan claims with observed behavior (FR-003, FR-008). It chooses no model and no ranking setting. |
| III. Reproducible, pinned runs | All tools and runtimes are pinned (FR-001, FR-002). Preparation takes sample path and commit and writes only under `.index/` (FR-013). Fixture provenance is recorded (FR-014). |
| IV. Stable identity | Logical keys exclude line numbers and TokenSave IDs (FR-020). The three hashes are separate (FR-022). Profiles are versioned independently (FR-023). Profile mismatch is rejected (FR-027). |
| V. Isolated contracts | TokenSave is accessed read-only with explicit schema detection (FR-003, FR-004). The store boundary allows an SQLite baseline (FR-025). Inference stages have a disabled mode (FR-024). Native licensed fusion is not used (FR-007). |
| VI. Scope-correct, honest retrieval | Occurrences keep scope, path, lines and snapshot per scope (Story 3, scenario 4). Unresolved and ambiguous relationships are preserved (FR-017). Results carry citations and provenance (FR-019, FR-026). |
| VII. Staged simplicity | No pipeline, store or inference implementation is delivered, and unused abstractions are forbidden (FR-028). |

## Assumptions

- The target environment is the existing devcontainer (Docker Compose with the Elasticsearch service). Verification on other host setups is not required.
- The Elasticsearch version currently in the devcontainer (9.5.5) is the starting candidate for the pin, and is confirmed or replaced by the verification.
- TokenSave is currently installed as the latest release. Pinning it to a specific release is part of this feature.
- "Approved public corpora" means public repositories with a license that permits local use. This feature delivers the preparation procedure for them; choosing and approving specific corpora belongs to the evaluation work (F06). The synthetic repository is the required fixture.
- Contracts are delivered as written definitions plus the minimal typed definitions needed for the identity tests, not as working pipeline components.
- Authorization in this feature only means that the source scope contract carries an authorization boundary. Enforcing authorization is covered by F02.
- Story 3 depends on Story 1 (F01-03 depends on F01-01). Story 2 has no dependency and can proceed in parallel.
- The feature is complete only when all three child stories and the parent feature acceptance criteria are met, in line with the backlog rule that a parent closes only after its children.

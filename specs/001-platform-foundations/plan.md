# Implementation Plan: Verified Platform Foundations

**Branch**: `001-platform-foundations` | **Date**: 2026-10-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-platform-foundations/spec.md`

## Summary

Replace three assumptions with evidence and tested contracts:

1. **Pinned, verified platform (P1).** Pin TokenSave 7.16.0, Elasticsearch 9.5.5 (Basic), .NET 10 and Python 3.12. A .NET compatibility check resolves the active TokenSave database, compares the binary version, the index version and the schema with an embedded expectation, and stops before any import on a mismatch. A smoke test exercises authenticated BM25 and `knn` on synthetic documents, without licensed retrievers. All observations go into `docs/platform-compatibility.md`.
2. **Reproducible fixtures (P2).** A small synthetic C# repository stored as plain files and materialized as a git repository with fixed dates, so its commit SHA is deterministic. It has declarative named mutations and JSON expectation records. A preparation command clones any sample at a given commit into `.index/work/<name>/` without touching the source. A committed, pinned public corpus (`testrepo/cisharpai`) adds real-code tests.
3. **Versioned identities and boundaries (P3).** Minimal C# records for identities, the three hashes, profile versions, typed errors and snapshot provenance. Identity tests run across all seven mutations. Component boundaries are written contracts only, with no unused interfaces.

Decisions and their rationale are in [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10. The SDK is pinned in `global.json` (10.0.x, roll forward to the latest patch). Python 3.12 is recorded in the compatibility record but this feature contains no Python code.

**Primary Dependencies**:
- `Microsoft.Data.Sqlite`, used read-only.
- `Elastic.Clients.Elasticsearch` 9.x, matching the server pin.
- `System.CommandLine` for the `polifemo` CLI.
- xUnit for tests.
- External tools: the pinned `tokensave` 7.16.0 binary and `git`.

**Storage**:
- The TokenSave SQLite database, opened read-only.
- Local Elasticsearch 9.5.5, used only for synthetic smoke documents.
- Generated worktrees and indexes go under `.index/` (git-ignored).

**Testing**:
- xUnit unit tests: hashing, identities, resolver, schema comparison on synthesized databases.
- Integration tests that need the pinned TokenSave: fixture expectations, identity across mutations, the cisharpai corpus.
- Elasticsearch smoke tests: they run only when `ELASTICSEARCH_URL` is set.

**Target Platform**: the devcontainer (Linux, CPU only, Docker Compose with Elasticsearch). macOS hosts may run the unit tests but are not a supported verification target.

**Project Type**: a .NET class library plus a CLI, with test projects.

**Performance Goals**: full platform verification plus the smoke test run in under 10 minutes on a clean environment (SC-002). Fixture preparation and mutation take a few seconds.

**Constraints**:
- Offline once the pinned assets are present.
- Never write into `samples/`, `testrepo/` or the TokenSave database.
- No sample content in logs, and no credentials in errors.
- No licensed Elasticsearch features.

**Scale/Scope**:
- The synthetic fixture has about 15–25 files.
- The cisharpai corpus has 445 `.cs` files, about 65k LOC.
- Seven mutation types.
- Eight boundary contracts, all written.

All items are resolved; no NEEDS CLARIFICATION remains.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / gate | Assessment | Status |
|---|---|---|
| I. Local-only sample data | The smoke test uses synthetic documents on the localhost-bound store. TokenSave runs locally, and `disable-upload-counter` and `report_savings: false` are applied in postcreate and recorded. Offline operation is verified with networking disabled (quickstart step 6). No private sample content is committed. The cisharpai corpus is public and is covered by the amended rule below. | PASS |
| II. Evidence before adoption | Expectations come from observed extractor behavior. No model or ranking choice is made. Unsupported cases are recorded, not hidden. | PASS |
| III. Reproducible, pinned runs | TokenSave is pinned by tag and sha256, Elasticsearch by image tag, .NET by `global.json`, and Python by the devcontainer feature. Preparation takes a sample path and commit and writes only under `.index/`. The fixture commit SHA is deterministic. The corpus is pinned by a submodule gitlink and `corpora.json`. **Amendment required**: a "Public test corpora" rule for `testrepo/` (research R11), made in AGENTS.md and the constitution (v1.1.0) in the same change. | PASS (with amendment) |
| IV. Stable identity | Logical keys exclude line numbers and TokenSave IDs. The three hashes have separate inputs. Profile versions are recorded independently. A profile mismatch is rejected. Publication itself is out of scope. | PASS |
| V. Isolated contracts | There is one read-only TokenSave adapter with explicit schema detection. The store boundary is written for Elasticsearch and SQLite, and the inference boundaries have a disabled mode. The RRF retriever and native rerankers are not used. | PASS |
| VI. Scope-correct, honest retrieval | Occurrences carry their own scope, path, lines and snapshot. Unresolved and ambiguous relations are preserved in expectations. Results carry `SnapshotProvenance`. | PASS |
| VII. Staged simplicity | No pipeline, store or inference implementation. Code types exist only where the check, the CLI or the tests use them (FR-028). Each new folder is required by a deliverable. | PASS |
| Required sync/publication coverage | Identity tests cover add, edit, delete, rename, line shift, docs-only edit and changed association. Changed excludes, profile versions, concurrent sync and publication crashes belong to F04, since no publication is delivered here. | PASS (scoped) |
| Correctness gates | Cross-scope leakage: the occurrence key includes the scope (tested). Half-published snapshots and recovery: N/A because there is no publication. | PASS (scoped) |

**Post-design re-check (after Phase 1)**: PASS. data-model.md and the contracts add no component implementation. Every type listed in [contracts/identities-and-hashes.md](./contracts/identities-and-hashes.md) is used by a test or the CLI. Writing anything into TokenSave, `samples/` or `testrepo/` is forbidden by [contracts/cli.md](./contracts/cli.md).

## Project Structure

### Documentation (this feature)

```text
specs/001-platform-foundations/
├── plan.md              # This file
├── research.md          # Phase 0 decisions R1–R11
├── data-model.md        # Entities, fields, validation, mutation × identity matrix
├── quickstart.md        # Runnable validation scenarios for SC-001…SC-008
├── contracts/
│   ├── cli.md
│   ├── tokensave-schema.md
│   ├── identities-and-hashes.md
│   ├── boundaries.md
│   └── expectation-record.schema.json
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks; not created here)
```

### Source Code (repository root)

```text
global.json
Polifemo.slnx
.gitattributes                         # * text=auto eol=lf; src/fixtures/** -text
src/
├── Polifemo.Contracts/                 # LogicalKey, OccurrenceKey, hashes, ProfileVersions,
│                                      # EmbeddingProfile, SnapshotProvenance, PolifemoException tree
├── Polifemo.Platform/
│   ├── TokenSave/                     # ActiveDatabaseResolver, TokenSaveReader (read-only),
│   │                                  # CompatibilityChecker, expected-schema.v7.16.0.json (embedded)
│   ├── Elasticsearch/                 # SmokeTest (BM25 + knn, license/version probe)
│   └── Fixtures/                      # FixtureBuilder, SamplePreparer, MutationApplier
├── Polifemo.Cli/                       # `polifemo platform check|smoke`, `polifemo fixture build|prepare|mutate`
├── Polifemo.Tests/                    # single test project; first-level folders mirror the section tested
│   ├── Support/                       # TestWorkspace, RequireTokenSave
│   ├── Contracts/                     # hashing, keys, profile mismatch, identity matrix over mutations
│   ├── Platform/                      # resolver, schema mismatch (synthesized DBs), preparation
│   │                                  # isolation, mutation determinism, fixture expectations, ES smoke
│   ├── Cli/                           # exit codes, command output contracts
│   └── Corpus/                        # cisharpai: curated, invariants, snapshot
│       └── Expectations/cisharpai/
└── fixtures/synthetic-csharp/
    ├── src/ docs/ tokensave-docs/     # fixture content (excluded from compilation)
    ├── mutations/<name>/mutation.json # + payload files
    ├── expectations/<state>.json
    └── PROVENANCE.md
testrepo/
├── corpora.json                       # url, commit, license, approval date
└── cisharpai/                         # committed public submodule, pinned
docs/platform-compatibility.md         # the compatibility record
.devcontainer/postcreate.sh            # pinned TokenSave + sha256, disable-upload-counter, submodule init
```

**Structure Decision**: one solution with two libraries, one CLI and one test project (`src/Polifemo.Tests`, with first-level folders named after the section tested). Contracts are separate from Platform so that identity code has no dependency on SQLite or Elasticsearch, as FR-025 requires for provider isolation. The fixture content lives outside every project directory, so its `.cs` files are never compiled.

## Issue Traceability (GitHub #5 and children)

Each acceptance criterion of the source issues, mapped to the plan element that satisfies it.

| Issue | Acceptance criterion | Plan element |
|---|---|---|
| #2 F01-01 | Compatibility checks identify schema/version mismatches before import | R1, R2; `platform check`; [tokensave-schema.md](./contracts/tokensave-schema.md); quickstart step 2 |
| #2 | Active DB resolution via branch metadata and absent-metadata fallback verified | R3; resolver tests; quickstart step 2 |
| #2 | Smoke test: authenticated BM25 + supported vector APIs, no licensed RRF/reranker | R4; `platform smoke`; quickstart step 3 |
| #2 | C# extraction, companion docs, branch/worktree behavior recorded incl. unsupported/ambiguous | R7, R11; compatibility record sections `extraction`, `branchBehavior`, `unsupported` |
| #2 | Devcontainer/runtime pins and offline/network assumptions documented; no 7/8 support implied | R1, R4; `postcreate.sh`, `global.json`; compatibility record `pins`, `network`; quickstart step 6 |
| #2 (scope) | Inspect available languages | The compatibility record lists the TokenSave language list (from `metadata.language_detection`); only C# and Markdown are supported targets |
| #3 F01-02 | Preparation takes sample path + commit, writes only under `.index/` | R5; `fixture prepare`; quickstart step 4 |
| #3 | Deterministic mutations: add/edit/delete/rename/line shift, docs-only, changed association | R6; [data-model.md](./data-model.md) Mutation; quickstart step 4 |
| #3 | Known expected symbols, extents, relationships incl. unresolved/ambiguous and encoding/line-ending variants | R7; [expectation-record.schema.json](./contracts/expectation-record.schema.json); R11 corpus tests |
| #3 | No private URLs/contents/inventories/gitlinks/generated data committed; provenance reproducible | R11 policy amendment; `PROVENANCE.md`, `corpora.json`; quickstart step 7 |
| #3 (scope) | Two worktrees and dirty edits | R5 (`git worktree add` inside the clone); quickstart step 4 |
| #4 F01-03 | Stable logical keys with overload/fragment disambiguation; lines and TokenSave IDs are provenance | R8; [identities-and-hashes.md](./contracts/identities-and-hashes.md) |
| #4 | Content revisions vs scope-specific occurrences; rename and line-movement identity tests | data-model matrix; identity tests |
| #4 | Separately specified file, chunk and embedding-input hash inputs | R8; identities-and-hashes.md |
| #4 | Extractor/chunker/mapping/model/input-template versions evolve independently | data-model ProfileVersions table |
| #4 | Store supports ES and a later SQLite baseline; no provider leakage | [boundaries.md](./contracts/boundaries.md) Store |
| #4 | Cancellation, explicit errors, snapshot provenance, matching embedding profiles | R9; boundaries.md; `EnsureSameSpace` test |
| #5 F01 | Supported versions, languages and license assumptions explicit | compatibility record |
| #5 | Repeatable fixtures exercise C# edge cases, docs and dirty worktrees | #3 rows above |
| #5 | Identity, provenance, profile versioning and store/inference boundaries defined | #4 rows above |
| #5 (out of scope) | Production search, hosted providers, choosing a model winner | Not planned; see Constitution Check I and VII |

**Additions beyond the issues** (they come from the user's request during planning, not from #2–#5): the `testrepo/cisharpai` real-code corpus and the policy amendment it needs (R11). These fall under #3's "approved pinned public corpora" scope, but committing the submodule goes beyond what #3 states.

## Requirement Traceability

| Requirement | Where it is addressed |
|---|---|
| FR-001, FR-002, FR-009, FR-010 | research R1, R4; `docs/platform-compatibility.md`; `postcreate.sh` |
| FR-003, FR-004 | research R1, R2; [contracts/tokensave-schema.md](./contracts/tokensave-schema.md); `platform check` |
| FR-005 | research R3; [contracts/cli.md](./contracts/cli.md) error codes |
| FR-006, FR-007 | research R4; `platform smoke` |
| FR-008 | research R7; compatibility record sections; fixture expectations |
| FR-011, FR-012 | research R6, R7; `src/fixtures/synthetic-csharp` |
| FR-013, FR-014, FR-016 | research R5, R11; `fixture prepare`; `testrepo/corpora.json` |
| FR-015 | research R6; [data-model.md](./data-model.md) Mutation |
| FR-017 | [contracts/expectation-record.schema.json](./contracts/expectation-record.schema.json) |
| FR-018 | `.gitignore`; quickstart step 7 |
| FR-019 to FR-023, FR-027 | [data-model.md](./data-model.md); [contracts/identities-and-hashes.md](./contracts/identities-and-hashes.md) |
| FR-024 to FR-026, FR-028 | [contracts/boundaries.md](./contracts/boundaries.md) |

## Complexity Tracking

No unjustified violations. The `testrepo/` committed submodule is a new data category that needs a policy amendment rather than a complexity exception. It is tracked in research R11 and is delivered together with the code.

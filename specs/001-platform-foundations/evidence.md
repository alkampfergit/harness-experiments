# Evidence: acceptance criteria and success criteria

Where each criterion of issues #2 to #5 and each success criterion of the spec is shown. Test names are
in `src/Polifemo.Tests/`. The runs that need the rebuilt devcontainer are marked **open**.

## Issue criteria

| Issue | Criterion | Evidence |
|---|---|---|
| #2 | Compatibility checks identify schema and version mismatches before import | `Platform/TokenSave/CompatibilityCheckerTests` (every code, all defects reported at once), `TokenSaveGraph.LoadAsync` refuses to read nodes or edges unless the check passes; `Cli/PlatformCommandTests` |
| #2 | Active database resolution and the absent-metadata fallback | `Platform/TokenSave/ActiveDatabaseResolverTests` (branch entry, default branch entry, absent, malformed, no usable entry, missing file, escaping path) |
| #2 | Smoke test: authenticated BM25 and the supported vector API, no licensed retrievers | `Platform/Elasticsearch/SmokeTestTests` (real store: version, license, bm25, knn, cleanup; stub store: auth, version, license, unreachable). Basic rejects RRF and the reranker: `docs/platform-compatibility.md` section 3 |
| #2 | C# extraction, companion docs, branch and worktree behavior, unsupported and ambiguous cases recorded | `docs/platform-compatibility.md` section 7; `src/fixtures/synthetic-csharp/expectations/*.json` |
| #2 | Runtime pins, offline and network assumptions documented; no 7.x or 8.x support implied | `docs/platform-compatibility.md` sections 1 and 5; `.devcontainer/postcreate.sh`; `global.json`. Offline run **open** (T060) |
| #2 | Available languages inspected | `docs/platform-compatibility.md` section 6 |
| #3 | Preparation takes a sample path and commit and writes only under `.index/` | `Platform/Fixtures/SamplePreparerTests`; `Corpus/CisharpaiTests.Preparing_and_indexing_leave_the_corpus_untouched` |
| #3 | Deterministic mutations: add, edit, delete, rename, line shift, docs-only, changed association | `Platform/Fixtures/MutationApplierTests` (all eight, byte-identical results, two independent clones) |
| #3 | Known symbols, extents and relationships including unresolved and ambiguous, and encoding and line-ending variants | `Platform/Fixtures/ExpectationTests` (9 states, extents re-checked in the raw bytes, schema validation); `Corpus/CisharpaiTests.CuratedSymbolsExtracted`, `CuratedRelationships` |
| #3 | Nothing private, no private URLs, no gitlinks except approved ones, provenance reproducible | `Corpus/CisharpaiTests.Only_public_corpora_under_testrepo_are_committed_as_submodules`, `PinnedCommitMatchesProvenance`; `src/fixtures/synthetic-csharp/PROVENANCE.md`; `testrepo/corpora.json`; hygiene pass of 2026-10-24 |
| #3 | Two worktrees and dirty edits | `Platform/Fixtures/WorktreeIsolationTests` |
| #4 | Stable logical keys with overload and fragment disambiguation; lines and TokenSave ids are provenance | `Contracts/LogicalKeyTests`; `Contracts/IdentityMatrixTests.Overloads_partial_classes_and_nested_generics_have_their_own_keys` |
| #4 | Content revisions versus scope-specific occurrences; rename and line-movement tests | `Contracts/IdentityMatrixTests` (rename, line shift, two scopes) |
| #4 | Separately specified file, chunk and embedding-input hash inputs | `contracts/identities-and-hashes.md`; `Contracts/HashingTests` (known answers computed independently) |
| #4 | Extractor, chunker, mapping, model and template versions evolve independently | `Contracts/EmbeddingProfileTests.Changing_one_version_changes_only_the_hashes_that_depend_on_it` |
| #4 | Store supports Elasticsearch and a later SQLite baseline; no provider leakage | `contracts/boundaries.md`; `Contracts/BoundaryHygieneTests` (no interfaces, no provider names in the contracts, no dependency on SQLite or Elasticsearch) |
| #4 | Cancellation, typed errors, snapshot provenance, matching embedding profiles | `Platform/Processes/ProcessRunnerTests`, `Cli/ExitCodeTests`, `Cli/ResultProvenanceTests`, `Contracts/EmbeddingProfileTests` (`EMB_PROFILE_MISMATCH`) |
| #5 | Supported versions, languages and license assumptions explicit | `docs/platform-compatibility.md` |
| #5 | Repeatable fixtures exercise C# edge cases, docs and dirty worktrees | rows of #3 |
| #5 | Identity, provenance, profile versioning and store/inference boundaries defined | rows of #4 |

## Success criteria

| Criterion | Status | Evidence |
|---|---|---|
| SC-001 detect every mismatch type before import | met | `CompatibilityCheckerTests`, `ActiveDatabaseResolverTests` |
| SC-002 full verification and smoke test under 10 minutes, offline | **open** | The full suite runs in about 30 s on the host. The timed offline run in the devcontainer is T060 |
| SC-003 preparation and mutation are repeatable | met | `FixtureBuilderTests`, `MutationApplierTests` |
| SC-004 zero changes to the sample checkout | met | `SamplePreparerTests`, `CisharpaiTests.Preparing_and_indexing_leave_the_corpus_untouched` |
| SC-005 every expectation is checked, unresolved and ambiguous cases are reported as such | met | `ExpectationTests`, `ExpectationComparer` |
| SC-006 identity tests cover all seven mutation types | met | `Contracts/IdentityMatrixTests` |
| SC-007 nothing private in the history | met on the current tree | hygiene pass; the only gitlink is `testrepo/cisharpai` |
| SC-008 a newcomer finds every pin and the reason for each unsupported feature in 5 minutes | met in the document | `docs/platform-compatibility.md` sections 1, 3 and 7 |

## Deviations from the plan

- **Single test project** (`Polifemo.Tests`, folders named after the section tested) instead of three, at the user's request.
- **`HttpClient` instead of `Elastic.Clients.Elasticsearch`** in the smoke test: it needs raw JSON bodies and exact status handling, and the typed client adds nothing here.
- **Fixture content under `src/fixtures/`**, not `tests/fixtures/`, after the test projects moved into `src/`.
- **Identity tests read the expectation records and the fixture bytes**, not a TokenSave database, so they need no TokenSave. TokenSave itself is verified by the `Category=TokenSave` tests.
- **Signature normalization cuts expression bodies**, because TokenSave puts them into the signature (found on real data).
- **Hung runs fixed:** the process runner closes the child's stdin and enforces a timeout, because `tokensave init` blocked on an inherited terminal.

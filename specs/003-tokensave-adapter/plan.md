# Implementation Plan: Read-Only, Version-Checked TokenSave Adapter

**Branch**: `003-tokensave-adapter` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-tokensave-adapter/spec.md` (issue #10, F03-01, child of #13)

## Summary

F01 already delivered the resolver (`ActiveDatabaseResolver`), the pinned schema check (`CompatibilityChecker`, `expected-schema.v7.16.0.json`), the read-only connection (`TokenSaveReader`) and a row loader (`TokenSaveGraph.LoadAsync`). This feature turns those pieces into the one adapter the constitution asks for: a `TokenSaveAdapter` that returns a `GraphSnapshot` with a revision identity, typed doc associations, preserved unresolved/ambiguous relationships, a documented snapshot/locking strategy, source-versus-graph verification with the approved bounded retry (3 retries, then an explicit error), and actionable errors for corrupt or locked databases. It also closes the absent-metadata rule the spec records (exactly one candidate database, otherwise fail) and proves by test that no TokenSave timestamp is read.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK `10.0.402`, `global.json`)

**Primary Dependencies**: `Microsoft.Data.Sqlite` 10.0.13 (already referenced); no new package

**Storage**: TokenSave SQLite database under `<worktree>/.tokensave/`, opened `Mode=ReadOnly`; nothing written anywhere by this feature

**Testing**: xUnit v3 in `src/Polifemo.Tests` (`Platform/TokenSave/`, `Corpus/`), synthetic databases from `SyntheticTokenSave`, pinned `tokensave` 7.16.0 for the real-sync tests (fail, never skip, when missing)

**Target Platform**: Linux devcontainer (CPU only)

**Project Type**: library (`Polifemo.Platform`) + CLI (`polifemo`)

**Performance Goals**: reading the cisharpai corpus graph (602 files, ~4.4k edges) plus source verification in under 2 s on the devcontainer; no measured target beyond "not slower than the F01 loader by more than the hashing cost"

**Constraints**: zero warnings, `tools/sonar-local.sh` reports 0 findings, store byte-identical after every read (empty `-wal`/`-shm` sidecars excepted, see research R2), no raw source or query text in errors

**Scale/Scope**: one worktree, one database per read; corpora up to a few thousand files

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design (no change).*

| Principle | Result |
|---|---|
| I Local-Only (NON-NEGOTIABLE) | Everything runs in-process against local files. No network. `TS_UPLOAD_ENABLED` remains a hard mismatch. Errors name relative paths, codes and counts only; `SafeMessage` scrubs them (R7). |
| II Evidence | No ranking, weight or heuristic is introduced. The only numeric choices are the retry limit fixed by the approved spec (3) and a lock timeout (5 s) recorded as a setting, not a tuned value. |
| III Pinned runs | Supported set is `{PlatformPins.TokenSave}` = 7.16.0 for both the binary and `metadata.last_full_index_version`; the schema expectation stays the embedded 7.16.0 resource. Tests run on `.index/test/` copies and the pinned `testrepo/cisharpai`. |
| IV Identity and publication | Node ids and line numbers stay provenance (`SymbolRow.Id`, lines). `GraphRevision` derives from file paths and content hashes only (R4). No TokenSave timestamp is read (R6, guarded by test). A snapshot pairs one graph revision with verified source bytes or fails (R5). Publication itself is F04; of the required sync coverage this feature owns **concurrent sync** and the "graph changes between reads" case; additions/edits/deletions/renames/line shifts/docs-only/excludes/profile versions/crash boundaries are owned by F03-02 and F04 and are listed as not applicable here. |
| V Isolated contracts | `TokenSaveAdapter` is the only public door; `TokenSaveReader` stays `internal`; table and column names appear only under `src/Polifemo.Platform/TokenSave/`. Consumers (`ExpectationObserver`, `CisharpaiCorpus`, fixture tests) move to the adapter. |
| VI Scope-correct, honest retrieval | Unresolved and ambiguous relationships are returned as such, with candidate lists; `EdgeRow.ResolvedBy` is kept; doc associations are file-level, as TokenSave defines them. No scope filtering is involved (the adapter does not serve queries). |
| VII Staged simplicity | No new project, package, service or folder besides `specs/003-tokensave-adapter/contracts/`. One small CLI subcommand (`platform snapshot`) reuses the existing `platform` command and output path. |

Correctness gates: zero cross-scope leakage and half-published snapshots do not apply (nothing is published); deterministic recovery is addressed by "a read either returns one complete revision or throws" (SC-003).

## Project Structure

### Documentation (this feature)

```text
specs/003-tokensave-adapter/
├── spec.md
├── plan.md                      # this file
├── research.md                  # R1-R8
├── data-model.md
├── quickstart.md
├── contracts/tokensave-adapter.md
├── checklists/requirements.md
└── tasks.md                     # /speckit-tasks output (next gate)
```

### Source Code (repository root)

```text
src/Polifemo.Contracts/Errors/PolifemoException.cs        # + TokenSaveSnapshotException (new codes)
src/Polifemo.Platform/TokenSave/
├── ActiveDatabaseResolver.cs                             # absent metadata: exactly one candidate, else TS_META_ABSENT_AMBIGUOUS; MetadataAbsent flag
├── CompatibilityChecker.cs                               # unchanged rules; CheckOpenAsync reused inside the snapshot transaction
├── TokenSaveReader.cs                                    # + lock timeout, DocAssociations(), corrupt/locked mapping; stays internal
├── TokenSaveGraph.cs                                     # rows only; LoadAsync removed (replaced by the adapter)
├── TokenSaveAdapter.cs                                   # NEW: ReadAsync -> GraphSnapshot, bounded retry, source verification
├── GraphSnapshot.cs                                      # NEW: snapshot, revision, provenance, integrity, options
└── SourceVerifier.cs                                     # NEW: sha256 of worktree files vs files.content_hash
src/Polifemo.Cli/Commands/PlatformCommands.cs             # + `platform snapshot --worktree` (counts, revision, provenance; typed errors)
src/Polifemo.Tests/Platform/TokenSave/
├── ActiveDatabaseResolverTests.cs                        # + absent-metadata single/ambiguous cases
├── TokenSaveAdapterTests.cs                              # NEW: happy path, empty graph, doc associations, every failure code, retry success/exhaustion, store unchanged, determinism
├── SnapshotStrategyTests.cs                              # NEW: real `tokensave sync` racing reads on the synthetic fixture; no mixed revision
└── NoTimestampReadTests.cs                               # NEW: adapter SQL and snapshot type carry no TokenSave time fields
src/Polifemo.Tests/Support/SyntheticTokenSave.cs          # + row builders (files, nodes, edges, docs, unresolved, ambiguous), corrupt file, writer lock
src/Polifemo.Tests/Support/ExpectationObserver.cs         # consume GraphSnapshot.DocAssociations
src/Polifemo.Tests/Corpus/CisharpaiCorpus.cs              # use TokenSaveAdapter
docs/platform-compatibility.md                            # + section "Snapshot and locking strategy" (FR-008 documentation)
```

**Structure Decision**: single solution, existing three projects and one test project; the adapter stays in `Polifemo.Platform/TokenSave/` where F01 put the TokenSave knowledge. No new project.

## Complexity Tracking

No constitution violation to justify. Two deliberate limits are recorded instead of hidden:

| Limit | Why accepted | Where stated |
|---|---|---|
| Source verification checks graph → disk only (every indexed file must exist with the recorded hash); files on disk that TokenSave skipped are not detected | Reproducing TokenSave's exclude rules would duplicate its logic; unindexed files are a F03-02 concern (canonical files) | research R5, contract |
| Dangling edges/references (an edge whose node is missing) are counted in `IntegrityReport`, not treated as corruption | The schema uses `ON DELETE CASCADE`, so dangling rows signal a TokenSave defect, not a broken file; failing would block real corpora on a vendor bug | research R3, contract |

# Code retrieval implementation backlog

**Status:** proposed, unimplemented issue drafts. No GitHub issues have been created.

Derived from the [consolidated plan](../plan_phase_1.md). Read the
[technology overview](../technology.md) for architectural principles and current
repository constraints. This backlog covers the whole plan, including later
phases; its filename does not limit the work to the first BM25 release.

## How to use these files

Each feature file is a parent issue. Each child file is a separately actionable
implementation or research issue. Stable IDs such as `F04-03` are local planning
identifiers, **not GitHub issue numbers**.

1. Create parent issues using their heading as the title and remaining Markdown
   as the body. Suggested labels are metadata, not labels already created.
2. Create each child issue the same way and attach it using GitHub's native
   sub-issue relationship, if available. Otherwise use linked task lists.
3. Replace local parent, child and dependency links in the published bodies with
   actual GitHub issue URLs. Keep a local ID-to-URL mapping when importing.
   Do not paste relative file links into GitHub issues unchanged.
4. Translate `Depends on` into explicit blocking relationships or linked blockers.
   A parent is grouping, not a prerequisite: it closes only after its children and
   feature-level acceptance criteria are complete.
5. Do not import deferred research as committed delivery scope. In particular,
   hosted processing remains blocked by current repository policy.

No publishing, commits, implementation or dependency installations are part of
this documentation task.

### Common definition of done for active implementation children

- Meet the child acceptance criteria and relevant technology/privacy principles.
- Add focused tests for success, invalid input and failure/recovery where relevant.
- Pass affected builds/type checks/tests using existing repository tooling.
- Update directly related usage/experiment documentation.
- Keep artifacts and caches ignored; never modify sample source or publish private
  sample metadata. Tests use synthetic or approved local fixtures.
- Report errors explicitly; optional degradation must identify the failed stage.

Research issues instead finish with a reproducible experiment, recorded results
(including negative results) and an explicit adopt/defer/reject decision.

## Feature hierarchy and sequencing

| Feature | Outcome | Children | Delivery gate |
|---|---|---|---|
| [F01 Foundations](./F01-foundations.md) | Verified versions, fixtures and contracts | F01-01 through F01-03 | M0 |
| [F02 Privacy and scope](./F02-privacy-and-scope.md) | Local-only policy, authorization and safe data handling | F02-01 through F02-03 | M0; required before data processing |
| [F03 Extraction and chunking](./F03-extraction-and-chunking.md) | Canonical files, docs and deterministic chunks | F03-01 through F03-03 | M1 |
| [F04 Incremental publication](./F04-incremental-publication.md) | Recoverable, consistent searchable snapshots | F04-01 through F04-04 | M1 |
| [F05 Baseline search](./F05-baseline-search.md) | Scoped BM25, structural navigation, CLI/API/MCP | F05-01 through F05-03 | M1 |
| [F06 Evaluation](./F06-evaluation.md) | Gold set, metrics and lexical baseline | F06-01 through F06-03 | M1; baseline before optimization |
| [F07 Local hybrid search](./F07-local-hybrid-search.md) | Local embeddings, reusable cache, dense search and RRF | F07-01 through F07-03 | M2 |
| [F08 Local reranking](./F08-local-reranking.md) | Optional bounded cross-encoder stage | F08-01 through F08-03 | M3; evidence-gated |
| [F09 Context assembly](./F09-context-assembly.md) | Bounded graph/docs context with citations | F09-01 through F09-03 | M3; evidence-gated |
| [F10 Operations](./F10-operations.md) | Rebuild, retention, diagnostics and scale evidence | F10-01 through F10-03 | M4; minimum diagnostics in M1 |
| [F11 Deferred research](./F11-deferred-research.md) | Explicitly gated product/research extensions | F11-01 through F11-04 | Not on the release critical path |

M1 is not complete until additions, deletions, docs changes, dirty-worktree isolation
and crash recovery pass, and a reproducible BM25 baseline exists. M2 adds inference
without making it mandatory. M3 stages remain independently switchable. Operational
basics belong in their owning children from the start; M4 adds lifecycle and scale
hardening, not permission to postpone correctness or safe logs.

Dependencies are specified at child level. Work without a dependency may proceed
in parallel. In particular, gold-set preparation can start before ingestion is
complete, and graph/context work need not wait for a chosen embedding or reranker.

## Source-plan coverage

The source contains an architecture document followed by a separately numbered
ranking addendum. References below distinguish them.

| Source sections | Backlog coverage |
|---|---|
| Architecture 1: goals, hardware and constraints | F01, F02, F06, F10 |
| Architecture 2: embedding candidates and evaluation | F07, F06, F11-01 |
| Architecture 3-4: TokenSave and companion Markdown | F01, F03, F09 |
| Architecture 5-7: pipeline, contracts, mappings and chunking | F01-03, F03, F04, F05 |
| Architecture 8: manifests, invalidation and crash recovery | F04, F07-02 |
| Architecture 9: branches, worktrees, dedup and cleanup | F02-02, F04-04, F10-02, F11-03 |
| Architecture 10-12: retrieval, interfaces, deployment and privacy | F02, F05, F07, F09, F10 |
| Architecture 13: phases 0-6 | M0-M4 plus explicitly deferred F11 |
| Architecture 14: benchmark and success criteria | F06 and per-stage evaluation children |
| Architecture 15: decisions and unresolved questions | F01-01/03, F03-02/03, F10-02, F11 |
| Architecture 16: references | Technology overview and original plan; no new research claims |
| Addendum 1-3: research, fusion, alternatives and graph/MMR | F07-03, F09, F11-04 |
| Addendum 4-5: reranker models, HTTP contract and .NET boundaries | F08, F11-01 |
| Addendum 6-7: intent routing and Elasticsearch caveats | F01-01, F05, F07-03 |
| Addendum 8-9: profiles, traces, caches and separate privacy policies | F02, F07-02, F08-02, F10-03 |
| Addendum 10-12: experiments, decision gates and stack | F06-F11 and technology overview |

The optional UI, centralized job coordination, upstream export journal, ONNX/MLX
optimization and alternate-store comparisons are documented options, not implicit
first-release requirements. Their evidence gates appear in F10/F11.

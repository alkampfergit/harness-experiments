<!--
Sync Impact Report
- Version change: 1.0.0 -> 1.1.0 (MINOR: guidance materially expanded)
- Modified principles: III. Reproducible, Pinned Runs (adds public test corpora under testrepo/)
- Added sections: none
- Removed sections: none
- Templates requiring updates: none (plan, spec and tasks templates reference the principles by number only)
- Policy source: AGENTS.md "Public test corpora" is updated in the same change and prevails on conflict.
- Follow-up TODOs: RATIFICATION_DATE is still unknown.
-->

# Harness Experiments Constitution

## Core Principles

### I. Local-Only Sample Data (NON-NEGOTIABLE)

- Sample source, documentation, queries, graphs, embeddings, caches, logs and confidential
  metadata MUST NOT leave the machine. No hosted embedding, reranking or LLM API, hosted
  vector store or telemetry may receive them.
- Embeddings, reranking and any LLM step MUST use local models served by local processes
  (the Python sidecar under `tools/`). Elasticsearch MUST run locally and be bound to localhost.
- Public model weights MAY be downloaded once at a pinned revision. Later runs MUST be offline
  (`HF_HUB_OFFLINE=1`). No unsolicited runtime downloads.
- Optional TokenSave uploads MUST be disabled. Offline operation MUST be enforced with network
  controls, not only by turning off individual counters.
- Logs, traces and result reports MUST NOT contain raw source, query text, credentials or
  private URLs by default. Sample repository URLs and inventories MUST NOT be committed.
- Hosted providers named in the source plan are a deferred design option. They are blocked
  until a separate, authorized change to AGENTS.md permits them. Configuration flags alone
  never authorize them.

Rationale: the samples may be private. AGENTS.md makes local-only processing a repository-wide
rule that this constitution cannot weaken.

### II. Evidence Before Adoption

- Every technique MUST be evaluated on the shared eval set in `eval/` before it is adopted or
  optimized. Minimum metrics are recall@k and MRR. Add token and tool-call cost when testing
  through a harness.
- Every comparison MUST include a plain lexical baseline (BM25 or grep-like) on the same
  eval set. The planned comparisons are TokenSave-only, then Elasticsearch BM25, then
  enrichment, vector, hybrid, reranking and graph variants.
- Each experiment MUST change one variable at a time (chunking, model, ranking, store, etc.).
  Rerankers MUST be compared on the same candidate pool.
- Boosts, field weights, fusion constants, chunk sizes and file-type handling MUST be explicit
  settings justified by a measured ablation. Values from the plan (for example RRF `k = 60`,
  300-800 token chunks, candidate windows of 50-100) are starting points, not accepted
  thresholds.
- Development and tuning queries MUST be kept separate from held-out evaluation queries.
- Negative and inconclusive results MUST be recorded. Adopting a ranking upgrade requires an
  explicit decision that weighs quality against latency and resource cost.

Rationale: the repository exists to compare techniques with evidence, not to assume a winner.

### III. Reproducible, Pinned Runs

- Each sample is a git submodule pinned to a commit. Sample sources under `samples/` are
  read-only, and no artifact (including `.tokensave/`) may be generated there. Indexing MUST
  run on a worktree or copy under `.index/work/<name>/`. All indexes, caches and models stay in
  git-ignored storage under `.index/` or equivalent.
- Public test corpora MAY be committed as pinned git submodules under `testrepo/<name>` when the
  repository is public and its license permits redistribution and local analysis. Provenance (URL,
  commit, license, approval date) is recorded in `testrepo/corpora.json`, a pin change updates the
  expectation files in the same commit, and the same read-only and no-artifacts-in-the-source rules
  apply as for `samples/`. Private repositories MUST NOT be placed there.
- TokenSave build, Elasticsearch version, model revisions, chunker and enrichment rules, and
  mapping versions MUST be pinned and versioned.
- Run scripts MUST take the sample path and commit as inputs. They MUST write results to
  `results/` with technique, sample commit, date, extraction/chunk/mapping/model revisions,
  hardware/runtime, settings and failures.

Rationale: results are only comparable when every input that affects them is identifiable.

### IV. Stable Identity and Atomic Incremental Publication

- Document IDs MUST derive from sample/repository, path, qualified symbol and kind, with
  signature or ordinal disambiguation where needed. TokenSave node IDs and line numbers are
  provenance only, never application IDs.
- File hash, chunk hash and embedding-input hash MUST remain distinct. Embedding cache keys
  MUST include authorization boundary, provider, model and revision, input role, dimension,
  normalization, preparation settings and the exact input bytes.
- Vectors from different embedding profiles MUST NOT share a vector space or index.
- Change detection MUST diff against the exporter's own last-published manifests (deletions
  included) after an explicit TokenSave sync. TokenSave timestamps are not a change cursor.
  Documentation and association changes MUST invalidate dependent chunks.
- A graph revision MUST NOT be combined with a different source revision.
- Every Bulk API item MUST be checked. Retries MUST be idempotent. Queries MUST NEVER see a
  partly published snapshot. The checkpoint advances only after a complete publication.
- Incompatible mapping, chunk-profile or vector-space changes MUST be built in a new physical
  index, validated, and then activated by an alias swap. The previous generation stays until
  the retention policy allows cleanup.

Rationale: incremental updates are only trustworthy when identities are stable and partial
states cannot be observed.

### V. Isolated Contracts and Swappable Components

- TokenSave tables are not a public API. They MUST be read-only through one
  version-pinned adapter that resolves the active DB from `.tokensave/branch-meta.json`
  (`db_file`), detects unsupported schemas explicitly and never writes exporter state into the
  TokenSave database.
- Store access MUST go through a small store interface so that Elasticsearch and the SQLite
  FTS5 plus vector baseline can be compared.
- Inference MUST go through `IEmbedder` and `IReranker`, with an explicit disabled/no-op mode.
  Model-specific prefixes, templates, pooling, normalization, token limits and dimensions MUST
  be honored.
- Fusion MUST be reciprocal rank fusion implemented in C# with deterministic tie ordering. The
  pinned Basic-license Elasticsearch MUST work without licensed native retriever features.

Rationale: narrow seams keep each component replaceable and each experiment attributable.

### VI. Scope-Correct, Honest Retrieval

- Authorization scope and the published snapshot MUST be resolved before retrieval. The same
  scope filters MUST apply to exact, lexical, vector and graph paths, canonical file lookup,
  documentation expansion and caches. Filtering only after fusion is not allowed.
- Confidential payloads, vectors or memberships MUST NOT be deduplicated across tenants or
  authorization boundaries. Each occurrence keeps its own path, lines and snapshot.
- Tree-sitter extraction MUST NOT be presented as compiler-resolved. Ambiguous or unresolved
  relationships and edge confidence MUST be preserved and reported.
- Results MUST cite source path, line range and snapshot provenance consistently via CLI, API
  and MCP.
- When an optional stage such as reranking fails, the system MUST return the earlier-stage
  results with an explicit degraded-stage diagnostic. It must never present the failure as a
  successful run.

Rationale: retrieval for agents is only useful when its results are correctly scoped and
accurately labeled.

### VII. Staged Simplicity

- The first usable release is local TokenSave ingestion plus Elasticsearch BM25 search, with
  deterministic incremental updates and worktree isolation. Embeddings, reranking and graph
  expansion are later stages that are measured independently.
- The core pipeline MUST run CPU-only, without a discrete GPU, and MUST keep a mode with no
  inference dependencies.
- Complexity (content/occurrence overlays, centralized coordination, extra branch databases,
  MMR, learned ranking, ColBERT, a UI) MUST NOT be introduced until a measurement or a concrete
  requirement justifies it.
- Folders are created only when needed. Dependencies and services MUST NOT be added without
  a reason recorded in the experiment README.

Rationale: the repository is an experiment platform, not a commitment to build every proposed
feature.

## Technology and Data Constraints

- Exporter, jobs, search API, MCP server and evaluation are C# / .NET 10. Python is limited to
  helper processes under `tools/` (Python 3.12, FastAPI, sentence-transformers, uv).
- Structural extraction uses a pinned TokenSave build. Roslyn is an optional later comparison,
  not a prerequisite.
- The primary store is a pinned Elasticsearch 9 with the Basic license: single node,
  authentication on, HTTP without TLS, bound to localhost. Use `ELASTICSEARCH_URL`,
  `ELASTICSEARCH_USERNAME` and `ELASTICSEARCH_PASSWORD` inside the devcontainer. Override the
  password only in git-ignored files. These development defaults are not production advice.
- Initial indexes are `code-files-v1` for canonical file revisions and `code-chunks-v1` for
  independent chunk documents, with no nested or parent/child joins. Physical index names
  encode generation and, for vectors, technique, model and profile. Aliases select the active
  generation.
- Ollama is not used. ONNX Runtime or other private inference servers are allowed only after
  compatibility and performance are measured.
- Before use, every model's license, revision, and any `trust_remote_code` requirement MUST be
  verified. Research-only licenses (for example Jina) are not allowed for adopted components.
- Export checkpoints and the embedding cache live in separate SQLite databases. They are never
  stored inside TokenSave's database.
- Observability is local and privacy-safe: structured logs, metrics and OpenTelemetry record
  stage IDs, counts, revisions, latencies and degradation, with no payloads by default.

## Experiment Workflow and Quality Gates

- One experiment, one folder: `experiments/<name>/README.md` states hypothesis, technique,
  how to run and a results summary.
- Feature specs and plans MUST include a Constitution Check against Principles I-VII. Any
  violation MUST be justified in the plan's complexity tracking or the design changed.
- Required test coverage for sync and publication: additions, edits, deletions, renames, line
  shifts, docs-only changes, changed excludes and profile versions, concurrent sync, and
  crashes at publication boundaries.
- Required correctness gates: zero cross-scope leakage, no half-published snapshots and
  deterministic recovery.
- Reported measurements cover the relevant areas: candidate Recall@50/100; Recall@5/10,
  MRR@10 and nDCG@10; context per token budget; p50/p95 latency and resource use; incremental
  reuse ratios. Include uncertainty or paired bootstrap comparisons where supported.
- Commits stay small and focused. Generated data stays out of git. Destructive or
  outward-facing actions (force pushes, deleting data, publishing) require prior approval.

## Governance

- `AGENTS.md` is the single authoritative source of repository instructions. This constitution
  puts its principles and `docs/technology.md` into a form Spec Kit can check against. If the
  two conflict, `AGENTS.md` prevails, and this constitution MUST be amended to match.
- Amendments are proposed as a focused change to `.specify/memory/constitution.md` with a Sync
  Impact Report. An amendment that changes repository policy MUST update `AGENTS.md` in the
  same change, because a constitution amendment alone cannot relax `AGENTS.md`. Lifting
  Principle I specifically requires an explicit, authorized change of scope.
- Versioning follows semantic versioning. MAJOR: a principle is removed or redefined
  incompatibly. MINOR: a principle or section is added or guidance is materially expanded.
  PATCH: clarifications and wording only.
- Compliance: every spec, plan, task list and review MUST check conformance with these
  principles. Unjustified complexity or unmeasured heuristics are grounds for rejecting it.
  `docs/technology.md` is the runtime technical guidance, and the backlog under
  `docs/backlog/` tracks the implementation work.

**Version**: 1.1.0 | **Ratified**: TODO(RATIFICATION_DATE): original adoption date unknown; set when the constitution is first formally ratified | **Last Amended**: 2026-10-24

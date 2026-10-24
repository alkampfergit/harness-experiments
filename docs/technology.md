# Code retrieval: technologies and architectural principles

**Status:** proposed architecture, not an implemented or benchmarked system.  
**Source:** [consolidated plan](./plan_phase_1.md), dated 24 October 2026.  
**Implementation work:** [feature and child-issue backlog](./backlog/README.md).

This document describes the technical direction. The backlog describes the work
needed to implement and validate it. Model names and external references inherited
from the plan are research candidates, not independently verified current product
claims or evidence of C# retrieval quality.

## 1. Purpose and boundaries

Build a reproducible, privacy-first search and context-retrieval pipeline for
C# repositories and their documentation. Support exact-symbol discovery,
natural-language implementation search, relationship navigation, full-file
retrieval and bounded context for coding agents.

The repository is an experiment platform, not a commitment to build every proposed
production feature. Compare techniques on a shared evaluation set before adopting
them. The first usable release is local TokenSave ingestion and Elasticsearch BM25
search, with deterministic incremental updates and worktree isolation. Embeddings,
reranking and graph expansion are subsequent, independently measurable stages.

No discrete GPU is required for the core pipeline. CPU-only operation and a mode
without inference dependencies must remain available.

### Repository policy versus product possibilities

The original plan discusses optional hosted embeddings and reranking for a possible
future product. The authoritative [repository instructions](../AGENTS.md) require
all sample processing to stay local and experiments to work offline. Therefore:

- Current implementation and benchmarks use local models and services only.
- No sample source, documentation, queries, embeddings or confidential metadata
  may be sent to hosted providers, cloud LLMs, vector stores or telemetry.
- Hosted-provider implementation is a deferred design option, blocked on a
  separate authorized change of scope and applicable policy. Configuration flags
  alone do not authorize it.
- Public model weights may be downloaded once, with pinned revisions; subsequent
  runs use offline mode. Do not make unsolicited runtime downloads.

## 2. Technology stack

| Responsibility | Default technology | Reason and boundary |
|---|---|---|
| Exporter, jobs, search API, MCP server, evaluation | C# / .NET 10 | Typed contracts, hosted services, cancellation and later Roslyn integration |
| Initial structural extraction | Pinned TokenSave build, Tree-sitter, SQLite/libSQL graph | Reuse files, symbols, relationships and companion-document associations |
| Optional C# semantic enrichment | Roslyn | Later experiment for compiler-resolved cases; not a prerequisite |
| Primary search store | Pinned Elasticsearch 9, Basic license | BM25, metadata filters and optional dense-vector kNN |
| Elasticsearch access | Typed .NET client behind a small store interface | Keep retrieval and publication independent of storage details |
| Lightweight comparison store | SQLite FTS5 plus a vector implementation behind the same interface | Baseline experiment, not a replacement selected in advance |
| Export checkpoints and embedding cache | Separate SQLite database(s) | Durable manifests and reuse without modifying TokenSave |
| Rank fusion | RRF implemented in C# | Portable and independent of licensed native retriever features |
| Local embedding and reranking inference | Python 3.12, FastAPI, sentence-transformers/PyTorch, uv | HTTP sidecar behind `IEmbedder` and `IReranker` |
| Optional inference optimization | ONNX Runtime; compatible private inference servers | Only after runtime compatibility and performance measurements |
| Local development | Docker Compose devcontainer | .NET, Node, Python, uv, agent tools, TokenSave and Elasticsearch |
| Observability | Structured logs, metrics, OpenTelemetry | Local, privacy-safe diagnostics; no external telemetry carrying sample data |
| Initial interfaces | CLI, API and MCP | A UI is not required for the first deliverable |
| Possible later UI | Blazor and MudBlazor | Defer until a concrete user-interface requirement exists |

Ollama is not part of this architecture. Keep Python restricted to helper processes
under `tools/`; implement the exporter, orchestration and evaluation in C#.

The development Elasticsearch service uses authentication and HTTP without TLS,
bound to localhost on the host. Use the prescribed environment variables inside
the devcontainer. Override the development password outside tracked files.
Production networking, TLS, storage encryption and authentication require explicit
deployment decisions; the development defaults are not production recommendations.

## 3. Pipeline and responsibility boundaries

```text
Pinned sample commit
  -> worktree/copy under .index/work/<name>/
  -> explicit TokenSave sync
  -> consistent read-only graph + matching source snapshot
  -> .NET exporter
  -> canonical file revisions + documentation
  -> deterministic symbol/document chunks
  -> Elasticsearch BM25 + optional model-specific vectors

Authorized query + published snapshot
  -> exact/BM25 candidates + optional dense candidates
  -> scope-aware deduplication and C# RRF
  -> optional local reranking
  -> bounded graph/document expansion and context selection
  -> cited results for CLI/API/MCP
```

TokenSave owns parsing and its own synchronization. The exporter owns derived
contracts, change detection, publication and recovery. The search store owns
indexing and filtering. The retrieval coordinator owns authorization, query
routing, fusion, optional reranking and context budgeting.

Never generate indexes inside `samples/` or edit sample sources. Pin sample
commits, use local worktree copies and keep generated indexes, caches and models
under ignored storage. Local sample URLs and inventories must not be committed.

## 4. Structural extraction and documentation

### TokenSave adapter

Read TokenSave through one version-pinned adapter. Its database tables are not a
stable public export API. Resolve the active database from
`.tokensave/branch-meta.json` (`db_file`), falling back to
`.tokensave/tokensave.db` only when branch metadata is absent. Detect unsupported
schemas explicitly before importing; never write exporter state into this database.

Relevant data includes `files`, `nodes`, `edges`, unresolved references and ambiguous
calls. File hashes identify possible source changes; node ranges and signatures
describe symbols; edges support relationship queries. Internal FTS or vector tables
are not the authoritative Elasticsearch search index.

Tree-sitter extraction is not Roslyn semantic compilation. Preserve ambiguity and
resolution confidence. Validate overloads, generics, partial classes, extension
methods, aliases and DI cases rather than claiming compiler-level correctness.
Read actual source bodies from the matching revision; verify encoding, line endings
and ranges. Export must not combine one graph revision with another source revision.

### Companion Markdown

- Sidecars such as `PaymentService.readme.md` describe the matching source file.
- Central Markdown under the configured `docs_dir` uses `applies_to` associations.
- TokenSave's documented associations are file/document-level, not method-level.
- Index Markdown sections independently and preserve covered-file links and
  staleness signals. Do not silently discard stale documentation.
- Adding selected excerpts to code embedding input is an experiment. Do not copy
  an entire document into every method by default.
- Documentation edits and association changes invalidate dependent derived chunks,
  even when source-file hashes do not change.

## 5. Data contracts, identities and indexes

### Logical contracts

| Contract | Required information |
|---|---|
| Source scope | Authorization boundary, repository ID, branch ref, worktree ID and snapshot ID |
| File revision | Stable ID, path, language, raw-source hash, source text or approved protected reference, symbol outline, docs associations and extractor profile |
| Chunk occurrence | Stable logical symbol identity, kind, ordinal/extent, file revision, scope/snapshot, path, lines, signature, search text, docs and provenance |
| Embedding profile | Provider, model, immutable revision, query/document formatting, tokenizer/max length, dimension, pooling and normalization |
| Search result | Occurrence ID, authorized snapshot, path/lines, snippet, source revision, contributing strategies and scores |

Use stable keys derived from sample/repository, path, qualified symbol and kind,
with signature/ordinal disambiguation where necessary. TokenSave node IDs and line
numbers are provenance, not stable application IDs. Distinguish logical symbol
identity from immutable content revisions and scope-specific occurrences. A rename
can create a new path identity while still reusing an identical embedding input.

### Initial Elasticsearch layout

- `code-files-v1`: immutable canonical file revisions, complete source when
  permitted, symbol outlines and documentation associations. Enables full-file
  lookup and derived-index rebuilding without rerunning parsing.
- `code-chunks-v1`: independent method, class/API overview, member-group,
  long-method fragment, file-overview and Markdown-section documents.
- Physical indexes encode generation and, for vectors, technique/model/profile.
  Logical aliases select the active compatible generation.
- Use independent documents, not nested chunks or parent/child joins. Resolve
  canonical parent files in .NET after retrieval.
- Start with scope-specific chunk documents and an authorized shared embedding
  cache. Do not introduce content/occurrence overlays until storage measurements
  justify the complexity of scope-filtered approximate nearest-neighbor search.

Use exact `keyword` fields alongside analyzed identifier/signature/code/doc fields.
Test PascalCase, camelCase, underscores, namespaces, generics and punctuation while
preserving exact identifiers. Field boosts and generated/vendor/test-file handling
must be explicit experimental settings with ablations, not unmeasured magic weights.

### Three distinct hashes

| Hash | Meaning |
|---|---|
| File hash | Raw source bytes; determines files that need reassessment |
| Chunk hash | Versioned search fields and metadata; determines document updates |
| Embedding-input hash | Exact prepared model input; determines vector reuse |

An embedding cache key includes authorization boundary, provider, model/revision,
input role, dimension, normalization and all relevant preparation settings plus
the exact input bytes. Changing searchable metadata need not regenerate a vector
if those bytes and the profile are unchanged. Never deduplicate confidential
payloads, vectors or memberships across tenants.

## 6. Deterministic chunking

Prefer semantic boundaries over arbitrary windows:

- Keep small/medium methods whole, with namespace/type/signature context.
- Split large methods on statement/block boundaries; repeat necessary headers.
- Represent classes/interfaces as API overviews without duplicating all bodies.
- Extract properties/fields individually or in deterministic related groups.
- Split Markdown by headings/sections and retain source associations.
- Keep full canonical files available separately.

Version the chunker and enrichment rules. Record the source extent, search text,
exact embedding input, ordinal, profile versions and graph/document provenance.
The plan's 300-800 token target is an experimental starting range, not an accepted
quality threshold; use the actual model tokenizer and enforce its input limit.

## 7. Incremental synchronization and publication

Keep independent exporter manifests for last-published files and chunk occurrences.
Record scope, hashes, profile versions, index generation, job status and published
snapshot. TokenSave's last-sync timestamp and CLI diagnostics are not a downstream
change cursor.

1. Explicitly sync the prepared worktree and obtain consistent graph/source input.
2. Diff current file metadata against the last successfully published manifest,
   including an anti-join for deletions.
3. Expand invalidation for documentation, configuration, extraction and graph changes.
4. Reuse or create canonical revisions and deterministically regenerate affected
   chunk descriptors.
5. Diff chunks; upsert changed documents, remove obsolete occurrences and reuse
   embeddings only for compatible cache keys.
6. Check every Elasticsearch Bulk API item. Retry idempotently using deterministic
   IDs; surface permanent failures.
7. Publish only a complete searchable snapshot and then advance its checkpoint.
   Elasticsearch and SQLite are not one transaction: recovery needs durable state
   and explicit publication semantics.
8. Reconcile independently changing graph relationships and clean eligible orphans
   only after reference/retention checks.

Test additions, edits, deletions, renames, line shifts, docs-only changes, changed
excludes/profile versions, concurrent sync and crashes at publication boundaries.
Queries must never see a partly updated snapshot.

Rebuild incompatible mappings, chunk profiles or vector spaces in a new physical
index, validate it and swap the alias. Preserve the previous generation until the
retention/rollback policy permits cleanup.

## 8. Repository, worktree and authorization scope

Keep repository identity, branch name, worktree identity and immutable snapshot
identity separate. A Git commit alone cannot identify a dirty worktree snapshot.
Use one TokenSave index per active prepared worktree initially; do not add another
set of full branch databases without a measured need.

Resolve authorization and the published snapshot before retrieval. Apply the same
scope filters to exact, lexical, vector and graph paths, canonical file lookup,
documentation expansion and caches. Do not rely on filtering after fusion.

Identical content may reuse vectors within its authorized boundary, but each
occurrence retains the correct path, lines and snapshot. Scope cleanup must keep
content referenced elsewhere and respect active jobs, readers and retention rules.

## 9. Retrieval, fusion, reranking and context selection

These are separate stages with separate measurements.

### Candidate retrieval

BM25 and exact identifiers form the mandatory baseline. Optional dense kNN adds
semantic paraphrase discovery; embed queries with the compatible query profile.
Relationship questions should use structural navigation rather than vectors alone.
Candidate Recall@50/100 is important: later reranking cannot recover missing code.

Start with transparent query intent: exact symbol, API/signature, conceptual,
relationship, documentation and multi-file bug/task. Exact and graph questions may
bypass reranking. Return unresolved or ambiguous relationships honestly.

### Fusion

Implement reciprocal rank fusion in C#:

```text
RRF(candidate) = sum over input lists of 1 / (k + one-based rank)
```

Deduplicate within each list and fuse by authorized occurrence identity, not bare
content hash. Use deterministic tie ordering. The plan's `k = 60` and candidate
windows of roughly 50-100 are starting configurations to tune on development data.
BM25 scores and cosine similarities are not directly interchangeable.

Native Elasticsearch RRF, linear fusion and reranker retrievers have version/license
constraints. The pinned Basic-license configuration must work without them.
Weighted/normalized fusion is a later ablation, not a required first-release feature.

### Optional reranking

A cross-encoder scores query/candidate pairs after retrieval and fusion. Shortlist
only a bounded number of distinct candidates; test depths such as 20, 40 and 80.
Scores may be logits, not probabilities, and are not comparable across models.

Expose `/v1/rerank` behind `IReranker`; retain an explicit disabled/no-op mode.
Requests identify the model/profile, query, candidate IDs/text, `top_n` and timeout.
Responses identify the pinned model revision and scores keyed to submitted IDs.
Validate cardinality, IDs, finite scores and provider-specific input limits.

Use bounded queues, batching, cancellation and timeouts. On optional inference
failure, return the pre-reranked results only with an explicit degraded-stage
diagnostic and privacy-safe logging; never make failure look like successful
reranking. Do not automatically load large embedding and reranking models into
every HTTP worker. Separate processes are acceptable behind the same contracts.

Changing a reranker requires a new ranking configuration and evaluation, not
re-embedding or reparsing. Any optional score cache must include authorization/
snapshot, query hash, candidate-text hash and model/template revisions.

### Graph and context

After selecting relevant seeds, add a bounded number of callers, callees,
implementations, interfaces, tests, docs or parent-file excerpts. Distinguish graph
boosting from graph expansion. Preserve edge confidence and avoid replacing useful
seeds with irrelevant neighbors.

Select final context using a declared token budget, citations and a simple
file/symbol distinctness policy first. MMR is a later diversity experiment, not
a substitute for relevance. Return source paths, line ranges and snapshot
provenance consistently through the API and MCP interface.

## 10. Model selection and deployment

No candidate is an established C# winner. Verify artifact availability, license,
revision, runtime and required input formatting before use.

| Stage | Initial local candidates from the plan | Later comparisons |
|---|---|---|
| Embeddings | `codefuse-ai/C2LLM-0.5B`, `BAAI/bge-code-v1`, `nomic-ai/CodeRankEmbed` | C2LLM-7B, Nomic Embed Code and a general embedding baseline |
| Reranking | `Alibaba-NLP/gte-reranker-modernbert-base`, `Qwen/Qwen3-Reranker-0.6B` | BGE reranker v2 M3, Qwen3 4B, compatible ONNX CPU variants |

The plan identifies Apache-2.0 or MIT candidates, but licenses and remote-code
requirements must be checked at the pinned revision. Jina code embeddings and
reranker v3 are research-only unless their noncommercial restrictions are resolved.
Review any `trust_remote_code` implementation before execution.

Honor model-specific query/document prefixes, templates, pooling, normalization,
token limits and output dimensions. Never mix vectors from different profiles
in one vector space.

Measure CPU-only, MacBook Air M5 32 GB and, when available, RTX 5060 Ti 16 GB /
RTX 3070 8 GB profiles. Docker on macOS runs these models on CPU; native MPS or
MLX capability is a separate compatibility experiment, not an assumed feature.

Deferred hosted research in the source plan includes Voyage Code 4, Cohere Embed
5, Codestral Embed, general Google/OpenAI embedding baselines, Voyage rerank-3/
lite and Cohere Rerank 5 Pro/Fast. Names, pricing, availability, data handling and
claimed quality require fresh verification before any future authorized work.
Embedding, reranking and external-query permissions would be independent, denied
by default and enforced server-side. They do not override current local-only policy.

## 11. Privacy, operations and observability

Treat source, graphs, checkpoints, vectors, caches and logs as confidential data.
Enforce exclusions and secret handling before inference/indexing; use endpoint
allowlists, access controls, protected storage and explicit retention/erasure rules.
Avoid raw source, query text, credentials and private URLs in logs or result reports.

Disable optional TokenSave uploads and use network controls to enforce offline
operation; disabling one counter does not disable every possible network request.
Protect local inference endpoints with appropriate authentication, request limits
and binding rules.

Trace stage identifiers, counts, profile revisions, latencies, degradation and
selected occurrence IDs without payloads by default. Record initial and incremental
throughput, cache hits, index size, model cold start, errors and resource peaks.
Centralized distributed coordination is future work unless measurements establish
that the single-worker SQLite model is insufficient.

## 12. Evaluation and adoption gates

Use pinned public C# corpora initially, with 100-200 manually checked queries and
file/symbol relevance labels. Include async/cancellation, HTTP retries, EF/LINQ,
DI, interfaces, overloads, multi-file bugs, docs, near misses and dirty worktrees.
Keep development/tuning queries separate from held-out evaluation.

Required comparisons progress from TokenSave-only and Elasticsearch BM25 to
BM25+docs, vectors-only, RRF hybrid, local reranking and graph/context variants.
Compare rerankers on the same candidate pool. Change one variable at a time.

| Area | Measurements |
|---|---|
| Candidate coverage | Recall@50/100 and pool diversity |
| Search ranking | Recall@5/10, MRR@10, nDCG@10 |
| Context | Relevant distinct symbols per declared budget, redundancy and file coverage |
| Agent usefulness | Local-harness task success and test pass rate where feasible |
| Performance | p50/p95 latency, throughput, RAM/VRAM, CPU, cold start and disk size |
| Incrementalism | Files/chunks reassessed/written, embeddings generated, cache hit ratio |
| Correctness | Zero cross-scope leakage, no half-published snapshots, deterministic recovery |

Write results with technique, sample commit, date, extraction/chunk/mapping/model
revisions, hardware/runtime, settings and failures. Include uncertainty or paired
bootstrap comparisons where supported. Record negative and inconclusive results.
Adopt a ranking upgrade only after an explicit quality-versus-latency/resource
decision; illustrative numbers in the plan are not preapproved success thresholds.

## 13. Deferred decisions

- Select the actual supported TokenSave schema/build and pinned Elasticsearch version.
- Decide source-storage/reference policy and stable overload/rename identity details.
- Measure source-only versus XML/Markdown-enriched inputs and doc invalidation cost.
- Define dirty-snapshot retention, cleanup ownership and rollback duration.
- Choose whether TokenSave alone suffices for graph access or Roslyn adds value.
- Measure when content/occurrence overlays or centralized coordination pay off.
- Consider linear fusion, MMR, learned-to-rank, ColBERT and local listwise reranking
  only after labels and measured bottlenecks justify them.
- A TokenSave export journal is a possible upstream proposal, not an existing CDC
  contract to depend on.

## 14. Primary references

These links are inherited reading material, not newly verified research:

- [TokenSave repository](https://github.com/aovestdipaperino/tokensave),
  [index design](https://github.com/aovestdipaperino/tokensave/blob/master/docs/INDEX-DESIGN.md),
  [user guide](https://github.com/aovestdipaperino/tokensave/blob/master/docs/USER-GUIDE.md),
  [branch/worktree guide](https://github.com/aovestdipaperino/tokensave/blob/master/docs/BRANCHING-USER-GUIDE.md).
- [Elasticsearch dense vectors](https://www.elastic.co/docs/reference/elasticsearch/mapping-reference/dense-vector),
  [RRF](https://www.elastic.co/docs/reference/elasticsearch/rest-apis/retrievers/rrf-retriever),
  [ranking](https://www.elastic.co/docs/solutions/search/ranking),
  [licensing FAQ](https://www.elastic.co/pricing/faq).
- [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai),
  [Roslyn SDK](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/).
- [C2LLM-0.5B](https://huggingface.co/codefuse-ai/C2LLM-0.5B),
  [BGE Code](https://huggingface.co/BAAI/bge-code-v1),
  [CodeRankEmbed](https://huggingface.co/nomic-ai/CodeRankEmbed),
  [GTE reranker](https://huggingface.co/Alibaba-NLP/gte-reranker-modernbert-base),
  [Qwen3 reranker](https://huggingface.co/Qwen/Qwen3-Reranker-0.6B).
- [RRF paper](https://doi.org/10.1145/1571941.1572114),
  [MMR paper](https://doi.org/10.1145/290941.291025),
  [ColBERT](https://arxiv.org/abs/2004.12832),
  [ColBERTv2](https://aclanthology.org/2022.naacl-main.272/).
- [CoREB](https://huggingface.co/datasets/hq-bench/coreb),
  [CoIR](https://aclanthology.org/2025.acl-long.1072/),
  [CodeRAG-Bench](https://aclanthology.org/2025.findings-naacl.176/),
  [CodeRAG](https://aclanthology.org/2025.emnlp-main.1187/),
  [CoQuIR](https://aclanthology.org/2026.acl-long.512/).

See the [source plan](./plan_phase_1.md) for its complete historical research,
illustrative JSON/SQL and conceptual interface examples. Those examples are not
compile-ready contracts or verified deployment instructions.

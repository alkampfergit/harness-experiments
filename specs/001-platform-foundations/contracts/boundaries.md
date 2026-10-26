# Contract: component boundaries (written only)

This covers FR-024 to FR-026 and FR-028. These boundaries are **specifications, not code**: no interface is added to the solution until a feature implements and uses it (F03 onward). Each later feature that implements a boundary must conform to this contract or amend it.

Common rules for every boundary:
- **Cancellation:** every operation accepts a cancellation token. Cancellation ends with an explicit cancelled outcome, never an empty success.
- **Errors:** failures are typed (`PolifemoException` subclasses with stable codes). "No results" is a valid success only when the operation actually ran to completion.
- **Provenance:** every output carries `SnapshotProvenance`.
- **Scope:** inputs that touch indexed data take a resolved `SourceScope` and snapshot. Scope filtering happens inside the boundary, before fusion (Constitution VI).
- **Provider isolation:** provider-specific options (Elasticsearch mappings and queries, SQLite pragmas, model names, HTTP endpoints) appear only in the configuration of the component that owns them. They never appear in chunking or orchestration contracts.

| Boundary | Single responsibility | Input → output | Disabled mode | Owner feature |
|---|---|---|---|---|
| Exporter | Read the TokenSave graph plus matching source (read-only) and emit file revisions and change sets against its own last-published manifest | worktree + snapshot → `FileRevision[]`, deletions | — (required) | F03/F04 |
| Chunker | Turn file revisions and docs into deterministic chunk occurrences | `FileRevision` + docs + `chunkerVersion` → `ChunkOccurrence[]` with `ChunkHash` | — (required) | F03 |
| Store | Persist and query chunk and file documents for a scope and snapshot; atomic publication | `PublicationBatch` → ack; lexical/vector query → ranked occurrence refs | — (required). Implementations: Elasticsearch now, SQLite FTS5+vector baseline later, with no caller change | F04/F05 |
| Embedder | Turn prepared inputs into vectors under one `EmbeddingProfile` | inputs + role → vectors + `EmbeddingInputHash` | **Disabled**: the pipeline runs BM25-only, and vector requests fail with `EMB_DISABLED` | F07 |
| Retriever | Run one strategy (exact, BM25, vector, graph) for an authorized query | query + scope + snapshot → ranked candidates with strategy name and score | each strategy can be disabled individually | F05/F07 |
| Fusion | Merge candidate lists with C# RRF and deterministic tie order | ranked lists → fused list with per-strategy contributions | single-list passthrough | F07 |
| Reranker | Reorder a fixed candidate pool | query + candidates → reordered candidates | **Disabled** = passthrough. On failure: return the input order plus a degraded-stage diagnostic | F08 |
| Context selector | Choose cited results within a token budget, with optional bounded graph/doc expansion | fused/reranked candidates + budget → `CitedSearchResult[]` | expansion off = top-k only | F09 |

Interfaces between them use only the contract entities in [../data-model.md](../data-model.md).

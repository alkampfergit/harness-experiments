# [F07-02] Cache embeddings and publish model-compatible vector generations

- Type: Child issue
- Parent: [F07](./F07-local-hybrid-search.md)
- Depends on: [F07-01](./F07-01-local-embedding-service.md), [F04-03](./F04-03-incremental-diff.md)
- Suggested labels: `task`, `embeddings`, `indexing`
- Source: architecture 6-9; addendum 8

## Scope

Persist vectors by exact input/profile and authorization boundary, integrate
embedding work into incremental publication and add model-specific vector mappings.
Reuse vectors across authorized occurrences without merging their locations.

## Acceptance criteria

- [ ] Cache keys include authorization, provider/model/revision, input role,
  dimension, normalization and all input-preparation settings.
- [ ] Tests vary every key component and prove incompatible vectors are never reused.
- [ ] A one-method edit regenerates only changed inputs; docs/profile changes
  invalidate the appropriate embeddings. No-op reruns make zero embedding calls.
- [ ] Identical authorized inputs across worktrees reuse vectors while preserving
  distinct occurrence/snapshot membership.
- [ ] Failed/partial inference cannot publish a falsely complete vector snapshot;
  BM25-only versus vector-ready behavior is explicit.
- [ ] Index names encode technique/model/profile generation, mapping dimensions
  match outputs and cache hit/miss/generated counts are observable.

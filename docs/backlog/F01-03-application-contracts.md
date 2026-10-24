# [F01-03] Define versioned identities, profiles and application boundaries

- Type: Child issue
- Parent: [F01](./F01-foundations.md)
- Depends on: [F01-01](./F01-01-platform-compatibility.md)
- Suggested labels: `task`, `architecture`
- Source: architecture 5-8, 9 and 11; addendum 5 and 8

## Scope

Define typed source scope, immutable file revision, chunk occurrence, embedding
profile, publication batch and cited search-result contracts. Establish exporter,
chunker, store, embedder, retriever, fusion, reranker and context-selector boundaries
without introducing implementations or unused abstractions.

## Acceptance criteria

- [ ] Stable logical keys use repository/sample, path, qualified name and kind with
  overload/fragment disambiguation; line numbers and TokenSave IDs are provenance.
- [ ] Content revisions and scope-specific occurrences are distinct; rename and line
  movement behavior is documented with identity tests.
- [ ] File, chunk and embedding-input hashes have separately specified inputs.
- [ ] Extractor/chunker/mapping/model/input-template versions can evolve independently.
- [ ] Store access supports Elasticsearch and a later small SQLite baseline; provider
  options do not leak into chunking or orchestration.
- [ ] Contracts cover cancellation, explicit errors, snapshot provenance and
  matching query/document embedding profiles.

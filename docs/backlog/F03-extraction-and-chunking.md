# [F03] Extract canonical source, documentation and deterministic chunks

- Type: Feature
- Suggested labels: `feature`, `indexing`
- Milestone: M1
- Source: architecture 3-7; addendum 8

## Outcome

Produce reproducible search inputs from TokenSave without changing its database or
requiring an embedding model.

## Children

- [ ] [F03-01 Implement the read-only adapter](./F03-01-tokensave-adapter.md)
- [ ] [F03-02 Prepare canonical files and docs](./F03-02-files-and-documentation.md)
- [ ] [F03-03 Build deterministic semantic chunks](./F03-03-deterministic-chunks.md)

## Feature acceptance

- [ ] Matching source/graph snapshots produce repeatable file and chunk contracts.
- [ ] Markdown is independently searchable and linked at its actual association granularity.
- [ ] All chunk kinds retain exact source provenance and rebuild metadata.

## Out of scope

Embedding inference, Roslyn enrichment and arbitrary nested/parent-child search layouts.

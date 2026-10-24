# [F09] Assemble bounded graph- and documentation-aware context

- Type: Feature
- Suggested labels: `feature`, `graph`, `context`
- Milestone: M3, independently evidence-gated
- Source: architecture 4, 10 and 13 phase 5; addendum 3, 6 and phase D

## Outcome

Return useful related material with citations and a strict token budget rather than
an unbounded neighborhood of graph edges or repetitive chunks.

## Children

- [ ] [F09-01 Expand authorized seeds](./F09-01-graph-document-expansion.md)
- [ ] [F09-02 Select diverse cited context](./F09-02-context-selection.md)
- [ ] [F09-03 Evaluate context and local-agent utility](./F09-03-context-experiment.md)

## Feature acceptance

- [ ] Graph ambiguity, documentation staleness and source provenance remain visible.
- [ ] Final context meets its actual tokenizer budget and preserves relevant seeds.
- [ ] Expansion and diversity gains are measured separately from retrieval/reranking.

## Out of scope

Assuming graph edges are compiler-resolved or making embeddings/reranking mandatory.

# [F11-04] Select one evidence-backed advanced retrieval or store experiment

- Type: Child issue
- Parent: [F11](./F11-deferred-research.md)
- Status: Deferred research selection; not a bundle of mandatory implementations
- Depends on: [F09-03](./F09-03-context-experiment.md), [F08-03](./F08-03-reranking-experiment.md)
- Suggested labels: `experiment`, `deferred`, `retrieval`
- Source: architecture 8 and 15; addendum 3 and phase D; repository baseline rules

## Scope

Use measured bottlenecks to select a single next hypothesis: normalized linear
fusion, graph boosting, MMR, labeled learned-to-rank, local listwise reranking,
ColBERT/late interaction or the lightweight SQLite FTS5/vector store comparison.
Do not implement all alternatives under this issue.

## Acceptance criteria

- [ ] Document the baseline limitation, selected variable, test configuration,
  expected measurable benefit and quality/resource decision rule before implementation.
- [ ] Learned ranking requires sufficient trustworthy labels; late interaction
  requires evidence that simpler retrieval/reranking cannot meet the budget.
- [ ] Implement only the chosen bounded experiment behind existing boundaries;
  add dependencies/services only with an experiment README rationale.
- [ ] Run shared held-out evaluation and report quality, latency, resource/storage
  cost, privacy and operational impact, including negative findings.
- [ ] Record adopt/defer/reject and create separate drafts only for justified follow-on work.

An upstream TokenSave journal/export API is a separate possible proposal if manifest
diffing becomes a measured bottleneck; do not assume such a feed exists or publish
an upstream issue without authorization. UI and distributed coordination similarly
require new, evidenced requirements rather than implementation in this experiment.

# [F06-03] Measure TokenSave, BM25 and documentation baselines

- Type: Child issue
- Parent: [F06](./F06-evaluation.md)
- Depends on: [F06-02](./F06-02-evaluation-runner.md), [F05-03](./F05-03-query-interfaces.md)
- Suggested labels: `experiment`, `evaluation`
- Source: architecture 13 phase 1 and 14; addendum E0 and phase A

## Scope

Create an experiment folder/README and compare TokenSave-only, Elasticsearch
BM25-source and BM25-source-plus-docs on the fixed dataset. Keep other settings
constant and record retrieval quality, cost and update behavior.

## Acceptance criteria

- [ ] Reproducible commands take sample path/commit and write dated results with
  all required metadata.
- [ ] Reports include Recall@k, MRR@10, nDCG@10, latency, index size and incremental
  files/chunks touched for the same snapshots.
- [ ] Cross-scope leakage is zero and crash/restart tests show no half-published state.
- [ ] Baseline operates with no inference service or model downloads.
- [ ] Negative/inconclusive documentation gains are recorded; no unmeasured boosts
  are adopted. State quality/latency budgets before later model selection.

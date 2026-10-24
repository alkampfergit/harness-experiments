# [F09-03] Measure graph, documentation and diversity contributions

- Type: Child issue
- Parent: [F09](./F09-context-assembly.md)
- Depends on: [F09-02](./F09-02-context-selection.md), [F06-03](./F06-03-baseline-experiment.md)
- Suggested labels: `experiment`, `evaluation`, `context`
- Source: architecture 14; addendum E7-E8 and phase D

## Scope

Ablate no expansion, graph expansion, docs linking and file/symbol diversity at
fixed context budgets. Compare with and without reranking only when that stage is
available, using the same snapshots and candidate configurations.

## Acceptance criteria

- [ ] Report relevant distinct symbols per 8K-token budget, redundancy, file coverage,
  ranking quality and added latency.
- [ ] Graph ranking boosts and MMR, if tested, each change only one variable per run.
- [ ] A local-model harness can measure task/fix success and test pass rate on
  approved tasks without sending sample code to cloud models.
- [ ] If agent evaluation is infeasible, record that limitation and do not claim
  downstream task improvements from retrieval metrics alone.
- [ ] Adopt/defer decisions include irrelevant-neighbor and lost-seed regressions,
  not just examples where expansion helps.

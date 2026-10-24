# [F08-03] Benchmark reranker quality, candidate depths and CPU feasibility

- Type: Child issue
- Parent: [F08](./F08-local-reranking.md)
- Depends on: [F08-02](./F08-02-reranking-orchestration.md), [F06-02](./F06-02-evaluation-runner.md)
- Suggested labels: `experiment`, `evaluation`, `reranking`
- Source: addendum 4, 10 E4-E5 and phase B

## Scope

Compare no reranker, GTE ModernBERT and Qwen3 0.6B on the same frozen candidate
pools and query representations. Evaluate development-set depths such as 20/40/80
and then held-out quality/cost.

## Acceptance criteria

- [ ] Report Recall@10, MRR@10, nDCG@10, candidate recall, p50/p95 end-to-end latency,
  ms/candidate, cold start and peak memory.
- [ ] Measure CPU-only and available native M5 profiles separately; record unavailable
  hardware/runtime combinations instead of inventing results.
- [ ] Record provider-mandated representation differences and exact model/template revisions.
- [ ] Report quality changes with paired uncertainty where feasible; include exact-symbol
  regressions and inference failures.
- [ ] Select a default only against quality and p95/resource budgets declared before
  held-out evaluation; otherwise retain disabled or opt-in reranking.

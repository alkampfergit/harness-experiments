# [F06-02] Implement a reproducible C# retrieval evaluation runner

- Type: Child issue
- Parent: [F06](./F06-evaluation.md)
- Depends on: [F06-01](./F06-01-gold-set.md), [F01-03](./F01-03-application-contracts.md)
- Suggested labels: `task`, `evaluation`
- Source: architecture 14; addendum 10

## Scope

Run interchangeable retrieval configurations against the same pinned snapshot.
Write comparable results under `results/` and support candidate, ranking,
operational and later context/agent metrics.

## Acceptance criteria

- [ ] Hand-computed fixtures verify Recall@5/10/50/100, MRR@10 and graded nDCG@10,
  including ties, duplicates, no hits and multiple relevant symbols.
- [ ] Record p50/p95 latency, throughput, errors, memory, index size and incremental
  work/cache counters when the stage exposes them.
- [ ] Output identifies technique, date, sample commit, snapshot, all profile/model
  revisions, hardware/runtime and tuning settings.
- [ ] Development/held-out separation is enforced in reports; paired uncertainty
  or bootstrap comparisons are supported with a documented method.
- [ ] Failures, incomplete runs and absent measurements are explicit, not zeroed
  or represented as successful benchmark results.

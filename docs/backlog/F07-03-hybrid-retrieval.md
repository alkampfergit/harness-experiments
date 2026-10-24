# [F07-03] Implement scoped dense retrieval, deterministic RRF and hybrid evaluation

- Type: Child issue
- Parent: [F07](./F07-local-hybrid-search.md)
- Depends on: [F07-02](./F07-02-embedding-cache.md), [F05-03](./F05-03-query-interfaces.md), [F06-03](./F06-03-baseline-experiment.md)
- Suggested labels: `task`, `retrieval`, `evaluation`
- Source: architecture 10 and 14; addendum 2, 7 and E1-E2

## Scope

Embed queries using the compatible profile, retrieve authorized kNN candidates and
fuse lexical/dense lists in C#. Expose lexical, dense and hybrid modes consistently.

## Acceptance criteria

- [ ] RRF uses one-based ranks, per-list deduplication, authorized occurrence IDs
  and deterministic tie ordering, with hand-calculated unit tests.
- [ ] Tests cover empty lists, overlapping IDs, different score scales, invalid
  constants/windows and cross-scope identical content.
- [ ] Exact-symbol/relationship paths can bypass semantic ranking; every arm uses
  the same snapshot filters before ranking.
- [ ] Model mismatch and inference failure are explicit; an allowed BM25 degradation
  includes a failed-stage diagnostic rather than a claimed hybrid success.
- [ ] Measure BM25, vectors-only and RRF on fixed snapshots with candidate Recall@50/100,
  ranking quality, p95 latency, memory, storage and cache effects.
- [ ] Tune RRF constants/windows only on development data; record model/hardware
  comparisons and an adopt/defer decision without relying on vendor benchmarks.

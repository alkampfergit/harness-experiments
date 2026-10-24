# [F10-03] Instrument pipeline stages and measure CPU/worktree scale

- Type: Child issue
- Parent: [F10](./F10-operations.md)
- Depends on: [F05-03](./F05-03-query-interfaces.md), [F02-03](./F02-03-sensitive-data-handling.md)
- Suggested labels: `task`, `observability`, `performance`
- Source: architecture 1, 13 phase 6 and 14; addendum 8, 10 and 12

## Scope

Add structured local traces/metrics and reproducible scale profiles for initial
indexing, incremental refresh, query stages and multiple worktrees. Reuse evaluation
measurements rather than creating a competing metrics system.

## Acceptance criteria

- [ ] Traces include scope/snapshot IDs, profile revisions, candidate counts,
  fusion/rerank settings, selected IDs, latencies and explicit degradation.
- [ ] Metrics include files/chunks per second, writes/deletes, cache hits, errors,
  model cold start, CPU/RAM/VRAM and index/vector disk size where applicable.
- [ ] Tests verify no raw source/query/secrets enter logs or external telemetry.
- [ ] CPU-only and available M5/GPU profiles report p50/p95 latency and many-worktree
  storage growth; unavailable hardware is identified.
- [ ] Record whether single-worker SQLite coordination or duplicated chunk storage
  is a measured bottleneck before proposing centralized services or overlays.
- [ ] Deployment guidance distinguishes container CPU, native accelerators, local
  development credentials and production network/storage requirements.

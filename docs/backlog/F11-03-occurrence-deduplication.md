# [F11-03] Compare scope-specific chunks against content/occurrence overlays

- Type: Child issue
- Parent: [F11](./F11-deferred-research.md)
- Status: Deferred until storage-growth measurements justify it
- Depends on: [F10-03](./F10-03-observability-and-scale.md), [F07-02](./F07-02-embedding-cache.md)
- Suggested labels: `experiment`, `deferred`, `storage`
- Source: architecture 9 and 15

## Scope

Compare the simpler scope-specific chunk model plus embedding cache with unique
content/vector records and explicit occurrence membership. Choose a measured
branch/worktree count where the added complexity could pay off.

## Acceptance criteria

- [ ] Baseline and overlay runs use identical scopes, dirty snapshots and model profiles.
- [ ] Report disk/vector savings, update cost, query p95, filtered ANN recall and
  membership-resolution overhead.
- [ ] Authorization and snapshot filtering occur before ranking; cross-scope leakage
  remains zero even for identical content in different tenants.
- [ ] Cleanup and crash recovery preserve correct reference counts and occurrences.
- [ ] Adopt only if benefits justify complexity; an inconclusive result retains the MVP model.

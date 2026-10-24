# [F10] Harden rebuilds, lifecycle management and operational evidence

- Type: Feature
- Suggested labels: `feature`, `operations`
- Milestone: M4; basic safe diagnostics are required from M1
- Source: architecture 8-9, 12 and 13 phase 6; addendum 8 and 12

## Outcome

Operate versioned experiments safely, reclaim obsolete data without breaking live
scopes and measure whether more complicated deployment/storage models are needed.

## Children

- [ ] [F10-01 Rebuild and swap generations](./F10-01-rebuild-and-alias.md)
- [ ] [F10-02 Manage retention and reference-safe cleanup](./F10-02-retention-and-cleanup.md)
- [ ] [F10-03 Add operational metrics and scale profiles](./F10-03-observability-and-scale.md)

## Feature acceptance

- [ ] Rebuild failure preserves the active searchable generation and supports rollback.
- [ ] Cleanup cannot remove content referenced by live snapshots or active jobs/readers.
- [ ] Scale reports quantify performance, storage and coordination bottlenecks.

## Out of scope

Unmeasured distributed coordination, deleting sample checkouts and production UI.

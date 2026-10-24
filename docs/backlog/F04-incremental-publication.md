# [F04] Publish incremental, recoverable and isolated search snapshots

- Type: Feature
- Suggested labels: `feature`, `indexing`, `reliability`
- Milestone: M1
- Source: architecture 5-9 and 13 phases 1-2

## Outcome

Make repeated exports cheap and correct under deletions, dirty worktrees, partial
Elasticsearch failures and process crashes.

## Children

- [ ] [F04-01 Persist exporter manifests](./F04-01-exporter-state.md)
- [ ] [F04-02 Write canonical and chunk indexes](./F04-02-elasticsearch-writer.md)
- [ ] [F04-03 Detect changes and invalidation](./F04-03-incremental-diff.md)
- [ ] [F04-04 Publish and recover consistent snapshots](./F04-04-snapshot-recovery.md)

## Feature acceptance

- [ ] Unchanged reruns do not rewrite unchanged search documents.
- [ ] Deleted and invalidated occurrences disappear from the next published snapshot.
- [ ] Partial writes never become a visible complete snapshot or successful checkpoint.
- [ ] Recovery converges to the correct state with no cross-worktree leakage.

## Out of scope

TokenSave CDC assumptions, distributed coordinators and aggressive dedup overlays.

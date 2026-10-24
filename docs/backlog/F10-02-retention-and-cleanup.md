# [F10-02] Implement reference-safe scope, snapshot and cache retention

- Type: Child issue
- Parent: [F10](./F10-operations.md)
- Depends on: [F04-04](./F04-04-snapshot-recovery.md), [F02-02](./F02-02-authorized-scope.md)
- Suggested labels: `task`, `operations`, `privacy`
- Source: architecture 9.6, 12 and 15

## Scope

Define dirty-snapshot lifetime, cleanup ownership, grace periods and erasure policy.
Remove retired memberships first, then unreferenced canonical/chunk/vector data.
Coordinate cleanup with active jobs, readers and rollback generations.

## Acceptance criteria

- [ ] Dry-run output identifies exact scope/generation/cache targets and reference counts.
- [ ] Shared authorized content survives while any retained snapshot references it.
- [ ] Live indexing jobs, query readers and worktree MCP/SQLite users prevent unsafe cleanup.
- [ ] Tests cover stale worktrees, concurrent cleanup, retries and interrupted deletion.
- [ ] Destructive execution requires explicit authorization and a bounded target;
  sample source directories are never cleanup targets.
- [ ] Retention and erasure cover graphs, checkpoints, vectors and privacy-safe logs,
  with limitations recorded explicitly.

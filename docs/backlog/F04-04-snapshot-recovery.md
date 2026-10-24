# [F04-04] Publish consistent snapshots and recover across partial failures

- Type: Child issue
- Parent: [F04](./F04-incremental-publication.md)
- Depends on: [F04-03](./F04-03-incremental-diff.md), [F02-02](./F02-02-authorized-scope.md)
- Suggested labels: `task`, `reliability`, `scope`
- Source: architecture 8-9 and 13 phase 2

## Scope

Implement staged snapshot membership and a durable publication protocol spanning
Elasticsearch and SQLite. Choose a concrete visibility/refresh strategy that keeps
queries on the previous complete snapshot until the next is searchable.

## Acceptance criteria

- [ ] Fault injection before/during bulk writes, after downstream success, during
  publication and before checkpoint advancement yields recoverable state.
- [ ] Concurrent queries see one complete snapshot, never mixed old/new membership.
- [ ] Restart/retry converges without lost deletions or duplicate logical occurrences.
- [ ] Two dirty worktrees at the same HEAD remain isolated through publication.
- [ ] Concurrent TokenSave/source modification is blocked or detected and retried
  explicitly; no mixed source/graph revisions are accepted.
- [ ] State transitions and the lack of a cross-store atomic transaction are documented.

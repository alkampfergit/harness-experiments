# [F05-02] Expose exact structural navigation with resolution confidence

- Type: Child issue
- Parent: [F05](./F05-baseline-search.md)
- Depends on: [F03-01](./F03-01-tokensave-adapter.md), [F04-04](./F04-04-snapshot-recovery.md), [F02-02](./F02-02-authorized-scope.md)
- Suggested labels: `task`, `retrieval`, `graph`
- Source: architecture 3 and 10; addendum 3 and 6

## Scope

Implement symbol resolution, callers/callees, inheritance and implementation
queries over the correct TokenSave graph snapshot. Normalize graph nodes to
application occurrences when available.

## Acceptance criteria

- [ ] Relationship questions do not rely on embedding similarity to assert edges.
- [ ] Overload/ambiguous/unresolved cases retain confidence and candidate provenance.
- [ ] Graph results cannot cross worktree/snapshot boundaries or expose excluded sources.
- [ ] Missing/stale graph snapshots yield explicit diagnostics rather than reading
  a different active branch silently.
- [ ] Tests distinguish definitions, call sites, interfaces and implementations.

# [F02-02] Resolve authorized repository, worktree and published snapshot scope

- Type: Child issue
- Parent: [F02](./F02-privacy-and-scope.md)
- Depends on: [F01-03](./F01-03-application-contracts.md)
- Suggested labels: `task`, `privacy`, `scope`
- Source: architecture 9-10 and 12; addendum 7 and 9

## Scope

Implement a shared scope-resolution boundary for indexing and queries. Keep
repository, branch, worktree and snapshot IDs separate. Bind dirty states to a
content manifest rather than only Git HEAD.

## Acceptance criteria

- [ ] Unrecognized/unauthorized scopes and unpublished snapshots fail explicitly.
- [ ] Every retrieval arm, file lookup, docs/graph expansion and cache access receives
  the same validated authorization/snapshot context.
- [ ] Tests use identical content in different scopes and different dirty content
  at the same HEAD; no unauthorized occurrence or source text is returned.
- [ ] Scope checks occur before candidate retrieval/fusion, not just after ranking.
- [ ] Cross-tenant payload/vector deduplication is disallowed.

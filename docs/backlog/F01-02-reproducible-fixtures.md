# [F01-02] Prepare reproducible C# and documentation fixtures

- Type: Child issue
- Parent: [F01](./F01-foundations.md)
- Depends on: None
- Suggested labels: `task`, `testing`
- Source: architecture 1, 4, 13 phases 0-2 and 14; addendum 10

## Scope

Create a small synthetic test repository and a local preparation procedure for
approved pinned public corpora. Include overloads, generics, partial classes,
extension methods, interfaces, DI, async/cancellation and multi-file relationships.
Include sidecar/central Markdown, stale docs, two worktrees and dirty edits.

## Acceptance criteria

- [ ] Preparation takes sample path and commit and writes worktrees/indexes under
  `.index/`, never under the read-only sample checkout.
- [ ] Deterministic mutations cover add/edit/delete/rename/line shift, docs-only
  edits and changed documentation associations.
- [ ] Tests have known expected symbols, source extents and relationships, including
  unresolved/ambiguous cases and encoding/line-ending variants.
- [ ] Private sample URLs, contents, local inventories, gitlinks and generated data
  are not committed; public/synthetic fixture provenance is reproducible.

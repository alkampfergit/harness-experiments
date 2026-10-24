# [F03-01] Implement a read-only, version-checked TokenSave adapter

- Type: Child issue
- Parent: [F03](./F03-extraction-and-chunking.md)
- Depends on: [F01-01](./F01-01-platform-compatibility.md), [F01-02](./F01-02-reproducible-fixtures.md), [F01-03](./F01-03-application-contracts.md)
- Suggested labels: `task`, `indexing`
- Source: architecture 3, 8 and 13 phase 0

## Scope

Isolate all TokenSave table knowledge in one adapter. Resolve branch metadata,
open SQLite read-only and extract files, nodes, edges, doc associations and
unresolved/ambiguous relationships from a consistent snapshot.

## Acceptance criteria

- [ ] Branch metadata selects the active DB; malformed metadata or missing targets
  fail explicitly rather than silently reading a different database.
- [ ] Unsupported schema/version and corrupt/incomplete data have actionable errors.
- [ ] Adapter tests cover absent branch metadata, supported schema and mismatch cases.
- [ ] Read operations never mutate TokenSave or treat its last-sync time as a
  downstream publication cursor.
- [ ] Source/graph consistency is established through a documented snapshot/locking
  strategy; concurrent sync cannot produce mixed revision output.

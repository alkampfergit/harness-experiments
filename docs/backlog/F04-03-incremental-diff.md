# [F04-03] Detect file/chunk changes, deletions and dependent invalidation

- Type: Child issue
- Parent: [F04](./F04-incremental-publication.md)
- Depends on: [F04-02](./F04-02-elasticsearch-writer.md)
- Suggested labels: `task`, `indexing`, `reliability`
- Source: architecture 8

## Scope

Diff TokenSave file hashes against published manifests, regenerate only affected
descriptors, diff chunk sets and delete obsolete occurrences. Track documentation,
configuration and independently changing graph dependencies.

## Acceptance criteria

- [ ] Tests cover add/edit/delete/rename/line shifts and repeated no-op runs.
- [ ] Docs-only changes and changed associations invalidate their own and dependent
  code inputs even when `.cs` hashes remain unchanged.
- [ ] Extractor/chunker/exclusion/profile changes trigger the declared invalidation,
  while unchanged documents are not rewritten unnecessarily.
- [ ] Graph reconciliation does not infer edge correctness only from source hashes.
- [ ] Run counters expose files/chunks reassessed, upserted and deleted, enabling
  exact incremental-work assertions.
- [ ] A change to one method in a multi-method file does not rewrite unaffected
  chunks unless their searchable metadata actually changes.

# [F04-02] Implement canonical and chunk index mappings and idempotent bulk writes

- Type: Child issue
- Parent: [F04](./F04-incremental-publication.md)
- Depends on: [F04-01](./F04-01-exporter-state.md), [F03-03](./F03-03-deterministic-chunks.md)
- Suggested labels: `task`, `elasticsearch`
- Source: architecture 5-6 and 8

## Scope

Implement the store writer with independent file/chunk documents, deterministic
IDs, exact/analyzed fields and explicit generation metadata. Initially support
BM25-only mappings; vector mappings are added with compatible embedding profiles.

## Acceptance criteria

- [ ] File records and chunks are independently written, retrieved and deleted
  without nested-document or parent/child joins.
- [ ] Identifier analyzer tests cover case splitting, underscores, namespaces,
  generics and exact qualified-name lookup.
- [ ] Every Bulk API item is checked; retryable and permanent errors are distinguished.
- [ ] Retries are bounded/cancellable and idempotent; permanent failures are reported.
- [ ] Index creation/mapping conflicts fail explicitly and do not overwrite a
  currently published incompatible generation.

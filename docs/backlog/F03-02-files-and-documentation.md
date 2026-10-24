# [F03-02] Prepare canonical file revisions and companion documentation

- Type: Child issue
- Parent: [F03](./F03-extraction-and-chunking.md)
- Depends on: [F03-01](./F03-01-tokensave-adapter.md), [F02-03](./F02-03-sensitive-data-handling.md)
- Suggested labels: `task`, `indexing`, `documentation`
- Source: architecture 3-6 and 15

## Scope

Read matching source revisions and materialize canonical file records with symbol
outlines, language, hashes, profile metadata and documentation associations.
Choose and document the initial full-source storage policy.

## Acceptance criteria

- [ ] File IDs obey the shared identity policy and preserve authorization boundaries.
- [ ] Source slicing handles tested encodings/line endings and rejects invalid ranges.
- [ ] Sidecar and configured central-doc associations retain paths, `applies_to`,
  provenance and staleness without inventing method-level links.
- [ ] Markdown remains independent content; excluded paths cannot be reintroduced
  through documentation associations.
- [ ] Prepared files include enough source/metadata to rebuild chunks without
  rerunning TokenSave; protected-reference alternatives are explicitly deferred if unused.

# [F04-01] Persist independent exporter manifests and publication state

- Type: Child issue
- Parent: [F04](./F04-incremental-publication.md)
- Depends on: [F01-03](./F01-03-application-contracts.md)
- Suggested labels: `task`, `reliability`
- Source: architecture 8

## Scope

Create a separate SQLite exporter store for published files/chunks, profile versions,
index generation, snapshot IDs and job/retry status. Define transactional state
transitions and schema migration behavior without writing into TokenSave.

## Acceptance criteria

- [ ] File and chunk manifests are keyed by scope and stable occurrence/profile IDs.
- [ ] State distinguishes staged, failed and published batches.
- [ ] A checkpoint cannot be recorded as published before downstream success.
- [ ] Restart tests preserve the last published manifest and unfinished job information.
- [ ] Incompatible state schema/configuration is detected explicitly and has a
  documented migrate/rebuild path.

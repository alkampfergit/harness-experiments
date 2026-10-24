# [F10-01] Rebuild derived indexes and atomically switch validated generations

- Type: Child issue
- Parent: [F10](./F10-operations.md)
- Depends on: [F04-04](./F04-04-snapshot-recovery.md)
- Suggested labels: `task`, `operations`, `elasticsearch`
- Source: architecture 5-8 and 15; addendum 8

## Scope

Rebuild chunks/mappings and, when enabled, vectors from canonical prepared files
into a new physical generation. Validate before alias switch and retain rollback
information without requiring another TokenSave parse.

## Acceptance criteria

- [ ] Physical names encode technique, mapping/chunk profile and optional model/profile.
- [ ] Counts, required provenance, scope membership and vector compatibility are
  validated before publication.
- [ ] Failed builds leave the old alias and checkpoint active; alias-switch/restart
  fault tests converge correctly.
- [ ] Model/chunker/analyzer changes trigger appropriate rebuilds; reranker-only
  changes do not rebuild or re-embed.
- [ ] Rollback and old-generation retention are documented and exercised locally.

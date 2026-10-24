# [F05-01] Implement authorized BM25, exact-symbol and parent-file lookup

- Type: Child issue
- Parent: [F05](./F05-baseline-search.md)
- Depends on: [F04-04](./F04-04-snapshot-recovery.md), [F02-02](./F02-02-authorized-scope.md)
- Suggested labels: `task`, `retrieval`, `elasticsearch`
- Source: architecture 6, 10 and 13 phase 1; addendum 3 and 6

## Scope

Search identifier, qualified-name, signature, source and documentation fields.
Support exact symbols, general BM25, independent documentation hits and on-demand
canonical file lookup. Keep query intent and optional boosts transparent.

## Acceptance criteria

- [ ] Exact symbol/signature queries preserve identifiers and deterministic ordering.
- [ ] Source and docs hits include correct path/lines, file revision and snapshot.
- [ ] Every search and parent-file lookup filters by validated scope before ranking.
- [ ] Result limits, cancellation, empty/invalid input and unavailable Elasticsearch
  are handled explicitly.
- [ ] Baseline settings are recorded; any optional boosts/exclusion rules can be
  disabled for ablation and are not presented as measured improvements.

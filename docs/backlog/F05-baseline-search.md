# [F05] Deliver scoped lexical search and structural navigation

- Type: Feature
- Suggested labels: `feature`, `retrieval`
- Milestone: M1
- Source: architecture 1, 10-11 and 13 phase 1; addendum 5-7 and phase A

## Outcome

Provide a usable no-inference search path with precise citations and the same
authorized results through CLI, API and MCP.

## Children

- [ ] [F05-01 Implement BM25 and exact lookup](./F05-01-lexical-search.md)
- [ ] [F05-02 Implement structural navigation](./F05-02-structural-navigation.md)
- [ ] [F05-03 Expose consistent query interfaces](./F05-03-query-interfaces.md)

## Feature acceptance

- [ ] Search works without model artifacts or an inference server.
- [ ] Full-file lookup, docs search and relationship navigation preserve snapshot provenance.
- [ ] Invalid scope/input and unavailable dependencies produce explicit errors.

## Out of scope

UI work, semantic inference, automatic graph context expansion and hand-tuned boosts.

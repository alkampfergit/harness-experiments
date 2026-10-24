# [F09-01] Expand selected seeds through scoped graph and documentation links

- Type: Child issue
- Parent: [F09](./F09-context-assembly.md)
- Depends on: [F05-03](./F05-03-query-interfaces.md)
- Suggested labels: `task`, `graph`, `context`
- Source: architecture 4 and 10; addendum 3.2 and 6

## Scope

Use query intent to add bounded callers, callees, interfaces, implementations,
related tests and companion docs after seed selection. Run after reranking when
enabled, or after baseline ranking otherwise.

## Acceptance criteria

- [ ] Depth/count limits and allowed relationship types are explicit and tested
  against cycles and high-degree nodes.
- [ ] Every neighbor and doc lookup uses the seed's authorized published snapshot.
- [ ] Results distinguish retrieved seeds from expanded context and preserve edge
  confidence, doc staleness and association provenance.
- [ ] Expansion does not silently replace highly relevant seeds with weaker neighbors.
- [ ] Graph ranking boosts remain separate optional experiments, not hidden expansion behavior.
- [ ] Parent-file and documentation fetches cannot reintroduce excluded or unauthorized content.

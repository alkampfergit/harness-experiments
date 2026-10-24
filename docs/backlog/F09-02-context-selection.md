# [F09-02] Select distinct context within a measured token budget

- Type: Child issue
- Parent: [F09](./F09-context-assembly.md)
- Depends on: [F09-01](./F09-01-graph-document-expansion.md)
- Suggested labels: `task`, `context`
- Source: architecture 10; addendum 3.3 and 5

## Scope

Implement `IContextSelector` with deterministic file/symbol grouping, relevance
preservation, token budgeting and citations. Start with a simple distinctness
policy; make MMR a separate measured option rather than a required dependency.

## Acceptance criteria

- [ ] Count with the declared consumer tokenizer, including headers/citations;
  final output never exceeds the supplied budget, including small/zero-budget cases.
- [ ] Repeated overloads and overlapping fragments do not dominate by accident.
- [ ] Essential implementation/interface/docs combinations can coexist without
  arbitrary diversity replacing key evidence.
- [ ] Every excerpt retains path, line range, snapshot/revision and selection reason.
- [ ] Deterministic tests cover oversized chunks, duplicate docs, stale/ambiguous
  neighbors, no candidates and truncation/splitting policy.
- [ ] CLI/API/MCP expose the same selection behavior and diagnostics.

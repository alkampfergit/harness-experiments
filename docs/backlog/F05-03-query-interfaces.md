# [F05-03] Expose indexing and search through consistent CLI, API and MCP surfaces

- Type: Child issue
- Parent: [F05](./F05-baseline-search.md)
- Depends on: [F05-01](./F05-01-lexical-search.md), [F05-02](./F05-02-structural-navigation.md)
- Suggested labels: `task`, `api`, `mcp`
- Source: architecture 1 and 11; addendum 5-6 and 12

## Scope

Expose explicit sync/index and search/navigation operations using shared typed
contracts. Route exact-symbol, conceptual, relationship and documentation requests
transparently; no LLM classifier is required.

## Acceptance criteria

- [ ] Equivalent CLI/API/MCP requests select the same authorized snapshot and results.
- [ ] Outputs carry occurrence IDs, paths/lines, source revisions, strategy scores
  and stage diagnostics; errors are not success-shaped empty responses.
- [ ] All surfaces validate scope, limits and cancellation consistently.
- [ ] A documented local end-to-end example indexes a pinned fixture, searches it
  and retrieves a canonical parent file without inference.
- [ ] MCP harness examples use local models when sample content enters the harness.
- [ ] No Blazor/MudBlazor UI is required or introduced without a separate need.

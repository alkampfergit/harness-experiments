# [F11-02] Measure whether Roslyn resolves important C# retrieval gaps

- Type: Child issue
- Parent: [F11](./F11-deferred-research.md)
- Status: Deferred until structural/context baseline evidence exists
- Depends on: [F09-03](./F09-03-context-experiment.md)
- Suggested labels: `experiment`, `deferred`, `roslyn`
- Source: architecture 3, 13 phase 5 and 15

## Scope

Identify measured TokenSave resolution failures, then test a bounded Roslyn
enrichment adapter for compiler symbols and relationships without replacing the
multi-language extraction path.

## Acceptance criteria

- [ ] Select explicit overload/generic/partial/extension/alias cases from observed
  failures; do not promise DI runtime wiring can always be compiler-resolved.
- [ ] Preserve provenance between TokenSave and Roslyn identities/confidence.
- [ ] Measure relationship accuracy, retrieval/context changes, compilation/setup
  requirements, latency and resource cost on pinned corpora.
- [ ] Missing dependencies/compilation failures are reported explicitly; no invented edges.
- [ ] Record adopt/defer/reject and whether a separate graph index is justified.

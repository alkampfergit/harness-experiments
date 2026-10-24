# [F06-01] Curate a versioned C# query and relevance dataset

- Type: Child issue
- Parent: [F06](./F06-evaluation.md)
- Depends on: [F01-02](./F01-02-reproducible-fixtures.md)
- Suggested labels: `task`, `evaluation`
- Source: architecture 14; addendum 10

## Scope

Prepare shared queries and file/symbol relevance labels under `eval/` for pinned
approved corpora. Separate tuning and held-out sets and document judgment rules.

## Acceptance criteria

- [ ] Initial dataset contains 100-200 manually verified queries with stable
  relevant file/symbol identities and sample commit provenance.
- [ ] Coverage includes exact symbols, conceptual questions, DI, EF/LINQ,
  HTTP/async/cancellation, interfaces, overloads and multi-file bugs.
- [ ] Include docs-to-code, negative/near-miss, correctness/safety counterexamples,
  and branch/dirty-worktree isolation cases.
- [ ] Graded relevance supports nDCG; unknown judgments are not silently treated
  as verified negatives without a declared evaluation policy.
- [ ] Dataset and reports disclose no private sample source, URLs or confidential queries.

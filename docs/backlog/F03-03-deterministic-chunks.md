# [F03-03] Build versioned method, overview and Markdown chunks

- Type: Child issue
- Parent: [F03](./F03-extraction-and-chunking.md)
- Depends on: [F03-02](./F03-02-files-and-documentation.md), [F01-03](./F01-03-application-contracts.md)
- Suggested labels: `task`, `indexing`, `chunking`
- Source: architecture 6-7; addendum 3 and 8

## Scope

Implement deterministic method chunks, class/interface API summaries, member groups,
file overviews, block-aligned long-method fragments and heading-based Markdown
sections. Keep BM25 text separate from prepared embedding input.

## Acceptance criteria

- [ ] Identical input/profile produces identical IDs, hashes, order and extents.
- [ ] Long-method splitting preserves declared semantic boundaries and repeated
  context; class summaries do not duplicate every method body.
- [ ] Chunks record file revision, symbol identity, ordinal/extent, lines, docs
  dependencies and chunker/enrichment revisions.
- [ ] Initial size rules are explicit, not claimed optimal; future embedding
  profiles can enforce actual tokenizer limits.
- [ ] Tests cover missing symbols, non-C# entities, empty/oversized docs and overloads.
- [ ] Source-only versus XML/sidecar enrichment is configurable for later ablation,
  without blindly injecting full documentation into every method.

# [F07-01] Serve pinned, model-aware local embeddings behind IEmbedder

- Type: Child issue
- Parent: [F07](./F07-local-hybrid-search.md)
- Depends on: [F01-03](./F01-03-application-contracts.md), [F02-01](./F02-01-local-only-policy.md), [F02-03](./F02-03-sensitive-data-handling.md)
- Suggested labels: `task`, `embeddings`, `inference`
- Source: architecture 2, 7, 11 and 13 phase 3; addendum 5 and 12

## Scope

Implement the uv-managed FastAPI helper under `tools/embed-server/` and a C#
`IEmbedder` HTTP adapter. Start with one verified local model/profile; make C2LLM-0.5B,
BGE-Code-v1 and CodeRankEmbed reproducible comparison candidates, not assumed winners.

## Acceptance criteria

- [ ] `/v1/embeddings` distinguishes query/document input and reports pinned
  model/revision, output dimension and preparation settings.
- [ ] Required prefixes/templates, tokenizer limits, pooling and normalization are
  model-specific and tested; invalid/oversized inputs fail explicitly.
- [ ] Batches, authentication, bounded queues, timeout, cancellation and finite
  output/dimension validation are implemented without raw-input logging.
- [ ] CPU-only startup and fully offline inference work with pre-acquired artifacts.
- [ ] Dependencies and any reviewed custom model code are pinned; missing artifacts
  do not trigger downloads.
- [ ] Native MPS/MLX/CUDA and container CPU support are measured separately; no GPU
  dependency is introduced into the core exporter/search mode.

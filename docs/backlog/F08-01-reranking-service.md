# [F08-01] Serve a bounded, model-agnostic local reranking endpoint

- Type: Child issue
- Parent: [F08](./F08-local-reranking.md)
- Depends on: [F07-01](./F07-01-local-embedding-service.md)
- Suggested labels: `task`, `reranking`, `inference`
- Source: addendum 4-5 and phase B

## Scope

Expose `/v1/rerank` in the helper service or a separate compatible process.
Implement pinned GTE ModernBERT base and Qwen3-Reranker-0.6B comparison profiles
after verifying their actual runtime and score semantics.

## Acceptance criteria

- [ ] Requests identify query, distinct candidate IDs/text, model/profile, `top_n`
  and timeout; responses identify pinned revision and candidate-keyed finite scores.
- [ ] Validate empty/oversized inputs, duplicate IDs, top-N bounds and tokenizer limits.
- [ ] Model-specific prompts, output/logit semantics and batching stay in adapters;
  scores are not presented as calibrated probabilities without evidence.
- [ ] CPU mode, offline artifacts, authentication, cancellation and bounded resource
  use are tested.
- [ ] Embedding/reranking models are not automatically replicated into every worker;
  separate-process deployment and readiness behavior are documented.
- [ ] Optional ONNX/MPS optimization is adopted only with verified model compatibility.

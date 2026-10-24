# [F08-02] Integrate IReranker with validated outputs and explicit degradation

- Type: Child issue
- Parent: [F08](./F08-local-reranking.md)
- Depends on: [F08-01](./F08-01-reranking-service.md), [F07-03](./F07-03-hybrid-retrieval.md)
- Suggested labels: `task`, `reranking`, `reliability`
- Source: addendum 5-9 and phase B

## Scope

Add disabled/no-op and FastAPI `IReranker` implementations. Shortlist distinct
authorized candidates after RRF, preserve original scores/provenance and route
exact/structural questions without unnecessary cross-encoding.

## Acceptance criteria

- [ ] Candidate depth, text representation, template and timeout are versioned settings.
- [ ] Unknown/duplicate/missing IDs, nonfinite scores and invalid response shape cause
  explicit stage failure, not corrupted candidate ordering.
- [ ] Timeout/cancellation/provider failure yields declared pre-reranked degradation
  with privacy-safe diagnostics; caller cancellation is propagated appropriately.
- [ ] Replacing a reranker does not parse source, rebuild chunks or regenerate embeddings.
- [ ] Ranking traces distinguish raw retrieval, fusion and rerank stages.
- [ ] No score cache is required initially; if introduced, keys include authorization/
  snapshot, query hash, candidate-text hash and model/template revision.

# [F08] Add and evaluate optional local cross-encoder reranking

- Type: Feature
- Suggested labels: `feature`, `reranking`
- Milestone: M3, evidence-gated
- Source: addendum 2, 4-5, 8 and phase B

## Outcome

Improve precision within an already retrieved candidate pool while keeping
inference optional, bounded and independent of embedding/index versions.

## Children

- [ ] [F08-01 Serve local rerank models](./F08-01-reranking-service.md)
- [ ] [F08-02 Orchestrate validated optional reranking](./F08-02-reranking-orchestration.md)
- [ ] [F08-03 Measure quality versus inference cost](./F08-03-reranking-experiment.md)

## Feature acceptance

- [ ] Disabled/unavailable reranking never prevents the supported baseline mode.
- [ ] Inference failures are observable and preserve explicitly degraded pre-reranked results.
- [ ] Default adoption requires held-out quality gains within declared resource budgets.

## Out of scope

Reranking the whole repository, assuming scores are probabilities or hosted services.

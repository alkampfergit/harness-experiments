# [F11-01] Gate any future hosted embedding and reranking comparison

- Type: Child issue
- Parent: [F11](./F11-deferred-research.md)
- Status: Blocked by repository local-only policy
- Depends on: [F02-01](./F02-01-local-only-policy.md), [F06-03](./F06-03-baseline-experiment.md)
- Suggested labels: `research`, `deferred`, `privacy`
- Source: architecture 2.3, 11-13 phase 4; addendum 4, 9 and phase C

## Scope

Record a future product feasibility decision only. Do not implement or call hosted
providers under current scope. The historical shortlist includes Voyage Code 4,
Cohere Embed 5, Codestral Embed, Google/OpenAI embedding baselines, Voyage rerank-3/
lite and Cohere Rerank 5 Pro/Fast.

## Acceptance criteria for unblocking

- [ ] Obtain explicit authorization and resolve the conflict with current repository
  policy before implementation or payload-bearing provider calls.
- [ ] Reverify model IDs, availability, licenses, pricing, data retention/training,
  processing region and private-deployment options; inherited claims are not evidence.
- [ ] Separately approve embeddings, reranking candidate text and external query text;
  server-side defaults remain denied.
- [ ] Define approved corpus/data handling, endpoint allowlists, exclusions and cost limits.

## Conditional follow-on, only after unblocking

Add provider adapters behind existing interfaces and compare fixed local/hosted
representations, candidate pools, quality, latency, cost and failures. Never mix
incompatible vector profiles or infer one permission from another. Split substantial
provider implementation into separately approved child issues at that time.

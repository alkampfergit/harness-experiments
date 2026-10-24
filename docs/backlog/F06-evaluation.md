# [F06] Establish shared evaluation and a measured lexical baseline

- Type: Feature
- Suggested labels: `feature`, `evaluation`
- Milestone: M1
- Source: architecture 2.4 and 14; addendum 10

## Outcome

Judge every technique using the same corpus, relevance judgments, snapshots and
reproducible metrics before optimizing.

## Children

- [ ] [F06-01 Curate C# retrieval judgments](./F06-01-gold-set.md)
- [ ] [F06-02 Implement reproducible metric runs](./F06-02-evaluation-runner.md)
- [ ] [F06-03 Publish structural and BM25 baselines](./F06-03-baseline-experiment.md)

## Feature acceptance

- [ ] A manually verified initial set contains 100-200 queries and held-out judgments.
- [ ] Repeated runs preserve configuration/provenance and compute tested metric definitions.
- [ ] Baseline evidence includes quality, incremental correctness and resource costs.

## Out of scope

Declaring a model winner from vendor leaderboards or tuning on held-out answers.

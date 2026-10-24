# [F02] Enforce privacy and authorized scope throughout the pipeline

- Type: Feature
- Suggested labels: `feature`, `privacy`
- Milestone: M0, mandatory before processing real corpora
- Source: architecture 1, 9, 11-12; addendum 9

## Outcome

Make local-only operation and repository/worktree isolation enforceable server-side,
not assumptions made by callers or the frontend.

## Children

- [ ] [F02-01 Enforce offline inference and processing policy](./F02-01-local-only-policy.md)
- [ ] [F02-02 Resolve authorized scope and snapshots](./F02-02-authorized-scope.md)
- [ ] [F02-03 Protect payloads, secrets and diagnostics](./F02-03-sensitive-data-handling.md)

## Feature acceptance

- [ ] No sample payload leaves the machine in supported experiments.
- [ ] Unauthorized and cross-worktree retrieval attempts return zero leaked results.
- [ ] Every data-bearing store and diagnostic channel has an explicit protection policy.

## Out of scope

Authorizing hosted processing or treating development defaults as production security.

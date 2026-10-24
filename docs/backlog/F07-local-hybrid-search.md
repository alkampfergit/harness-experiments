# [F07] Add optional local embeddings and portable hybrid search

- Type: Feature
- Suggested labels: `feature`, `embeddings`, `retrieval`
- Milestone: M2, after the measured lexical baseline
- Source: architecture 2, 7-8, 10 and 13 phase 3; addendum 2, 7 and phase A

## Outcome

Add semantic recall without sacrificing offline operation, scope correctness or
the ability to use BM25 with no inference service.

## Children

- [ ] [F07-01 Serve pinned local embedding profiles](./F07-01-local-embedding-service.md)
- [ ] [F07-02 Cache and index compatible vectors](./F07-02-embedding-cache.md)
- [ ] [F07-03 Retrieve dense candidates and fuse with RRF](./F07-03-hybrid-retrieval.md)

## Feature acceptance

- [ ] Document/query formatting, dimensions and revisions match end to end.
- [ ] Unchanged inputs reuse embeddings within their authorized boundary.
- [ ] BM25, vectors-only and hybrid runs are compared on the shared held-out set.
- [ ] All retrieval arms enforce the same scope before fusion.

## Out of scope

Hosted APIs, assuming a C# model winner and dependency on native licensed RRF.

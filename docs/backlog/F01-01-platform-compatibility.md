# [F01-01] Verify and pin TokenSave, Elasticsearch and runtimes

- Type: Child issue
- Parent: [F01](./F01-foundations.md)
- Depends on: None
- Suggested labels: `task`, `foundation`
- Source: architecture 3, 6, 13 phase 0 and 15; addendum 7

## Scope

Inspect the installed TokenSave version/build, available languages, C# kinds and
actual SQLite schema. Pin the supported version and schema expectations. Select a
specific Elasticsearch 9 version with Basic-license BM25/kNN support and record
the .NET/Python/toolchain versions. Document unsupported native ranking features.

## Acceptance criteria

- [ ] Compatibility checks identify schema/version mismatches before import.
- [ ] Active DB resolution via branch metadata and absent-metadata fallback are verified.
- [ ] A local smoke test exercises authenticated BM25 and supported vector APIs
  without licensed native RRF/reranker dependencies.
- [ ] C# extraction, companion docs and branch/worktree behavior are recorded,
  including unsupported or ambiguous cases.
- [ ] Devcontainer/runtime pins and offline/network assumptions are documented;
  no production support for arbitrary Elasticsearch 7/8 versions is implied.

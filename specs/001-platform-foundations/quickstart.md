# Quickstart: validating Verified Platform Foundations

These scenarios prove the feature end to end. Run them inside the devcontainer, from the repository root. Command shapes are defined in [contracts/cli.md](./contracts/cli.md), and expected identities in [data-model.md](./data-model.md#mutation--identity-matrix-sc-006).

## Prerequisites

- The devcontainer is rebuilt after the postcreate change. `tokensave --version` prints `7.16.0`, and `curl -s -u elastic:$ELASTICSEARCH_PASSWORD $ELASTICSEARCH_URL` reports `9.5.5`.
- `git submodule update --init testrepo/cisharpai` has been run once (needs network).
- `dotnet --version` matches `global.json`.

## 1. Build and unit tests

```bash
dotnet build Polifemo.slnx
dotnet test Polifemo.slnx
```

Expected: all tests pass. The Elasticsearch tests run because `ELASTICSEARCH_URL` is set, and the corpus tests run because the submodule is initialized.

## 2. TokenSave compatibility (SC-001)

```bash
dotnet run --project src/Polifemo.Cli -- fixture build --fixture src/fixtures/synthetic-csharp --out .index/fixtures/synthetic-csharp
dotnet run --project src/Polifemo.Cli -- fixture prepare --sample .index/fixtures/synthetic-csharp --commit <sha from PROVENANCE.md> --name synth --index
dotnet run --project src/Polifemo.Cli -- platform check --worktree .index/work/synth
```

Expected:
- Exit code 0, `ok: true`, and `resolution` reported.
- Extra tables are listed under `warnings`.

The `Polifemo.Tests` suite (`Platform/` folder) builds broken copies under `.index/test/` and asserts one named error and exit code 1 for each:
- the wrong pinned version (`--expect-tokensave 0.0.0`): `TS_VERSION_MISMATCH`
- a dropped table: `TS_TABLE_MISSING`
- a dropped column: `TS_COLUMN_MISSING`
- malformed `branch-meta.json`: `TS_META_MALFORMED`
- `db_file` pointing at a missing file: `TS_DB_MISSING`

With `branch-meta.json` removed, the check resolves `tokensave.db` (`resolution: "default"`).

## 3. Search store smoke test

```bash
dotnet run --project src/Polifemo.Cli -- platform smoke
ELASTICSEARCH_PASSWORD=wrong dotnet run --project src/Polifemo.Cli -- platform smoke
```

Expected:
- First run: exit code 0, with version, license=basic, bm25, knn and cleanup all `ok`.
- Second run: exit code 3 with `ES_AUTH_FAILED`. The output contains no password.

## 4. Fixture determinism, isolation and worktrees (SC-003, SC-004)

```bash
git -C .index/fixtures/synthetic-csharp status --porcelain > /tmp/before   # sample stays clean
for run in a b; do
  dotnet run --project src/Polifemo.Cli -- fixture prepare --sample .index/fixtures/synthetic-csharp --commit <sha> --name synth-$run
  dotnet run --project src/Polifemo.Cli -- fixture mutate --worktree .index/work/synth-$run --mutation src/fixtures/synthetic-csharp/mutations/rename-file
done
git -C .index/work/synth-a rev-parse HEAD; git -C .index/work/synth-b rev-parse HEAD
git -C .index/fixtures/synthetic-csharp status --porcelain | diff /tmp/before -
dotnet run --project src/Polifemo.Cli -- fixture worktree --work .index/work/synth-a --name dirty --dirty-from edit-method
```

Expected:
- Both HEADs are identical.
- The `diff` is empty.
- The dirty worktree has uncommitted changes, and `synth-a` is unaffected.
- `fixture prepare` with an unknown commit exits with 1 and `PREP_UNKNOWN_COMMIT`, and no directory is created.

The same holds for `--sample testrepo/cisharpai --commit 8103c0d822211a60bb31815a28d354578be6bd6c`: `git -C testrepo/cisharpai status --porcelain` stays empty, and there is no `testrepo/cisharpai/.tokensave`.

## 5. Expectations and identities (SC-005, SC-006)

```bash
dotnet test --filter-trait "Category=Expectations"
dotnet test --filter-trait "Category=Identity"
dotnet test --filter-namespace "Polifemo.Tests.Corpus"
```

Expected:
- Every symbol, extent and relationship in the expectation records is checked.
- Unresolved, ambiguous and unsupported cases are reported with their status.
- The identity matrix holds for all mutations, and untouched symbols are unchanged.
- The cisharpai curated, invariant and snapshot tests pass.

## 6. Offline run (SC-002)

Disconnect the devcontainer from external networks while keeping the Compose network, for example with `docker network disconnect bridge <container>` or by blocking egress. Then repeat steps 2 to 5 and time them.

Expected: everything passes in under 10 minutes, and no command tries to download anything.

## 7. Nothing private committed (SC-007)

```bash
git ls-files | grep -E '^(samples/|\.index/)' ; git ls-files -s | awk '$1=="160000"'
grep -n 'samples/' .gitmodules 2>/dev/null
```

Expected:
- The first command prints nothing.
- The only gitlink is `testrepo/cisharpai`.
- `.gitmodules` lists only `testrepo/` entries, and `samples.local.md` is untracked.

## 8. Compatibility record (SC-008)

Open `docs/platform-compatibility.md`.

Expected: a reader finds within 5 minutes:
- every pinned version;
- the TokenSave language list;
- the C# kinds and edges;
- doc association behavior;
- branch and worktree behavior;
- the unsupported Elasticsearch features and why;
- the licenses;
- the offline and network assumptions.

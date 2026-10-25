# Quickstart: validating Verified Platform Foundations

These scenarios prove the feature end to end. Run them inside the devcontainer, from the repository root. Command shapes are defined in [contracts/cli.md](./contracts/cli.md), and expected identities in [data-model.md](./data-model.md#mutation--identity-matrix-sc-006).

## Prerequisites

- The devcontainer is rebuilt after the postcreate change. `tokensave --version` prints `7.16.0`, and `curl -s -u elastic:$ELASTICSEARCH_PASSWORD $ELASTICSEARCH_URL` reports `9.5.5`.
- `git submodule update --init testrepo/cisharpai` has been run once (needs network).
- `dotnet --version` matches `global.json`.
- Each online or offline pass uses a fresh `RUN_ID`, exported once and reused through steps 2 and 4. The fixture builder deletes its `--out` directory, and `fixture prepare` rejects an existing work name; use run-specific paths and do not delete prior `.index/` data.

## 1. Build and unit tests

```bash
dotnet build Polifemo.slnx
dotnet test Polifemo.slnx
```

Expected: all tests pass. The Elasticsearch tests run because `ELASTICSEARCH_URL` is set, and the corpus tests run because the submodule is initialized.

## 2. TokenSave compatibility (SC-001)

```bash
export RUN_ID="quickstart-$(date -u +%Y%m%dT%H%M%SZ)"
FIXTURE=".index/fixtures/synthetic-csharp-$RUN_ID"
WORK="synth-$RUN_ID"
dotnet run --project src/Polifemo.Cli -- fixture build --fixture src/fixtures/synthetic-csharp --out "$FIXTURE"
dotnet run --project src/Polifemo.Cli -- fixture prepare --sample "$FIXTURE" --commit <sha from src/fixtures/synthetic-csharp/PROVENANCE.md> --name "$WORK" --index
dotnet run --project src/Polifemo.Cli -- platform check --worktree ".index/work/$WORK"
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
set -euo pipefail
FIXTURE=".index/fixtures/synthetic-csharp-$RUN_ID"
WORK="synth-$RUN_ID"
git -C "$FIXTURE" status --porcelain > "/tmp/before-$RUN_ID"   # fixture stays clean
git -C testrepo/cisharpai status --porcelain > "/tmp/cisharpai-before-$RUN_ID"
for run in a b; do
  dotnet run --project src/Polifemo.Cli -- fixture prepare --sample "$FIXTURE" --commit <sha> --name "$WORK-$run"
  dotnet run --project src/Polifemo.Cli -- fixture mutate --worktree ".index/work/$WORK-$run" --mutation src/fixtures/synthetic-csharp/mutations/rename-file
done
git -C ".index/work/$WORK-a" rev-parse HEAD; git -C ".index/work/$WORK-b" rev-parse HEAD
git -C "$FIXTURE" status --porcelain | diff "/tmp/before-$RUN_ID" -
dotnet run --project src/Polifemo.Cli -- fixture worktree --work ".index/work/$WORK" --name "dirty-$RUN_ID" --dirty-from edit-method
set +e
unknown_output=$(dotnet run --project src/Polifemo.Cli -- fixture prepare --sample "$FIXTURE" --commit 0000000000000000000000000000000000000000 --name "unknown-$RUN_ID" 2>&1)
unknown_status=$?
set -e
printf '%s\\n' "$unknown_output"
test "$unknown_status" -eq 1
[[ "$unknown_output" == *PREP_UNKNOWN_COMMIT* ]]
test ! -e ".index/work/unknown-$RUN_ID"
dotnet run --project src/Polifemo.Cli -- fixture prepare --sample testrepo/cisharpai --commit 8103c0d822211a60bb31815a28d354578be6bd6c --name "cisharpai-$RUN_ID"
git -C testrepo/cisharpai status --porcelain | diff "/tmp/cisharpai-before-$RUN_ID" -
test ! -e testrepo/cisharpai/.tokensave
```

Expected:
- Both HEADs are identical.
- The `diff` is empty.
- The dirty worktree has uncommitted changes, and the base clone `.index/work/$WORK` is unaffected.
- `fixture prepare` with the unknown commit exits with 1 and `PREP_UNKNOWN_COMMIT`, and no `.index/work/unknown-$RUN_ID` directory is created.
- Preparing the pinned cisharpai submodule leaves its source status empty and creates no `testrepo/cisharpai/.tokensave`.

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

Disconnect the devcontainer from external networks while keeping the Compose network, for example with `docker network disconnect bridge <container>` or by blocking egress. Start a fresh `RUN_ID` for this pass, then repeat steps 2 to 5 and time them. Reuse that value in steps 2 and 4; all generated paths must be new.

Expected: everything passes in under 10 minutes, and no command tries to download anything.

## 7. Nothing private committed (SC-007)

```bash
python3 - <<'PY'
from pathlib import Path
import subprocess

tracked = subprocess.check_output(["git", "ls-files"], text=True).splitlines()
private_or_generated = [path for path in tracked if path.startswith(("samples/", ".index/"))]
entries = subprocess.check_output(["git", "ls-files", "-s"], text=True).splitlines()
gitlinks = []
for line in entries:
    fields = line.split(maxsplit=3)
    if fields[0] == "160000":
        gitlinks.append(fields[3])
module_paths = [
    line.strip().split("=", 1)[1].strip()
    for line in Path(".gitmodules").read_text().splitlines()
    if line.strip().startswith("path =")
]
sample_list_status = subprocess.check_output(
    ["git", "status", "--short", "--", "samples.local.md"], text=True
).strip()
assert not private_or_generated, private_or_generated
assert gitlinks == ["testrepo/cisharpai"], gitlinks
assert all(path.startswith("testrepo/") for path in module_paths), module_paths
print("tracked samples/.index:", private_or_generated)
print("gitlinks:", gitlinks)
print(".gitmodules paths:", module_paths)
print("samples.local.md status:", sample_list_status or "absent/clean")
PY
```

Expected:
- No tracked files are under `samples/` or `.index/`.
- The only gitlink is `testrepo/cisharpai`.
- `.gitmodules` lists only `testrepo/` entries. If private samples were used, `samples.local.md` remains untracked; it may be absent when no private samples are used.

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

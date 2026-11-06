# Quickstart: validating F03-01

Prerequisites: the devcontainer (.NET 10 SDK, `tokensave` 7.16.0 on PATH; the TokenSave-dependent tests fail, never skip, without it). Optional: `git submodule update --init testrepo/cisharpai` for the corpus tests (skipped locally when absent).

```sh
dotnet build Polifemo.slnx                                   # 0 warnings
dotnet test Polifemo.slnx                                    # all green, including Platform/TokenSave/*
tools/sonar-local.sh                                         # 0 finding(s)

# Operator view on a prepared worktree (never on samples/ or testrepo/ directly):
polifemo fixture prepare --sample testrepo/cisharpai --commit <pinned> --name cisharpai --index
polifemo platform snapshot --worktree .index/work/cisharpai  # ok:true, revision, counts, attempts:1
```

Expected outcomes per success criterion:

| Criterion | Evidence |
|---|---|
| SC-001 distinct, actionable errors, no partial data | `TokenSaveAdapterTests`: one test per code in [contracts/tokensave-adapter.md](contracts/tokensave-adapter.md); each asserts the code, the action text and that no snapshot was returned |
| SC-002 store unchanged | `TokenSaveAdapterTests.Store_is_byte_identical_after_read` (hash of every file under `.tokensave/`, empty sidecars excepted) |
| SC-003 no mixed revisions under concurrent sync | `SnapshotStrategyTests`: real `tokensave sync` runs in a loop while reads repeat; every result is a verified single revision or an explicit `TS_REVISION_UNSTABLE`/`TS_SOURCE_MISMATCH`; retry success is asserted with `Attempts > 1` on the seam test |
| SC-004 coverage and zero warnings/findings | absent metadata (single and ambiguous), supported schema, version mismatch tests; `dotnet build`, `dotnet test`, `tools/sonar-local.sh` |

Manual failure checks (each should print one `TS_*` code and exit 1):

Each check uses its own copy, so one failure cannot mask the next (a malformed `branch-meta.json` would stop the corrupt-database check at `TS_META_MALFORMED`).

```sh
rm -rf /tmp/w-meta /tmp/w-db

cp -r .index/work/cisharpai /tmp/w-meta && echo '{ nope' > /tmp/w-meta/.tokensave/branch-meta.json
polifemo platform snapshot --worktree /tmp/w-meta             # TS_META_MALFORMED

# Valid metadata; corrupt the database it selects (header overwritten, size kept, as T004 does).
cp -r .index/work/cisharpai /tmp/w-db
db=$(jq -r '.branches[.default_branch].db_file' /tmp/w-db/.tokensave/branch-meta.json)
head -c 100 /dev/urandom | dd of="/tmp/w-db/.tokensave/$db" bs=100 count=1 conv=notrunc status=none
polifemo platform snapshot --worktree /tmp/w-db               # TS_DB_CORRUPT
```

The prepared fixture worktree is checked out on the metadata's default branch; if it is not, pick the `db_file` of the branch the worktree is on.

Entities: [data-model.md](data-model.md). Strategy and codes: [contracts/tokensave-adapter.md](contracts/tokensave-adapter.md). Decisions: [research.md](research.md).

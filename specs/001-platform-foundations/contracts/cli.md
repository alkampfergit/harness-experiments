# Contract: `polifemo` CLI

These are the only executable entry points delivered by this feature. Every command:
- writes a single JSON document to stdout (`--format json`, the default) or a human summary (`--format text`);
- writes diagnostics to stderr, without credentials or sample source text;
- honors Ctrl+C as cancellation: it exits with code 130 and writes no partial success.

Exit codes are the same for every command:

| Code | Meaning |
|---|---|
| 0 | Success, no mismatches (warnings allowed) |
| 1 | The check ran and found mismatches or failed assertions |
| 2 | Invalid arguments |
| 3 | Environment error (missing tool, unreachable store, I/O) |
| 130 | Cancelled |

## `polifemo platform check --worktree <dir> [--expect-tokensave <ver>]`

This reads `<dir>/.tokensave/` only and never writes to it. `--expect-tokensave` defaults to the embedded pin, 7.16.0.

Output:

```json
{
  "ok": false,
  "tokensaveBinaryVersion": "7.16.0",
  "indexVersion": "7.15.0",
  "databasePath": ".tokensave/tokensave.db",
  "resolution": "branch-meta",
  "mismatches": [{ "code": "TS_INDEX_VERSION_MISMATCH", "subject": "metadata.last_full_index_version", "expected": "7.16.0", "actual": "7.15.0" }],
  "warnings": [{ "code": "TS_EXTRA_TABLE", "subject": "read_cache" }]
}
```

The error codes are listed in [tokensave-schema.md](./tokensave-schema.md) and [../data-model.md](../data-model.md#compatibilityreport-code).

## `polifemo platform smoke [--url] [--username]`

The defaults come from `ELASTICSEARCH_URL`, `ELASTICSEARCH_USERNAME` and `ELASTICSEARCH_PASSWORD`. The password is never accepted as an argument and never printed.

Output: `{ ok, serverVersion, license, checks: [{name: "version"|"license"|"bm25"|"knn"|"cleanup", ok, detail}] }`

Errors: `ES_UNREACHABLE` and `ES_VERSION_UNSUPPORTED` exit with 3. `ES_AUTH_FAILED` exits with 3, and its message names the URL and user only. A failed assertion exits with 1.

## `polifemo fixture build --fixture <dir> --out <dir-under-.index>`

This turns plain fixture files into a git repository with fixed dates. Output: `{ commit, treeSha }`. The command is deterministic: the same input gives the same commit.

## `polifemo fixture prepare --sample <repo> --commit <sha> --name <name> [--index]`

This clones into `.index/work/<name>/` as described in [research R5](../research.md#r5-preparing-a-sample-without-touching-it). `--index` also runs the pinned `tokensave init` in the clone. The command refuses any `--name` that resolves outside `.index/work/`.

Output: `{ workDir, commit, sourceUnchanged: true, indexed }`

Errors:
- `PREP_NOT_A_REPO` and `PREP_UNKNOWN_COMMIT` create nothing.
- `PREP_SOURCE_MODIFIED` is a hard failure.
- `PREP_TARGET_EXISTS` is raised unless `--replace` is given.

## `polifemo fixture worktree --work <dir> --name <name> [--dirty-from <mutation>]`

This adds a second git worktree **inside the clone**. It can optionally apply a mutation without committing, to make a dirty worktree.

## `polifemo fixture mutate --worktree <dir> --mutation <path-to-mutation-dir> [--no-commit]`

This applies the ops byte-exactly and, unless `--no-commit` is given, commits with fixed dates. Output: `{ base, commit, treeSha, changedPaths[] }`. If HEAD differs from the declared base it raises `MUT_BASE_MISMATCH`.

## Invariants

- No command writes under `samples/`, `testrepo/` or any `.tokensave/` directory it did not create in `.index/`.
- No command performs network I/O except `platform smoke`, which connects to the configured localhost store.

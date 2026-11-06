# Contract: TokenSave adapter (pin 7.16.0)

Extends [001 contracts/tokensave-schema.md](../../001-platform-foundations/contracts/tokensave-schema.md), which keeps the schema expectation and the compatibility codes. This contract fixes the adapter's public surface, its snapshot strategy and the new error codes.

## Public surface

```csharp
namespace Polifemo.Platform.TokenSave;

public sealed class TokenSaveAdapter
{
    public TokenSaveAdapter();                                   // pinned checker, git-based branch lookup
    public TokenSaveAdapter(CompatibilityChecker checker);      // test seam
    public Task<GraphSnapshot> ReadAsync(string worktree, SnapshotReadOptions? options = null, CancellationToken ct = default);
    public const int MaxRetries = 3;
}
```

- `ReadAsync` is the only way to obtain graph rows. `TokenSaveReader` is `internal`; no other type outside `Polifemo.Platform.TokenSave` names a TokenSave table or `TokenSaveReader` (a test, T015, scans `src/` for `FROM <tokensave table>` and `TokenSaveReader`). The only exemptions are the adapter's own white-box tests under `src/Polifemo.Tests/Platform/TokenSave/` (they reach `TokenSaveReader` through `InternalsVisibleTo`) and the synthetic fixture builder `src/Polifemo.Tests/Support/SyntheticTokenSave.cs`, which creates TokenSave tables; every other consumer is scanned.
- The method never creates, modifies, checkpoints or deletes any file under `.tokensave/`. The read transaction ends with `ROLLBACK`.
- Cancellation ends with `OperationCanceledException`, never an empty snapshot.

## Snapshot strategy (documented for FR-008)

1. Resolve the database (`branch-meta.json`, or `tokensave.db` when metadata is absent and it is the only `*.db`).
2. Record size and mtime of the database and of a non-empty `-wal`.
3. Open `Mode=ReadOnly`, busy timeout = `LockTimeout` (default 5 s).
4. `BEGIN` and a first `SELECT`: from here the connection sees one committed state (WAL snapshot, or SHARED lock in rollback mode).
5. Compatibility check (every mismatch reported), then all rows, inside that transaction. `ROLLBACK`.
6. Compare the file snapshot of step 2 with a new one. Different → a sync committed during the read → retry from step 1.
7. Verify the source as research R5 defines it: stat every `files.path`, hash every file and compare with `content_hash`, then stat every file again. A missing file, a hash mismatch or any stat change → retry from step 1 (unless `VerifySource = false`).
8. After 3 retries: `TS_REVISION_UNSTABLE` (step 6 kept failing) or `TS_SOURCE_MISMATCH` (step 7 kept failing). Otherwise return the `GraphSnapshot` with `Provenance.Attempts`.

Guarantee: every row of a snapshot comes from one SQLite commit state, and the graph was paired with source bytes that matched it at the end of one verification pass, with no stat change during that pass. Consumption rule (verified consumption): the snapshot hands out `files.content_hash` per path, not a frozen copy of the tree, so a consumer that later reads a source file (chunking, F03-02/F04) MUST hash the bytes it actually reads and compare them with that path's `ContentHash` before using them; any difference means the pairing is stale and the consumer discards its work and calls `ReadAsync` again. This makes every byte that leaves the pipeline provably part of the snapshot's revision, even for files changed after verification. Limits: a running sync's uncommitted work is invisible; a worktree edit reverted within one pass with identical size and mtime is not detected by step 7 (the consumption rule above catches it when the bytes are read); files TokenSave skipped are not verified; `.tokensave/` must allow SQLite to create its data-free sidecars, an empty `-wal` and a zero-frame `-shm` WAL index (FR-004 as reconciled in spec.md).

## Error codes added by this feature

| Code | Exception | When | Message must contain |
|---|---|---|---|
| `TS_META_ABSENT_AMBIGUOUS` | `ActiveDatabaseResolutionException` | metadata absent and `.tokensave/` holds several `*.db` or a single one not named `tokensave.db` | the candidate file names and "run `tokensave sync`" |
| `TS_DB_LOCKED` | `TokenSaveSnapshotException` | `SQLITE_BUSY`/`SQLITE_LOCKED` after `LockTimeout` | the timeout and "wait for `tokensave sync` to finish" |
| `TS_DB_CORRUPT` | `TokenSaveSnapshotException` | `SQLITE_NOTADB`, `SQLITE_CORRUPT`, `SQLITE_IOERR` on open or read | relative database path and "`tokensave sync -f`" |
| `TS_DATA_INCOMPLETE` | `TokenSaveSnapshotException` | a row cannot be represented (bad JSON candidates, NULL in a NOT NULL column) | table name and row key, no row content |
| `TS_REVISION_UNSTABLE` | `TokenSaveSnapshotException` | database changed during 4 consecutive attempts | attempt count |
| `TS_SOURCE_MISMATCH` | `TokenSaveSnapshotException` | worktree bytes ≠ graph hashes after 4 attempts | number of mismatched/missing files and up to 5 relative paths |

Existing codes keep their meaning: `TS_META_MALFORMED`, `TS_META_NO_DB`, `TS_DB_MISSING`, `TS_VERSION_MISMATCH`, `TS_INDEX_VERSION_MISMATCH`, `TS_TABLE_MISSING`, `TS_COLUMN_MISSING`, `TS_COLUMN_CHANGED`, `TS_UPLOAD_ENABLED`, `TS_DB_MODIFIED` (now only from `CompatibilityChecker.CheckAsync`; the adapter turns the same observation into a retry).

## Things the adapter never reads

`files.modified_at`, `files.indexed_at`, `nodes.updated_at`, `metadata.last_sync_at`, `metadata.last_full_sync_at`, `metadata.last_sync_duration_ms`, `branch-meta.json` `created_at`/`last_synced_at`, and TokenSave's FTS, vector, memory, `read_cache`, `node_fingerprints` and `trait_dispatch_callers` tables.

## CLI

`polifemo platform snapshot --worktree <dir> [--no-verify-source] [--lock-timeout <seconds>]` → JSON `{ ok, revision, counts{files,symbols,edges,unresolved,ambiguous,docAssociations}, integrity, tokensave{databasePath,resolution,metadataAbsent,indexVersion,binaryVersion,attempts,sourceVerified}, provenance }`; on failure `{ ok:false, code, message }` with exit code 1 (`TS_*`), 2 (usage) or 3 (environment). No row content is printed.

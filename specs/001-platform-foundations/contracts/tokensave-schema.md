# Contract: TokenSave schema expectation (pin 7.16.0)

TokenSave tables are not a public API (Constitution V). This contract fixes what the single read-only adapter relies on and how the check judges every difference. The machine-readable form is the embedded resource `expected-schema.v7.16.0.json`. It is generated from `PRAGMA table_info` on a database created by the pinned binary inside the devcontainer, and it replaces the observed list below if the two differ.

## Required tables and columns

Columns were observed on 2026-10-24 (macOS host, binary 7.16.0, index 7.15.0) and must be confirmed in the devcontainer.

| Table | Required columns |
|---|---|
| `files` | `path` TEXT PK, `content_hash` TEXT NN, `size` INTEGER NN, `modified_at` INTEGER NN, `indexed_at` INTEGER NN, `node_count` INTEGER NN, `kind` TEXT NN |
| `nodes` | `id` TEXT PK, `kind` TEXT NN, `name` TEXT NN, `qualified_name` TEXT NN, `file_path` TEXT NN, `start_line`, `end_line`, `start_column`, `end_column` INTEGER NN, `docstring` TEXT, `signature` TEXT, `visibility` TEXT NN, `is_async` INTEGER NN, `parent_id` TEXT, `attrs_start_line` INTEGER NN |
| `edges` | `id` INTEGER PK, `source` TEXT NN, `target` TEXT NN, `kind` TEXT NN, `line` INTEGER, `resolved_by` INTEGER |
| `unresolved_refs` | `from_node_id` TEXT NN, `reference_name` TEXT NN, `reference_kind` TEXT NN, `line` INTEGER NN, `col` INTEGER NN, `file_path` TEXT NN |
| `ambiguous_calls` | `from_node_id` TEXT NN, `reference_name` TEXT NN, `file_path` TEXT NN, `line` INTEGER NN, `candidate_node_ids` TEXT NN |
| `metadata` | key/value rows. Required key: `last_full_index_version` |

## Rules

| Difference | Result | Code |
|---|---|---|
| `tokensave --version` ≠ pin | fail | `TS_VERSION_MISMATCH` |
| `metadata.last_full_index_version` ≠ pin, or missing | fail (hint: full re-index) | `TS_INDEX_VERSION_MISMATCH` |
| required table missing | fail | `TS_TABLE_MISSING` |
| required column missing or renamed | fail | `TS_COLUMN_MISSING` |
| declared type or NOT NULL changed on a required column | fail | `TS_COLUMN_CHANGED` |
| extra table (e.g. FTS, `vectors`, `memory_*`, `read_cache`) | pass + warning | `TS_EXTRA_TABLE` |
| extra column in a required table | pass + warning | `TS_EXTRA_COLUMN` |
| database size/mtime (incl. `-wal`) changed during the check | fail | `TS_DB_MODIFIED` |

Database resolution codes (`TS_META_MALFORMED`, `TS_META_NO_DB`, `TS_DB_MISSING`) follow [research R3](../research.md#r3-active-database-resolution).

Every mismatch is reported; the check does not stop at the first one. The check runs before any import, and nothing reads `nodes`/`edges` content until `ok == true`.

## Upgrading the pin

To move to a new TokenSave version, regenerate the resource and update the pin and sha256 in `postcreate.sh`, the fixture expectations, the corpus snapshot and the compatibility record. All of this is one reviewed change.

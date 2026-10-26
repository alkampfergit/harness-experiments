# Data Model: Verified Platform Foundations

These are the entities from [spec.md](./spec.md) Key Entities. Encodings and hash inputs are in [contracts/identities-and-hashes.md](./contracts/identities-and-hashes.md). Only the entities marked **(code)** become C# types in this feature. The others exist as written contracts or files (FR-028).

## PlatformCompatibilityRecord (document + embedded JSON)

The record is `docs/platform-compatibility.md` for humans, plus `expected-schema.v7.16.0.json` for the check.

| Field | Content |
|---|---|
| pins | TokenSave 7.16.0 (+ sha256 per arch), Elasticsearch 9.5.5 Basic, .NET SDK (global.json), Python 3.12, git, uv |
| expectedSchema | required tables → columns (name, declared type, notnull) |
| extraction | C# node kinds and edge kinds observed; companion-doc association behavior (sidecar, `docs_dir` + `applies_to`) |
| branchBehavior | branch-meta shape, per-branch DB files, worktree behavior |
| unsupported | observed ambiguous or unsupported cases; ES licensed retrievers (RRF, text_similarity_reranker) |
| license | TokenSave, Elasticsearch (Basic/ELv2/AGPL as applicable), .NET, Python |
| network | what needs network once (image pull, binary download, submodule fetch), what must run offline, upload counter disabled |

## CompatibilityReport (code)

`{ tokensaveBinaryVersion, indexVersion, databasePath, resolution: "branch-meta"|"default", mismatches[], warnings[], ok }`. A mismatch is `{ code, subject, expected, actual }`.

The codes are:
- `TS_VERSION_MISMATCH`
- `TS_INDEX_VERSION_MISMATCH`
- `TS_TABLE_MISSING`
- `TS_COLUMN_MISSING`
- `TS_COLUMN_CHANGED`
- `TS_META_MALFORMED`
- `TS_META_NO_DB`
- `TS_DB_MISSING`
- `TS_DB_MODIFIED`

Validation: `ok == (mismatches.Count == 0)`. Warnings never change `ok`.

## Fixture

`{ name, kind: synthetic|public, source (path or URL), commit, license, mutations[], expectations[] }`

- The synthetic fixture records its provenance in `src/fixtures/synthetic-csharp/PROVENANCE.md`. The deterministic commit SHA is asserted.
- Public corpora record theirs in `testrepo/corpora.json`.
- Validation: the commit is a full 40-hex SHA, and the license is non-empty.

## Mutation

`mutation.json`: `{ name, base: <state name or commit>, ops[] }`. Each op is one of:
- `{ op: "write", path, payloadFile }`
- `{ op: "delete", path }`
- `{ op: "rename", from, to }`
- `{ op: "insertLines", path, atLine, payloadFile }`
- `{ op: "replaceBytes", path, payloadFile }`

Validation:
- The paths are relative and stay inside the worktree.
- The base must equal the current HEAD state.
- Applying the same mutation twice from the same base gives the same tree SHA and the same commit SHA.

## ExpectationRecord

The schema is in [contracts/expectation-record.schema.json](./contracts/expectation-record.schema.json). For each fixture state it holds:
- `symbols[]` `{path, qualifiedName, kind, signature?, startLine, endLine}`
- `relationships[]` `{from, to, kind, status: resolved|unresolved|ambiguous|unsupported, candidates?}`
- `docAssociations[]` `{doc, appliesTo[], stale?}`
- `files[]` `{path, encoding, lineEnding, extracted: bool}`

A test that finds an `unresolved` or `ambiguous` case reports it as that case. It is never counted as a pass or a missing item.

## SourceScope (code)

`{ authorizationBoundary, repositoryId, branchRef, worktreeId, snapshotId }`, all non-empty strings. Authorization is carried, not enforced (F02).

## FileRevision (contract; code only for `FileHash`)

`{ fileHash, path, language, encoding, symbolOutline[], docAssociations[], extractorVersion }`. The revision is immutable: the same bytes give the same `fileHash`, whatever the path or scope.

## ChunkOccurrence (code: `LogicalKey`, `OccurrenceKey`, `ChunkHash`)

`{ occurrenceKey, logicalKey, kind, ordinal, fileHash, scope: SourceScope, path, startLine, endLine, signature, docLinks[], provenance: { tokensaveNodeId, lines } }`

`tokensaveNodeId` and the lines are provenance only and never feed a key.

## ProfileVersions (code)

`{ extractorVersion, chunkerVersion, mappingVersion, embeddingProfileId, inputTemplateVersion }`. Each is an independent string. Which hash depends on which version:

| Version | LogicalKey | FileHash | ChunkHash | EmbeddingInputHash |
|---|---|---|---|---|
| extractorVersion | – (kind/qname may change as data, not as an input) | – | via fields | – |
| chunkerVersion | – | – | ✔ | – |
| mappingVersion | – | – | ✔ | – |
| embeddingProfileId | – | – | – | ✔ |
| inputTemplateVersion | – | – | – | ✔ |

## EmbeddingProfile (code)

`{ provider, model, revision, documentPrefix, queryPrefix, tokenizer, maxTokens, dimension, pooling, normalize }`. `profileId` = H(canonical fields).

Validation: dimension > 0, maxTokens > 0, and revision is an immutable ref, never `main`. `EnsureSameSpace(queryProfile, indexProfile)` throws `EmbeddingProfileMismatchException` when the profile ids differ (FR-027).

## PublicationBatch (contract only)

`{ batchId, scope, snapshotId, profileVersions, upserts[], deletes[] }`. It is defined for F04, and no code is written in this feature.

## CitedSearchResult (contract only)

`{ occurrenceKey, snapshot: SnapshotProvenance, path, startLine, endLine, snippet, fileHash, strategies[{name, rank, score}] }`.

## SnapshotProvenance (code)

`{ scope, snapshotId, sourceCommit, extractorVersion }`. Every result type in the code carries it.

## Mutation × identity matrix (SC-006)

The matrix is for the symbol(s) touched by each mutation. "same" means unchanged, "new" means a different value, and "–" means not applicable. The identity tests assert every cell, and also assert that **untouched** symbols keep all their values.

| Mutation | LogicalKey | FileHash (content rev.) | OccurrenceKey | ChunkHash | EmbeddingInputHash | Location (lines) |
|---|---|---|---|---|---|---|
| add-file | new (added) | new | new | new | new | new |
| edit-method (body) | same | new | same | new | new | same or new |
| delete-file | removed | removed | removed | removed | removed | – |
| rename-file (same bytes) | new (path) | same | new | new | **same** | same |
| line-shift (insert above) | same | new (file) | same | new only if lines are a search field, else same | same | **new** |
| docs-only-edit | same | same (source) | same | new (doc fields) | same unless doc excerpt is in input | same |
| change-doc-association | same | same | same | new (docLinks) | same | same |
| rename-and-edit (edge) | new | new | new | new | new | – |
| same file in two scopes | same | same | **different** | same | same | per scope |

Note on line-shift: `startLine`/`endLine` are provenance and are **not** chunk-hash inputs, so the chunk hash stays the same. This is fixed in the contract.

# Contract: identities and hashes

This contract covers FR-019 to FR-023 and FR-027. The types marked (code) live in `src/Polifemo.Contracts` and are used by the identity tests and the CLI. Nothing else is implemented.

## Canonical encoding

`Enc(f1..fn)` = for each field: 4-byte big-endian length followed by its UTF-8 bytes (raw bytes for byte fields).
`H(tag, f1..fn)` = lowercase hex SHA-256 of `Enc(tag, f1..fn)`. Each tag carries its scheme version (`lk/v1`, ...). Changing any rule below requires a new tag.

Normalization:
- **path**: relative to the repository root, `/` separators, no leading `./`, case kept, NFC.
- **signature**: TokenSave `signature` with whitespace runs collapsed to one space and trimmed. An empty signature becomes `""`.
- **kind**: the TokenSave kind string as-is (the observed set is recorded in the compatibility record).

## Keys and hashes

| Name (code) | Definition | Inputs explicitly excluded |
|---|---|---|
| `LogicalKey` | `H("lk/v1", repoId, path, qualifiedName, kind, disambiguator)`. `disambiguator` = signature. If more than one symbol in the same file shares (qualifiedName, kind, signature), the disambiguator is `signature + "#" + ordinal`, where ordinal is the 0-based source order among those symbols. | line/column numbers, TokenSave node id, scope, content |
| `FileHash` (content revision) | `H("fh/v1", rawBytes)` | path, encoding label, scope |
| `OccurrenceKey` | `H("oc/v1", logicalKey, authorizationBoundary, repoId, branchRef, worktreeId, snapshotId)` | lines, content |
| `ChunkHash` | `H("ch/v1", chunkerVersion, mappingVersion, logicalKey, canonicalJson(searchFields + metadata))` | `startLine`/`endLine` (provenance), TokenSave id, embedding profile |
| `EmbeddingInputHash` | `H("eh/v1", embeddingProfileId, inputTemplateVersion, inputRole, preparedInputBytes)` | path, scope, chunker/mapping versions (they act only through the prepared bytes) |
| `EmbeddingProfileId` | `H("ep/v1", provider, model, revision, documentPrefix, queryPrefix, tokenizer, maxTokens, dimension, pooling, normalize)` | — |

`canonicalJson` means keys sorted ordinally, no insignificant whitespace, numbers in invariant culture, and UTF-8.

Note on the embedding cache key: it is not defined here. It is `EmbeddingInputHash` plus the authorization boundary (Constitution IV), and belongs to F07.

## Behavior rules

- **Rename**: produces a new `LogicalKey` and `OccurrenceKey`. `FileHash` and `EmbeddingInputHash` stay the same, so the vector can be reused.
- **Line movement**: `LogicalKey`, `OccurrenceKey` and `ChunkHash` stay the same; only provenance lines change.
- **Same bytes in two scopes**: one `FileHash`, two `OccurrenceKey`s.
- **Partial classes in different files**: the paths differ, so the keys differ.
- **Nested generic types with the same simple name**: `qualifiedName` keeps them apart. A test asserts this.

## Profile compatibility (code)

`EmbeddingProfile.EnsureSameSpace(query, index)` throws `EmbeddingProfileMismatchException(Code = "EMB_PROFILE_MISMATCH", expected, actual)` when the profile ids differ. Vector search callers must call it before issuing a query. The test asserts that the exception is thrown and that no result is returned.

## Errors and provenance (code)

`PolifemoException(Code, Message)` has these subclasses:
- `TokenSaveCompatibilityException`
- `ActiveDatabaseResolutionException`
- `SearchStoreException`
- `FixturePreparationException`
- `EmbeddingProfileMismatchException`

`SnapshotProvenance(SourceScope scope, string snapshotId, string sourceCommit, string extractorVersion)`.

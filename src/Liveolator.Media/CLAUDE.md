# Liveolator.Media — module rules

**Purpose:** the data/persistence layer — filesystem enumeration, the JSON catalog
cache, and playlist file writing.

**Design source of truth:** [`docs/13`](../../docs/13-data-and-persistence.md).

## Iron rules

1. **Implements Core seams** (`IFileEnumerator`, catalog store). Persistence concerns
   stay here, out of `Liveolator.Core`.
2. **On-disk formats (JSON catalog, playlist files) are a contract.** Changes must
   preserve backward compatibility / be reversible (global standards #20/#22).
3. **All IO is wrapped with handling + logging** — a single unreadable file/folder is
   skipped, never crashes the operation, and is never silently lost (global #16/#26).

**Tests:** `tests/Liveolator.Media.Tests`.

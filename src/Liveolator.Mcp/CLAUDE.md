# Liveolator.Mcp — module rules

**Purpose:** an MCP server that exposes music-intelligence tools (library, analysis,
harmonic, playlist, visual) to external AI agents.

**Design source of truth:** [`docs/17`](../../docs/17-mcp-agent-interface.md).

## Iron rules

1. **Thin adapter layer over Core.** Tools call `Liveolator.Core` services; no domain
   logic is duplicated or reimplemented here.
2. **`Contracts/` are serializable DTOs and form the agent-facing API.** Keep them
   stable and predictable — consistent success/error shapes (global standard #23).
3. **Validate agent/external input before calling Core** (global standard #19); handle
   and log tool failures rather than letting them escape raw.

**Tests:** add under `tests/Liveolator.Mcp.Tests` (none yet).

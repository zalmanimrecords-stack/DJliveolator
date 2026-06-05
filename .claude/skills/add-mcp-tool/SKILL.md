---
name: add-mcp-tool
description: Add a new music-intelligence MCP tool to Liveolator.Mcp for external AI agents — an attributed tool method over a Core service, returning a serializable Contracts DTO with validated input. Use when exposing a Core capability (library, analysis, harmonic, playlist, visual) to agents, adding an MCP tool/endpoint, or extending the agent-facing API.
---

# Add an MCP tool

`Liveolator.Mcp` is a **thin adapter over `Liveolator.Core`** that exposes
music-intelligence to external AI agents (module `CLAUDE.md`). A tool is an attributed
method that validates its input, calls a Core service via an injected session, and
returns a serializable `Contracts` DTO.

Authoritative design: [`docs/17`](../../../docs/17-mcp-agent-interface.md).
Pattern to copy: `src/Liveolator.Mcp/Tools/LibraryTools.cs`.

## How discovery works (don't hand-register)

`Program.cs` calls `.WithToolsFromAssembly()`, so tools are found by attribute — there is
**no manual tool list to update**. You only register a *service/session* if you add one
(in `ServiceRegistration.cs`).

## Steps

1. **Pick the home class.** Add the method to the existing `[McpServerToolType]` class for
   the concern (`LibraryTools`, `AnalysisTools`, `HarmonicTools`, `PlaylistTools`,
   `VisualTools`). Create a new `[McpServerToolType]` class only for a genuinely new
   concern.

2. **Write the tool method**, following the house style:
   - annotate with `[McpServerTool(Name = "snake_case_name")]` and a thorough
     `[Description(...)]` — the description is the agent's only guide, so state what it
     does, prerequisites ("scan first"), and failure behavior;
   - inject the relevant **session** (e.g. `LibrarySession`, `VisualSession`) as the first
     parameter and a trailing `CancellationToken`;
   - annotate each parameter with `[Description(...)]`; give sane defaults;
   - **return a `Contracts` DTO** (or `IReadOnlyList<DTO>`), never a raw Core domain type —
     map via the DTO's `From(...)` factory.

3. **Validate agent input** before calling Core (global standard #19): unknown
   enum/filter/sort values throw `ArgumentException` with an **actionable** message naming
   the valid options (see `ListTracks`); clamp paging (`Math.Clamp(limit, 1, 1000)`).

4. **Call Core through the session — no business logic here.** Sorting/filtering of an
   in-memory snapshot is fine; real analysis/algorithms belong in Core.

5. **Add a Contracts DTO** if needed under `Contracts/`: a serializable record with a
   `From(coreType)` mapper. Treat existing DTO shapes as a **stable contract** — extend
   additively, don't break fields (module `CLAUDE.md`, global standard #23).

6. **Register a new service/session** in `ServiceRegistration.cs` only if the tool needs
   one not already provided.

## Guardrails

- Logs go to **stderr** — stdout carries only the stdio protocol (see `Program.cs`). Never
  `Console.WriteLine` to stdout from a tool.
- Don't duplicate Core logic into the tool layer; the tool orchestrates and shapes output.
- Keep responses predictable: consistent success shape, actionable errors.

## Validate

```powershell
dotnet build
dotnet test
```
Then smoke-test the tool over stdio with an MCP client / agent.

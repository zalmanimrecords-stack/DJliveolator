# 09 — Permissions and roles

- **Purpose:** who is allowed to do what, where that is enforced, and where the enforcement is weaker than it looks. The permission matrix in this documentation set lives only here.
- **Scope:** authentication, authorisation, trust and capability gating visible in code.
- **Source of truth:** `src/Liveolator.Media/Extensions/**`, `src/Liveolator.Core/Settings/**`, `src/Liveolator.Core/Legal/**`, `src/Liveolator.Mcp/Program.cs`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High — the absence of a multi-user authorisation model is a positive finding, not an unverified gap.
- **Related:** [side effects](./05-integrations-and-side-effects.md) · [open questions](./11-open-questions-and-assumptions.md) · [improvements](./14-final-improvement-report.md)

## Authorisation posture

Liveolator is a local desktop application plus a local MCP stdio process. The code defines no
accounts, no tenants, no server-side roles and no per-user resource ownership. Anyone who can run the
process under an operating-system account acts with that account's file, device and network
authority. UI visibility is therefore a usability choice, never a security boundary.

What exists instead are capability and trust controls, plus one consent gate.

## Capability matrix

| Subject | Capability | Gate | Enforcement point |
| --- | --- | --- | --- |
| Any local user | Run the application at all | Terms of use accepted at the current version | `App.axaml.cs` `EnforceTermsAcceptanceAsync`, `TermsOfUse.CurrentVersion`, `LegalSettings` |
| Any local user | Read, write and delete their own media and catalog | None beyond the OS account | Filesystem |
| An extension package | Have its content loaded | Structure, contained paths, hashes, signature, declared dependencies | `ExtensionPackageValidator` |
| An extension package | Be installed at all | Publisher present in the trusted-publisher store | `JsonTrustedPublisherStore`, `ExtensionInstaller` |
| An extension package | Bypass publisher trust | Developer mode enabled in settings | `ExtensionSettings` |
| The application | Query an online metadata provider | API credentials configured, helper executable present | `OnlineSettings`, `Liveolator.Online` |
| The application | Use a MIDI or audio device | Device selected and successfully opened | `MidiControlSession`, audio engine startup |
| An external agent | Read, analyse and author through MCP | Whatever the host process is configured with; no per-tool check | `src/Liveolator.Mcp/Program.cs` |

## Security-relevant observations

- **Extension validation is the only real trust boundary in the product,** and developer mode
  deliberately weakens it. That is a legitimate design, but the reduced guarantee needs to stay
  visible to the operator: `Needs validation` on whether the current UI makes that clear.
- **The MCP process inherits the host's filesystem authority** and applies no path scoping or
  request limits of its own. That is acceptable while the transport is local stdio. Changing it to a
  network transport would require authentication, authorisation, request limits and explicit path
  scoping before it is safe.
- **Native plugin and helper execution widens the surface.** Shader compilation, FFmpeg and Python
  subprocesses, and any VST3 bridging run with the same authority as the app. They should stay
  isolated, time-bounded and least-privileged.
- **API keys live in settings.** `Needs validation` on how they are stored and whether provider
  responses or keys can reach the log file.
- **No enforcement differs between the UI and a backend,** because there is no backend. The usual
  "the UI hides it but the API allows it" class of finding does not apply; the equivalent risk here
  is that a destructive operation is guarded only by a dialog — see the library-repair item in
  [11](./11-open-questions-and-assumptions.md).

## Data handled

The product persists absolute media paths, track metadata and analysis, cue and play history,
settings, mappings, projects, visual programmes, fingerprint caches, recordings, renders, extension
trust state and logs. Online enrichment transmits fingerprints and track-identifying metadata to the
configured providers. Storage locations and rules are in
[05](./05-integrations-and-side-effects.md); the missing retention, deletion and redaction policy is
tracked in [11](./11-open-questions-and-assumptions.md) and proposed in
[14](./14-final-improvement-report.md).

# Permissions and Roles

- Last updated: 2026-08-01
- Scope analyzed: Authentication, authorization, trust, and local capability restrictions visible in code.
- Confidence note: High: no multi-user authorization model is implemented in the analyzed desktop architecture.

## Authorization posture

Liveolator is a local desktop application and MCP stdio process. The code does not define accounts, tenants, server-side roles, or per-user resource ownership. Consequently, UI visibility is not a security boundary, and anyone who can run the process under the operating-system account generally acts with that account's file, device, and network permissions.

The meaningful authorization-like rules are capability and trust controls:

- Extension packages are checked against publisher trust, cryptographic integrity, dependencies, and developer-mode policy.
- Online enrichment is available only when the relevant API credentials and helper executable are configured.
- Device operations require a selected and successfully opened MIDI/audio device.
- MCP tools expose the host process's configured catalog and filesystem authority; no business-role checks are evident.
- Terms acceptance/settings may gate presentation or startup workflow, but they are not an operating-system security boundary.

## Needs validation

- Whether a distributed MCP host is ever exposed beyond local stdio. If changed to a network transport, explicit authentication, authorization, and path scoping would be required.
- Whether add-on developer mode is sufficiently prominent about its reduced trust guarantees.

## Code References

- `src/Liveolator.Media/Extensions/ExtensionPackageValidator.cs`
- `src/Liveolator.Media/Extensions/JsonTrustedPublisherStore.cs`
- `src/Liveolator.Core/Settings/ExtensionSettings.cs`
- `src/Liveolator.Core/Settings/OnlineSettings.cs`
- `src/Liveolator.Mcp/Program.cs`
- `src/Liveolator.Core/Legal/TermsOfUse.cs`

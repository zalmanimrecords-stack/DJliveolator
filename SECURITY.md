# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue for security vulnerabilities.

Instead, report privately through one of these channels:

- **GitHub private advisory** (preferred): go to the
  [Security tab](https://github.com/zalmanimrecords-stack/DJliveolator/security/advisories/new)
  and open a draft advisory.
- **Email:** simon@dorix.co.il

Please include steps to reproduce, the affected version or commit, and the potential
impact. You'll get an acknowledgement as soon as reasonably possible; because this is
a solo-maintained project, there is no formal response-time guarantee, but every
report is taken seriously.

## Scope

Liveolator is a desktop performance application. The most relevant concerns are file
and catalog handling, the optional online-metadata lookups, and the MCP server
(`Liveolator.Mcp`) when exposed over HTTP. Note that the **BASS** native libraries
are a third-party dependency fetched from un4seen and are outside this repository's
scope — report issues in BASS to <https://www.un4seen.com/>.

## Supported versions

This is an actively developed project with a single maintained line (`main`).
Security fixes land on `main`; there are no separately maintained release branches.

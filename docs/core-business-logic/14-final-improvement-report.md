# 14 — Improvement report

- **Purpose:** opportunities and gaps, classified by how much evidence stands behind each. Nothing here describes current behaviour; nothing here has been implemented.
- **Scope:** product, security, operations and delivery. Code-structure work is in [15](./15-refactor-recommendations.md).
- **Last validated:** 2026-08-02 (against commit `6a32b80`; items 5 and 10 revised after code verification)
- **Confidence:** the classification of each item states its own evidence base.
- **Related:** [UI coverage](./06-ui-feature-coverage.md) · [open questions](./11-open-questions-and-assumptions.md) · [hotspots](./10-business-logic-hotspots.md)

## Evidence-based gaps

**1. Autopilot is finished and unreachable.**
*Evidence:* no reference to `AutopilotEngine` outside `Core/Autopilot` except persistence
([02](./02-core-domains.md)). *Impact:* a whole documented feature is invisible to users, and its
design document reads as live. *Priority:* high — as a decision, not necessarily as code.
*Dependencies:* the product question in [11](./11-open-questions-and-assumptions.md) item 1.
*Risk:* wiring it up exposes untested show-critical behaviour on a live floor; retiring it wastes
completed work.

**2. Fourteen action kinds have no supported route to a user.**
*Evidence:* the reachability analysis in [06](./06-ui-feature-coverage.md). *Impact:* the effects
rack cannot load, remove, reorder or bypass an effect; loop halve and double, hot-cue clear, cue-play,
EQ kill and quantize toggle are all implemented but unusable; two live-queue editing commands cannot
be invoked. *Priority:* high. *Dependencies:* a decision on whether `MappingsViewModel.BuildTargets`
should be generated from the action vocabulary rather than hand-listed. *Risk:* low — most need only
a learn target or a button.

**3. Visual scenes can be played but not authored.**
*Evidence:* banks are read by `ServiceConfig.LoadBanksOrStarter`; no application or MCP code writes
one. *Impact:* the VJ half of the product's identity depends on hand-written JSON. *Priority:* high.
*Risk:* an authoring surface is substantial new UI.

**4. No stated concurrency rule for shared stores.**
*Evidence:* the app and the MCP process open the same catalog and live JSON stores; no locking or
transactional policy was found. *Impact:* a corrupted or silently overwritten catalog. *Priority:*
high. *Suggested direction:* a single-writer rule, or SQLite transactions for the mutable data.

**5. Library repair is scaffolding that never runs.**
*Evidence (revised 2026-08-02, from reading the code rather than the file sizes):*
`LibraryDoctor.Preview`, `LibraryRepairPlan`, `LibraryRepairAction` and the `LibraryReferenceRewriter`
registered at `ServiceConfig.cs:529` have **no call site in `src`**. The Doctor reports issues; nothing
applies a repair. The earlier version of this item claimed destructive repairs lacked a confirmation
contract — that was wrong: the one path that deletes a user file
(`VisualLibraryViewModel.DeleteAssetAsync`) confirms first, disables its command without a confirmation
service, and is covered by three tests. *Impact:* code shaped like a safety mechanism, that never runs,
invites the reader to assume repairs are guarded. *Priority:* medium. *Suggested direction:* decide
whether repair is a feature, then either wire it up behind the existing preview type or delete the
scaffolding ([15](./15-refactor-recommendations.md) item 8).

**6. No macOS delivery path.**
*Evidence:* CI runs a macOS build and test job; `scripts/build-installer.ps1` and
`installer/windows/Liveolator.iss` are Windows-only, with no packaging, notarisation or signing
elsewhere. *Impact:* the stated cross-platform promise is unmet in practice. *Priority:* high.

**7. No retention, deletion or redaction policy.**
*Evidence:* the data inventory in [09](./09-permissions-and-roles.md) against the absence of any
policy artefact. *Impact:* absolute media paths, fingerprints, provider payloads, recordings and logs
accumulate indefinitely with no export or deletion story. *Priority:* medium — rising if the product
gains an audience.

**8. Three shipped themes, one document declaring a single canonical line.**
*Evidence:* `BuiltInUiThemes` versus `docs/19-ui-design-line.md`. *Impact:* nobody can tell whether
an amber screenshot is on-brand. *Priority:* medium.

## Improvement opportunities

**9. Release gates per platform.** Executable checks for native dependency presence, audio routing,
cue output, MIDI feedback, visual rendering, recording, the update flow, signing, and upgrade and
uninstall data preservation. *Priority:* high. *Dependencies:* item 6.

**10. Soak tests for the performance paths.** Clock switching, sync correction, device reconnect, and
running with degraded native dependencies. *Priority:* high. *Evidence base:* the concurrency seam
noted in [10](./10-business-logic-hotspots.md). *Partly addressed:* deck-end into queue-advance is now
pinned by the cross-assembly invariant tests ([15](./15-refactor-recommendations.md) item 1), so what
remains is the timing- and device-dependent half that needs real hardware.

**11. A threat model for everything the product executes.** Extension archives, shader compilation,
FFmpeg and Python subprocesses, VST3 bridging, and MCP filesystem reach. Keep developer mode visibly
separate from trusted behaviour. *Priority:* medium.

**12. Retry, timeout and idempotency policies for the HTTP integrations.** *Priority:* medium.
*Evidence base:* none were confirmed to exist ([05](./05-integrations-and-side-effects.md)).

**13. A published compatibility guarantee for authored formats.** Snapshots are already versioned and
load defensively; what is missing is a stated promise, before third-party preset and add-on
ecosystems grow past the point where it can be given. *Priority:* medium.

**14. Secret handling and log redaction.** Confirm API keys and provider responses cannot reach the
rolling log. *Priority:* medium.

**15. Keep this documentation current from the code side.** A refresh run belongs in whatever process
changes observable behaviour, so [06](./06-ui-feature-coverage.md) and
[03](./03-business-entities-and-rules.md) do not drift into the state the archived reviews reached.
*Priority:* low, but cheap.

## Speculative feature ideas

**16. Generate mapping targets from the action vocabulary.** Would close item 2 permanently rather
than one control at a time, and would make every future action kind bindable by default. *Evidence:*
none beyond the current hand-maintained list.

**17. Expose autopilot through MCP rather than a new UI.** If the rule engine survives item 1, an
agent-driven show may be a cheaper and more distinctive surface than authoring UI. *Evidence:* none —
this is a product idea, offered because the MCP surface already exists.

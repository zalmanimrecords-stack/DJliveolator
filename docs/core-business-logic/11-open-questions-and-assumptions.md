# 11 — Open questions and assumptions

- **Purpose:** the work queue of everything this documentation could not resolve from code, ordered by impact. An item leaves this document when it is answered and the answer moves into its owner.
- **Scope:** contradictions, unenforced rules, runtime-sensitive behaviour and product decisions.
- **Source of truth:** the code that was read, and the places where it was silent.
- **Last validated:** 2026-09-12 (against the merge of `feat/shipped-controller-profiles`)
- **Confidence:** every item here is deliberately uncertain and labelled.
- **Related:** [rules](./03-business-entities-and-rules.md) · [UI coverage](./06-ui-feature-coverage.md) · [permissions](./09-permissions-and-roles.md)

## Product decisions needed

1. **Is autopilot still in scope?** `Conflicts with implementation`. A complete, tested rule engine
   with its own persistence format has no host and no UI. Either it is wired up or it is retired;
   leaving it is the worst option because `docs/10-autopilot-show-rules.md` reads as a live feature.
   *Who can answer:* product. *Evidence needed:* a decision, not more code reading.
2. **How are visual banks and scenes meant to be authored?** `Unclear from code`. They are read at
   startup and can only be produced by hand-writing JSON. *Who can answer:* product.
3. **Which UI theme is the product's line?** `Conflicts with implementation`.
   `docs/19-ui-design-line.md` names the navy single-blue Spartan look canonical and explicitly
   forbids amber as a primary accent; `BuiltInUiThemes` ships Spartan as the default alongside an
   amber Brasswork theme and a lime Retro Sci-Fi theme. Both statements are true — the question is
   which one the product presents as its identity. *Who can answer:* product.
4. ~~**Should the fixed MIDI-learn target list become the full action vocabulary?**~~
   **Closed 2026-09-12.** It did. `ActionTargetVocabulary` classifies every declared kind as bindable
   (60) or explicitly not bindable (16), a test fails if a new kind is neither, and the 28 hand-written
   entries are preserved verbatim for the hardware knowledge they carry. Six previously unreachable
   kinds are now routed; the eight that remain cannot be reached by any learn target, because
   `ControllerBinding` carries neither `PerformanceAction.Target` nor a free-form id — that is a picker
   seam, and it is now the open question, not the list. Four kinds whose value is a physical quantity
   were deliberately withdrawn. Details in [06](./06-ui-feature-coverage.md).
5. **Which features are production-supported on macOS?** `Needs validation`. CI builds and tests on
   macOS, but no packaging, notarisation or signing workflow exists, and BASS/CoreAudio routing, MIDI
   device behaviour and camera capture have not been verified there. *Who can answer:* the owner.
6. **What is "release-ready"?** `Unclear from code`. There is no single definition spanning the
   Windows installer, macOS packaging, native dependencies, signing and update publishing.

## Correctness and safety

7. **Is library repair a feature, or dead scaffolding?** `Conflicts with implementation`.
   `LibraryDoctor.Preview`, `LibraryRepairPlan`, `LibraryRepairAction` and the `LibraryReferenceRewriter`
   registered in `ServiceConfig` have no call site: the Doctor reports issues and nothing ever applies a
   repair. The original worry here — that a destructive repair might apply without a preview — does not
   arise, because there is no apply step; the one path that deletes a user file
   (`VisualLibraryViewModel.DeleteAssetAsync`) confirms first and is tested. So the open question is the
   opposite one: wire repair up, or remove the scaffolding. *Who can answer:* product.
   See [15](./15-refactor-recommendations.md) items 5 and 8.
8. **What happens when the app and the MCP process touch the same catalog concurrently?**
   **Partly answered 2026-09-12, and worse than "unclear".** The catalog itself is protected —
   `SqliteCatalogStore` sets `journal_mode=WAL` and `busy_timeout=5000`, and every JSON store writes to
   a temp file then moves it, so corruption was never the exposure. Two real defects were measured
   instead. (a) Six JSON stores used a FIXED `<path>.tmp`, so two writers collided on the temp path and
   a temp abandoned by a killed process broke every later save — fixed, each now uses a unique name.
   (b) **Still open:** concurrent saves lose a race on the final `File.Move(..., overwrite: true)`,
   throwing `UnauthorizedAccessException`. The `ConcurrentSaves` tests fail 1-2 of 4 on nearly every
   run, and this reproduces on clean `master`, so it predates the fix above. `_saveGate` serializes one
   store instance and nothing else, while the app and the MCP server both write `JsonPlaylistStore` and
   `JsonStudioProjectStore`. *Treatment:* a bounded retry around the move, or a cross-process mutex.
   What remains genuinely unanswered is the lost-update question: both processes read-modify-write whole
   files, so the last writer silently wins.
9. ~~**Is the manual-beat-grid protection rule actually enforced?**~~ **Closed 2026-09-11.**
   It is. `MusicTrack.AnalysisIsManual` gates the reanalysis path in `MusicLibrary` and
   `CatalogReanalysisService`, a failed analysis never replaces a good one, and
   `CatalogReanalysisServiceTests` covers both. The rule moved to
   [03](./03-business-entities-and-rules.md).
10. **Do the HTTP integrations have retry, timeout and idempotency policies?** `Needs validation`.
    None were confirmed for AcoustID, the BPM provider or the update manifest fetch.
11. **Are API keys and provider responses kept out of the log file?** `Needs validation`.
    Diagnostics write a rolling log; key handling in `OnlineSettings` was not audited. The same
    check covers whether the add-on UI makes developer mode's reduced trust guarantee obvious — it is
    the one supported way to bypass publisher trust ([09](./09-permissions-and-roles.md)), so an
    operator must not be able to leave it on without knowing.
12. **Is `defaults/live/` real?** `Needs validation`. The never-write-to-defaults rule is documented;
    the directory was not observed in code in this pass.

## Runtime behaviour that only hardware can settle

13. **Sync timing.** `Needs validation`. The grid-confidence gate that
    `docs/SYNC-BEHAVIOR-SPEC.md` called for now exists and is two-sided
    ([03](./03-business-entities-and-rules.md)), so the spec's largest gap is closed. What remains is
    unchanged: "does it beat-match like professional software" is a listening test, and the spec's
    acceptance tests are still not implemented.
14. **Native device latency, LED feedback and GL behaviour.** `Needs validation` — pure Core rules
    cannot guarantee any of it.
15. **Is `DjView.axaml` genuinely dead?** `Needs validation`. No view references it and it is not a
    shell page, but confirm no launch path renders it before removing it.

## Policy gaps

16. **Retention, deletion and export policy** for media paths, fingerprints, online lookup payloads,
    logs, recordings, renders and analysis artefacts. `Unclear from code` — storage exists, policy
    does not.
17. **Backward-compatibility policy for authored formats.** `Unclear from code`. Snapshots are
    versioned and load defensively, but no published guarantee covers presets, add-ons, mappings and
    projects as third-party ecosystems grow.
18. **Will MCP remain local stdio only?** `Unclear from code`. The answer changes the security
    requirements in [09](./09-permissions-and-roles.md) completely.

## Opened by the 2026-09-11 refresh

19. **Should the DJ set builder have a UI?** `Unclear from code`. `Core/Studio/Set` is 1,813 lines
    reachable only from MCP — the largest capability in the product with no surface at all. A DJ
    without an agent cannot use any of it, and the agent that can cannot hear what it produced.
    *Who can answer:* product. Coverage row in [06](./06-ui-feature-coverage.md).
20. **Is `TargetLufs = -9` right for every set, or only for dance music?** `Assumption`. The default
    is justified in `SetBuildOptions` for masters sitting around −8 to −6. Nothing stops a set of
    quieter material being gained toward a target that does not suit it, and no tool reports the
    resulting headroom. *Evidence needed:* a measured pass over a mixed-genre catalog.
21. **What is the supported catalog schema floor?** `Needs validation`. The phase-sync gate treats a
    pre-v12 row as unknown and the structure detector was added at a later analyzer version, so
    behaviour now depends on which analyzer version last touched a track. No document states which
    catalog versions are supported or when a forced re-analysis becomes mandatory.
22. **Five High-severity transitive dependency advisories are unaddressed.** `Verified` as present
    via `dotnet list package --vulnerable --include-transitive`; the decision to bump is open.
    Tracked as GitHub issue #11; treatment in [15](./15-refactor-recommendations.md).

## Assumptions this documentation makes

- Code and executable tests outrank every document, including this one; where they disagreed, the
  code won and the disagreement was recorded rather than smoothed over.
- "Actor" means a code-visible interaction, not an authenticated role.
- A pure-Core behaviour is treated as implemented even when its native adapter or UI exposure is
  absent — with the exposure gap recorded separately in [06](./06-ui-feature-coverage.md).
- The design documents in `docs/` describe intent. Where one describes behaviour that does not exist,
  that is recorded here, not silently corrected in the design document.

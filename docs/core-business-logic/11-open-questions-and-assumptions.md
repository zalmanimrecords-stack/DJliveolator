# 11 — Open questions and assumptions

- **Purpose:** the work queue of everything this documentation could not resolve from code, ordered by impact. An item leaves this document when it is answered and the answer moves into its owner.
- **Scope:** contradictions, unenforced rules, runtime-sensitive behaviour and product decisions.
- **Source of truth:** the code that was read, and the places where it was silent.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
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
4. **Should the fixed MIDI-learn target list become the full action vocabulary?** Fourteen action
   kinds are unreachable by supported means ([06](./06-ui-feature-coverage.md)). *Who can answer:*
   product plus engineering.
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
   `Needs validation`. Both open the same stores. No transaction or cross-process locking policy was
   found. *Evidence needed:* a concurrency test, or an explicit single-writer rule.
9. **Is the manual-beat-grid protection rule actually enforced?** `Needs validation`. The rule is
   stated in `docs/13-data-and-persistence.md` and a manual flag exists on the grid, but the
   enforcement point in the reanalysis path was not re-proved in this pass.
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

13. **Sync timing.** `Needs validation`. The phase-lock loop is implemented and the state machine is
    explicit, but "does it beat-match like professional software" is a listening test.
    `docs/SYNC-BEHAVIOR-SPEC.md` proposes the contract and acceptance tests; it is not implemented.
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

## Assumptions this documentation makes

- Code and executable tests outrank every document, including this one; where they disagreed, the
  code won and the disagreement was recorded rather than smoothed over.
- "Actor" means a code-visible interaction, not an authenticated role.
- A pure-Core behaviour is treated as implemented even when its native adapter or UI exposure is
  absent — with the exposure gap recorded separately in [06](./06-ui-feature-coverage.md).
- The design documents in `docs/` describe intent. Where one describes behaviour that does not exist,
  that is recorded here, not silently corrected in the design document.

---
name: system-gap-review
description: Run the Liveolator "ten-expert panel" over the whole system to map the current state, find verified bugs, list missing features, and recommend the next steps. Each expert (DSP engineer, tempo/sync specialist, professional DJ, VJ/graphics engineer, controller engineer, library/metadata engineer, UX designer, software architect, QA/release engineer, product manager) reads the actual code and every High/Critical bug is adversarially re-verified. Use when the owner is stuck, wants a gap map / status check / "where are we", a pre-merge or pre-release audit, or asks "what's broken / missing / what should we do next". Optionally pass a focus (e.g. "the synced decks keep drifting") to sharpen every lens.
---

# Liveolator system gap-review (ten-expert panel)

When you're stuck or need a true picture of the system, convene the panel. It maps reality
against the code (not the docs), produces a **verified** bug list, and ends with a prioritized
next-steps list. This is the workflow that produced [`docs/24-system-review-2026-06-07.md`](../../../docs/24-system-review-2026-06-07.md);
use that as the output template.

**Requires multi-agent orchestration** (the `Workflow` tool). Running this skill is the owner's
explicit opt-in — proceed without asking again.

## Steps

1. **Capture the baseline (cheap, do it first).** Run the build and the full test suite in the
   background while the panel works, so the report quotes ground truth, not memory:
   - `dotnet build Liveolator.sln -clp:ErrorsOnly --nologo`
   - `dotnet test Liveolator.sln --nologo -clp:ErrorsOnly`  (record per-project pass/fail/skip)
   - `git status` — note in-flight uncommitted work; the panel must review the **current tree**.

2. **Run the panel.** Invoke the saved workflow — do not re-author it:
   `Workflow({ scriptPath: ".claude/skills/system-gap-review/expert-review-workflow.js" })`.
   If the owner gave a focus ("we're stuck on X"), pass it: `Workflow({ scriptPath: …, args: "X" })`.
   It runs 10 read-only expert reviewers in parallel, then adversarially verifies every High/Critical
   bug against the code, and returns structured `{reviews:[{subsystem,summary,map,bugs,verifiedBugs,
   recommendations,missingFeatures}]}`. It's heavy (~20+ agents, minutes) — let it run in the
   background and read the result file when notified.

3. **Trust the verifier.** Treat a bug as confirmed only if its `verdict` is `confirmed`; demote or
   drop `refuted`/`uncertain` ones (use `correctedSeverity`). The verifier is deliberately skeptical.

4. **Synthesize the report.** Write `docs/NN-system-review-<YYYY-MM-DD>.md` (next free doc number;
   today's date from context). Mirror doc 24's structure: headline verdict → system map table (one
   row per subsystem) → bugs (Critical/High table with file:line + fix, then Medium, then Low) →
   cross-cutting recommendations → missing features by track → **the next 10 steps, ordered by ROI**
   (quick "make built features actually work" fixes first, then differentiator-correctness + safety
   net, then large build-outs). Note which steps can run in parallel on separate worktrees.

5. **Update the living docs + memory.** Add a pointer + the new test count to [`docs/18`](../../../docs/18-implementation-status.md)
   and an update banner to [`docs/22`](../../../docs/22-status-and-roadmap.md) ("doc NN supersedes the
   priorities below for the next wave"). Fix any self-contradiction the panel surfaced. Update the
   `open-questions-and-known-gaps` memory with the verified High bugs and the doc pointer.

6. **Report to the owner** (their language): headline verdict, the verified High bugs (with file:line),
   and the 10 next steps. Offer to start on the top quick-wins. **Do not fix code in this skill** — it
   is a mapping/review pass; fixes are separate, TDD-first work the owner chooses from the list.

## Notes

- The panel is **read-only by design** — no edits, so it's safe to run anytime, mid-feature included.
- Tuning the lenses (add/remove an expert, change a subsystem's scope) = edit
  `expert-review-workflow.js` `DIMENSIONS`; keep the schemas and the verify stage intact.
- Findings reflect the tree at run time. Re-run after a wave lands; each report is dated and additive
  (don't overwrite prior reviews — they're the project's audit trail).

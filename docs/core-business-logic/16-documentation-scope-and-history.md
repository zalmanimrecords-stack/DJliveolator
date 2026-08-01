# Documentation Scope and History

- Last updated: 2026-08-01
- Scope analyzed: Numbered product documentation, living status/review documents, and this generated business-logic set.
- Confidence note: High for repository document boundaries; historical accuracy of older design claims is not re-certified here.

## Canonical boundary

This folder documents behavior grounded in the current repository. It distinguishes Core policy from orchestration, infrastructure, and UI, and uses the canonical English-only filenames `01` through `16` defined by the `core-business-logic-docs` skill.

The broader `docs/` folder serves different purposes:

- `00-LIVEOLATOR-CONTEXT.md` and `00-architecture-overview.md` explain product intent and architecture.
- Numbered design documents describe subsystems and may contain planned or historical behavior.
- `18-implementation-status.md` is a living implementation map but contains dated increments.
- Later system reviews and QA reports record point-in-time findings and may supersede earlier reviews.
- This folder consolidates business behavior from code and should not be used as a roadmap or release checklist.

## Evidence and maintenance policy

When documents disagree, prefer executable code and tests, then the newest focused review, then implementation-status prose, then aspirational design documents. Update the affected files here whenever a change alters a business rule, lifecycle, critical flow, authorization/trust decision, persisted entity, or external side effect.

Repository hygiene performed on 2026-08-01: the canonical directory did not previously exist; no locale-suffixed or orphan numbered files required migration or deletion.

## Code References

- `docs/00-LIVEOLATOR-CONTEXT.md`
- `docs/00-architecture-overview.md`
- `docs/18-implementation-status.md`
- `docs/31-system-review-2026-06-27.md`
- `tests/Liveolator.Core.Tests/`
- `src/Liveolator.Core/`

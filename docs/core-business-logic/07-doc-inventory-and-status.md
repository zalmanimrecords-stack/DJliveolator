# 07 — Documentation inventory and status

- **Purpose:** the audit trail of the consolidation — every documentation file, what happened to it, and where its content went.
- **Scope:** every markdown file in the repository outside this canonical directory.
- **Source of truth:** the files themselves; this document records decisions about them, never their content.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High for status and destination; the design documents were classified from their own status banners and purpose sections rather than a line-by-line re-verification, and are labelled accordingly.
- **Related:** [context](./00-project-context.md) · [open questions](./11-open-questions-and-assumptions.md)

## Migration of the previous numbered set

This directory previously held a 16-document set (`01`–`16`) produced on 2026-08-01 by an earlier
variant of this skill. It has been migrated to the canonical `00`–`15` scheme. The old files were
merged into their new owners and then removed; they remain in git history at commit `6a32b80`.

| Old path | New owner |
| --- | --- |
| `01-system-overview.md` | `01-system-overview.md` (rewritten and deepened) |
| `02-core-domains.md` | `02-core-domains.md` (consolidation map moved to `15`) |
| `03-business-entities.md` | `03-business-entities-and-rules.md` §Entities |
| `04-business-rules.md` | `03-business-entities-and-rules.md` §Rules |
| `05-critical-flows.md` | `04-critical-flows.md` |
| `06-state-machines-and-lifecycles.md` | `08-state-machines-and-lifecycles.md` |
| `07-integrations-and-side-effects.md` | `05-integrations-and-side-effects.md` |
| `08-permissions-and-roles.md` | `09-permissions-and-roles.md` |
| `09-business-logic-hotspots.md` | `10-business-logic-hotspots.md` |
| `10-open-questions-and-assumptions.md` | `11-open-questions-and-assumptions.md` |
| `11-glossary.md` | `12-glossary.md` |
| `12-executive-summary.md` | `13-executive-summary.md` |
| `13-high-priority-recommendations.md` | split: product and operational items to `14`, engineering items to `15` |
| `14-security-privacy-and-compliance.md` | split: posture to `09`, data handling to `05`, gaps to `11` and `14` |
| `15-deployment-distribution-and-operations.md` | split: boundaries to `01`, commands to `00`, update path to `05`, operational gaps to `14` |
| `16-documentation-scope-and-history.md` | this document |

Two owners had no predecessor and were written from code in this run:
`00-project-context.md` and `06-ui-feature-coverage.md`.

## Design and specification documents

Kept in place. These are the project's subsystem design record, they are linked from the
per-project `CLAUDE.md` adapters and from six repository skills, and several carry their own accurate
status banners. Where a design document and this set disagree about *current behaviour*, this set
wins; where it explains *why* a subsystem is shaped as it is, the design document is the only record.

| Path | Purpose | Relevance | Status | Notes |
| --- | --- | --- | --- | --- |
| `docs/README.md` | Index of the design set | Current | `Maintained adapter` | Index updated for the files archived below |
| `docs/00-LIVEOLATOR-CONTEXT.md` | Why the project exists, direction, what carries over | Current | `Canonical` | Self-declared authoritative for direction; referenced by root `CLAUDE.md` |
| `docs/00-architecture-overview.md` | Cross-cutting layering and the action-layer principle | Current | `Canonical` | Design rationale behind [01](./01-system-overview.md) |
| `docs/01-audio-source-layer.md` | Audio source seam | Partly stale | `Historical` | Banner marks the NAudio types as historical; BASS decision resolved |
| `docs/02-audio-frame-pipeline.md` | Frames for analysis and rendering | Partly stale | `Historical` | Still refers to projectM as a consumer |
| `docs/03-beat-engine.md` | Beat clock design | Current | `Canonical` | Accurate built/pending banner |
| `docs/04-performance-action-system.md` | The action seam | Current | `Canonical` | Design source for [03](./03-business-entities-and-rules.md) §Action routing |
| `docs/05-controller-mapping-engine.md` | Mapping and learn design | Current | `Canonical` | |
| `docs/06-push-profile.md` | Ableton Push 1 profile | Current | `Canonical` | Device-specific LED and mode detail |
| `docs/07-dj-controller-profile.md` | CMD STUDIO 2A profile | Current | `Canonical` | |
| `docs/08-visual-performance-engine.md` | Scenes, banks, macros, quantised launch | Current | `Canonical` | Replaces the projectM-era version |
| `docs/09-live-playlist-engine.md` | Queue model | Current | `Canonical` | |
| `docs/10-autopilot-show-rules.md` | Autopilot rules | Design only | `Canonical` | Describes a host driving `Tick`; no such host exists — see [06](./06-ui-feature-coverage.md) |
| `docs/11-deck-ab-pro-dj.md` | Deck and mixer design | Current | `Canonical` | Output-routing wording predates the BASS decision |
| `docs/12-ui-modules.md` | UI module layout | Partly stale | `Historical` | Predates the current seven-tab shell |
| `docs/13-data-and-persistence.md` | Persistence design and rules | Current | `Merged` | Storage layout now owned by [05](./05-integrations-and-side-effects.md); the file keeps the design rationale |
| `docs/16-track-analysis-library.md` | Analysis and library design | Current | `Canonical` | |
| `docs/17-mcp-agent-interface.md` | Agent interface design | Current | `Canonical` | Tool list now owned by [05](./05-integrations-and-side-effects.md) |
| `docs/18-implementation-status.md` | Living implementation map | Stale as a status claim | `Archive candidate` | Last updated 2026-06-12 while claiming to track reality; root `CLAUDE.md` instructs agents to read it first, so retiring it is an owner decision. Pointer added to this set |
| `docs/19-ui-design-line.md` | The canonical visual line | Partly contradicted | `Historical` | Correct about the default theme; the two alternative built-in themes it forbids now ship — see [11](./11-open-questions-and-assumptions.md) |
| `docs/21-extension-system.md` | Extension packaging | Current | `Canonical` | |
| `docs/23-learnings-from-mixxx.md` | Study of Mixxx, with a licensing wall | Current | `Canonical` | Records why no Mixxx code may be copied |
| `docs/25-track-linked-media-and-vj-foundation.md` | Track-linked media plan | Partly built | `Canonical` | |
| `docs/26-visual-addon-standard.md` | Third-party add-on contract | Current | `Canonical` | Public developer standard |
| `docs/28-controllable-preset-generator-addon.md` | Generator preset add-on | Built since | `Historical` | Banner still says PLAN/TODO; the feature ships |
| `docs/29-frktl-preset-authoring.md` | `.frktl` authoring guide | Current | `Canonical` | Public authoring guide |
| `docs/30-ui-skins-png-controls.md` | PNG control skins | Design and POC | `Canonical` | |
| `docs/32-python-analysis-seam.md` | Python seam work plan | Plan | `Canonical` | Pending owner sign-off per its own banner |
| `docs/SYNC-BEHAVIOR-SPEC.md` | Proposed sync contract and acceptance tests | Proposal | `Canonical` | Proposed, not current; referenced from [04](./04-critical-flows.md) |
| `docs/research/audio-stack-recommendation.md` | Input to the audio-library decision | Superseded by the decision | `Historical` | Recommended against BASS; BASS was chosen |
| `docs/research/dj-market-and-dsp-research.md` | Market and DSP research | Background | `Historical` | |
| `docs/research/dj-gaps-keydetect-latency-automix-avsync.md` | Gap-closure research | Background | `Historical` | |

## Archived

Point-in-time reviews, roadmaps and status snapshots. Each read like a live plan while describing a
tree that has since moved, which is the failure mode this consolidation exists to remove. Moved to
`docs/archive/` with a date prefix and a not-current banner; nothing in them was deleted.

| Path | Purpose | Status | Destination |
| --- | --- | --- | --- |
| `docs/14-testing-and-validation.md` | Test strategy | `Stale` | Test strategy and commands now in [00](./00-project-context.md); the file described the abandoned Zalmanolator stack and said so itself |
| `docs/15-phased-roadmap.md` | Build order | `Superseded` | Delivery planning is not part of this set |
| `docs/20-dj-feature-gap-analysis.md` | DJ feature audit, 2026-06-06 | `Historical` | Surviving gaps in [06](./06-ui-feature-coverage.md) and [14](./14-final-improvement-report.md) |
| `docs/21-dj-feature-gap-analysis-followup.md` | Re-audit after the integration merge | `Historical` | as above |
| `docs/22-status-and-roadmap.md` | Status review and roadmap, 2026-06-06 | `Historical` | Its four-deck statement was accurate; the current position is in [01](./01-system-overview.md) |
| `docs/24-system-review-2026-06-07.md` | Ten-expert panel review | `Historical` | Findings that still hold are in [10](./10-business-logic-hotspots.md) |
| `docs/27-system-review-2026-06-10.md` | Ten-expert panel review | `Historical` | as above |
| `docs/31-system-review-2026-06-27.md` | Panel review, music-library focus | `Historical` | as above |
| `docs/improvement-report.md` | Autonomous maintenance pass, 2026-07-18 | `Historical` | Refactor themes in [15](./15-refactor-recommendations.md) |
| `docs/qa-reports/qa-report-2026-06-18.md` | Full-app QA sweep | `Historical` | Verdict was tied to an in-flight branch |

Their archived paths, all under `docs/archive/` and covered by
[`docs/archive/README.md`](../archive/README.md):
`2026-08-01-14-testing-and-validation.md`, `2026-08-01-15-phased-roadmap.md`,
`2026-08-01-20-dj-feature-gap-analysis.md`, `2026-08-01-21-dj-feature-gap-analysis-followup.md`,
`2026-08-01-22-status-and-roadmap.md`, `2026-08-01-24-system-review-2026-06-07.md`,
`2026-08-01-27-system-review-2026-06-10.md`, `2026-08-01-31-system-review-2026-06-27.md`,
`2026-08-01-improvement-report.md`, `2026-08-01-qa-report-2026-06-18.md`. The now-empty
`docs/qa-reports/` directory was removed.

## AI-context and instruction files

| Path | Target | Status | Action |
| --- | --- | --- | --- |
| `CLAUDE.md` (root) | Claude Code, whole repo | `Maintained adapter` | Trimmed of duplicated project narrative; points to [00](./00-project-context.md) |
| `AGENTS.md` | Codex and other agents | `Maintained adapter` | Same treatment |
| `src/Liveolator.App/CLAUDE.md` | App project | `Maintained adapter` | Directory-specific rules kept; design-line claim reconciled |
| `src/Liveolator.Core/CLAUDE.md` | Core project | `Maintained adapter` | Boundary rules are directory-specific and correct |
| `src/Liveolator.Audio/CLAUDE.md`, `.Mcp`, `.Media`, `.Midi`, `.Platform`, `.Visuals` | Those projects | `Maintained adapter` | Left as-is; each is short and directory-specific |
| `.claude/skills/**/SKILL.md`, `.agents/skills/**/SKILL.md` | Workflow skills | `Maintained adapter` | Six seam skills plus `code-alignment`, `dj-software-auditor`, `system-gap-review`; workflow only, no project narrative |
| `.claude/agents/qa-engineer.md` | Sub-agent definition | `Maintained adapter` | Tooling, not documentation |
| `.claude/skills/dj-software-auditor/agents/claude.md` and its `references/*.md` (`competitor-comparison-guide.md`, `dj-software-feature-map.md`, `dj-workflow-principles.md`, `launch-readiness-checklist.md`, `qa-test-scenarios.md`) | That skill | `Maintained adapter` | Skill-internal reference material |
| `.claude/worktrees/**` | Throwaway agent worktrees | `Duplicate` | Copies of tracked files; not maintained and not inventoried individually |

## Other repository markdown

| Path | Purpose | Status | Action |
| --- | --- | --- | --- |
| `README.md` | Public repository landing page | `Canonical` | Status links repointed from the archived reviews to this set |
| `CONTRIBUTING.md` | Contribution guide | `Canonical` | Unchanged |
| `design/mockups/README.md` | UI mockup index | `Canonical` | Unchanged |
| `tests/corpus/README.md` | Test-audio corpus notes | `Canonical` | Unchanged |
| `website/README.md`, `website/DEPLOY.md`, `website/RELEASE_NOTES_NEXT.md` | Marketing site and its deployment | `Canonical` | Separate deliverable, outside the product runtime |
| `marketing/brand-brief.md`, `content-plan-week1.md`, `launch-email.md`, `launch-post.md`, `marketing-manager.agent.md` | Marketing collateral | `Canonical` | Not product documentation |
| `orchestration/README.md`, `OWNERSHIP.md`, `TASKS.md` | Multi-agent worktree coordination (git-ignored) | `Maintained adapter` | Working state, not documentation |
| `artifacts/codex-brief-hardware-controls.md` | One-off agent brief (git-ignored) | `Historical` | Left in a git-ignored directory |
| `THIRD-PARTY-NOTICES.txt`, `LICENSE`, `LICENSE-EXCEPTION.txt` | Legal | `Canonical` | Required; never archived |

## Cleanup log

- Removed: the sixteen previous canonical files listed in the migration table, after merging.
- Archived: the ten files listed above, to `docs/archive/`.
- Left in place pending an owner decision: `docs/18-implementation-status.md`.
- Links fixed: `README.md` (two), `docs/README.md` (index rows for the archived files),
  `docs/18-implementation-status.md` (pointer to this set).
- Known broken links inside archived files: `docs/20` and `docs/21-followup` cross-reference each
  other and `docs/18` by their original paths. Archived files are not maintained.

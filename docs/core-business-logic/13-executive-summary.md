# 13 — Executive summary

- **Purpose:** what a decision-maker with five minutes and no code access needs to know.
- **Scope:** the whole product.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High for what is built; Medium for readiness, which depends on hardware and distribution work that cannot be verified from source.
- **Related:** [context](./00-project-context.md) · [UI coverage](./06-ui-feature-coverage.md) · [improvements](./14-final-improvement-report.md)

## What the product is

A desktop DJ and VJ performance application: two audio decks with a full software mixer, an analysed
music library, and a GPU visual engine — with audio and visuals driven from one shared beat clock.
That shared clock is the product's differentiator. It is free software, licensed GPLv3 with a
documented exception for its proprietary audio dependency.

## Main capabilities

Live two-deck playback and mixing with headphone cue and a master limiter; beat detection,
synchronisation, cues and loops; a music library with scanning, analysis, key and tempo detection,
online cross-checking, and import from six other DJ applications; harmonic playlist building and
per-deck live queues; a scene-based visual compositor with beat-synced launching and third-party
add-ons; a timeline arrangement mode with offline rendering; master recording; MIDI learn for any
controller; and an agent interface exposing 22 tools to external AI clients.

## Business-critical flows

Loading a track onto a deck, advancing the live queue, synchronising two decks, and scanning and
analysing the library. Each is designed so that a failure is reported rather than silent — an
unreachable file, an engine that cannot open a track, and a per-file analysis failure all produce a
stated outcome instead of an apparent success.

## Important dependencies

The realtime audio library is proprietary and is fetched at build time rather than vendored, under a
licence that is free only while the product is. Visual rendering needs a working OpenGL context.
Video, camera work, advanced analysis and metadata enrichment each depend on an optional external
tool or service, and the product is designed to run in a degraded state when they are absent.

## Documentation health

This set is now the single source for current behaviour. The consolidation retired ten point-in-time
reviews and roadmaps that read like live plans while describing a tree that had moved on, and added
the two things the previous set lacked: an entry point for newcomers, and an audit of which features
a user can actually reach. Roughly thirty subsystem design documents remain as the record of *why*
the system is shaped as it is; where they and this set disagree about current behaviour, this set is
authoritative.

## UI coverage health

Most of what is built is reachable. Three gaps stand out. An entire subsystem — the autopilot rule
engine, complete with its own saved format — has no way to run. Visual scenes, one of the product's
headline concepts, can be played but not authored inside the product. And fourteen performance
commands have working implementations that no button, no shipped controller mapping and no mapping
target can reach, including the effects rack's load, remove and bypass controls.

## Main risks

The risks are operational rather than conceptual. Native audio, MIDI and graphics dependencies can
only be proven on real hardware. The application and the agent process can open the same catalog with
no stated concurrency rule. Library repair can rewrite and delete user files. Extension installation
is the product's only real trust boundary and ships with a deliberate bypass. macOS is a stated target
with continuous integration but no packaging path.

## Open decisions

Whether autopilot ships or is retired; how visual scenes are meant to be authored; which of the three
shipped themes is the product's visual identity; what macOS support means concretely; and what the
retention policy is for the paths, fingerprints, recordings and logs the product accumulates.

## Recommended priorities

1. Decide autopilot's fate — it is the largest piece of finished-but-dead work.
2. Define per-platform release gates, especially for macOS.
3. Put a single concurrency rule around shared catalog access.
4. Guarantee preview-and-confirm on every destructive library operation.
5. Close the reachability gaps in [06](./06-ui-feature-coverage.md), starting with the effects rack.

Detail and sequencing: [14](./14-final-improvement-report.md) and
[15](./15-refactor-recommendations.md).

# Audio Stack Recommendation (open-license) — Input for the doc 00 audio-library decision

> **Status:** Research recommendation, 2026-06-03. Resolves the open audio-library decision
> in `docs/00-LIVEOLATOR-CONTEXT.md` **pending user sign-off**. Constraints fixed before the
> research: FFmpeg for decode; open playback lib; separate open time-stretch/keylock DSP;
> open/free redistribution is critical (BASS excluded). Adversarial verification: 22 sources
> → 86 claims → 25 verified, **23 confirmed / 2 killed**.

## Recommended stack

| Concern | Recommendation | .NET binding | License |
|---|---|---|---|
| **Low-latency output** | **PortAudio** | **PortAudioSharp2** (bundles precompiled native) | PortAudio **MIT** · wrapper **Apache-2.0** |
| **Time-stretch / keylock** | **Signalsmith Stretch** (primary) | P/Invoke wrapper | **MIT** |
| ↳ fallback (zero native interop) | **SoundTouch.Net** | pure-managed C# | **LGPL-2.1** |
| **Decode** | **FFmpeg — LGPL build** (no GPL parts like x264), dynamic-linked | FFmpeg.AutoGen (or CLI via Xabe) | **LGPL** |
| **Mixer / EQ / filter** | in-house BiQuad DSP in managed code | — | ours |

## Why PortAudio is decisive

PortAudio is the **only** candidate that natively supports **Windows ASIO + WDM-KS** *and*
exposes a **CoreAudio channel-map API** (`PaMacCore_SetupChannelMap`) — exactly what routes
**master → ch 1/2** and **headphone cue → ch 3/4** of one multichannel interface like the
**Behringer CMD STUDIO 2A**. miniaudio lacks native ASIO/WDM-KS (its lowest-latency Windows
backend is WASAPI) and its maintainer has said ASIO won't make 1.0 — so it loses on the
exact low-latency + cue-routing requirement, despite being maximally permissive (public
domain / MIT-0). RtAudio is permissive (MIT) but weaker bindings/fit for this case.

## License compatibility (for closed-source free redistribution)

| Component | License | Obligation for a distributed closed-source app |
|---|---|---|
| PortAudio | MIT/Expat | Attribution only ✅ |
| PortAudioSharp2 | Apache-2.0 | Attribution only ✅ |
| Signalsmith Stretch | MIT | Attribution only ✅ |
| SoundTouch.Net | LGPL-2.1 | Dynamic-link + allow relink; **no app source disclosure** ✅ |
| FFmpeg (LGPL build) | LGPL | Dynamic-link + allow relink; **must not** enable GPL parts (x264) ✅ |
| **Rubber Band** | **GPL / paid commercial** | **AVOID** — GPL forces the whole app to GPL; proprietary needs a paid license; can't ship to macOS App Store under GPL ❌ |

## .NET binding notes

- **PortAudioSharp2** — supports Windows + macOS, **bundles precompiled PortAudio** on
  NuGet (no separate native build). Apache-2.0 wrapper over MIT core.
- **Signalsmith Stretch** — C++ MIT lib; needs a thin P/Invoke layer (we build/ship the
  native per-platform). Phase-vocoder, decoupled tempo/pitch — good DJ keylock quality.
- **SoundTouch.Net** — fully-managed C# rewrite (no native interop), .NET Standard/Framework/
  Core; LGPL. The safe fallback if the Signalsmith P/Invoke proves fiddly cross-platform.
  *(Note: one verifier flagged SoundTouch.Net real-time-streaming suitability 1-2 — validate
  its latency under our buffer sizes before committing it as the live-path engine.)*
- **FFmpeg** — use a stock **LGPL** shared build; bind via FFmpeg.AutoGen (in-proc) or shell
  out to the CLI (Xabe). Either is LGPL-compliant via dynamic linking; **do not** bundle a
  GPL build.

## Top risks

1. **Signalsmith P/Invoke + native packaging** per-platform (Win/Mac) is custom work —
   SoundTouch.Net is the managed fallback if it bites.
2. **FFmpeg build hygiene:** accidentally shipping a GPL-configured binary (x264 etc.) breaks
   the open-redistribution requirement — pin an LGPL build in CI.
3. **SoundTouch.Net real-time latency** unverified for the live deck path (fine for offline
   analysis regardless).
4. PortAudio device/channel-map quirks vary by driver — validate cue routing on the actual
   CMD STUDIO 2A.

## Effect on the codebase

- `IAudioDecoder` (doc 16, already in `Liveolator.Core`) → implemented in `Liveolator.Audio`
  over FFmpeg.
- The deck output path + keylock (doc 01 / doc 11) bind PortAudio + Signalsmith.
- The **analysis functions already built** (`Liveolator.Core/Analysis`) are unaffected — they
  consume PCM and don't care which library produced it.

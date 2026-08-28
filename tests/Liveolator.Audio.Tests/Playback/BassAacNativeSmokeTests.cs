using System;
using System.IO;
using Liveolator.Audio;
using Liveolator.Audio.Render;
using ManagedBass;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Regression guard for the BASS_AAC native (bug 2026-08-28): <c>bass_aac</c> was never in
/// <c>scripts/bass-libraries.manifest</c>, so no install had it and <see cref="BassAudioDecoder"/> never
/// added .m4a/.aac/.mp4 to its supported set. In the offline renderer an m4a clip at project tempo
/// (warp 1.0) then failed the native stereo decode and fell back to the MONO managed decoder — the mix
/// lost the stereo image, and the MCP export gate now refuses such a mix outright. The fix ships it via
/// <c>bass_aac|optional|z/2/</c> → copied into bin by <c>src/Bass.Native.targets</c>.
///
/// CI has NO native BASS, so this test SKIPS when core BASS cannot init. When core BASS works (a real
/// dev/installed machine) it ASSERTS the add-on is there and that the renderer's decode really is stereo,
/// so the regression (core present, bass_aac missing) fails here rather than only in a rendered export.
/// </summary>
public sealed class BassAacNativeSmokeTests
{
    // Left = 440 Hz, right = silence. An asymmetric fixture is what separates a genuine stereo decode
    // from the mono fallback, which yields L == R.
    private const string FixtureName = "stereo-tone-left.m4a";
    private const int RenderRate = 44_100;

    [Fact]
    public void M4aDecodesInStereo_WhenCoreBassIsAvailable_ProvingBassAacIsShipped()
    {
        try
        {
            if (!(Bass.Init(0) || Bass.LastError == Errors.Already))
                return; // core BASS could not init in this environment — skip.
        }
        catch (DllNotFoundException)
        {
            return; // core bass.dll absent (CI) — the bass_aac guard does not apply.
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", FixtureName);
        Assert.True(File.Exists(path), $"Missing test fixture '{path}'.");

        Assert.True(
            new BassAudioDecoder().CanDecode(path),
            "bass_aac native is missing while core BASS is present — m4a/aac/mp4 clips decode as mono " +
            "(or not at all). Ensure 'bass_aac|optional|z/2/' is in scripts/bass-libraries.manifest and " +
            "run scripts/fetch-bass.");

        // The exact renderer path for a clip already at the project tempo: tempoPercent 0 ⇒ no warp.
        StereoBuffer decoded = new BassFxRenderDecoder().DecodeStretchedStereo(path, RenderRate, tempoPercent: 0.0);
        Assert.True(decoded.Length > RenderRate / 4, $"expected >0.25 s of decoded audio, got {decoded.Length} frames.");

        double left = Rms(decoded.Left);
        double right = Rms(decoded.Right);
        Assert.True(left > 0.05, $"left channel is silent (rms {left:F4}) — the AAC decode produced no signal.");
        Assert.True(
            right < left / 10.0,
            $"right channel (rms {right:F4}) tracks the left (rms {left:F4}) — the m4a came back mono, " +
            "so the stereo image was lost.");
    }

    private static double Rms(float[] samples)
    {
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
            sum += (double)samples[i] * samples[i];
        return samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length);
    }
}

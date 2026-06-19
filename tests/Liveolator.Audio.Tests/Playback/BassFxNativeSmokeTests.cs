using System;
using System.IO;
using ManagedBass;
using ManagedBass.Fx;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Regression guard for the BASS_FX native (bug 2026-06-17): a deck's tempo/key-lock stream is built with
/// <see cref="BassFx.TempoCreate"/> (<c>BassMixerBackend.PlugDeck</c>), which needs <c>bass_fx</c> shipped
/// next to the core <c>bass</c> library. When it was missing, every <c>DeckLoadTrack</c> failed with
/// <see cref="DllNotFoundException"/> "Unable to load DLL 'bass_fx'". The fix ships it via
/// <c>scripts/bass-libraries.manifest</c> (<c>bass_fx|required|z/0/</c>) → copied into bin.
///
/// CI has NO native BASS, so this test SKIPS when even core BASS is unavailable. But when core BASS works
/// (a real dev/installed machine), it ASSERTS that <c>bass_fx</c> is present too — so the regression
/// (core present, bass_fx missing) would fail this test loudly rather than only surfacing at runtime.
/// </summary>
public sealed class BassFxNativeSmokeTests
{
    [Fact]
    public void TempoCreate_Succeeds_WhenCoreBassIsAvailable_ProvingBassFxIsShipped()
    {
        string wavPath = WriteShortSineWav();
        int source = 0;
        int tempo = 0;
        try
        {
            // Init the "no sound" decode device (device 0) — mirrors BassAudioDecoder. A DllNotFoundException
            // here means core BASS itself is absent (the CI case) → nothing to assert, skip.
            try
            {
                if (!(Bass.Init(0) || Bass.LastError == Errors.Already))
                    return; // core BASS could not init in this environment — skip.
            }
            catch (DllNotFoundException)
            {
                return; // core bass.dll absent (CI) — the bass_fx guard does not apply.
            }

            source = Bass.CreateStream(wavPath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (source == 0)
                return; // could not decode even with BASS up — not a bass_fx concern.

            // The exact call that failed when bass_fx was missing. With the native shipped it must succeed.
            try
            {
                tempo = BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);
            }
            catch (DllNotFoundException ex)
            {
                Assert.Fail(
                    "bass_fx native is missing while core BASS is present — DeckLoadTrack will fail. " +
                    "Ensure 'bass_fx|required|z/0/' is in scripts/bass-libraries.manifest and copied to bin. " +
                    ex.Message);
            }

            Assert.True(tempo != 0, $"BassFx.TempoCreate failed even though bass_fx loaded: {Bass.LastError}");
        }
        finally
        {
            // FxFreeSource frees the underlying source with the tempo stream; otherwise free the source.
            if (tempo != 0) Bass.StreamFree(tempo);
            else if (source != 0) Bass.StreamFree(source);
            try { File.Delete(wavPath); } catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    // A minimal valid 16-bit PCM mono WAV (a short sine) so BASS can open a real decode stream.
    private static string WriteShortSineWav()
    {
        const int sampleRate = 44100;
        const int samples = sampleRate / 10; // 0.1 s
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-bassfx-{Guid.NewGuid():N}.wav");

        using var writer = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write));
        int dataBytes = samples * 2;
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);                 // fmt chunk size
        writer.Write((short)1);           // PCM
        writer.Write((short)1);           // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);     // byte rate (mono, 16-bit)
        writer.Write((short)2);           // block align
        writer.Write((short)16);          // bits per sample
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)sampleRate;
            writer.Write((short)(Math.Sin(t * 2 * Math.PI * 440) * 16000));
        }
        return path;
    }
}

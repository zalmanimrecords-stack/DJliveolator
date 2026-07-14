using Liveolator.App.Features.Libraries;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Persistence;

namespace Liveolator.App.Tests.Libraries;

/// <summary>
/// The pure cue → 0..1 waveform-fraction mapping behind the library overview's hot-cue markers.
/// </summary>
public sealed class LibrariesViewModelCueMarkerTests
{
    private static TrackCueRecord Record(int sampleRate, params long[] positions) => new(
        "/music/Alpha.wav", sampleRate, SlotCount: 8, PrimaryCueSamples: null,
        HotCues: positions.Select((p, i) => new HotCue(i, p)).ToArray());

    [Fact]
    public void Maps_each_cue_to_its_fraction_of_the_track()
    {
        // 44.1 kHz, 10 s track: 1 s → 0.1, 5 s → 0.5, 9 s → 0.9.
        TrackCueRecord record = Record(44_100, 44_100, 220_500, 396_900);

        IReadOnlyList<double> fractions = LibrariesViewModel.CueMarkerFractions(record, durationSeconds: 10.0);

        Assert.Equal(new[] { 0.1, 0.5, 0.9 }, fractions.Select(f => Math.Round(f, 3)));
    }

    [Fact]
    public void Drops_cues_past_the_end_of_the_track()
    {
        // A cue at 12 s on a 10 s track (a stale grid) must not paint off the strip.
        TrackCueRecord record = Record(44_100, 220_500, 529_200);

        IReadOnlyList<double> fractions = LibrariesViewModel.CueMarkerFractions(record, durationSeconds: 10.0);

        Assert.Equal(new[] { 0.5 }, fractions.Select(f => Math.Round(f, 3)));
    }

    [Theory]
    [InlineData(0.0, 44_100)]   // unknown duration
    [InlineData(10.0, 0)]       // unknown sample rate
    public void Returns_empty_when_duration_or_sample_rate_is_unknown(double duration, int sampleRate)
    {
        TrackCueRecord record = Record(sampleRate, 44_100);

        Assert.Empty(LibrariesViewModel.CueMarkerFractions(record, duration));
    }

    [Fact]
    public void Click_fraction_maps_to_the_sample_offset_at_that_position()
    {
        // 0.5 of a 10 s / 44.1 kHz track → 5 s → 220_500 samples.
        Assert.Equal(220_500L, LibrariesViewModel.CueSamplesAt(0.5, durationSeconds: 10.0, sampleRate: 44_100));
    }

    [Theory]
    [InlineData(-0.2)]  // a click left of the strip
    [InlineData(1.5)]   // ...or past its end
    public void Click_fraction_is_clamped_into_the_track(double fraction)
    {
        long samples = LibrariesViewModel.CueSamplesAt(fraction, durationSeconds: 10.0, sampleRate: 44_100);

        Assert.InRange(samples, 0L, 441_000L); // never negative, never past the last sample
    }
}

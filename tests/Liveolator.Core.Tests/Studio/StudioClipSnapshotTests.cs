using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

/// <summary>
/// The persisted STUDIO snapshot (in Liveolator.Media) serializes the <see cref="StudioClip"/> record
/// directly with System.Text.Json. These tests pin that on-disk contract from Core: the new gain/fade
/// fields round-trip, and legacy JSON written before they existed loads with unity/zero defaults.
/// They mirror the exact serializer options the store uses (string enums) without depending on Media.
/// </summary>
public class StudioClipSnapshotTests
{
    private const double Tol = 1e-9;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void StudioClip_WithGainAndFades_RoundTrips()
    {
        var clip = new StudioClip(
            DeckSlot: 1, TrackPath: "/m/a.wav", TimelineStartSeconds: 8,
            SourceIn: TimeSpan.FromSeconds(2), SourceOut: TimeSpan.FromSeconds(42),
            SourceBpm: 128, WarpEnabled: true,
            Gain: 0.7, FadeInSeconds: 3, FadeOutSeconds: 5);

        string json = JsonSerializer.Serialize(clip, Options);
        StudioClip? back = JsonSerializer.Deserialize<StudioClip>(json, Options);

        Assert.NotNull(back);
        Assert.Equal(clip, back); // record value equality covers every field, including the new ones
        Assert.Equal(0.7, back!.Gain, Tol);
        Assert.Equal(3, back.FadeInSeconds, Tol);
        Assert.Equal(5, back.FadeOutSeconds, Tol);
    }

    [Fact]
    public void LegacyClipJson_WithoutGainOrFades_DefaultsToUnityAndZero()
    {
        // A clip JSON as written before gain/fades existed (the fields are simply absent).
        const string legacy = """
        {
          "DeckSlot": 0,
          "TrackPath": "/m/old.wav",
          "TimelineStartSeconds": 4,
          "SourceIn": "00:00:00",
          "SourceOut": "00:00:30",
          "SourceBpm": 120,
          "WarpEnabled": false
        }
        """;

        StudioClip? clip = JsonSerializer.Deserialize<StudioClip>(legacy, Options);

        Assert.NotNull(clip);
        Assert.Equal(1.0, clip!.Gain, Tol);         // unity by default
        Assert.Equal(0.0, clip.FadeInSeconds, Tol); // no fades by default
        Assert.Equal(0.0, clip.FadeOutSeconds, Tol);
        Assert.Equal("/m/old.wav", clip.TrackPath); // existing fields still load
    }
}

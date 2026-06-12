using Liveolator.Audio.Playback;
using Liveolator.Core.Settings;
using ManagedBass;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Verifies the pure resolution from the user's persisted <see cref="AudioSettings"/> to native BASS
/// init parameters — the only part of "apply the output choice" that runs with no native BASS. The
/// device id is the BASS device-index string produced by <c>BassOutputDeviceCatalog</c>; a stale /
/// blank / non-numeric id (or the "no sound" device 0) must fold to the system-default sentinel so the
/// app never tries to open a bogus device.
/// </summary>
public sealed class BassInitOptionsTests
{
    [Fact]
    public void From_Null_UsesDefaultDeviceAndDefaultBuffer()
    {
        BassInitOptions options = BassInitOptions.From(null);

        Assert.Equal(BassInitOptions.DefaultDevice, options.DeviceIndex);
        Assert.Equal(AudioSettings.DefaultBufferMs, options.BufferMilliseconds);
    }

    [Fact]
    public void From_NumericDeviceId_ParsesBackToBassIndex()
    {
        BassInitOptions options = BassInitOptions.From(new AudioSettings { OutputDeviceId = "3" });

        Assert.Equal(3, options.DeviceIndex);
    }

    [Theory]
    [InlineData(null)]      // platform default
    [InlineData("")]        // blank
    [InlineData("   ")]     // whitespace
    [InlineData("default")] // non-numeric
    [InlineData("0")]       // BASS "No sound" device — never a real output
    [InlineData("-5")]      // out of range
    public void From_NonRealDeviceId_FoldsToDefaultDevice(string? deviceId)
    {
        BassInitOptions options = BassInitOptions.From(new AudioSettings { OutputDeviceId = deviceId });

        Assert.Equal(BassInitOptions.DefaultDevice, options.DeviceIndex);
    }

    [Fact]
    public void From_Null_HasNoCueDevice()
    {
        BassInitOptions options = BassInitOptions.From(null);

        Assert.Equal(BassInitOptions.NoCueDevice, options.CueDeviceIndex);
        Assert.False(options.HasCueDevice);
    }

    [Fact]
    public void From_NumericCueDeviceId_ParsesAndFlagsCuePresent()
    {
        BassInitOptions options = BassInitOptions.From(
            new AudioSettings { OutputDeviceId = "1", CueOutputDeviceId = "2" });

        Assert.Equal(2, options.CueDeviceIndex);
        Assert.True(options.HasCueDevice);
    }

    [Theory]
    [InlineData(null)]      // not configured
    [InlineData("")]        // blank
    [InlineData("notnum")]  // non-numeric
    [InlineData("0")]       // BASS "No sound" device
    [InlineData("-1")]      // out of range
    public void From_NonRealCueDeviceId_FoldsToNoCueDevice(string? cueDeviceId)
    {
        BassInitOptions options = BassInitOptions.From(
            new AudioSettings { CueOutputDeviceId = cueDeviceId });

        Assert.Equal(BassInitOptions.NoCueDevice, options.CueDeviceIndex);
        Assert.False(options.HasCueDevice);
    }

    [Fact]
    public void From_CarriesOutputPairsFromSettings()
    {
        BassInitOptions options = BassInitOptions.From(
            new AudioSettings { MasterOutputPair = 0, CueOutputPair = 1 });

        Assert.Equal(0, options.MasterOutputPair);
        Assert.Equal(1, options.CueOutputPair);
    }

    [Fact]
    public void SpeakerFlag_FrontPair_KeepsDefaultStereoRouting()
    {
        // Pair 0 (outputs 1/2) must stay BASS default so the common single-output path is unchanged.
        Assert.Equal(BassFlags.Default, BassInitOptions.SpeakerFlag(0));
    }

    [Theory]
    [InlineData(1, BassFlags.SpeakerPair2)] // outputs 3/4 (the CMD STUDIO 2A cue)
    [InlineData(2, BassFlags.SpeakerPair3)] // outputs 5/6
    [InlineData(3, BassFlags.SpeakerPair4)] // outputs 7/8
    public void SpeakerFlag_NonFrontPair_SetsBassSpeakerAssignment(int pairIndex, BassFlags expected)
        => Assert.Equal(expected, BassInitOptions.SpeakerFlag(pairIndex));

    [Fact]
    public void SpeakerFlag_OutOfRangePair_IsClampedToTheLastPair()
        => Assert.Equal(BassFlags.SpeakerPair4, BassInitOptions.SpeakerFlag(99));

    [Fact]
    public void CueSpeakerFlag_ReflectsTheCuePair()
    {
        BassInitOptions options = BassInitOptions.From(
            new AudioSettings { OutputDeviceId = "1", CueOutputDeviceId = "1", CueOutputPair = 1 });

        Assert.Equal(BassFlags.SpeakerPair2, options.CueSpeakerFlag);
        Assert.Equal(BassFlags.Default, options.MasterSpeakerFlag);
    }

    [Fact]
    public void From_BufferOutOfRange_IsClampedToSupportedRange()
    {
        Assert.Equal(
            AudioSettings.MaxBufferMs,
            BassInitOptions.From(new AudioSettings { BufferMilliseconds = 10_000 }).BufferMilliseconds);
        Assert.Equal(
            AudioSettings.MinBufferMs,
            BassInitOptions.From(new AudioSettings { BufferMilliseconds = 1 }).BufferMilliseconds);
    }

    [Theory]
    [InlineData(AudioSettings.DefaultBufferMs, 10)] // 40 / 4 = 10
    [InlineData(AudioSettings.MinBufferMs, 5)]      // 10 / 4 = 2 -> clamped up to 5
    [InlineData(AudioSettings.MaxBufferMs, 20)]     // 200 / 4 = 50 -> clamped down to 20
    public void UpdatePeriod_IsAQuarterOfBuffer_ClampedToSafeRange(int bufferMs, int expectedPeriod)
    {
        BassInitOptions options = BassInitOptions.From(new AudioSettings { BufferMilliseconds = bufferMs });

        Assert.Equal(expectedPeriod, options.UpdatePeriodMilliseconds);
    }

    [Theory]
    [InlineData(AudioSettings.MinBufferMs)]
    [InlineData(AudioSettings.DefaultBufferMs)]
    [InlineData(AudioSettings.MaxBufferMs)]
    public void UpdatePeriod_StaysBelowBuffer_SoThePlaybackBufferNeverStarves(int bufferMs)
    {
        // The invariant that prevents the ~0.4x slow-playback bug: BASS refills the buffer on this
        // period, so it must be strictly below the buffer length across the whole supported range.
        BassInitOptions options = BassInitOptions.From(new AudioSettings { BufferMilliseconds = bufferMs });

        Assert.True(
            options.UpdatePeriodMilliseconds < options.BufferMilliseconds,
            $"update period {options.UpdatePeriodMilliseconds}ms must be below buffer {options.BufferMilliseconds}ms");
    }
}

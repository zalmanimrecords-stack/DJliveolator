using Liveolator.Audio.Playback;
using Liveolator.Core.Settings;
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
    public void From_BufferOutOfRange_IsClampedToSupportedRange()
    {
        Assert.Equal(
            AudioSettings.MaxBufferMs,
            BassInitOptions.From(new AudioSettings { BufferMilliseconds = 10_000 }).BufferMilliseconds);
        Assert.Equal(
            AudioSettings.MinBufferMs,
            BassInitOptions.From(new AudioSettings { BufferMilliseconds = 1 }).BufferMilliseconds);
    }
}

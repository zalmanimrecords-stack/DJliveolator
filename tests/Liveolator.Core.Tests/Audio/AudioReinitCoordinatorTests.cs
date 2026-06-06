using System.Collections.Generic;
using Liveolator.Core.Audio;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

/// <summary>
/// Verifies the runtime audio re-init decision + rollback logic (doc 12 Settings): a device/buffer
/// change re-opens the engine, an unchanged selection is a no-op, and a failed re-open rolls back to
/// the last working settings so the app is never left without audio.
/// </summary>
public sealed class AudioReinitCoordinatorTests
{
    private sealed class FakeReinitializer : IAudioEngineReinitializer
    {
        public List<AudioSettings> Calls { get; } = new();

        // Devices for which Reinitialize returns false (an open failure).
        public HashSet<string?> FailingDevices { get; } = new();
        public bool ThrowOnce { get; set; }

        public bool Reinitialize(AudioSettings settings)
        {
            Calls.Add(settings);
            if (ThrowOnce)
            {
                ThrowOnce = false;
                throw new InvalidOperationException("native fault");
            }
            return !FailingDevices.Contains(settings.OutputDeviceId);
        }
    }

    private static AudioSettings Audio(string? device, int buffer = 40)
        => new() { OutputDeviceId = device, BufferMilliseconds = buffer };

    [Fact]
    public void Apply_SameDeviceAndBuffer_IsNoOp()
    {
        var fake = new FakeReinitializer();
        var coordinator = new AudioReinitCoordinator(fake, startupSettings: Audio("1"));

        AudioReinitResult result = coordinator.Apply(Audio("1"));

        Assert.Equal(AudioReinitResult.NoChange, result);
        Assert.Empty(fake.Calls);
        Assert.Equal("1", coordinator.Current.OutputDeviceId);
    }

    [Fact]
    public void Apply_DeviceChanged_ReopensAndBecomesCurrent()
    {
        var fake = new FakeReinitializer();
        var coordinator = new AudioReinitCoordinator(fake, startupSettings: Audio("1"));

        AudioReinitResult result = coordinator.Apply(Audio("2"));

        Assert.Equal(AudioReinitResult.Reinitialized, result);
        Assert.Equal("2", Assert.Single(fake.Calls).OutputDeviceId);
        Assert.Equal("2", coordinator.Current.OutputDeviceId);
    }

    [Fact]
    public void Apply_BufferChangedOnly_Reopens()
    {
        var fake = new FakeReinitializer();
        var coordinator = new AudioReinitCoordinator(fake, startupSettings: Audio("1", buffer: 40));

        AudioReinitResult result = coordinator.Apply(Audio("1", buffer: 100));

        Assert.Equal(AudioReinitResult.Reinitialized, result);
        Assert.Equal(100, coordinator.Current.BufferMilliseconds);
    }

    [Fact]
    public void Apply_ReopenFails_RollsBackToPreviousWorkingSettings()
    {
        var fake = new FakeReinitializer();
        fake.FailingDevices.Add("2");
        var coordinator = new AudioReinitCoordinator(fake, startupSettings: Audio("1"));

        AudioReinitResult result = coordinator.Apply(Audio("2"));

        Assert.Equal(AudioReinitResult.RolledBack, result);
        // Tried the new device, then re-opened the previous one as the rollback.
        Assert.Collection(fake.Calls,
            c => Assert.Equal("2", c.OutputDeviceId),
            c => Assert.Equal("1", c.OutputDeviceId));
        // Current stays the last working device, so it remains the authoritative rollback target.
        Assert.Equal("1", coordinator.Current.OutputDeviceId);
    }

    [Fact]
    public void Apply_ReinitializerThrows_TreatedAsFailureAndRollsBack()
    {
        var fake = new FakeReinitializer { ThrowOnce = true };
        var coordinator = new AudioReinitCoordinator(fake, startupSettings: Audio("1"));

        AudioReinitResult result = coordinator.Apply(Audio("2"));

        Assert.Equal(AudioReinitResult.RolledBack, result);
        Assert.Equal("1", coordinator.Current.OutputDeviceId);
    }

    [Fact]
    public void Apply_NormalizesIncomingSettings_OutOfRangeBufferDoesNotForceReopen()
    {
        // Startup buffer 200 (max). An incoming 9999 normalizes to 200 -> no real change -> no-op.
        var fake = new FakeReinitializer();
        var coordinator = new AudioReinitCoordinator(fake, startupSettings: Audio("1", buffer: AudioSettings.MaxBufferMs));

        AudioReinitResult result = coordinator.Apply(Audio("1", buffer: 9999));

        Assert.Equal(AudioReinitResult.NoChange, result);
        Assert.Empty(fake.Calls);
    }
}

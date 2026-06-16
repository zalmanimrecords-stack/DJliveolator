using Liveolator.Core.Platform;

namespace Liveolator.Core.Tests.Platform;

/// <summary>
/// Test double for <see cref="ISystemVolumeController"/>: records the last value written, can simulate an
/// unsupported host (<see cref="IsAvailable"/> = false) and a controller that throws on write.
/// </summary>
internal sealed class FakeSystemVolumeController : ISystemVolumeController
{
    private double _volume;

    public FakeSystemVolumeController(bool available = true, double initial = 0.5)
    {
        IsAvailable = available;
        _volume = initial;
    }

    public bool IsAvailable { get; }

    public bool ThrowOnSet { get; set; }

    public int SetCount { get; private set; }

    public double GetVolume() => _volume;

    public void SetVolume(double level)
    {
        SetCount++;
        if (ThrowOnSet)
            throw new InvalidOperationException("simulated volume failure");
        _volume = level;
    }
}

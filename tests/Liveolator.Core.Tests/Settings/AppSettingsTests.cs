using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Default_HasSystemDefaultsAndNoSelections()
    {
        AppSettings settings = AppSettings.Default;

        Assert.Null(settings.Audio.OutputDeviceId);             // system default device
        Assert.Equal(AudioSettings.DefaultBufferMs, settings.Audio.BufferMilliseconds);
        Assert.Null(settings.Midi.ControllerInputName);
        Assert.Null(settings.Midi.FeedbackOutputName);
    }

    [Theory]
    [InlineData(0, AudioSettings.MinBufferMs)]      // below floor -> clamp up
    [InlineData(5, AudioSettings.MinBufferMs)]
    [InlineData(40, 40)]                            // in range -> unchanged
    [InlineData(10_000, AudioSettings.MaxBufferMs)] // above ceiling -> clamp down
    public void Normalized_ClampsBufferIntoSupportedRange(int input, int expected)
    {
        var audio = AudioSettings.Default with { BufferMilliseconds = input };

        Assert.Equal(expected, audio.Normalized().BufferMilliseconds);
    }

    [Fact]
    public void Normalized_PreservesDeviceSelections()
    {
        var audio = new AudioSettings { OutputDeviceId = "bass:2", BufferMilliseconds = 30 };

        AudioSettings result = audio.Normalized();

        Assert.Equal("bass:2", result.OutputDeviceId);
        Assert.Equal(30, result.BufferMilliseconds);
    }

    [Fact]
    public void Normalized_NormalizesNestedAudio()
    {
        var settings = AppSettings.Default with
        {
            Audio = AppSettings.Default.Audio with { BufferMilliseconds = 999 },
        };

        Assert.Equal(AudioSettings.MaxBufferMs, settings.Normalized().Audio.BufferMilliseconds);
    }

    [Fact]
    public void Midi_BlankSelectionIsTreatedAsNone()
    {
        // A picker that yields "" (no device chosen) must normalize to null, not an empty selection.
        var midi = new MidiSettings { ControllerInputName = "  ", FeedbackOutputName = "" };

        MidiSettings result = midi.Normalized();

        Assert.Null(result.ControllerInputName);
        Assert.Null(result.FeedbackOutputName);
    }
}

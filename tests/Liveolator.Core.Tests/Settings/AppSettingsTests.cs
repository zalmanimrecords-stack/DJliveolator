using Liveolator.Core.Audio;
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
        Assert.False(settings.Extensions.DeveloperMode);
        Assert.Null(settings.Extensions.ActiveUiThemeId);
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
    public void Default_HasNoCaptureSource()
    {
        Assert.Null(AppSettings.Default.Audio.CaptureDeviceId);
        Assert.Null(AppSettings.Default.Audio.CaptureSource);
    }

    [Fact]
    public void Normalized_PreservesConsistentCaptureSelection()
    {
        var audio = new AudioSettings
        {
            CaptureDeviceId = "3",
            CaptureSource = CaptureSourceKind.SystemLoopback,
        };

        AudioSettings result = audio.Normalized();

        Assert.Equal("3", result.CaptureDeviceId);
        Assert.Equal(CaptureSourceKind.SystemLoopback, result.CaptureSource);
    }

    [Theory]
    [InlineData("", CaptureSourceKind.LineInput)]   // id missing -> no capture
    [InlineData("  ", CaptureSourceKind.LineInput)] // blank id  -> no capture
    public void Normalized_BlankCaptureIdFoldsToNoCapture(string id, CaptureSourceKind kind)
    {
        var audio = new AudioSettings { CaptureDeviceId = id, CaptureSource = kind };

        AudioSettings result = audio.Normalized();

        Assert.Null(result.CaptureDeviceId);
        Assert.Null(result.CaptureSource);
    }

    [Fact]
    public void Normalized_CaptureIdWithoutKindFoldsToNoCapture()
    {
        // A half-written / hand-edited config (id present, kind missing) must not yield an
        // inconsistent selection — fold the whole choice to "no capture".
        var audio = new AudioSettings { CaptureDeviceId = "2", CaptureSource = null };

        AudioSettings result = audio.Normalized();

        Assert.Null(result.CaptureDeviceId);
        Assert.Null(result.CaptureSource);
    }

    [Fact]
    public void Normalized_TrimsCaptureId()
    {
        var audio = new AudioSettings { CaptureDeviceId = " 5 ", CaptureSource = CaptureSourceKind.LineInput };

        Assert.Equal("5", audio.Normalized().CaptureDeviceId);
    }

    [Fact]
    public void Default_HasNoCueOutputAndFrontPairs()
    {
        AudioSettings audio = AppSettings.Default.Audio;

        Assert.Null(audio.CueOutputDeviceId);   // headphones have nowhere to play until chosen
        Assert.Equal(0, audio.MasterOutputPair); // outputs 1/2
        Assert.Equal(0, audio.CueOutputPair);    // outputs 1/2
    }

    [Theory]
    [InlineData(-1, 0)]                                  // below floor -> first pair
    [InlineData(1, 1)]                                   // valid -> kept
    [InlineData(99, OutputChannelPair.MaxPairIndex)]     // beyond addressable -> last pair
    public void Normalized_ClampsOutputPairsToAddressableRange(int input, int expected)
    {
        var audio = new AudioSettings { MasterOutputPair = input, CueOutputPair = input };

        AudioSettings result = audio.Normalized();

        Assert.Equal(expected, result.MasterOutputPair);
        Assert.Equal(expected, result.CueOutputPair);
    }

    [Theory]
    [InlineData("", null)]      // blank -> no cue output
    [InlineData("   ", null)]   // whitespace -> no cue output
    [InlineData(" 2 ", "2")]    // trimmed
    public void Normalized_FoldsBlankCueDeviceIdToNull_AndTrims(string id, string? expected)
    {
        var audio = new AudioSettings { CueOutputDeviceId = id };

        Assert.Equal(expected, audio.Normalized().CueOutputDeviceId);
    }

    [Fact]
    public void Normalized_PreservesCueDeviceSelection()
    {
        var audio = new AudioSettings { CueOutputDeviceId = "3", CueOutputPair = 1 };

        AudioSettings result = audio.Normalized();

        Assert.Equal("3", result.CueOutputDeviceId);
        Assert.Equal(1, result.CueOutputPair);
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

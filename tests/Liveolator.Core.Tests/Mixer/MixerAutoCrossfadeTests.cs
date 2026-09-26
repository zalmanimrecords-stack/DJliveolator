using System.Collections.Generic;
using Liveolator.Core.Actions;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Core.Tests.Mixer;

// AUTO (MixerAutoCrossfade) + MIX SEC (MixerAutoCrossfadeTime): the crossfader travels to the far side over
// MIX SEC, driven by PumpAutoCrossfade on a fake clock so every step is deterministic.
public class MixerAutoCrossfadeTests
{
    private const double Tol = 1e-9;

    private readonly bool[] _playing = { true, true };
    private double _now;

    private MixerActionHandler NewHandler(bool guardDecks = true)
        => new(new FakeMixer(), isDeckPlaying: guardDecks ? slot => _playing[slot] : null, nowSeconds: () => _now);

    private static PerformanceAction Auto(bool pressed = true)
        => new(PerformanceActionKind.MixerAutoCrossfade, IsPressed: pressed);

    private static PerformanceAction Crossfade(double position, string? origin = null)
        => new(PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, position, Origin: origin);

    private static PerformanceAction MixSec(double knob)
        => new(PerformanceActionKind.MixerAutoCrossfadeTime, ActionInputMode.Absolute, knob);

    private static bool IsFading(MixerActionHandler handler)
        => handler.GetFeedback(PerformanceActionKind.MixerAutoCrossfade, 0).IsActive;

    [Fact]
    public void MixSec_DefaultsToTenSeconds_ReportedAsHalfTheKnob()
    {
        MixerActionHandler handler = NewHandler();

        Assert.Equal(10.0, handler.State.AutoCrossfadeSeconds, Tol);
        Assert.Equal(0.5, handler.GetFeedback(PerformanceActionKind.MixerAutoCrossfadeTime, 0).Value, Tol);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 5.0)]
    [InlineData(0.26, 5.0)]   // whole seconds only
    [InlineData(1.0, 20.0)]
    [InlineData(1.7, 20.0)]   // clamped to the 0..20 s range
    [InlineData(-0.3, 0.0)]
    public void MixSec_MapsTheKnobToWholeSecondsWithinZeroToTwenty(double knob, double expectedSeconds)
    {
        MixerActionHandler handler = NewHandler();

        handler.Handle(MixSec(knob));

        Assert.Equal(expectedSeconds, handler.State.AutoCrossfadeSeconds, Tol);
        Assert.Equal(expectedSeconds / 20.0,
            handler.GetFeedback(PerformanceActionKind.MixerAutoCrossfadeTime, 0).Value, Tol);
    }

    [Fact]
    public void Auto_FromTheASide_FadesToB_OverMixSec()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(Crossfade(0.0));

        handler.Handle(Auto());
        Assert.True(IsFading(handler));

        _now = 5.0;
        handler.PumpAutoCrossfade();
        Assert.Equal(0.5, handler.State.Crossfader, Tol);
        Assert.True(IsFading(handler));

        _now = 10.0;
        handler.PumpAutoCrossfade();
        Assert.Equal(1.0, handler.State.Crossfader, Tol);
        Assert.False(IsFading(handler));
    }

    [Fact]
    public void Auto_FromTheBSide_FadesToA()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(Crossfade(0.8));

        handler.Handle(Auto());
        _now = 10.0;
        handler.PumpAutoCrossfade();

        Assert.Equal(0.0, handler.State.Crossfader, Tol);
    }

    [Fact]
    public void Auto_FromTheCentre_GoesToB()
    {
        MixerActionHandler handler = NewHandler(); // default crossfader = centre

        handler.Handle(Auto());
        _now = 10.0;
        handler.PumpAutoCrossfade();

        Assert.Equal(1.0, handler.State.Crossfader, Tol);
    }

    [Fact]
    public void Auto_FromPartway_StillTakesTheWholeMixSec()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(Crossfade(0.3));

        handler.Handle(Auto());
        _now = 5.0;
        handler.PumpAutoCrossfade();
        Assert.Equal(0.65, handler.State.Crossfader, Tol);

        _now = 9.9;
        handler.PumpAutoCrossfade();
        Assert.True(IsFading(handler));
    }

    [Fact]
    public void Auto_WithZeroSeconds_CutsToTheOtherSideAtOnce()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(MixSec(0.0));
        handler.Handle(Crossfade(0.0));

        handler.Handle(Auto());

        Assert.Equal(1.0, handler.State.Crossfader, Tol);
        Assert.False(IsFading(handler));
    }

    [Fact]
    public void Auto_PressedAgainMidFade_StopsWhereItIs()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(Crossfade(0.0));
        handler.Handle(Auto());
        _now = 4.0;
        handler.PumpAutoCrossfade();

        handler.Handle(Auto());
        _now = 8.0;
        handler.PumpAutoCrossfade();

        Assert.False(IsFading(handler));
        Assert.Equal(0.4, handler.State.Crossfader, Tol);
    }

    [Fact]
    public void ManualCrossfade_MidFade_TakesOver()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(Crossfade(0.0));
        handler.Handle(Auto());
        _now = 2.0;
        handler.PumpAutoCrossfade();

        handler.Handle(Crossfade(0.1));
        _now = 6.0;
        handler.PumpAutoCrossfade();

        Assert.False(IsFading(handler));
        Assert.Equal(0.1, handler.State.Crossfader, Tol);
    }

    [Fact]
    public void Auto_Release_DoesNothing()
    {
        MixerActionHandler handler = NewHandler();

        handler.Handle(Auto(pressed: false));

        Assert.False(IsFading(handler));
    }

    [Fact]
    public void Auto_IntoADeckThatIsNotPlaying_IsRefused_AndFlashesBriefly()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(Crossfade(0.0));
        _playing[MixerState.DeckB] = false;

        handler.Handle(Auto());

        ActionFeedbackState refused = handler.GetFeedback(PerformanceActionKind.MixerAutoCrossfade, 0);
        Assert.False(refused.IsActive);
        Assert.Equal(MixerActionHandler.AutoCrossfadeRefused, refused.Argument);
        _now = 10.0;
        handler.PumpAutoCrossfade();
        Assert.Equal(0.0, handler.State.Crossfader, Tol);          // never moved
        Assert.Null(handler.GetFeedback(PerformanceActionKind.MixerAutoCrossfade, 0).Argument); // flash over
    }

    [Fact]
    public void Auto_WithNoPlayStateSource_IsNotGuarded()
    {
        MixerActionHandler handler = NewHandler(guardDecks: false);
        handler.Handle(Crossfade(0.0));

        handler.Handle(Auto());

        Assert.True(IsFading(handler));
    }

    [Fact]
    public void Auto_LeavesThePhysicalCrossfaderNeedingPickup_UntilAHumanMovesIt()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(MixSec(0.0));
        handler.Handle(Crossfade(0.0));
        Assert.False(handler.GetFeedback(PerformanceActionKind.MixerCrossfade, 0).RequiresPickup);

        handler.Handle(Auto());
        Assert.True(handler.GetFeedback(PerformanceActionKind.MixerCrossfade, 0).RequiresPickup);

        handler.Handle(Crossfade(0.9, origin: "studio")); // other automation keeps the hardware out of sync
        Assert.True(handler.GetFeedback(PerformanceActionKind.MixerCrossfade, 0).RequiresPickup);

        handler.Handle(Crossfade(0.9));                   // a human move: the hardware is the truth again
        Assert.False(handler.GetFeedback(PerformanceActionKind.MixerCrossfade, 0).RequiresPickup);
    }

    [Fact]
    public void Fade_RaisesCrossfadeFeedback_AsItMoves()
    {
        MixerActionHandler handler = NewHandler();
        handler.Handle(Crossfade(0.0));
        var positions = new List<double>();
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.MixerCrossfade)
                positions.Add(e.State.Value);
        };

        handler.Handle(Auto());
        _now = 2.5;
        handler.PumpAutoCrossfade();
        _now = 10.0;
        handler.PumpAutoCrossfade();

        Assert.Equal(new[] { 0.0, 0.25, 1.0 }, positions);
    }
}

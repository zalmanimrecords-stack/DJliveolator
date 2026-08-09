using Liveolator.Core.Library.Music;
using Liveolator.Core.Mixer;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Render;
using Liveolator.Core.Studio.Set;
using Xunit;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// The acceptance criteria for a built set. These are the things a DJ would notice immediately: the decks
/// running at the same tempo, the phrases lining up, nothing dropping into silence, and no record cut at
/// its peak or stretched until it sounds wrong.
/// </summary>
public class DjSetArrangerTests
{
    private static readonly SetBuildOptions Options = new();

    /// <summary>Every file reachable — these tests never touch the filesystem.</summary>
    private readonly DjSetArranger _arranger = new(_ => true);

    private static MusicTrack[] StandardPool() =>
        new[]
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.Track("b.mp3", "8A", 127, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.Track("c.mp3", "9A", 129, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.Track("d.mp3", "9A", 130, structure: SetTrackFixture.StandardStructure()),
        };

    private DjSetPlan BuildStandard(SetBuildOptions? options = null, MusicTrack[]? pool = null)
    {
        pool ??= StandardPool();
        return _arranger.Build(pool, pool[0], new HarmonicSetOptions(pool.Length), options ?? Options);
    }

    // ---- tempo and warp -------------------------------------------------------------------------

    [Fact]
    public void Build_RunsEveryClip_AtOneSetTempo()
    {
        DjSetPlan plan = BuildStandard();

        Assert.Equal(4, plan.TrackCount);
        Assert.All(plan.Project.Clips, clip => Assert.True(clip.WarpEnabled, $"{clip.TrackPath} is not warped"));
        Assert.All(plan.Project.Clips, clip => Assert.True(clip.SourceBpm > 0, $"{clip.TrackPath} has no source tempo"));
    }

    [Fact]
    public void Build_SoundsBothDecks_AtTheSetTempo_ThroughEveryBlend()
    {
        // The decisive invariant: a 0.03% tempo difference is 10 ms of drift across a 30 s blend, which is
        // not a flam — it is two records playing different songs. Checked as the tempo each deck actually
        // sounds at (source tempo x warp factor), since two records at different native tempi reach the
        // same result through different factors.
        DjSetPlan plan = BuildStandard();
        var mix = new MixPlan(plan.Project);

        foreach (SetTransition transition in plan.Transitions)
        {
            double middle = transition.StartSeconds + (transition.OverlapSeconds / 2.0);
            var sounding = Enumerable.Range(0, MixerState.DeckCount)
                .Select(slot => mix.EvaluateDeck(slot, middle))
                .Where(state => state.HasAudio)
                .ToList();

            Assert.Equal(2, sounding.Count);
            foreach (DeckMixState state in sounding)
            {
                double sourceBpm = plan.Project.Clips.First(c => c.TrackPath == state.SourcePath).SourceBpm;
                Assert.Equal(plan.TempoBpm, sourceBpm * state.WarpFactor, 9);
            }
        }
    }

    [Fact]
    public void Build_RejectsATrack_ThatWouldStretchPastTheLimit_AndSaysByHowMuch()
    {
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.Track("b.mp3", "8A", 128),
            SetTrackFixture.Track("slow.mp3", "8A", 100),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(3, BpmTolerance: 40), Options);

        RejectedCandidate rejected = Assert.Single(plan.Rejected, r => r.Path == "slow.mp3");
        Assert.Equal(RejectReason.OutsideTempoRange, rejected.Reason);
        Assert.NotNull(rejected.NeededWarpPercent);
        Assert.Equal(28.0, rejected.NeededWarpPercent!.Value, 1);
        Assert.DoesNotContain(plan.Project.Clips, c => c.TrackPath == "slow.mp3");
    }

    [Fact]
    public void Build_KeepsEveryPlacedTrack_InsideTheWarpLimit()
    {
        DjSetPlan plan = BuildStandard(new SetBuildOptions(MaxWarpPercent: 3.0));

        Assert.All(plan.Transitions, t =>
        {
            Assert.True(Math.Abs(t.FromWarpPercent) <= 3.0, $"{t.FromPath} warped {t.FromWarpPercent}%");
            Assert.True(Math.Abs(t.ToWarpPercent) <= 3.0, $"{t.ToPath} warped {t.ToWarpPercent}%");
        });
    }

    // ---- phase and phrase alignment -------------------------------------------------------------

    [Fact]
    public void Build_StartsEveryClip_OnAProjectPhraseLine()
    {
        // Phrase-aligned starts are what keep two warped records in phase for a whole crossfade, with no
        // per-transition correction.
        DjSetPlan plan = BuildStandard();
        double phrase = SetBuildOptions.PhraseBars * SetBuildOptions.BarSeconds(plan.TempoBpm);

        Assert.All(plan.Project.Clips, clip =>
        {
            double phrases = clip.TimelineStartSeconds / phrase;
            Assert.Equal(Math.Round(phrases), phrases, 6);
        });
    }

    [Fact]
    public void Build_EntersEveryClip_OnItsOwnPhraseLine()
    {
        DjSetPlan plan = BuildStandard();

        Assert.All(plan.Project.Clips, clip =>
        {
            double phrase = SetBuildOptions.PhraseBars * (SetBuildOptions.BeatsPerBar * 60.0 / clip.SourceBpm);
            double offset = (clip.SourceIn.TotalSeconds - clip.SourceDownbeatSeconds) / phrase;
            Assert.Equal(Math.Round(offset), offset, 6);
        });
    }

    [Fact]
    public void Build_KeepsEveryOverlap_APhraseIntegerInsideTheLegalRange()
    {
        DjSetPlan plan = BuildStandard(new SetBuildOptions(OverlapBars: 32));

        Assert.All(plan.Transitions, t =>
        {
            Assert.InRange(t.OverlapBars, SetBuildOptions.MinOverlapBars, SetBuildOptions.MaxOverlapBars);
            Assert.Equal(0, t.OverlapBars % SetBuildOptions.OverlapStepBars);
        });
    }

    [Fact]
    public void Build_NeverDropsIntoSilence_BetweenTheFirstAndLastClip()
    {
        DjSetPlan plan = BuildStandard();
        var mix = new MixPlan(plan.Project);

        for (double t = 0.0; t < plan.TotalSeconds; t += 0.5)
        {
            bool sounding = Enumerable.Range(0, MixerState.DeckCount).Any(slot => mix.EvaluateDeck(slot, t).HasAudio);
            Assert.True(sounding, $"no deck is sounding at {t:F1}s");
        }
    }

    [Fact]
    public void Build_NeverCutsARecord_BeforeItsLastDrop()
    {
        DjSetPlan plan = BuildStandard();

        // The standard structure's last drop is at 180 s.
        Assert.All(plan.Transitions, t =>
            Assert.True(t.OutAnchor.SourceSeconds >= 180.0, $"transition {t.Index} leaves at {t.OutAnchor.SourceSeconds}s"));
    }

    // ---- eligibility ----------------------------------------------------------------------------

    [Theory]
    [InlineData(RejectReason.NoDuration)]
    [InlineData(RejectReason.NoBpm)]
    [InlineData(RejectReason.NoKey)]
    public void Build_KeepsUnmixableTracks_OffTheTimeline(RejectReason expected)
    {
        MusicTrack good = SetTrackFixture.Track("good.mp3", "8A", 128);
        MusicTrack bad = expected switch
        {
            RejectReason.NoDuration => SetTrackFixture.Track("bad.mp3") with { Duration = null },
            RejectReason.NoBpm => SetTrackFixture.Track("bad.mp3") with { Bpm = null },
            _ => SetTrackFixture.Track("bad.mp3") with { Key = null },
        };

        DjSetPlan plan = _arranger.Build(new[] { good, bad }, good, new HarmonicSetOptions(2), Options);

        Assert.DoesNotContain(plan.Project.Clips, c => c.TrackPath == "bad.mp3");
        Assert.Equal(expected, Assert.Single(plan.Rejected, r => r.Path == "bad.mp3").Reason);
    }

    [Fact]
    public void Build_KeepsAnUnreachableFile_OffTheTimeline()
    {
        // An unreachable path renders as pure silence with no error, so this gate is not optional.
        var arranger = new DjSetArranger(path => path != "offline.mp3");
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.Track("offline.mp3", "8A", 128),
        };

        DjSetPlan plan = arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        Assert.DoesNotContain(plan.Project.Clips, c => c.TrackPath == "offline.mp3");
        Assert.Equal(RejectReason.FileUnreachable, Assert.Single(plan.Rejected).Reason);
    }

    [Fact]
    public void Build_ReturnsAnEmptyProject_WhenNothingCanBeMixed()
    {
        MusicTrack[] pool = { SetTrackFixture.Track("a.mp3") with { Bpm = null } };

        DjSetPlan plan = _arranger.Build(pool, null, new HarmonicSetOptions(1), Options);

        Assert.Empty(plan.Project.Clips);
        Assert.Single(plan.Rejected);
    }

    // ---- the grid-confidence gate ---------------------------------------------------------------

    [Fact]
    public void Build_DoesNotWarp_ARecordWhoseGridCannotBeTrusted()
    {
        // Stretching by a ratio derived from a guessed tempo is wrong twice over, so it plays native and
        // takes the shortest blend instead.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.UntrustedGrid("shaky.mp3", "8A", 127),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        StudioClip shaky = Assert.Single(plan.Project.Clips, c => c.TrackPath == "shaky.mp3");
        Assert.False(shaky.WarpEnabled);

        SetTransition transition = Assert.Single(plan.Transitions);
        Assert.False(transition.PhaseLocked);
        Assert.Equal(SetBuildOptions.LowConfidenceOverlapBars, transition.OverlapBars);
        Assert.Contains(SetWarning.LowGridConfidence, transition.Warnings);
    }

    [Fact]
    public void Build_ExcludesUntrustedGrids_OnlyWhenAsked()
    {
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.UntrustedGrid("shaky.mp3", "8A", 128),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), new SetBuildOptions(ExcludeLowGridConfidence: true));

        Assert.DoesNotContain(plan.Project.Clips, c => c.TrackPath == "shaky.mp3");
        Assert.Equal(RejectReason.LowGridConfidence, Assert.Single(plan.Rejected).Reason);
    }

    [Fact]
    public void Build_FlagsAPreConfidenceCatalog_WithoutChangingHowItMixes()
    {
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.UnanalyzedGrid("old.mp3", "8A", 128),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        SetTransition transition = Assert.Single(plan.Transitions);
        Assert.True(transition.PhaseLocked);
        Assert.Contains(SetWarning.GridNotAnalyzed, transition.Warnings);
    }

    // ---- the mix itself -------------------------------------------------------------------------

    [Fact]
    public void Build_HoldsTheLevel_ThroughEveryCrossfade()
    {
        // Two uncorrelated records at half amplitude sum to -3 dB; an equal-power pair keeps the sum flat.
        // Checked through the render plan, which is what the offline mix actually reads.
        DjSetPlan plan = BuildStandard();
        var mix = new MixPlan(plan.Project);

        foreach (SetTransition transition in plan.Transitions)
        {
            for (double progress = 0.05; progress < 1.0; progress += 0.05)
            {
                double t = transition.StartSeconds + (progress * transition.OverlapSeconds);
                double a = mix.EvaluateDeck(0, t).Gain;
                double b = mix.EvaluateDeck(1, t).Gain;
                double power = (a * a) + (b * b);
                Assert.InRange(power, 0.97, 1.03);
            }
        }
    }

    [Fact]
    public void Build_SwapsTheLowBands_AcrossEveryCrossfade()
    {
        // Two kicks and two basslines stacked for half a minute is mud, and the loudest tell that nobody
        // was actually mixing.
        DjSetPlan plan = BuildStandard();
        var mix = new MixPlan(plan.Project);

        foreach (SetTransition transition in plan.Transitions)
        {
            double middle = transition.StartSeconds + (transition.OverlapSeconds / 2.0);
            double lowA = mix.EvaluateDeck(0, middle).Eq.Low;
            double lowB = mix.EvaluateDeck(1, middle).Eq.Low;

            // Both lows meet at a full cut in the middle of the blend, so the two basslines never stack.
            Assert.Equal(0.0, Math.Min(lowA, lowB), 3);
            Assert.Equal(0.0, Math.Max(lowA, lowB), 3);
        }
    }

    [Fact]
    public void Build_RestoresTheLowBand_AfterTheBlend()
    {
        DjSetPlan plan = BuildStandard();
        var mix = new MixPlan(plan.Project);
        SetTransition first = plan.Transitions[0];

        // A phrase after the blend the incoming deck must be back to flat, or the set has no bass.
        double afterwards = first.EndSeconds + (SetBuildOptions.PhraseBars * SetBuildOptions.BarSeconds(plan.TempoBpm));
        DeckMixState incoming = mix.EvaluateDeck(1, afterwards);

        Assert.True(incoming.HasAudio);
        Assert.Equal(EqBands.Unity, incoming.Eq.Low, 6);
    }

    [Fact]
    public void Build_LeavesTheLevelsToTheAutomation_NotToClipFades()
    {
        // A linear clip fade folded into the equal-power deck curve would put the -3 dB dip straight back.
        DjSetPlan plan = BuildStandard();

        Assert.All(plan.Project.Clips, clip =>
        {
            Assert.Equal(0.0, clip.FadeInSeconds);
            Assert.Equal(0.0, clip.FadeOutSeconds);
        });
    }

    [Fact]
    public void Build_AlternatesDecks_SoAdjacentTracksNeverShareOne()
    {
        DjSetPlan plan = BuildStandard(new SetBuildOptions(StartDeckSlot: 1));

        Assert.Equal(new[] { 1, 0, 1, 0 }, plan.Project.Clips.Select(c => c.DeckSlot).ToArray());
    }

    // ---- the report -----------------------------------------------------------------------------

    [Fact]
    public void Build_ReportsOneTransition_PerJoin()
    {
        DjSetPlan plan = BuildStandard();

        Assert.Equal(plan.TrackCount - 1, plan.Transitions.Count);
        Assert.Equal(plan.TrackCount, plan.PhaseLockedCount + 1);
        for (int i = 0; i < plan.Transitions.Count; i++)
        {
            Assert.Equal(i, plan.Transitions[i].Index);
            Assert.Equal(plan.Project.Clips[i].TrackPath, plan.Transitions[i].FromPath);
            Assert.Equal(plan.Project.Clips[i + 1].TrackPath, plan.Transitions[i].ToPath);
            Assert.Equal(plan.TempoBpm, plan.Transitions[i].TempoBpm);
        }
    }

    [Fact]
    public void Build_ReportsTheKeyMove_ForEveryJoin()
    {
        DjSetPlan plan = BuildStandard();

        Assert.All(plan.Transitions, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.KeyFrom));
            Assert.False(string.IsNullOrWhiteSpace(t.KeyTo));
            Assert.False(string.IsNullOrWhiteSpace(t.KeyRelationship));
        });
    }

    [Fact]
    public void Build_NamesTheProject_AsAsked()
    {
        DjSetPlan plan = BuildStandard(new SetBuildOptions(ProjectName: "Friday Warmup"));

        Assert.Equal("Friday Warmup", plan.Project.Name);
    }
}

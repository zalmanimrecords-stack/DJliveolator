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
    public void Build_GainsUnequalMasters_ToOneLevel()
    {
        // Two records 7 dB apart is ordinary for commercial masters. At unity the mix would step at the
        // join and the equal-power crossfade would sum two different loudnesses; gained, they sit level.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("loud.mp3", "8A", 128, integratedLufs: -6.0,
                structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.Track("quiet.mp3", "8A", 128, integratedLufs: -13.0),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        StudioClip loud = Assert.Single(plan.Project.Clips, c => c.TrackPath == "loud.mp3");
        StudioClip quiet = Assert.Single(plan.Project.Clips, c => c.TrackPath == "quiet.mp3");

        Assert.True(loud.Gain < quiet.Gain, "the louder master must be pulled down relative to the quieter one");
        Assert.Equal(
            -6.0 + (20.0 * Math.Log10(loud.Gain)),
            -13.0 + (20.0 * Math.Log10(quiet.Gain)),
            precision: 6);
    }

    [Fact]
    public void Build_LeavesAnUnmeasuredCatalog_AtUnityGain()
    {
        // Backward compatibility: a catalog with no loudness measured yet must arrange exactly as before.
        DjSetPlan plan = BuildStandard();

        Assert.All(plan.Project.Clips, clip => Assert.Equal(1.0, clip.Gain));
    }

    [Fact]
    public void Build_StillWarps_ARecordWithASteadyTempoButASmearedKick()
    {
        // A phase downgrade is not a tempo downgrade. Leaving a rock-steady record at its native rate
        // against the set tempo guarantees the drift the confidence gate exists to prevent, so it warps —
        // and only loses the phase lock and the long blend, which are what the loose grid fit really costs.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.SmearedKick("soft-kick.mp3", "8A", 127),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        StudioClip softKick = Assert.Single(plan.Project.Clips, c => c.TrackPath == "soft-kick.mp3");
        Assert.True(softKick.WarpEnabled, "a steady tempo must still be warped to the set tempo");

        SetTransition transition = Assert.Single(plan.Transitions);
        Assert.False(transition.PhaseLocked);
        Assert.Equal(SetBuildOptions.LowConfidenceOverlapBars, transition.OverlapBars);
        Assert.Contains(SetWarning.LowGridConfidence, transition.Warnings);
    }

    [Fact]
    public void Build_ARefusedPhase_FallsBackToNoLockInsteadOfAligningOnAGuess()
    {
        // The analyzer could not vouch for this track's beat phase (kick-identity gate failed). The set must
        // still be built and still warped — but with no phase lock and the short blend, so a possibly
        // half-beat-off anchor can no longer flam a 32-bar crossfade.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.RefusedPhase("unvouched.mp3", "8A", 127),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        StudioClip clip = Assert.Single(plan.Project.Clips, c => c.TrackPath == "unvouched.mp3");
        Assert.True(clip.WarpEnabled, "a refused phase must never stop the tempo match");
        SetTransition transition = Assert.Single(plan.Transitions);
        Assert.False(transition.PhaseLocked);
        Assert.Equal(SetBuildOptions.LowConfidenceOverlapBars, transition.OverlapBars);
        Assert.Contains(SetWarning.LowGridConfidence, transition.Warnings);
    }

    [Fact]
    public void Build_AVouchedPhase_PhaseLocks_AndKeepsTheFullBlend_ThoughTheKickFitIsLoose()
    {
        // The measured payoff: four tracks of the 11-track set (03, 07, 08, 09) have anchors within
        // 6.4-15.8 ms of an audio-derived reference and the kick-identity gate vouches for them, yet their
        // grid COHERENCE (0.368-0.533) sat under the phase floor and clamped every one of their joins to 8
        // bars with no phase lock. A direct measurement of the anchor outranks a proxy for it.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.VouchedPhase("vouched.mp3", "8A", 127),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        StudioClip clip = Assert.Single(plan.Project.Clips, c => c.TrackPath == "vouched.mp3");
        Assert.True(clip.WarpEnabled);
        SetTransition transition = Assert.Single(plan.Transitions);
        Assert.True(transition.PhaseLocked, "a vouched anchor must be phase-aligned");
        Assert.Equal(Options.NormalizedOverlapBars, transition.OverlapBars);
        Assert.DoesNotContain(SetWarning.LowGridConfidence, transition.Warnings);
    }

    [Fact]
    public void Build_KeepsAVouchedPhase_EvenWhenAskedToExcludeLowConfidence()
    {
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.VouchedPhase("vouched.mp3", "8A", 128),
        };

        DjSetPlan plan = _arranger.Build(
            pool, pool[0], new HarmonicSetOptions(2), new SetBuildOptions(ExcludeLowGridConfidence: true));

        Assert.Contains(plan.Project.Clips, c => c.TrackPath == "vouched.mp3");
        Assert.Empty(plan.Rejected);
    }

    [Fact]
    public void Build_ReportsTheRealWarp_OnAJoinThatLostItsPhaseLock()
    {
        // The clip IS stretched to the set tempo (phase and warp are separate gates), so the transition
        // report must say so — a reported 0% next to a warped clip reads as an unwarped join that is drifting.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
            SetTrackFixture.RefusedPhase("unvouched.mp3", "8A", 124),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        SetTransition transition = Assert.Single(plan.Transitions);
        Assert.False(transition.PhaseLocked);
        Assert.All(
            plan.Project.Clips.Select(c => c.WarpEnabled),
            warped => Assert.True(warped));
        Assert.NotEqual(0.0, transition.FromWarpPercent);
        Assert.NotEqual(0.0, transition.ToWarpPercent);
    }

    [Fact]
    public void Build_ExcludesARefusedPhase_WhenAskedToExcludeLowConfidence()
    {
        // How "05 - 145 - 6A - Vibe Tribe and Spade - Beyond and Beyond" is kept out of a set: its declared
        // tempo is itself wrong, so no single global phase exists and the analyzer refuses one.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.RefusedPhase("unvouched.mp3", "8A", 128),
        };

        DjSetPlan plan = _arranger.Build(
            pool, pool[0], new HarmonicSetOptions(2), new SetBuildOptions(ExcludeLowGridConfidence: true));

        Assert.DoesNotContain(plan.Project.Clips, c => c.TrackPath == "unvouched.mp3");
        Assert.Equal(RejectReason.LowGridConfidence, Assert.Single(plan.Rejected).Reason);
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

            // The two lows meet part-way, not at a full cut each: this assertion used to demand 0.0 on both
            // sides, which is the hand-over passing through zero — an eight-bar hole in the mix's low end on
            // every join. Equal-and-cut is the DJ move: neither record holds the low end alone (so the
            // basslines still never stack), and the band never leaves.
            // Sampled a hair off the true centre, not exactly on it: the reported StartSeconds/OverlapSeconds
            // are rounded to milliseconds while the automation lane keeps the unrounded blend start, so the
            // two sides land within about a thousandth of each other rather than bit-equal.
            Assert.True(Math.Abs(lowA - lowB) < 0.01, $"the hand-over is lopsided: {lowA} vs {lowB}");
            Assert.InRange(lowA, 0.30, 0.40);
            Assert.InRange(lowB, 0.30, 0.40);
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

    // ---- why the set came back short --------------------------------------------------------------
    //
    // The two commonest ways a set ends early — the key ring closing and the tempo trend locking out — were
    // both invisible: a build that returned one clip out of a four-track pool reported an EMPTY rejection
    // list, so the only reading left was "the library is thin" and the next call was the wrong one.

    [Fact]
    public void Build_ReportsTracksTheChainNeverPicked()
    {
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.Track("b.mp3", "8A", 128),
            SetTrackFixture.Track("far1.mp3", "2A", 128),
            SetTrackFixture.Track("far2.mp3", "3B", 128),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(4), Options);

        Assert.Equal(2, plan.TrackCount);
        Assert.Equal(
            new[] { "far1.mp3", "far2.mp3" },
            plan.Rejected.Where(r => r.Reason == RejectReason.NoHarmonicMatch).Select(r => r.Path).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void Build_ReportsTracksTheTrendLockedOut()
    {
        // Rising is non-decreasing at every step with no lookahead, so seeding at the pool's top tempo
        // finishes the chain immediately. "Drop the trend, or reseed low" is only a possible next call if
        // the report says the trend was what closed it.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("top.mp3", "8A", 130),
            SetTrackFixture.Track("b.mp3", "8A", 126),
            SetTrackFixture.Track("c.mp3", "8A", 127),
            SetTrackFixture.Track("d.mp3", "8A", 128),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(4, 6.0, BpmTrend.Rising), Options);

        Assert.Equal(1, plan.TrackCount);
        Assert.Equal(3, plan.Rejected.Count(r => r.Reason == RejectReason.BlockedByTrend));
        Assert.DoesNotContain(plan.Rejected, r => r.Reason == RejectReason.NoHarmonicMatch);
    }

    [Fact]
    public void Build_DistinguishesTheLengthCap_FromARejection()
    {
        // build_dj_set defaults to 8 tracks against a 1,300-track catalog. Every unplaced record is not a
        // rejection there — it is a request that was honoured, and it must read as one.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.Track("b.mp3", "8A", 128),
            SetTrackFixture.Track("c.mp3", "8A", 128),
            SetTrackFixture.Track("d.mp3", "8A", 128),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        Assert.Equal(2, plan.TrackCount);
        RejectedCandidate cap = Assert.Single(plan.Rejected);
        Assert.Equal(RejectReason.LengthCapReached, cap.Reason);
        Assert.Contains("2", cap.Title);
        Assert.Contains("untried", cap.Title);
    }

    [Fact]
    public void Build_DoesNotSilentlyDropTheSeed()
    {
        // The seed helps choose the median tempo and is then filtered against it like any other track, so
        // the caller can get a set that does not start where it asked. Reported distinctly because the
        // remedy is different: reseed, not widen the warp limit.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("seed.mp3", "8A", 128),
            SetTrackFixture.Track("b.mp3", "8A", 129),
            SetTrackFixture.Track("c.mp3", "8A", 134),
            SetTrackFixture.Track("d.mp3", "8A", 135),
            SetTrackFixture.Track("e.mp3", "8A", 136),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(5), new SetBuildOptions(MaxWarpPercent: 3.0));

        Assert.DoesNotContain(plan.Project.Clips, c => c.TrackPath == "seed.mp3");
        RejectedCandidate seed = Assert.Single(plan.Rejected, r => r.Path == "seed.mp3");
        Assert.Equal(RejectReason.SeedOutsideTempoRange, seed.Reason);
        Assert.Equal(4.69, seed.NeededWarpPercent!.Value, 2);
    }

    [Fact]
    public void Build_DoesNotBlameAFifteenMinuteRecordAsTooShort()
    {
        // Measured: a.mp3's drums start at 280 s of a 300 s file, so its own entry is pushed to 270 s and it
        // has no runway left to leave from. That failure has nothing to do with the incoming record, yet a
        // fifteen-minute one was reported as too short to mix.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, durationSeconds: 300, kicks: new[] { 280.0 }),
            SetTrackFixture.Track("b.mp3", "8A", 128, durationSeconds: 900),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        Assert.DoesNotContain(plan.Rejected, r => r.Path == "b.mp3");
        RejectedCandidate blamed = Assert.Single(plan.Rejected);
        Assert.Equal("a.mp3", blamed.Path);
        Assert.Equal(RejectReason.NoMixOutRunway, blamed.Reason);
    }

    [Fact]
    public void Build_StopsBlamingTracks_OnceTheOutgoingRunwayIsGone()
    {
        // The condition is independent of the incoming track, so continuing walked the rest of the chain
        // rejecting every record in turn: one bad kick-onset array turned a twelve-track set into a
        // one-track set with eleven innocent records blamed.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128, durationSeconds: 300, kicks: new[] { 280.0 }),
            SetTrackFixture.Track("b.mp3", "8A", 128, durationSeconds: 900),
            SetTrackFixture.Track("c.mp3", "8A", 128, durationSeconds: 900),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(3), Options);

        Assert.Equal(RejectReason.NoMixOutRunway, Assert.Single(plan.Rejected).Reason);
    }

    [Fact]
    public void Build_BlamesTheIncomingRecord_WhenItIsTheOneWithNoRoomLeft()
    {
        // The other side of the same null: b.mp3's drums start at 110 s of a two-minute record, so its entry
        // is pushed to 90 s and the blend plus a phrase no longer fit. The outgoing record is fine here, and
        // the reason must say so — this is not TooShort either, which is about the file's length alone.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.Track("b.mp3", "8A", 128, durationSeconds: 120, kicks: new[] { 110.0 }),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        RejectedCandidate blamed = Assert.Single(plan.Rejected);
        Assert.Equal("b.mp3", blamed.Path);
        Assert.Equal(RejectReason.NoTransitionPlanned, blamed.Reason);
    }

    [Fact]
    public void Build_RejectsAGenuinelyShortRecord_AsTooShort()
    {
        // The baseline the false TooShort was hiding behind: at 128 BPM a record needs 75 s to hold a
        // phrase, the shortest legal blend and a phrase after it.
        MusicTrack[] pool =
        {
            SetTrackFixture.Track("a.mp3", "8A", 128),
            SetTrackFixture.Track("stub.mp3", "8A", 128, durationSeconds: 60),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        Assert.Equal(RejectReason.TooShort, Assert.Single(plan.Rejected, r => r.Path == "stub.mp3").Reason);
    }

    [Fact]
    public void Build_MeasuresTheRealOverlap_WhenAnUnwarpedClipIsPhraseSnapped()
    {
        // An unwarped clip runs on its own bar length, so the incoming clip is re-anchored to the project
        // phrase grid — which moves the start of the blend without moving where the outgoing record leaves.
        // Reporting the planned overlap after that is how an unwarped join read as a clean blend while the
        // timeline actually held a cut, and it is the figure the crossfade automation is built from.
        MusicTrack[] pool =
        {
            SetTrackFixture.UntrustedGrid("shaky.mp3", "8A", 132),
            SetTrackFixture.Track("b.mp3", "8A", 128, structure: SetTrackFixture.StandardStructure()),
        };

        DjSetPlan plan = _arranger.Build(pool, pool[0], new HarmonicSetOptions(2), Options);

        StudioClip outgoing = Assert.Single(plan.Project.Clips, c => c.TrackPath == "shaky.mp3");
        Assert.False(outgoing.WarpEnabled);
        SetTransition transition = Assert.Single(plan.Transitions);

        double outgoingEnd = outgoing.TimelineStartSeconds + outgoing.SourceDuration!.Value.TotalSeconds;
        double plannedOverlap = transition.OverlapBars * SetBuildOptions.BarSeconds(outgoing.SourceBpm);

        Assert.NotEqual(plannedOverlap, transition.OverlapSeconds, 3);
        // 2 decimals, not 3: the report rounds each position to the millisecond independently, so subtracting
        // one rounded figure from an unrounded one carries a millisecond of slack. The lie this catches is
        // four seconds wide.
        Assert.Equal(outgoingEnd - transition.StartSeconds, transition.OverlapSeconds, 2);
        Assert.Equal(outgoingEnd, transition.EndSeconds, 3);
        Assert.True(transition.OverlapSeconds > 0.0);
    }
}

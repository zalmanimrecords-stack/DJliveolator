using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Liveolator.App.Composition;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.App.Tests.Composition;

/// <summary>
/// The controller profiles shipped as data in <c>mappings/</c> — loaded from the build output, which is
/// the same folder layout the installer lays down, so this exercises the real artifact rather than a
/// fixture. Every shipped profile must be conflict-free and auto-selectable; the DDJ-400 map is then
/// checked control-by-control against AlphaTheta's published MIDI message list.
/// </summary>
public sealed class ShippedControllerProfilesTests
{
    private static IReadOnlyList<ControllerMappingProfile> Shipped() =>
        ShippedMappingProfiles.LoadFrom(
            Path.Combine(AppContext.BaseDirectory, ShippedMappingProfiles.FolderName),
            warning => throw new Xunit.Sdk.XunitException($"A shipped mapping profile failed to load: {warning}"));

    private static ControllerMappingProfile Profile(string deviceHint) =>
        Assert.Single(Shipped().Where(p => p.DeviceHint == deviceHint));

    private static PerformanceAction Dispatch(ControllerMappingProfile profile, MidiMessage message)
    {
        var dispatcher = new RecordingDispatcher();
        new ControllerMapper(profile, dispatcher, NullLogger<ControllerMapper>.Instance).Apply(message);
        return Assert.Single(dispatcher.Dispatched);
    }

    [Fact]
    public void Every_shipped_profile_is_conflict_free_and_auto_selectable()
    {
        IReadOnlyList<ControllerMappingProfile> shipped = Shipped();
        Assert.NotEmpty(shipped);

        foreach (ControllerMappingProfile profile in shipped)
        {
            Assert.NotEmpty(profile.Bindings);
            // An empty hint would make the profile win auto-selection for EVERY device, displacing the
            // generic learn-from-scratch template that is meant to be the only hintless profile.
            Assert.False(string.IsNullOrWhiteSpace(profile.DeviceHint), $"{profile.Name} has no device hint");
            Assert.Empty(MappingConflictDetector.Detect(profile));
        }
    }

    /// <summary>
    /// The line between a mapping a DJ can actually play on and a token one. Every shipped profile has
    /// to clear it on BOTH decks — a controller missing headphone cue or an EQ band is not "mostly
    /// mapped", it is unusable for the thing controllers exist to do.
    /// </summary>
    [Fact]
    public void Every_shipped_profile_covers_the_controls_a_floor_actually_needs()
    {
        foreach (ControllerMappingProfile profile in Shipped())
        {
            Assert.Contains(profile.Bindings, b => b.Action == PerformanceActionKind.MixerCrossfade);

            foreach (int slot in new[] { 0, 1 })
            {
                void Require(PerformanceActionKind kind, string? argument = null) =>
                    Assert.True(
                        profile.Bindings.Any(b => b.Action == kind && b.Slot == slot
                            && (argument is null || b.Argument == argument)),
                        $"{profile.Name} deck {slot} has no binding for {kind}{(argument is null ? "" : " " + argument)}");

                Require(PerformanceActionKind.DeckPlayPause);
                Require(PerformanceActionKind.DeckSyncToggle);
                Require(PerformanceActionKind.DeckJog);
                Require(PerformanceActionKind.DeckPitch);
                Require(PerformanceActionKind.MixerChannelGain);
                Require(PerformanceActionKind.MixerCueToggle);
                Require(PerformanceActionKind.MixerEqBand, "High");
                Require(PerformanceActionKind.MixerEqBand, "Mid");
                Require(PerformanceActionKind.MixerEqBand, "Low");
            }
        }
    }

    [Fact]
    public void Every_shipped_profile_gets_soft_takeover_the_right_way_round()
    {
        // The rule a shipped map must never get backwards: a channel fader or crossfader that refuses
        // to move until it crosses the software value is a fader that appears dead mid-set. A pitch
        // fader is the opposite — the on-screen one moves too, so it must pick up.
        foreach (ControllerMappingProfile profile in Shipped())
        {
            Assert.All(
                profile.Bindings.Where(b => b.Action is PerformanceActionKind.DeckPitch),
                b => Assert.True(b.SoftTakeover, $"{profile.Name}: pitch fader needs soft takeover"));

            Assert.All(
                profile.Bindings.Where(b =>
                    b.Action is PerformanceActionKind.MixerChannelGain or PerformanceActionKind.MixerCrossfade),
                b => Assert.False(b.SoftTakeover, $"{profile.Name}: volume faders must not soft-takeover"));
        }
    }

    [Fact]
    public void Every_relative_binding_declares_how_its_encoder_counts()
    {
        // A relative control left on the default tick count scrubs a track across the room on one flick.
        // The encoding itself is per-manufacturer (Pioneer jogs are offset-binary, Hercules two's
        // complement), so only the presence of a deliberate revolution size can be asserted generically.
        foreach (ControllerMappingProfile profile in Shipped())
        {
            Assert.All(
                profile.Bindings.Where(b => b.InputMode == ActionInputMode.Relative),
                b => Assert.True(b.RelativeTicksPerRevolution > 1.0,
                    $"{profile.Name}: {b.Action} on CC {b.Data1} never had its ticks-per-revolution set"));
        }
    }

    [Fact]
    public void Shipped_profiles_join_the_catalog_and_win_auto_selection_for_their_device()
    {
        IReadOnlyList<ControllerMappingProfile> catalog = ServiceConfig.AvailableMidiProfiles();

        // Each load builds fresh instances (a record whose Bindings list compares by reference), so the
        // identity that matters here is the device hint the selector actually matches on.
        ControllerMappingProfile? selected = MidiProfileSelector.Select("PIONEER DDJ-400 MIDI 1", catalog);

        Assert.NotNull(selected);
        Assert.Equal("DDJ-400", selected!.DeviceHint);
        Assert.Equal(Profile("DDJ-400").Bindings.Count, selected.Bindings.Count);
    }

    [Theory]
    // Deck 1 is MIDI channel 0 and deck 2 is channel 1 in AlphaTheta's list; slot follows the channel.
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void Ddj400_deck_strip_matches_the_published_midi_list(int channel, int slot)
    {
        ControllerMappingProfile profile = Profile("DDJ-400");

        Assert.Equal(PerformanceActionKind.DeckPlayPause,
            Dispatch(profile, new MidiMessage(MidiMessageType.NoteOn, channel, 11, 127)).Kind);
        Assert.Equal(PerformanceActionKind.DeckSyncToggle,
            Dispatch(profile, new MidiMessage(MidiMessageType.NoteOn, channel, 88, 127)).Kind);

        PerformanceAction eq = Dispatch(profile, new MidiMessage(MidiMessageType.ControlChange, channel, 7, 64));
        Assert.Equal(PerformanceActionKind.MixerEqBand, eq.Kind);
        Assert.Equal("High", eq.Argument);
        Assert.Equal(slot, eq.Slot);

        PerformanceAction hotCue = Dispatch(profile, new MidiMessage(MidiMessageType.NoteOn, slot == 0 ? 7 : 9, 2, 127));
        Assert.Equal(PerformanceActionKind.DeckHotCue, hotCue.Kind);
        Assert.Equal("2", hotCue.Argument);
        Assert.Equal(slot, hotCue.Slot);
    }

    [Theory]
    // The jog reports on a different CC depending on where it is touched and whether VINYL mode is on.
    // All three must reach the deck, or the wheel is dead in whichever mode was left switched on.
    [InlineData(33)] // wheel side
    [InlineData(34)] // platter, VINYL on
    [InlineData(35)] // platter, VINYL off
    public void Ddj400_jog_reaches_the_deck_in_every_vinyl_mode(int cc)
    {
        PerformanceAction action = Dispatch(
            Profile("DDJ-400"), new MidiMessage(MidiMessageType.ControlChange, 0, cc, 65));

        Assert.Equal(PerformanceActionKind.DeckJog, action.Kind);
        Assert.Equal(0, action.Slot);
    }

    [Fact]
    public void Ddj400_binds_headphone_cue_on_both_decks()
    {
        // CH CUE, note 84 per deck. Without PFL a DJ cannot pre-listen, so no amount of other bindings
        // makes the map usable on a floor — this is the check that stops a token mapping shipping.
        ControllerMappingProfile profile = Profile("DDJ-400");

        foreach (int channel in new[] { 0, 1 })
        {
            PerformanceAction cue = Dispatch(profile, new MidiMessage(MidiMessageType.NoteOn, channel, 84, 127));
            Assert.Equal(PerformanceActionKind.MixerCueToggle, cue.Kind);
            Assert.Equal(channel, cue.Slot);
        }

        Assert.Equal(PerformanceActionKind.MixerCueMix,
            Dispatch(profile, new MidiMessage(MidiMessageType.ControlChange, 6, 12, 64)).Kind);
        Assert.Equal(PerformanceActionKind.MixerCueLevel,
            Dispatch(profile, new MidiMessage(MidiMessageType.ControlChange, 6, 13, 64)).Kind);
    }

    [Fact]
    public void Inpulse300_uses_its_own_channel_scheme_and_encoder_encoding()
    {
        // Proof the data-driven catalog is not quietly Pioneer-shaped: Hercules puts deck A on MIDI
        // channel 1 (not 0), the crossfader on a global channel 0, and documents its jog as two's
        // complement rather than Pioneer's offset-binary.
        ControllerMappingProfile profile = Profile("Inpulse 300");

        Assert.Equal(PerformanceActionKind.DeckPlayPause,
            Dispatch(profile, new MidiMessage(MidiMessageType.NoteOn, 1, 9, 127)).Kind);
        Assert.Equal(PerformanceActionKind.DeckPlayPause,
            Dispatch(profile, new MidiMessage(MidiMessageType.NoteOn, 2, 9, 127)).Kind);
        Assert.Equal(PerformanceActionKind.MixerCrossfade,
            Dispatch(profile, new MidiMessage(MidiMessageType.ControlChange, 0, 0, 64)).Kind);

        Assert.All(
            profile.Bindings.Where(b => b.Action == PerformanceActionKind.DeckJog),
            b => Assert.Equal(RelativeEncoding.TwosComplement, b.Relative));
    }

    [Fact]
    public void The_catalog_covers_ten_controllers()
    {
        // Seven shipped as data plus the three built-in profile classes, with the hintless generic
        // template excluded — it is the learn-from-scratch fallback, not a supported device.
        string[] devices = ServiceConfig.AvailableMidiProfiles()
            .Where(p => !string.IsNullOrWhiteSpace(p.DeviceHint))
            .Select(p => p.DeviceHint)
            .Distinct()
            .ToArray();

        Assert.Equal(10, devices.Length);
    }

    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public List<PerformanceAction> Dispatched { get; } = new();
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => ActionFeedbackState.Unavailable;
    }
}

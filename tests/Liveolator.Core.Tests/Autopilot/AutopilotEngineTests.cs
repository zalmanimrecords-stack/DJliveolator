using Liveolator.Core.Actions;
using Liveolator.Core.Autopilot;
using Liveolator.Core.Beat;
using Liveolator.Core.Tests.Actions;
using Liveolator.Core.Tests.Mapping;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Liveolator.Core.Tests.Autopilot;

public class AutopilotEngineTests
{
    private readonly RecordingDispatcher _dispatcher = new();
    private readonly AutopilotEngine _engine;

    public AutopilotEngineTests() => _engine = new AutopilotEngine(_dispatcher, new CapturingLogger<AutopilotEngine>());

    private static BeatClockState State(
        int beat, int bar, bool isBeat, bool isDownbeat, double confidence = 1.0)
        => new(120, confidence, 0, 0, beat, bar, isBeat, isDownbeat, false, BeatClockSource.Manual, Array.Empty<TempoCandidate>());

    private static AutopilotTickContext Ctx(BeatClockState state, double energy = 0.5, double position = 0.0)
        => new(state, energy, position);

    private static AutopilotRule Rule(
        string name,
        RuleTrigger trigger,
        PerformanceActionKind kind = PerformanceActionKind.VisualBlackout,
        RuleCondition? condition = null,
        Cooldown? cooldown = null)
        => new(name, trigger, condition ?? RuleCondition.None, new PerformanceAction(kind), cooldown ?? Cooldown.None);

    private static AutopilotRuleSet RuleSet(
        AutopilotRule rule, ScenePool? pool = null, int seed = 0, AutopilotOverridePolicy? policy = null)
        => new("show", new[] { rule }, pool ?? ScenePool.Empty, seed, policy);

    [Fact]
    public void EveryNBars_FiresOnMatchingDownbeat()
    {
        _engine.Start(RuleSet(Rule("r", new RuleTrigger(TriggerKind.EveryNBars, 4))));

        _engine.Tick(Ctx(State(beat: 16, bar: 4, isBeat: true, isDownbeat: true)));
        _engine.Tick(Ctx(State(beat: 20, bar: 5, isBeat: true, isDownbeat: false)));

        Assert.Single(_dispatcher.Dispatched);
    }

    [Fact]
    public void EveryNBeats_FiresOnlyOnMultiples()
    {
        _engine.Start(RuleSet(Rule("r", new RuleTrigger(TriggerKind.EveryNBeats, 2))));

        _engine.Tick(Ctx(State(beat: 2, bar: 0, isBeat: true, isDownbeat: false)));
        _engine.Tick(Ctx(State(beat: 3, bar: 0, isBeat: true, isDownbeat: false)));

        Assert.Single(_dispatcher.Dispatched);
    }

    [Fact]
    public void Condition_GatesOnConfidence()
    {
        var rule = Rule("r", new RuleTrigger(TriggerKind.OnDownbeat), condition: new RuleCondition(MinConfidence: 0.7));
        _engine.Start(RuleSet(rule));

        _engine.Tick(Ctx(State(0, 1, true, true, confidence: 0.5)));
        Assert.Empty(_dispatcher.Dispatched);

        _engine.Tick(Ctx(State(0, 2, true, true, confidence: 0.9)));
        Assert.Single(_dispatcher.Dispatched);
    }

    [Fact]
    public void Condition_GatesOnEnergyWindow()
    {
        var rule = Rule("r", new RuleTrigger(TriggerKind.OnDownbeat), condition: new RuleCondition(MinEnergy: 0.8));
        _engine.Start(RuleSet(rule));

        _engine.Tick(Ctx(State(0, 1, true, true), energy: 0.5));
        Assert.Empty(_dispatcher.Dispatched);

        _engine.Tick(Ctx(State(0, 2, true, true), energy: 0.9));
        Assert.Single(_dispatcher.Dispatched);
    }

    [Fact]
    public void OnTrackPosition_FiresPastThreshold()
    {
        _engine.Start(RuleSet(Rule("r", new RuleTrigger(TriggerKind.OnTrackPosition, 90))));

        _engine.Tick(Ctx(State(0, 1, true, true), position: 0.5));
        Assert.Empty(_dispatcher.Dispatched);

        _engine.Tick(Ctx(State(0, 2, true, true), position: 0.95));
        Assert.Single(_dispatcher.Dispatched);
    }

    [Fact]
    public void Cooldown_PreventsRefiringWithinWindow()
    {
        var rule = Rule("r", new RuleTrigger(TriggerKind.OnDownbeat), cooldown: new Cooldown(4));
        _engine.Start(RuleSet(rule));

        _engine.Tick(Ctx(State(0, 1, true, true)));  // fires
        _engine.Tick(Ctx(State(0, 2, true, true)));  // within cooldown
        Assert.Single(_dispatcher.Dispatched);

        _engine.Tick(Ctx(State(0, 5, true, true)));  // 5 - 1 >= 4 → fires again
        Assert.Equal(2, _dispatcher.Dispatched.Count);
    }

    [Fact]
    public void ScenePool_FillsArgument_AndRespectsSceneCooldown()
    {
        var rule = Rule("r", new RuleTrigger(TriggerKind.OnDownbeat), PerformanceActionKind.VisualLoadScene);
        _engine.Start(RuleSet(rule, pool: new ScenePool(new[] { "s1", "s2" }, CooldownBars: 4), seed: 1));

        _engine.Tick(Ctx(State(0, 1, true, true)));
        _engine.Tick(Ctx(State(0, 2, true, true)));

        string first = _dispatcher.Dispatched[0].Argument!;
        string second = _dispatcher.Dispatched[1].Argument!;
        Assert.Contains(first, new[] { "s1", "s2" });
        Assert.NotEqual(first, second); // the just-used scene is on cooldown
    }

    [Fact]
    public void ScenePool_SameSeed_ProducesSameSequence()
    {
        var pool = new ScenePool(new[] { "a", "b", "c" }, CooldownBars: 0); // all always eligible
        var rule = Rule("r", new RuleTrigger(TriggerKind.OnDownbeat), PerformanceActionKind.VisualLoadScene);

        string[] Run()
        {
            var dispatcher = new RecordingDispatcher();
            var engine = new AutopilotEngine(dispatcher, new CapturingLogger<AutopilotEngine>());
            engine.Start(new AutopilotRuleSet("s", new[] { rule }, pool, Seed: 42));
            for (int bar = 1; bar <= 5; bar++)
                engine.Tick(Ctx(State(0, bar, true, true)));
            return dispatcher.Dispatched.Select(a => a.Argument!).ToArray();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Override_AutoResume_SuspendsThenResumes()
    {
        var policy = new AutopilotOverridePolicy(OverrideMode.AutoResume, ResumeAfterBars: 2);
        _engine.Start(RuleSet(Rule("r", new RuleTrigger(TriggerKind.OnDownbeat)), policy: policy));

        _engine.Tick(Ctx(State(0, 10, true, true)));   // fires (count 1)
        _engine.OnManualAction();                       // suspend until bar 12
        _engine.Tick(Ctx(State(0, 11, true, true)));   // suspended
        Assert.Single(_dispatcher.Dispatched);

        _engine.Tick(Ctx(State(0, 12, true, true)));   // resumes → fires (count 2)
        Assert.Equal(2, _dispatcher.Dispatched.Count);
    }

    [Fact]
    public void Override_PauseUntilReenabled_StaysOffUntilRestart()
    {
        var policy = new AutopilotOverridePolicy(OverrideMode.PauseUntilReenabled);
        var ruleSet = RuleSet(Rule("r", new RuleTrigger(TriggerKind.OnDownbeat)), policy: policy);
        _engine.Start(ruleSet);

        _engine.OnManualAction();
        _engine.Tick(Ctx(State(0, 5, true, true)));
        _engine.Tick(Ctx(State(0, 9, true, true)));
        Assert.Empty(_dispatcher.Dispatched);
        Assert.True(_engine.IsSuspended);

        _engine.Start(ruleSet); // re-enable
        _engine.Tick(Ctx(State(0, 12, true, true)));
        Assert.Single(_dispatcher.Dispatched);
    }

    [Fact]
    public void Stop_HaltsEvaluation()
    {
        _engine.Start(RuleSet(Rule("r", new RuleTrigger(TriggerKind.OnDownbeat))));
        _engine.Stop();

        _engine.Tick(Ctx(State(0, 1, true, true)));

        Assert.False(_engine.IsRunning);
        Assert.Empty(_dispatcher.Dispatched);
    }

    [Fact]
    public void ThrowingRule_IsDisabledAndLogged_NotPropagated()
    {
        var logger = new CapturingLogger<AutopilotEngine>();
        var engine = new AutopilotEngine(_dispatcher, logger);
        _dispatcher.ThrowOnDispatch = true; // make the emit throw inside rule evaluation
        engine.Start(RuleSet(Rule("boom", new RuleTrigger(TriggerKind.OnDownbeat))));

        var exception = Record.Exception(() => engine.Tick(Ctx(State(0, 1, true, true))));
        engine.Tick(Ctx(State(0, 2, true, true))); // rule now disabled → no second attempt

        Assert.Null(exception);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void NotRunning_TickAndManualAction_AreNoOps()
    {
        var exception = Record.Exception(() =>
        {
            _engine.Tick(Ctx(State(0, 1, true, true)));
            _engine.OnManualAction();
        });

        Assert.Null(exception);
        Assert.False(_engine.IsRunning);
        Assert.Empty(_dispatcher.Dispatched);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new AutopilotEngine(null!, new CapturingLogger<AutopilotEngine>()));
        Assert.Throws<ArgumentNullException>(() => new AutopilotEngine(_dispatcher, null!));
    }
}

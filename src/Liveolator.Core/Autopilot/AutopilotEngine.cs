using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Autopilot;

/// <summary>
/// Default autopilot: each tick it evaluates the rule set against the beat clock and tick inputs,
/// emitting due actions through the dispatcher. Scene-selecting actions draw from the curated pool
/// with a seeded RNG (reproducible shows). A manual action suspends evaluation per the override
/// policy. A throwing rule is disabled for the session and logged, never stalling the loop
/// (doc 10, global standards #16/#26).
/// </summary>
public sealed class AutopilotEngine : IAutopilotEngine
{
    private const double TrackPercentScale = 100.0;

    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly ILogger<AutopilotEngine> _logger;

    private AutopilotRuleSet? _ruleSet;
    private Random _random = new(0);
    private int[] _lastFiredBar = Array.Empty<int>();
    private bool[] _disabled = Array.Empty<bool>();
    private readonly Dictionary<string, int> _sceneLastUsedBar = new(StringComparer.Ordinal);

    private int _lastBar;
    private int? _suspendUntilBar;
    private bool _pausedByOverride;

    public AutopilotEngine(IPerformanceActionDispatcher dispatcher, ILogger<AutopilotEngine> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public bool IsSuspended
        => _pausedByOverride || (_suspendUntilBar is int until && _lastBar < until);

    /// <inheritdoc />
    public void Start(AutopilotRuleSet ruleSet)
    {
        _ruleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
        _random = new Random(ruleSet.Seed);
        _lastFiredBar = new int[ruleSet.Rules.Count];
        Array.Fill(_lastFiredBar, int.MinValue);
        _disabled = new bool[ruleSet.Rules.Count];
        _sceneLastUsedBar.Clear();
        _suspendUntilBar = null;
        _pausedByOverride = false;
        IsRunning = true;
    }

    /// <inheritdoc />
    public void Stop() => IsRunning = false;

    /// <inheritdoc />
    public void OnManualAction()
    {
        if (!IsRunning || _ruleSet is null)
            return;

        AutopilotOverridePolicy policy = _ruleSet.Policy;
        if (policy.Mode == OverrideMode.PauseUntilReenabled)
            _pausedByOverride = true;
        else
            _suspendUntilBar = _lastBar + policy.ResumeAfterBars;
    }

    /// <inheritdoc />
    public void Tick(AutopilotTickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsRunning || _ruleSet is null)
            return;

        _lastBar = context.State.BarNumber;

        // Clear an elapsed auto-resume window before evaluating.
        if (_suspendUntilBar is int until && _lastBar >= until)
            _suspendUntilBar = null;
        if (IsSuspended)
            return;

        IReadOnlyList<AutopilotRule> rules = _ruleSet.Rules;
        for (int i = 0; i < rules.Count; i++)
        {
            if (_disabled[i])
                continue;

            try
            {
                EvaluateRule(i, rules[i], context);
            }
            catch (Exception ex)
            {
                _disabled[i] = true;
                _logger.LogWarning(ex, "Autopilot rule '{Rule}' threw and was disabled for the session.", rules[i].Name);
            }
        }
    }

    private void EvaluateRule(int index, AutopilotRule rule, AutopilotTickContext context)
    {
        if (!TriggerFires(rule.Trigger, context))
            return;
        if (!rule.Condition.IsMet(context.State.Confidence, context.Energy, context.TrackPosition))
            return;
        if (!CooldownElapsed(index, context.State.BarNumber, rule.Cooldown))
            return;

        PerformanceAction action = ResolveAction(rule.Action, context.State.BarNumber);
        _dispatcher.Dispatch(action);
        _lastFiredBar[index] = context.State.BarNumber;
        _logger.LogDebug("Autopilot fired '{Rule}' -> {Kind}.", rule.Name, action.Kind);
    }

    private static bool TriggerFires(RuleTrigger trigger, AutopilotTickContext context)
    {
        BeatClockState state = context.State;
        return trigger.Kind switch
        {
            TriggerKind.EveryNBeats => state.IsBeat && trigger.N > 0 && state.BeatCount % trigger.N == 0,
            TriggerKind.EveryNBars => state.IsDownbeat && trigger.N > 0 && state.BarNumber % trigger.N == 0,
            TriggerKind.OnDownbeat => state.IsDownbeat,
            TriggerKind.OnTrackPosition => context.TrackPosition >= trigger.N / TrackPercentScale,
            _ => false,
        };
    }

    private bool CooldownElapsed(int index, int currentBar, Cooldown cooldown)
        => _lastFiredBar[index] == int.MinValue || currentBar - _lastFiredBar[index] >= cooldown.Bars;

    // Scene-selecting actions get their target filled from the curated pool; everything else passes through.
    private PerformanceAction ResolveAction(PerformanceAction template, int currentBar)
    {
        if (template.Kind != PerformanceActionKind.VisualLoadScene || _ruleSet!.ScenePool.SceneNames.Count == 0)
            return template;

        string? scene = ChooseScene(currentBar);
        return scene is null ? template : template with { Argument = scene };
    }

    private string? ChooseScene(int currentBar)
    {
        ScenePool pool = _ruleSet!.ScenePool;
        var eligible = pool.SceneNames
            .Where(name => !_sceneLastUsedBar.TryGetValue(name, out int last)
                           || currentBar - last >= pool.CooldownBars)
            .ToList();

        // If everything is on cooldown, fall back to the least-recently-used so the show never stalls.
        string chosen = eligible.Count > 0
            ? eligible[_random.Next(eligible.Count)]
            : pool.SceneNames.OrderBy(name => _sceneLastUsedBar.GetValueOrDefault(name, int.MinValue)).First();

        _sceneLastUsedBar[chosen] = currentBar;
        return chosen;
    }
}

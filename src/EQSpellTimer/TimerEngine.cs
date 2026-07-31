namespace EQSpellTimer;

public sealed class TimerEngine
{
    private readonly EngineContext _context;
    private readonly CastTracker _casts;
    private readonly HotTracker _hots;
    private readonly BuffTracker _buffs;
    private readonly LearningEngine _learning;

    public event Action<string>? Activity
    {
        add => _context.Activity += value;
        remove => _context.Activity -= value;
    }

    public event Action? TimersChanged
    {
        add => _context.TimersChanged += value;
        remove => _context.TimersChanged -= value;
    }

    public event Action? SpellDefinitionsChanged
    {
        add => _context.SpellDefinitionsChanged += value;
        remove => _context.SpellDefinitionsChanged -= value;
    }

    public bool LearningEnabled
    {
        get => _learning.Enabled;
        set => _learning.Enabled = value;
    }

    public Func<LearningCandidate, LearningDecision>? LearningRequested
    {
        get => _learning.RequestDecision;
        set => _learning.RequestDecision = value;
    }

    public IReadOnlyCollection<ActiveTimer> Timers =>
        _context.Timers.Values;

    public TimerEngine(
        Func<IReadOnlyList<SpellDefinition>> spells,
        Func<string> characterName)
    {
        _context = new EngineContext(spells, characterName);
        _casts = new CastTracker(_context);
        _hots = new HotTracker(_context);
        _buffs = new BuffTracker(_context);
        _learning = new LearningEngine(_context);
    }

    public void Process(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        _context.CleanupPending();

        var message = LogMessageParser.Parse(raw);

        // Parser priority is intentional:
        // 1. Casts/failures
        // 2. Known automatic HoTs
        // 3. Known configured buffs/fades
        // 4. Learning Mode last
        //
        // This prevents Learning Mode from consuming Flowering Heal
        // or any other automatic HoT message.
        if (_casts.TryHandle(message))
            return;

        if (_casts.TryHandleFailure(message.Text))
            return;

        if (_hots.TryHandle(message))
            return;

        if (_buffs.TryKnownLanding(message))
            return;

        if (_buffs.TryFade(message))
            return;

        _learning.TryHandle(message);
    }

    public void RemoveExpired(DateTime now)
    {
        var expired = _context.Timers
            .Where(kv => kv.Value.End <= now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expired)
        {
            var timer = _context.Timers[key];
            _context.Timers.Remove(key);
            _context.Say(
                $"Expired {timer.Spell} on {timer.Target}");
        }

        if (expired.Count > 0)
            _context.TimersChanged?.Invoke();
    }
}

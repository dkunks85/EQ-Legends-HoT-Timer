namespace EQSpellTimer;

public sealed class TimerEngine
{
    private readonly EngineContext _context;
    private readonly CastTracker _casts;
    private readonly HotTracker _hots;
    private readonly BuffTracker _buffs;
    private readonly DeathTracker _deaths;

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

    public bool LearnHotDurations
    {
        get => _context.LearnHotDurations;
        set => _context.LearnHotDurations = value;
    }

    public IReadOnlyCollection<ActiveTimer> Timers =>
        _context.Timers.Values;

    public TimerEngine(
        Func<IReadOnlyList<SpellDefinition>> spells,
        Func<string> characterName,
        string appDirectory)
    {
        _context = new EngineContext(
            spells,
            characterName,
            appDirectory);

        _casts = new CastTracker(_context);
        _hots = new HotTracker(_context);
        _buffs = new BuffTracker(_context);
        _deaths = new DeathTracker(_context);
    }

    public void Process(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        _context.CleanupPending();
        var message = LogMessageParser.Parse(raw);

        if (_casts.TryHandle(message))
            return;

        if (_casts.TryHandleFailure(message.Text))
            return;

        if (_deaths.TryHandle(message))
            return;

        if (_hots.TryHandle(message))
            return;

        _buffs.TryKnownLanding(message);
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
            _context.Say($"Expired {timer.Spell} on {timer.Target}");
        }

        if (expired.Count > 0)
            _context.TimersChanged?.Invoke();
    }
}

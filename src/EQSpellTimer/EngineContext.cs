namespace EQSpellTimer;

internal sealed class EngineContext
{
    private readonly Func<IReadOnlyList<SpellDefinition>> _spells;
    private readonly Func<string> _characterName;

    public Dictionary<string, ActiveTimer> Timers { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<PendingCast> Pending { get; } = [];

    public Action<string>? Activity { get; set; }
    public Action? TimersChanged { get; set; }
    public Action? SpellDefinitionsChanged { get; set; }

    public EngineContext(
        Func<IReadOnlyList<SpellDefinition>> spells,
        Func<string> characterName)
    {
        _spells = spells;
        _characterName = characterName;
    }

    public IReadOnlyList<SpellDefinition> Spells => _spells();

    public string CharacterName
    {
        get
        {
            var name = _characterName()?.Trim();
            return string.IsNullOrWhiteSpace(name) ? "You" : name;
        }
    }

    public SpellDefinition? FindDefinition(string spellName)
    {
        var baseName = SpellNames.Base(spellName);

        return Spells.FirstOrDefault(s =>
            s.Enabled &&
            (SpellNames.Base(s.Name).Equals(
                 baseName,
                 StringComparison.OrdinalIgnoreCase) ||
             SpellNames.Base(s.MatchName).Equals(
                 baseName,
                 StringComparison.OrdinalIgnoreCase)));
    }

    public PendingCast? TakeNewestPending(
        Func<PendingCast, bool> predicate)
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            if (!predicate(Pending[i]))
                continue;

            var pending = Pending[i];
            Pending.RemoveAt(i);
            return pending;
        }

        return null;
    }

    public PendingCast? PeekNewestPending(
        Func<PendingCast, bool> predicate)
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            if (predicate(Pending[i]))
                return Pending[i];
        }

        return null;
    }

    public void CleanupPending()
    {
        var now = DateTime.Now;
        Pending.RemoveAll(p => p.Expires < now);
    }

    public void CancelNewestSelf(string reason)
    {
        var pending = TakeNewestPending(p =>
            p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase));

        if (pending is not null)
            Say($"Pending cast cancelled: {pending.CastName} ({reason})");
    }

    public string DisplayCaster(string caster) =>
        caster.Equals("You", StringComparison.OrdinalIgnoreCase)
            ? CharacterName
            : caster.Trim();

    public string NormalizeTarget(string target, string caster)
    {
        var cleaned = target.Trim().TrimEnd('.');
        var actualCaster = DisplayCaster(caster);

        if (cleaned.Equals("you", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("yourself", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterName;
        }

        if (cleaned.Equals("himself", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("herself", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("itself", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("themselves", StringComparison.OrdinalIgnoreCase))
        {
            return actualCaster;
        }

        return cleaned;
    }

    public void StartHot(
        PendingCast pending,
        string target,
        DateTime start,
        bool tickSynced)
    {
        var duration = Math.Max(1, pending.Spell.DurationSeconds);

        var end = tickSynced
            ? start.AddSeconds(Math.Max(1, duration - 3))
            : start.AddSeconds(duration);

        var source = DisplayCaster(pending.Caster);

        var timer = new ActiveTimer
        {
            Key = HotKey(target),
            Spell = pending.CastName,
            BaseName = pending.BaseName,
            Target = target,
            Source = source,
            Category = "HoT",
            Start = start,
            End = end,
            Duration = Math.Max(1, (end - start).TotalSeconds),
            TickSynced = tickSynced,
            FirstTick = tickSynced ? start : null
        };

        // One shared HoT slot per target.
        Timers[timer.Key] = timer;

        Say(
            $"Started {timer.Spell} on {target} " +
            $"from {source} ({duration} sec)");

        TimersChanged?.Invoke();
    }

    public void StartBuff(
        PendingCast pending,
        string target,
        DateTime start)
    {
        var duration = Math.Max(1, pending.Spell.DurationSeconds);
        var source = DisplayCaster(pending.Caster);

        var timer = new ActiveTimer
        {
            Key = BuffKey(pending.BaseName, target),
            Spell = pending.CastName,
            BaseName = pending.BaseName,
            Target = target,
            Source = source,
            Category = pending.Spell.Category,
            Start = start,
            End = start.AddSeconds(duration),
            Duration = duration,
            TickSynced = false
        };

        Timers[timer.Key] = timer;

        Say(
            $"Started {pending.CastName} on {target} " +
            $"from {source} ({duration} sec)");

        TimersChanged?.Invoke();
    }

    public void Say(string message) => Activity?.Invoke(message);

    public static bool IsHot(SpellDefinition spell) =>
        spell.Category.Equals(
            "HoT",
            StringComparison.OrdinalIgnoreCase);

    public static string HotKey(string target) =>
        $"hot|{target.Trim()}".ToLowerInvariant();

    public static string BuffKey(string spell, string target) =>
        $"{SpellNames.Base(spell).Trim()}|{target.Trim()}"
            .ToLowerInvariant();
}

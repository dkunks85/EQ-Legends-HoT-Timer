namespace EQSpellTimer;

internal sealed class EngineContext
{
    private readonly Func<IReadOnlyList<SpellDefinition>> _spells;
    private readonly Func<string> _characterName;
    private readonly HotDurationStore _hotDurations;

    public Dictionary<string, ActiveTimer> Timers { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<PendingCast> Pending { get; } = [];

    public Action<string>? Activity { get; set; }
    public Action? TimersChanged { get; set; }

    public bool LearnHotDurations { get; set; } = true;

    public EngineContext(
        Func<IReadOnlyList<SpellDefinition>> spells,
        Func<string> characterName,
        string appDirectory)
    {
        _spells = spells;
        _characterName = characterName;
        _hotDurations = new HotDurationStore(appDirectory);
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

    public PendingCast? TakeNewestPending(Func<PendingCast, bool> predicate)
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

    public PendingCast? PeekNewestPending(Func<PendingCast, bool> predicate)
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
        var cleaned = NormalizeEntityName(target);
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

    private static string NormalizeEntityName(string value)
    {
        return value
            .Trim()
            .TrimEnd('.')
            // EverQuest may use different possessive marks in different
            // log messages. Normalize them so the same pet always uses
            // the same timer key.
            .Replace('`', '\'')
            .Replace('’', '\'')
            .Replace('‘', '\'');
    }

    public int GetHotDuration(PendingCast pending)
    {
        var fallback = Math.Max(1, pending.Spell.DurationSeconds);

        return LearnHotDurations
            ? _hotDurations.GetDuration(pending.CastName, fallback)
            : fallback;
    }

    public int RecordHotDuration(ActiveTimer timer, DateTime endedAt)
    {
        var observed = Math.Max(
            1,
            (int)Math.Round((endedAt - timer.Start).TotalSeconds));

        if (!LearnHotDurations)
            return observed;

        var learned = _hotDurations.Record(timer.Spell, observed);

        Say(
            $"Learned HoT duration for {timer.Spell}: " +
            $"{learned}s (observed {observed}s)");

        return learned;
    }

    public void StartHot(
        PendingCast pending,
        string target,
        DateTime start,
        bool tickSynced)
    {
        var duration = GetHotDuration(pending);

        var end = tickSynced
            ? start.AddSeconds(Math.Max(1, duration - 3))
            : start.AddSeconds(duration);

        var source = DisplayCaster(pending.Caster);

        var timer = new ActiveTimer
        {
            Key = HotKey(HotFamily(pending.BaseName), target),
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

    public void RemoveTimersForTarget(string target, string reason)
    {
        var keys = Timers
            .Where(kv => kv.Value.Target.Equals(
                target,
                StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keys)
        {
            var timer = Timers[key];
            Timers.Remove(key);
            Say($"Removed {timer.Spell} from {timer.Target} ({reason})");
        }

        if (keys.Count > 0)
            TimersChanged?.Invoke();
    }

    public void Say(string message) => Activity?.Invoke(message);

    public static bool IsHot(SpellDefinition spell) =>
        spell.Category.Equals("HoT", StringComparison.OrdinalIgnoreCase);

    public static string HotKey(string family, string target) =>
        $"hot|{family}|{target.Trim()}".ToLowerInvariant();

    public static string HotFamily(string baseName)
    {
        return IsClericHotName(baseName)
            ? "cleric"
            : "nature";
    }

    public static bool IsClericHotName(string baseName)
    {
        var value = SpellNames.Base(baseName);

        return
            value.Equals("Echo of Health", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Echoing Light", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Renewing Echo", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Celestial Echo", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Sacred Echo", StringComparison.OrdinalIgnoreCase);
    }

    public ActiveTimer StartProvisionalClericHot(PendingCast pending)
    {
        var unknownTarget =
            pending.Caster.Equals("You", StringComparison.OrdinalIgnoreCase)
                ? "Unknown target"
                : $"Unknown target ({DisplayCaster(pending.Caster)})";

        var duration = GetHotDuration(pending);
        var source = DisplayCaster(pending.Caster);

        var timer = new ActiveTimer
        {
            Key = HotKey("cleric", unknownTarget),
            Spell = pending.CastName,
            BaseName = pending.BaseName,
            Target = unknownTarget,
            Source = source,
            Category = "HoT",
            Start = pending.CastTime,
            End = pending.CastTime.AddSeconds(duration),
            Duration = duration,
            TickSynced = false
        };

        Timers[timer.Key] = timer;

        Say(
            $"Started provisional {pending.CastName} from {source} " +
            $"({duration} sec; target unknown)");

        TimersChanged?.Invoke();
        return timer;
    }

    public bool AssignClericHotTarget(
        PendingCast pending,
        string target,
        DateTime identifiedAt)
    {
        var source = DisplayCaster(pending.Caster);

        var provisional = Timers.Values
            .Where(timer =>
                timer.Category.Equals(
                    "HoT",
                    StringComparison.OrdinalIgnoreCase) &&
                HotFamily(timer.BaseName).Equals(
                    "cleric",
                    StringComparison.OrdinalIgnoreCase) &&
                timer.Spell.Equals(
                    pending.CastName,
                    StringComparison.OrdinalIgnoreCase) &&
                timer.Source.Equals(
                    source,
                    StringComparison.OrdinalIgnoreCase) &&
                timer.Target.StartsWith(
                    "Unknown target",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(timer => timer.Start)
            .FirstOrDefault();

        if (provisional is null)
        {
            StartHot(
                pending,
                target,
                pending.CastTime,
                tickSynced: false);

            return true;
        }

        Timers.Remove(provisional.Key);

        provisional.Target = target;
        provisional.Key = HotKey("cleric", target);

        // A newer Cleric Echo replaces only the Cleric slot on this
        // target. Nature HoTs remain untouched.
        Timers[provisional.Key] = provisional;

        Say(
            $"Assigned {provisional.Spell} to {target} " +
            $"at {identifiedAt:HH:mm:ss}");

        TimersChanged?.Invoke();
        return true;
    }

    public static string BuffKey(string spell, string target) =>
        $"{SpellNames.Base(spell).Trim()}|{target.Trim()}".ToLowerInvariant();
}

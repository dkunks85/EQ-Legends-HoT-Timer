using System.Globalization;
using System.Text.RegularExpressions;

namespace EQSpellTimer;

public sealed partial class TimerEngine
{
    private readonly Func<IReadOnlyList<SpellDefinition>> _spells;
    private readonly Func<string> _characterName;
    private readonly Dictionary<string, ActiveTimer> _timers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingCast> _pending = [];

    public event Action<string>? Activity;
    public event Action? TimersChanged;
    public IReadOnlyCollection<ActiveTimer> Timers => _timers.Values;

    public TimerEngine(Func<IReadOnlyList<SpellDefinition>> spells, Func<string> characterName)
    { _spells = spells; _characterName = characterName; }

    public void Process(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        CleanupPending();
        var when = ParseTimestamp(raw);
        var line = TimestampRegex().Replace(raw, "").Trim();

        var cast = YouCastRegex().Match(line);
        string? caster = null, castName = null;
        if (cast.Success) { caster = "You"; castName = cast.Groups["spell"].Value.Trim().TrimEnd('.'); }
        else
        {
            cast = OtherCastRegex().Match(line);
            if (cast.Success) { caster = cast.Groups["caster"].Value.Trim(); castName = cast.Groups["spell"].Value.Trim().TrimEnd('.'); }
        }

        if (castName is not null)
        {
            var def = FindDefinition(castName);
            if (def is not null && (SpellNames.IsSupportedHot(castName) || caster == "You"))
            {
                _pending.Add(new PendingCast { Spell=def, CastName=castName, BaseName=SpellNames.Base(castName), Caster=caster!, CastTime=when, Expires=when.AddSeconds(20) });
                Say($"Pending {castName} by {caster}");
                return;
            }
        }

        if (line.Equals("Your spell fizzles!", StringComparison.OrdinalIgnoreCase)) { CancelNewestSelf("fizzled"); return; }
        if (line.StartsWith("Your ", StringComparison.OrdinalIgnoreCase) && line.Contains(" spell is interrupted", StringComparison.OrdinalIgnoreCase)) { CancelNewestSelf("interrupted"); return; }

        // Druid landing messages.
        var land = SeededRegex().Match(line);
        if (land.Success)
        {
            var target = NormalizeTarget(land.Groups["target"].Value, "You");
            var pending = FindNewestPending(p => p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) && p.BaseName.EndsWith("Heal", StringComparison.OrdinalIgnoreCase));
            if (pending is not null) StartHot(pending, target, when, false);
            return;
        }
        if (line.Equals("You feel a heal flowering within you.", StringComparison.OrdinalIgnoreCase))
        {
            var pending = FindNewestPending(p => p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) && p.BaseName.EndsWith("Heal", StringComparison.OrdinalIgnoreCase));
            if (pending is not null) StartHot(pending, _characterName(), when, false);
            return;
        }

        // HoT tick lines: "Gryff has been healed for 123 hit points by Flowering Heal VII."
        var tick = HotTickRegex().Match(line);
        if (tick.Success)
        {
            var target = NormalizeTarget(tick.Groups["target"].Value, "You");
            var effect = SpellNames.Base(tick.Groups["effect"].Value);
            var pending = FindNewestPending(p => p.BaseName.Equals(effect, StringComparison.OrdinalIgnoreCase));
            if (pending is not null) { StartHot(pending, target, when, true); return; }
            var key = HotKey(target);
            if (_timers.TryGetValue(key, out var active) && !active.TickSynced)
            {
                var configured = FindDefinition(effect)?.DurationSeconds ?? (int)active.Duration;
                active.FirstTick = when;
                active.End = when.AddSeconds(Math.Max(1, configured - 3));
                active.Duration = Math.Max(1, (active.End - active.Start).TotalSeconds);
                active.TickSynced = true;
                Say($"{active.Spell} on {target} synchronized to server tick");
                TimersChanged?.Invoke();
            }
            return;
        }

        var trigger = TriggerRegex().Match(line);
        if (trigger.Success)
        {
            var target = NormalizeTarget(trigger.Groups["target"].Value, "You");
            if (_timers.Remove(HotKey(target), out var ended))
            {
                Say($"{ended.Spell} on {target} ended on trigger");
                TimersChanged?.Invoke();
            }
            return;
        }

        foreach (var spell in _spells().Where(s => s.Enabled && s.DetectionMode.Equals("Landing Message", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.LandingPattern)))
        {
            var match = TemplateRegex(spell.LandingPattern).Match(line);
            if (!match.Success) continue;
            var pending = FindNewestPending(p => ReferenceEquals(p.Spell, spell) || p.BaseName.Equals(SpellNames.Base(spell.Name), StringComparison.OrdinalIgnoreCase));
            if (pending is null) return;
            var target = match.Groups["target"].Success ? NormalizeTarget(match.Groups["target"].Value, "You") : _characterName();
            StartBuff(spell, target, when);
            return;
        }

        foreach (var spell in _spells().Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.FadePattern)))
        {
            var match = TemplateRegex(spell.FadePattern).Match(line);
            if (!match.Success) continue;
            var target = match.Groups["target"].Success ? NormalizeTarget(match.Groups["target"].Value, "You") : _characterName();
            var key = BuffKey(spell.Name, target);
            if (_timers.Remove(key)) { Say($"Fade detected: {spell.Name} on {target}"); TimersChanged?.Invoke(); }
            return;
        }
    }

    public void RemoveExpired(DateTime now)
    {
        var expired = _timers.Where(kv => kv.Value.End <= now).Select(kv => kv.Key).ToList();
        foreach (var key in expired) { var t = _timers[key]; _timers.Remove(key); Say($"Expired {t.Spell} on {t.Target}"); }
        if (expired.Count > 0) TimersChanged?.Invoke();
    }

    private SpellDefinition? FindDefinition(string spellName)
    {
        var baseName = SpellNames.Base(spellName);
        return _spells().FirstOrDefault(s => s.Enabled && (SpellNames.Base(s.Name).Equals(baseName, StringComparison.OrdinalIgnoreCase) || SpellNames.Base(s.MatchName).Equals(baseName, StringComparison.OrdinalIgnoreCase)));
    }

    private void StartHot(PendingCast pending, string target, DateTime start, bool tickSynced)
    {
        var duration = Math.Max(1, pending.Spell.DurationSeconds);
        var end = tickSynced ? start.AddSeconds(Math.Max(1, duration - 3)) : start.AddSeconds(duration);
        var timer = new ActiveTimer { Key=HotKey(target), Spell=pending.CastName, BaseName=pending.BaseName, Target=target, Source=pending.Caster, Category="HoT", Start=start, End=end, Duration=Math.Max(1, (end-start).TotalSeconds), TickSynced=tickSynced, FirstTick=tickSynced ? start : null };
        _timers[timer.Key] = timer; // Shared HoT slot per target.
        Say($"Started {timer.Spell} on {target} from {timer.Source} ({duration} sec)");
        TimersChanged?.Invoke();
    }

    private void StartBuff(SpellDefinition spell, string target, DateTime start)
    {
        var timer = new ActiveTimer { Key=BuffKey(spell.Name,target), Spell=spell.Name, BaseName=SpellNames.Base(spell.Name), Target=target, Source="You", Category=spell.Category, Start=start, End=start.AddSeconds(spell.DurationSeconds), Duration=spell.DurationSeconds, TickSynced=false };
        _timers[timer.Key] = timer; Say($"Started {spell.Name} on {target} ({spell.DurationSeconds} sec)"); TimersChanged?.Invoke();
    }

    private PendingCast? FindNewestPending(Func<PendingCast,bool> predicate)
    {
        for (var i=_pending.Count-1; i>=0; i--) if (predicate(_pending[i])) { var p=_pending[i]; _pending.RemoveAt(i); return p; }
        return null;
    }
    private void CleanupPending() => _pending.RemoveAll(p => p.Expires < DateTime.Now);
    private void CancelNewestSelf(string reason) { var p=FindNewestPending(p => p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase)); if (p is not null) Say($"Pending cast cancelled: {p.CastName} ({reason})"); }
    private string NormalizeTarget(string target, string caster) => target.Trim() switch { var x when x.Equals("you",StringComparison.OrdinalIgnoreCase) || x.Equals("yourself",StringComparison.OrdinalIgnoreCase) => _characterName(), var x when x.EndsWith("self",StringComparison.OrdinalIgnoreCase) || x.Equals("themselves",StringComparison.OrdinalIgnoreCase) => caster, var x => x };
    private static string HotKey(string target) => $"hot|{target.Trim()}".ToLowerInvariant();
    private static string BuffKey(string spell,string target) => $"{spell.Trim()}|{target.Trim()}".ToLowerInvariant();
    private void Say(string message) => Activity?.Invoke(message);

    private static DateTime ParseTimestamp(string line)
    {
        var m = TimestampRegex().Match(line);
        return m.Success && DateTime.TryParseExact(m.Groups["stamp"].Value, "ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : DateTime.Now;
    }
    private static Regex TemplateRegex(string template)
    {
        var pattern = Regex.Escape(template.Trim()).Replace("\\{target}", "(?<target>.+?)");
        return new Regex("^"+pattern+"$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    [GeneratedRegex(@"^\[(?<stamp>[^\]]+)\]\s*")]
    private static partial Regex TimestampRegex();
    [GeneratedRegex(@"^You begin casting (?<spell>.+?)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex YouCastRegex();
    [GeneratedRegex(@"^(?<caster>.+?) begins casting (?<spell>.+?)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex OtherCastRegex();
    [GeneratedRegex(@"^(?<target>.+?) is seeded with healing energy\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex SeededRegex();
    [GeneratedRegex(@"^(?<target>.+?) (?:has been healed|is healed|was healed) for .+? hit points by (?<effect>.+?)(?: \(Critical\))?\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex HotTickRegex();
    [GeneratedRegex(@"^(?:You healed (?<target>.+?)|(?<target>.+?) healed (?:himself|herself|itself|themselves)) for .+? hit points by (?<effect>.+?) Trigger(?: \(Critical\))?\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex TriggerRegex();
}

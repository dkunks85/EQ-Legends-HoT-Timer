using System.Text.RegularExpressions;

namespace EQSpellTimer;

internal sealed partial class HotTracker
{
    private readonly EngineContext _context;

    public HotTracker(EngineContext context)
    {
        _context = context;
    }

    public bool TryHandle(LogMessage message)
    {
        return
            TryDruidLanding(message) ||
            TrySelfDruidLanding(message) ||
            TryTick(message) ||
            TryTrigger(message);
    }

    private bool TryDruidLanding(LogMessage message)
    {
        var match = SeededRegex().Match(message.Text);

        if (!match.Success)
            return false;

        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase));

        if (pending is null)
            return true;

        var target = _context.NormalizeTarget(
            match.Groups["target"].Value,
            pending.Caster);

        _context.StartHot(
            pending,
            target,
            message.Timestamp,
            tickSynced: false);

        return true;
    }

    private bool TrySelfDruidLanding(LogMessage message)
    {
        if (!message.Text.Equals(
                "You feel a heal flowering within you.",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase));

        if (pending is not null)
        {
            _context.StartHot(
                pending,
                _context.CharacterName,
                message.Timestamp,
                tickSynced: false);
        }

        return true;
    }

    private bool TryTick(LogMessage message)
    {
        var tick = MatchTick(message.Text);

        if (tick is null)
            return false;

        var target = _context.NormalizeTarget(
            tick.Value.Target,
            tick.Value.Caster);

        var effect = SpellNames.Base(tick.Value.Effect);

        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            p.BaseName.Equals(
                effect,
                StringComparison.OrdinalIgnoreCase));

        if (pending is not null)
        {
            _context.StartHot(
                pending,
                target,
                message.Timestamp,
                tickSynced: true);
            return true;
        }

        var key = EngineContext.HotKey(target);

        if (_context.Timers.TryGetValue(key, out var active) &&
            !active.TickSynced)
        {
            var configured =
                _context.FindDefinition(effect)?.DurationSeconds ??
                (int)Math.Round(active.Duration);

            active.FirstTick = message.Timestamp;
            active.End = message.Timestamp.AddSeconds(
                Math.Max(1, configured - 3));
            active.Duration = Math.Max(
                1,
                (active.End - active.Start).TotalSeconds);
            active.TickSynced = true;

            _context.Say(
                $"{active.Spell} on {target} synchronized to server tick");
            _context.TimersChanged?.Invoke();
        }

        return true;
    }

    private bool TryTrigger(LogMessage message)
    {
        var match = TriggerRegex().Match(message.Text);

        if (!match.Success)
            return false;

        var caster = match.Groups["caster"].Success
            ? match.Groups["caster"].Value
            : "You";

        var target = _context.NormalizeTarget(
            match.Groups["target"].Value,
            caster);

        if (_context.Timers.Remove(
                EngineContext.HotKey(target),
                out var ended))
        {
            _context.Say(
                $"{ended.Spell} on {target} ended on trigger");
            _context.TimersChanged?.Invoke();
        }

        return true;
    }

    private static HotTickMatch? MatchTick(string line)
    {
        var match = PassiveHotTickRegex().Match(line);

        if (match.Success)
        {
            return new HotTickMatch(
                match.Groups["target"].Value,
                match.Groups["effect"].Value,
                "You");
        }

        match = ActiveHotTickRegex().Match(line);

        if (match.Success)
        {
            return new HotTickMatch(
                match.Groups["target"].Value,
                match.Groups["effect"].Value,
                match.Groups["caster"].Value);
        }

        match = YouHotTickRegex().Match(line);

        if (match.Success)
        {
            return new HotTickMatch(
                match.Groups["target"].Value,
                match.Groups["effect"].Value,
                "You");
        }

        return null;
    }

    private readonly record struct HotTickMatch(
        string Target,
        string Effect,
        string Caster);

    [GeneratedRegex(
        @"^(?<target>.+?) is seeded with healing energy\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SeededRegex();

    [GeneratedRegex(
        @"^(?<target>.+?) (?:has been healed|is healed|was healed) " +
        @"for .+? hit points by (?<effect>.+?)(?: \(Critical\))?\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex PassiveHotTickRegex();

    [GeneratedRegex(
        @"^(?<caster>.+?) healed (?<target>.+?) over time " +
        @"for .+? hit points by (?<effect>.+?)(?: \(Critical\))?\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ActiveHotTickRegex();

    [GeneratedRegex(
        @"^You healed (?<target>.+?) over time " +
        @"for .+? hit points by (?<effect>.+?)(?: \(Critical\))?\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex YouHotTickRegex();

    [GeneratedRegex(
        @"^(?:(?<caster>You) healed (?<target>.+?)|" +
        @"(?<caster>.+?) healed (?<target>.+?)|" +
        @"(?<target>.+?) healed (?:himself|herself|itself|themselves)) " +
        @"for .+? hit points by (?<effect>.+?) Trigger" +
        @"(?: \(Critical\))?\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TriggerRegex();
}

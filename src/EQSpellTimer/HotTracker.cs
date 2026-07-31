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
            TryDruidTargetLanding(message) ||
            TryDruidSelfLanding(message) ||
            TryShamanLanding(message) ||
            TryClericSelfEmbrace(message) ||
            TryClericImmediateHeal(message) ||
            TryTick(message) ||
            TryClericVisualFade(message) ||
            TryTrigger(message);
    }

    private bool TryDruidTargetLanding(LogMessage message)
    {
        var match = SeededRegex().Match(message.Text);

        if (!match.Success)
            return false;

        // The seeded message is unique to the Druid HoT family. Use the
        // newest local Druid HoT pending cast. Do not make target spelling
        // or possessive punctuation part of the pending-cast decision.
        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            p.Caster.Equals(
                "You",
                StringComparison.OrdinalIgnoreCase) &&
            IsDruidHot(p.BaseName) &&
            message.Timestamp >= p.CastTime &&
            message.Timestamp <= p.Expires);

        if (pending is null)
        {
            _context.Say(
                $"Detected seeded HoT landing on " +
                $"{match.Groups["target"].Value}, " +
                "but no pending local Druid HoT was found.");

            // The line was still a recognized HoT landing, so prevent
            // unrelated buff parsers from consuming it.
            return true;
        }

        var target = _context.NormalizeTarget(
            match.Groups["target"].Value,
            pending.Caster);

        _context.Say(
            $"Detected {pending.CastName} landing on {target}");

        _context.StartHot(
            pending,
            target,
            message.Timestamp,
            tickSynced: false);

        return true;
    }

    private bool TryDruidSelfLanding(LogMessage message)
    {
        var match = DruidSelfLandingRegex().Match(message.Text);

        if (!match.Success)
            return false;

        var stage = match.Groups["stage"].Value;

        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) &&
            IsDruidHot(p.BaseName) &&
            p.BaseName.StartsWith(
                stage,
                StringComparison.OrdinalIgnoreCase));

        // Fallback: use the newest local Druid HoT in case Legends
        // changes the landing adjective slightly.
        pending ??= _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) &&
            IsDruidHot(p.BaseName));

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

    private bool TryShamanLanding(LogMessage message)
    {
        var match = ShamanLandingRegex().Match(message.Text);

        if (!match.Success)
            return false;

        var spirit = match.Groups["spirit"].Value;
        var expectedBaseName = spirit.ToLowerInvariant() switch
        {
            "snail" => "Snails Healing",
            "tortoise" => "Tortoises Healing",
            "slug" => "Slugs Healing",
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(expectedBaseName))
            return false;

        var target = _context.NormalizeTarget(
            match.Groups["target"].Value,
            "You");

        // Match the newest visible Shaman cast of the correct family.
        // This supports the local player, group members, players, and pets.
        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            p.BaseName.Equals(
                expectedBaseName,
                StringComparison.OrdinalIgnoreCase) &&
            message.Timestamp >= p.CastTime &&
            message.Timestamp <= p.Expires);

        if (pending is null)
        {
            _context.Say(
                $"Detected {expectedBaseName} landing on {target}, " +
                "but no matching pending Shaman cast was found.");

            return true;
        }

        _context.Say(
            $"Detected {pending.CastName} landing on {target} " +
            $"from {_context.DisplayCaster(pending.Caster)}");

        _context.StartHot(
            pending,
            target,
            message.Timestamp,
            tickSynced: false);

        return true;
    }

    private bool TryClericSelfEmbrace(LogMessage message)
    {
        if (!message.Text.Equals(
                "You are embraced by a spirit of healing.",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            EngineContext.IsClericHotName(p.BaseName) &&
            p.Caster.Equals(
                "You",
                StringComparison.OrdinalIgnoreCase) &&
            message.Timestamp >= p.CastTime &&
            message.Timestamp <= p.Expires);

        if (pending is null)
        {
            _context.Say(
                "Detected Cleric self embrace, but no pending local " +
                "Cleric Echo cast was found.");

            return true;
        }

        _context.AssignClericHotTarget(
            pending,
            _context.CharacterName,
            message.Timestamp);

        return true;
    }

    private bool TryClericImmediateHeal(LogMessage message)
    {
        var match = ClericImmediateHealRegex().Match(message.Text);

        if (!match.Success)
            return false;

        var effect = SpellNames.Base(match.Groups["effect"].Value);

        if (!EngineContext.IsClericHotName(effect))
            return false;

        var caster = match.Groups["caster"].Success &&
                     !string.IsNullOrWhiteSpace(
                         match.Groups["caster"].Value)
            ? match.Groups["caster"].Value
            : "You";

        var target = _context.NormalizeTarget(
            match.Groups["target"].Value,
            caster);

        var pending = _context.TakeNewestPending(p =>
            EngineContext.IsHot(p.Spell) &&
            EngineContext.IsClericHotName(p.BaseName) &&
            p.BaseName.Equals(
                effect,
                StringComparison.OrdinalIgnoreCase) &&
            message.Timestamp >= p.CastTime &&
            message.Timestamp <= p.Expires);

        if (pending is null)
        {
            // The self-embrace may already have consumed the pending cast.
            // In that case, the active self timer is already correct.
            var active = _context.Timers.Values
                .Where(timer =>
                    timer.Category.Equals(
                        "HoT",
                        StringComparison.OrdinalIgnoreCase) &&
                    EngineContext.IsClericHotName(timer.BaseName) &&
                    timer.Spell.StartsWith(
                        effect,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(timer => timer.Start)
                .FirstOrDefault();

            if (active is not null &&
                !active.Target.Equals(
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                _context.Timers.Remove(active.Key);
                active.Target = target;
                active.Key = EngineContext.HotKey("cleric", target);
                _context.Timers[active.Key] = active;

                _context.Say(
                    $"Corrected {active.Spell} target to {target}");

                _context.TimersChanged?.Invoke();
            }

            return true;
        }

        _context.AssignClericHotTarget(
            pending,
            target,
            message.Timestamp);

        return true;
    }

    private bool TryClericVisualFade(LogMessage message)
    {
        if (!message.Text.Equals(
                "The echo of healing fades away.",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // This line is the short visual echo ending, not the HoT buff.
        _context.Say(
            "Ignored Cleric visual fade; the Echo HoT remains active.");

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

        var key = EngineContext.HotKey(
            EngineContext.HotFamily(effect),
            target);

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

        var effect = SpellNames.Base(
            match.Groups["effect"].Value);

        if (_context.Timers.Remove(
                EngineContext.HotKey(
                    EngineContext.HotFamily(effect),
                    target),
                out var ended))
        {
            _context.RecordHotDuration(
                ended,
                message.Timestamp);

            _context.Say(
                $"{ended.Spell} on {target} ended on trigger");

            _context.TimersChanged?.Invoke();
        }

        return true;
    }

    private static bool IsDruidHot(string baseName)
    {
        return
            baseName.Equals("Budding Heal", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("Sprouting Heal", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("Flowering Heal", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("Blooming Heal", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("Blossoming Heal", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("Efflorescing Heal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClericHot(string baseName)
    {
        return
            baseName.Equals(
                "Echo of Health",
                StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals(
                "Echoing Light",
                StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals(
                "Renewing Echo",
                StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals(
                "Celestial Echo",
                StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals(
                "Sacred Echo",
                StringComparison.OrdinalIgnoreCase);
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
        @"^You feel a heal (?<stage>budding|sprouting|flowering|blooming|blossoming|efflorescing) within you\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex DruidSelfLandingRegex();

    [GeneratedRegex(
        @"^(?<target>.+?) is healed by the spirit of the " +
        @"(?<spirit>snail|tortoise|slug)\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ShamanLandingRegex();

    [GeneratedRegex(
        @"^(?:(?<caster>You)|(?<caster>.+?)) healed (?<target>.+?) " +
        @"for .+? hit points by (?<effect>.+?)\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClericImmediateHealRegex();

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

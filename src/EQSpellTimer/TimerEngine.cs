using System.Globalization;
using System.Text.RegularExpressions;

namespace EQSpellTimer;

public sealed partial class TimerEngine
{
    private readonly Func<IReadOnlyList<SpellDefinition>> _spells;
    private readonly Func<string> _characterName;

    private readonly Dictionary<string, ActiveTimer> _timers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<PendingCast> _pending = [];

    public event Action<string>? Activity;
    public event Action? TimersChanged;
    public event Action? SpellDefinitionsChanged;

    public bool LearningEnabled { get; set; } = true;
    public Func<LearningCandidate, LearningDecision>? LearningRequested { get; set; }

    public IReadOnlyCollection<ActiveTimer> Timers => _timers.Values;

    public TimerEngine(
        Func<IReadOnlyList<SpellDefinition>> spells,
        Func<string> characterName)
    {
        _spells = spells;
        _characterName = characterName;
    }

    public void Process(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        CleanupPending();

        var when = ParseTimestamp(raw);
        var line = TimestampRegex().Replace(raw, string.Empty).Trim();

        // ------------------------------------------------------------
        // 1. Detect casts
        //
        // HoTs:
        //   Track casts from you and other visible players.
        //
        // Other buffs:
        //   Track only casts made by you, but allow any target
        //   (yourself, party members, or pets).
        // ------------------------------------------------------------
        var cast = YouCastRegex().Match(line);

        string? caster = null;
        string? castName = null;

        if (cast.Success)
        {
            caster = "You";
            castName = CleanSpellName(cast.Groups["spell"].Value);
        }
        else
        {
            cast = OtherCastRegex().Match(line);

            if (cast.Success)
            {
                caster = cast.Groups["caster"].Value.Trim();
                castName = CleanSpellName(cast.Groups["spell"].Value);
            }
        }

        if (castName is not null && caster is not null)
        {
            var definition = FindDefinition(castName);

            if (definition is not null)
            {
                var isHot = IsHot(definition);
                var casterIsYou = caster.Equals(
                    "You",
                    StringComparison.OrdinalIgnoreCase);

                if (isHot || casterIsYou)
                {
                    _pending.Add(new PendingCast
                    {
                        Spell = definition,
                        CastName = castName,
                        BaseName = SpellNames.Base(castName),
                        Caster = caster,
                        CastTime = when,
                        Expires = when.AddSeconds(20)
                    });

                    Say($"Pending {castName} by {DisplayCaster(caster)}");
                }
                else
                {
                    Say($"Ignored buff cast by another player: {castName} by {caster}");
                }
            }

            return;
        }

        // ------------------------------------------------------------
        // 2. Cancel failed self-casts
        // ------------------------------------------------------------
        if (line.Equals(
                "Your spell fizzles!",
                StringComparison.OrdinalIgnoreCase))
        {
            CancelNewestSelf("fizzled");
            return;
        }

        if (line.StartsWith(
                "Your ",
                StringComparison.OrdinalIgnoreCase) &&
            line.Contains(
                " spell is interrupted",
                StringComparison.OrdinalIgnoreCase))
        {
            CancelNewestSelf("interrupted");
            return;
        }

        if (line.Contains(
                "did not take hold",
                StringComparison.OrdinalIgnoreCase) ||
            line.Contains(
                "effect is currently blocked",
                StringComparison.OrdinalIgnoreCase))
        {
            CancelNewestSelf("blocked");
            return;
        }

        // ------------------------------------------------------------
        // 3. Druid HoT landing messages
        // ------------------------------------------------------------
        var landing = SeededRegex().Match(line);

        if (landing.Success)
        {
            var target = NormalizeTarget(
                landing.Groups["target"].Value,
                "You");

            var pending = FindNewestPending(p =>
                IsHot(p.Spell) &&
                p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) &&
                p.BaseName.EndsWith("Heal", StringComparison.OrdinalIgnoreCase));

            if (pending is not null)
                StartHot(pending, target, when, tickSynced: false);

            return;
        }

        if (line.Equals(
                "You feel a heal flowering within you.",
                StringComparison.OrdinalIgnoreCase))
        {
            var pending = FindNewestPending(p =>
                IsHot(p.Spell) &&
                p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) &&
                p.BaseName.EndsWith("Heal", StringComparison.OrdinalIgnoreCase));

            if (pending is not null)
                StartHot(pending, CharacterName(), when, tickSynced: false);

            return;
        }

        // ------------------------------------------------------------
        // 4. HoT ticks
        //
        // Supports several Legends forms, including:
        //   "Gryff has been healed ... by Flowering Heal VII."
        //   "You healed Cosmos over time ... by Flowering Heal."
        //   "Gryff healed Jarn over time ... by Snails Healing."
        // ------------------------------------------------------------
        var tick = MatchHotTick(line);

        if (tick is not null)
        {
            var target = NormalizeTarget(tick.Value.Target, tick.Value.Caster);
            var effect = SpellNames.Base(tick.Value.Effect);

            var pending = FindNewestPending(p =>
                IsHot(p.Spell) &&
                p.BaseName.Equals(effect, StringComparison.OrdinalIgnoreCase));

            if (pending is not null)
            {
                StartHot(pending, target, when, tickSynced: true);
                return;
            }

            var hotKey = HotKey(target);

            if (_timers.TryGetValue(hotKey, out var active) &&
                !active.TickSynced)
            {
                var configured =
                    FindDefinition(effect)?.DurationSeconds ??
                    (int)Math.Round(active.Duration);

                // Keep the configured duration authoritative, but align
                // the timer to the observed server tick. The three-second
                // adjustment preserves the behavior already used by the app.
                active.FirstTick = when;
                active.End = when.AddSeconds(Math.Max(1, configured - 3));
                active.Duration = Math.Max(
                    1,
                    (active.End - active.Start).TotalSeconds);
                active.TickSynced = true;

                Say($"{active.Spell} on {target} synchronized to server tick");
                TimersChanged?.Invoke();
            }

            return;
        }

        // ------------------------------------------------------------
        // 5. Final HoT trigger
        // ------------------------------------------------------------
        var trigger = TriggerRegex().Match(line);

        if (trigger.Success)
        {
            var target = NormalizeTarget(
                trigger.Groups["target"].Value,
                trigger.Groups["caster"].Success
                    ? trigger.Groups["caster"].Value
                    : "You");

            if (_timers.Remove(HotKey(target), out var ended))
            {
                Say($"{ended.Spell} on {target} ended on trigger");
                TimersChanged?.Invoke();
            }

            return;
        }

        // ------------------------------------------------------------
        // 6. Generic, data-driven landing messages
        //
        // Multiple patterns can be separated with:
        //   ||
        //
        // Example:
        //   You feel much faster. || {target} feels much faster.
        //
        // Non-HoT buffs are accepted only when the pending cast was
        // made by you. Their target may be yourself, another player,
        // or a pet.
        // ------------------------------------------------------------
        foreach (var spell in _spells().Where(s =>
                     s.Enabled &&
                     s.DetectionMode.Equals(
                         "Landing Message",
                         StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(s.LandingPattern)))
        {
            foreach (var pattern in SplitPatterns(spell.LandingPattern))
            {
                var match = TemplateRegex(pattern).Match(line);

                if (!match.Success)
                    continue;

                var pending = FindNewestPending(p =>
                    ReferenceEquals(p.Spell, spell) ||
                    p.BaseName.Equals(
                        SpellNames.Base(spell.Name),
                        StringComparison.OrdinalIgnoreCase) ||
                    p.BaseName.Equals(
                        SpellNames.Base(spell.MatchName),
                        StringComparison.OrdinalIgnoreCase));

                if (pending is null)
                {
                    Say($"Landing matched {spell.Name}, but no pending cast was found.");
                    return;
                }

                var isHot = IsHot(pending.Spell);
                var casterIsYou = pending.Caster.Equals(
                    "You",
                    StringComparison.OrdinalIgnoreCase);

                // HoTs may come from you or another player.
                // Other buffs must have been cast by you.
                if (!isHot && !casterIsYou)
                {
                    Say(
                        $"Ignored buff cast by another player: " +
                        $"{pending.CastName} by {pending.Caster}");
                    return;
                }

                var target = match.Groups["target"].Success
                    ? NormalizeTarget(
                        match.Groups["target"].Value,
                        pending.Caster)
                    : CharacterName();

                if (isHot)
                    StartHot(pending, target, when, tickSynced: false);
                else
                    StartBuff(pending, target, when);

                return;
            }
        }

        // ------------------------------------------------------------
        // 7. Generic fade messages
        //
        // Fade patterns also support || alternatives and {target}.
        // ------------------------------------------------------------
        foreach (var spell in _spells().Where(s =>
                     s.Enabled &&
                     !string.IsNullOrWhiteSpace(s.FadePattern)))
        {
            foreach (var pattern in SplitPatterns(spell.FadePattern))
            {
                var match = TemplateRegex(pattern).Match(line);

                if (!match.Success)
                    continue;

                var target = match.Groups["target"].Success
                    ? NormalizeTarget(
                        match.Groups["target"].Value,
                        "You")
                    : CharacterName();

                var key = IsHot(spell)
                    ? HotKey(target)
                    : BuffKey(SpellNames.Base(spell.Name), target);

                if (_timers.Remove(key, out var ended))
                {
                    Say($"Fade detected: {ended.Spell} on {target}");
                    TimersChanged?.Invoke();
                }

                return;
            }
        }

        TryLearnLandingMessage(line, when);
    }

    private void TryLearnLandingMessage(string line, DateTime when)
    {
        if (!LearningEnabled || LearningRequested is null || IsLearningNoise(line))
            return;

        var pending = _pending
            .Where(p =>
                !p.LearningPrompted &&
                !IsHot(p.Spell) &&
                p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) &&
                when >= p.CastTime &&
                when <= p.CastTime.AddSeconds(10))
            .OrderByDescending(p => p.CastTime)
            .FirstOrDefault();

        if (pending is null)
            return;

        pending.LearningPrompted = true;

        var canUseTarget = TryBuildTargetPattern(
            line,
            out var suggestedPattern,
            out var suggestedTarget);

        var candidate = new LearningCandidate
        {
            SpellName = pending.CastName,
            Message = line,
            SuggestedPattern = canUseTarget ? suggestedPattern : line,
            SuggestedTarget = canUseTarget ? suggestedTarget : CharacterName(),
            CanUseTarget = canUseTarget
        };

        var decision = LearningRequested(candidate);

        if (decision == LearningDecision.Ignore)
        {
            Say($"Ignored learning candidate for {pending.CastName}: {line}");
            return;
        }

        string learnedPattern;
        string target;

        if (decision == LearningDecision.Target && canUseTarget)
        {
            learnedPattern = suggestedPattern;
            target = suggestedTarget;
        }
        else
        {
            learnedPattern = line;
            target = CharacterName();
        }

        pending.Spell.DetectionMode = "Landing Message";
        pending.Spell.LandingPattern = AddPattern(
            pending.Spell.LandingPattern,
            learnedPattern);

        _pending.Remove(pending);

        Say($"Learned {pending.CastName}: {learnedPattern}");
        SpellDefinitionsChanged?.Invoke();
        StartBuff(pending, target, when);
    }

    private static string AddPattern(string existing, string pattern)
    {
        var patterns = SplitPatterns(existing).ToList();

        if (!patterns.Any(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
            patterns.Add(pattern.Trim());

        return string.Join(" || ", patterns);
    }

    private static bool TryBuildTargetPattern(
        string line,
        out string pattern,
        out string target)
    {
        var match = TargetPrefixRegex().Match(line);

        if (!match.Success)
        {
            pattern = line;
            target = string.Empty;
            return false;
        }

        target = match.Groups["target"].Value;

        if (target.Equals("You", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("Your", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("I", StringComparison.OrdinalIgnoreCase))
        {
            pattern = line;
            target = string.Empty;
            return false;
        }

        pattern = "{target}" + match.Groups["rest"].Value;
        return true;
    }

    private static bool IsLearningNoise(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        string[] fragments =
        [
            " tells ", " told you", " says", " shouts", " auction",
            "You begin casting", " begins casting ", " spell is interrupted",
            "Your spell fizzles", "Auto attack", " has been slain",
            "You gain experience", "You receive ", "You looted ",
            "Your faction standing", "LOADING, PLEASE WAIT",
            "You have entered ", "Targeted (", " regards you ",
            "Beginning to memorize", "You have finished memorizing",
            "You forget ", "You say", "You slash", "You hit ",
            "You try to ", "You are stunned", "You are no longer stunned",
            " points of damage", " damage from ", " misses", " dodges",
            " blocks", " parries", " kicks ", " punches ", " pierces ",
            " cleaves ", " backstabs ", " bashes ", " is burned by "
        ];

        return fragments.Any(fragment =>
            line.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveExpired(DateTime now)
    {
        var expired = _timers
            .Where(kv => kv.Value.End <= now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expired)
        {
            var timer = _timers[key];
            _timers.Remove(key);
            Say($"Expired {timer.Spell} on {timer.Target}");
        }

        if (expired.Count > 0)
            TimersChanged?.Invoke();
    }

    private SpellDefinition? FindDefinition(string spellName)
    {
        var baseName = SpellNames.Base(spellName);

        return _spells().FirstOrDefault(s =>
            s.Enabled &&
            (SpellNames.Base(s.Name).Equals(
                 baseName,
                 StringComparison.OrdinalIgnoreCase) ||
             SpellNames.Base(s.MatchName).Equals(
                 baseName,
                 StringComparison.OrdinalIgnoreCase)));
    }

    private void StartHot(
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

        // All supported HoTs share one HoT slot per target.
        _timers[timer.Key] = timer;

        Say(
            $"Started {timer.Spell} on {target} " +
            $"from {source} ({duration} sec)");

        TimersChanged?.Invoke();
    }

    private void StartBuff(
        PendingCast pending,
        string target,
        DateTime start)
    {
        var spell = pending.Spell;
        var duration = Math.Max(1, spell.DurationSeconds);
        var source = DisplayCaster(pending.Caster);

        var timer = new ActiveTimer
        {
            // Ranked versions overwrite the same base spell on the
            // same target, while different buffs remain independent.
            Key = BuffKey(pending.BaseName, target),
            Spell = pending.CastName,
            BaseName = pending.BaseName,
            Target = target,
            Source = source,
            Category = spell.Category,
            Start = start,
            End = start.AddSeconds(duration),
            Duration = duration,
            TickSynced = false
        };

        _timers[timer.Key] = timer;

        Say(
            $"Started {pending.CastName} on {target} " +
            $"from {source} ({duration} sec)");

        TimersChanged?.Invoke();
    }

    private PendingCast? FindNewestPending(
        Func<PendingCast, bool> predicate)
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (!predicate(_pending[i]))
                continue;

            var pending = _pending[i];
            _pending.RemoveAt(i);
            return pending;
        }

        return null;
    }

    private void CleanupPending()
    {
        var now = DateTime.Now;
        _pending.RemoveAll(p => p.Expires < now);
    }

    private void CancelNewestSelf(string reason)
    {
        var pending = FindNewestPending(p =>
            p.Caster.Equals(
                "You",
                StringComparison.OrdinalIgnoreCase));

        if (pending is not null)
            Say($"Pending cast cancelled: {pending.CastName} ({reason})");
    }

    private string NormalizeTarget(string target, string caster)
    {
        var cleaned = target.Trim().TrimEnd('.');
        var actualCaster = DisplayCaster(caster);

        if (cleaned.Equals("you", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("yourself", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterName();
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

    private string DisplayCaster(string caster)
    {
        return caster.Equals(
            "You",
            StringComparison.OrdinalIgnoreCase)
                ? CharacterName()
                : caster.Trim();
    }

    private string CharacterName()
    {
        var name = _characterName()?.Trim();
        return string.IsNullOrWhiteSpace(name) ? "You" : name;
    }

    private static bool IsHot(SpellDefinition spell)
    {
        return spell.Category.Equals(
            "HoT",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanSpellName(string value)
    {
        return value.Trim().TrimEnd('.');
    }

    private static string HotKey(string target)
    {
        return $"hot|{target.Trim()}".ToLowerInvariant();
    }

    private static string BuffKey(string spell, string target)
    {
        return $"{SpellNames.Base(spell).Trim()}|{target.Trim()}"
            .ToLowerInvariant();
    }

    private void Say(string message)
    {
        Activity?.Invoke(message);
    }

    private static DateTime ParseTimestamp(string line)
    {
        var match = TimestampRegex().Match(line);

        return match.Success &&
               DateTime.TryParseExact(
                   match.Groups["stamp"].Value,
                   "ddd MMM dd HH:mm:ss yyyy",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out var timestamp)
            ? timestamp
            : DateTime.Now;
    }

    private static IEnumerable<string> SplitPatterns(string patterns)
    {
        return patterns
            .Split(
                "||",
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern));
    }

    private static Regex TemplateRegex(string template)
    {
        var pattern = Regex
            .Escape(template.Trim())
            .Replace(
                "\\{target}",
                "(?<target>.+?)",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "\\{caster}",
                "(?<caster>.+?)",
                StringComparison.OrdinalIgnoreCase);

        return new Regex(
            "^" + pattern + "$",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);
    }

    private static HotTickMatch? MatchHotTick(string line)
    {
        var passive = PassiveHotTickRegex().Match(line);

        if (passive.Success)
        {
            return new HotTickMatch(
                passive.Groups["target"].Value,
                passive.Groups["effect"].Value,
                "You");
        }

        var active = ActiveHotTickRegex().Match(line);

        if (active.Success)
        {
            return new HotTickMatch(
                active.Groups["target"].Value,
                active.Groups["effect"].Value,
                active.Groups["caster"].Value);
        }

        var you = YouHotTickRegex().Match(line);

        if (you.Success)
        {
            return new HotTickMatch(
                you.Groups["target"].Value,
                you.Groups["effect"].Value,
                "You");
        }

        return null;
    }

    private readonly record struct HotTickMatch(
        string Target,
        string Effect,
        string Caster);

    [GeneratedRegex(@"^\[(?<stamp>[^\]]+)\]\s*")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(
        @"^You begin casting (?<spell>.+?)\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex YouCastRegex();

    [GeneratedRegex(
        @"^(?<caster>.+?) begins casting (?<spell>.+?)\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex OtherCastRegex();

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
        @"^(?<target>[A-Za-z][A-Za-z'`-]*)(?<rest>(?:'s|`s)?\s.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TargetPrefixRegex();

    [GeneratedRegex(
        @"^(?:(?<caster>You) healed (?<target>.+?)|" +
        @"(?<caster>.+?) healed (?<target>.+?)|" +
        @"(?<target>.+?) healed (?:himself|herself|itself|themselves)) " +
        @"for .+? hit points by (?<effect>.+?) Trigger" +
        @"(?: \(Critical\))?\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TriggerRegex();
}

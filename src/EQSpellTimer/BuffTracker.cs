namespace EQSpellTimer;

internal sealed class BuffTracker
{
    private readonly EngineContext _context;

    public BuffTracker(EngineContext context)
    {
        _context = context;
    }

    public bool TryKnownLanding(LogMessage message)
    {
        var pending = _context.PeekNewestPending(p =>
            !EngineContext.IsHot(p.Spell) &&
            p.Caster.Equals("You", StringComparison.OrdinalIgnoreCase) &&
            message.Timestamp >= p.CastTime &&
            message.Timestamp <= p.CastTime.AddSeconds(5) &&
            p.Spell.DetectionMode.Equals(
                "Landing Message",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(p.Spell.LandingPattern));

        if (pending is null)
            return false;

        var patterns = PatternMatcher
            .Split(pending.Spell.LandingPattern)
            .OrderBy(pattern =>
                pattern.Contains("{target}", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0);

        foreach (var pattern in patterns)
        {
            var match = PatternMatcher.Match(pattern, message.Text);

            if (!match.Success)
                continue;

            var target = match.Groups["target"].Success
                ? _context.NormalizeTarget(
                    match.Groups["target"].Value,
                    pending.Caster)
                : _context.CharacterName;

            if (LooksLikeGenericNpcSubject(target))
                return true;

            _context.Pending.Remove(pending);
            _context.StartBuff(pending, target, message.Timestamp);
            return true;
        }

        return false;
    }

    public bool TryFade(LogMessage message)
    {
        foreach (var spell in _context.Spells.Where(s =>
                     s.Enabled &&
                     !string.IsNullOrWhiteSpace(s.FadePattern)))
        {
            foreach (var pattern in PatternMatcher.Split(spell.FadePattern))
            {
                var match = PatternMatcher.Match(pattern, message.Text);

                if (!match.Success)
                    continue;

                var target = match.Groups["target"].Success
                    ? _context.NormalizeTarget(
                        match.Groups["target"].Value,
                        "You")
                    : _context.CharacterName;

                var key = EngineContext.IsHot(spell)
                    ? EngineContext.HotKey(
                        EngineContext.HotFamily(spell.Name),
                        target)
                    : EngineContext.BuffKey(spell.Name, target);

                if (_context.Timers.Remove(key, out var ended))
                {
                    _context.Say(
                        $"Fade detected: {ended.Spell} on {target}");
                    _context.TimersChanged?.Invoke();
                }

                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeGenericNpcSubject(string target)
    {
        var value = target.Trim();

        return
            value.StartsWith("a ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("an ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("the ", StringComparison.OrdinalIgnoreCase);
    }
}

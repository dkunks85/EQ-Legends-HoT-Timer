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
        foreach (var spell in _context.Spells.Where(s =>
                     s.Enabled &&
                     !EngineContext.IsHot(s) &&
                     s.DetectionMode.Equals(
                         "Landing Message",
                         StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(s.LandingPattern)))
        {
            foreach (var pattern in PatternMatcher.Split(
                         spell.LandingPattern))
            {
                var match = PatternMatcher.Match(
                    pattern,
                    message.Text);

                if (!match.Success)
                    continue;

                var pending = _context.TakeNewestPending(p =>
                    !EngineContext.IsHot(p.Spell) &&
                    p.Caster.Equals(
                        "You",
                        StringComparison.OrdinalIgnoreCase) &&
                    (ReferenceEquals(p.Spell, spell) ||
                     p.BaseName.Equals(
                         SpellNames.Base(spell.Name),
                         StringComparison.OrdinalIgnoreCase) ||
                     p.BaseName.Equals(
                         SpellNames.Base(spell.MatchName),
                         StringComparison.OrdinalIgnoreCase)));

                if (pending is null)
                {
                    _context.Say(
                        $"Landing matched {spell.Name}, " +
                        "but no pending self-cast was found.");
                    return true;
                }

                var target = match.Groups["target"].Success
                    ? _context.NormalizeTarget(
                        match.Groups["target"].Value,
                        pending.Caster)
                    : _context.CharacterName;

                _context.StartBuff(
                    pending,
                    target,
                    message.Timestamp);

                return true;
            }
        }

        return false;
    }

    public bool TryFade(LogMessage message)
    {
        foreach (var spell in _context.Spells.Where(s =>
                     s.Enabled &&
                     !string.IsNullOrWhiteSpace(s.FadePattern)))
        {
            foreach (var pattern in PatternMatcher.Split(
                         spell.FadePattern))
            {
                var match = PatternMatcher.Match(
                    pattern,
                    message.Text);

                if (!match.Success)
                    continue;

                var target = match.Groups["target"].Success
                    ? _context.NormalizeTarget(
                        match.Groups["target"].Value,
                        "You")
                    : _context.CharacterName;

                var key = EngineContext.IsHot(spell)
                    ? EngineContext.HotKey(target)
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
}

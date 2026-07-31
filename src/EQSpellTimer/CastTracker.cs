namespace EQSpellTimer;

internal sealed class CastTracker
{
    private readonly EngineContext _context;

    public CastTracker(EngineContext context)
    {
        _context = context;
    }

    public bool TryHandle(LogMessage message)
    {
        if (!LogMessageParser.TryReadCast(
                message.Text,
                out var caster,
                out var castName))
        {
            return false;
        }

        var definition = _context.FindDefinition(castName);

        if (definition is null)
            return true;

        var isHot = EngineContext.IsHot(definition);
        var casterIsYou = caster.Equals(
            "You",
            StringComparison.OrdinalIgnoreCase);

        // HoTs may be tracked from visible group members.
        // Normal buffs are tracked only when cast by you.
        if (!isHot && !casterIsYou)
        {
            _context.Say(
                $"Ignored buff cast by another player: " +
                $"{castName} by {caster}");
            return true;
        }

        _context.Pending.Add(new PendingCast
        {
            Spell = definition,
            CastName = castName,
            BaseName = SpellNames.Base(castName),
            Caster = caster,
            CastTime = message.Timestamp,
            Expires = message.Timestamp.AddSeconds(20)
        });

        _context.Say(
            $"Pending {castName} by {_context.DisplayCaster(caster)}");

        if (isHot &&
            EngineContext.IsClericHotName(
                SpellNames.Base(castName)))
        {
            var pending = _context.Pending[^1];
            _context.StartProvisionalClericHot(pending);
        }

        return true;
    }

    public bool TryHandleFailure(string line)
    {
        if (line.Equals(
                "Your spell fizzles!",
                StringComparison.OrdinalIgnoreCase))
        {
            _context.CancelNewestSelf("fizzled");
            return true;
        }

        if (line.StartsWith("Your ", StringComparison.OrdinalIgnoreCase) &&
            line.Contains(
                " spell is interrupted",
                StringComparison.OrdinalIgnoreCase))
        {
            _context.CancelNewestSelf("interrupted");
            return true;
        }

        if (line.Contains(
                "did not take hold",
                StringComparison.OrdinalIgnoreCase) ||
            line.Contains(
                "effect is currently blocked",
                StringComparison.OrdinalIgnoreCase))
        {
            _context.CancelNewestSelf("blocked");
            return true;
        }

        return false;
    }
}

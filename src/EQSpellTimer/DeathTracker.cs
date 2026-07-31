using System.Text.RegularExpressions;

namespace EQSpellTimer;

internal sealed partial class DeathTracker
{
    private readonly EngineContext _context;

    public DeathTracker(EngineContext context)
    {
        _context = context;
    }

    public bool TryHandle(LogMessage message)
    {
        if (SelfDeathRegex().IsMatch(message.Text))
        {
            _context.RemoveTimersForTarget(
                _context.CharacterName,
                "death");

            return true;
        }

        var match = TargetDeathRegex().Match(message.Text);

        if (!match.Success)
            return false;

        var target = _context.NormalizeTarget(
            match.Groups["target"].Value,
            "You");

        _context.RemoveTimersForTarget(target, "death");
        return true;
    }

    [GeneratedRegex(
        @"^(?:You have been slain by .+|You died)\.?[!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SelfDeathRegex();

    [GeneratedRegex(
        @"^(?<target>.+?) (?:has been slain by .+|died)\.?[!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TargetDeathRegex();
}

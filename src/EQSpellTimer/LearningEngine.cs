using System.Text.RegularExpressions;

namespace EQSpellTimer;

internal sealed partial class LearningEngine
{
    private readonly EngineContext _context;

    public bool Enabled { get; set; } = true;

    public Func<LearningCandidate, LearningDecision>? RequestDecision
    {
        get;
        set;
    }

    public LearningEngine(EngineContext context)
    {
        _context = context;
    }

    public bool TryHandle(LogMessage message)
    {
        if (!Enabled ||
            RequestDecision is null ||
            IsNoise(message.Text))
        {
            return false;
        }

        // Learning is deliberately limited to unknown, manual,
        // non-HoT buffs cast by the local player.
        var pending = _context.PeekNewestPending(p =>
            !p.LearningPrompted &&
            !EngineContext.IsHot(p.Spell) &&
            p.Caster.Equals(
                "You",
                StringComparison.OrdinalIgnoreCase) &&
            p.Spell.DetectionMode.Equals(
                "Landing Message",
                StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(p.Spell.LandingPattern) &&
            message.Timestamp >= p.CastTime &&
            message.Timestamp <= p.CastTime.AddSeconds(10));

        if (pending is null)
            return false;

        pending.LearningPrompted = true;

        var usesTarget = TryBuildTargetPattern(
            message.Text,
            out var suggestedPattern,
            out var suggestedTarget);

        var candidate = new LearningCandidate
        {
            SpellName = pending.CastName,
            Message = message.Text,
            SuggestedPattern = suggestedPattern,
            SuggestedTarget = usesTarget
                ? suggestedTarget
                : _context.CharacterName,
            UsesTarget = usesTarget
        };

        var decision = RequestDecision(candidate);

        if (decision == LearningDecision.Ignore)
        {
            _context.Say(
                $"Ignored learning candidate for " +
                $"{pending.CastName}: {message.Text}");
            return true;
        }

        pending.Spell.DetectionMode = "Landing Message";
        pending.Spell.LandingPattern = PatternMatcher.Add(
            pending.Spell.LandingPattern,
            suggestedPattern);

        _context.Pending.Remove(pending);

        _context.Say(
            $"Learned {pending.CastName}: {suggestedPattern}");

        _context.SpellDefinitionsChanged?.Invoke();

        // Start the timer on the same cast that taught the pattern.
        _context.StartBuff(
            pending,
            candidate.SuggestedTarget,
            message.Timestamp);

        return true;
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

    private static bool IsNoise(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        string[] fragments =
        [
            " tells ", " told you", " says", " shouts", " auction",
            "You begin casting", " begins casting ",
            " spell is interrupted", "Your spell fizzles",
            "Auto attack", " has been slain",
            "You gain experience", "You receive ", "You looted ",
            "Your faction standing", "LOADING, PLEASE WAIT",
            "You have entered ", "Targeted (", " regards you ",
            "Beginning to memorize", "You have finished memorizing",
            "You forget ", "You say", "You slash", "You hit ",
            "You try to ", "You are stunned",
            "You are no longer stunned", " points of damage",
            " damage from ", " misses", " dodges", " blocks",
            " parries", " kicks ", " punches ", " pierces ",
            " cleaves ", " backstabs ", " bashes ",
            " is burned by "
        ];

        return fragments.Any(fragment =>
            line.Contains(
                fragment,
                StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(
        @"^(?<target>[A-Za-z][A-Za-z'`-]*)(?<rest>(?:'s|`s)?\s.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TargetPrefixRegex();
}

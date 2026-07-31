using System.Text.RegularExpressions;

namespace EQSpellTimer;

internal static class PatternMatcher
{
    public static IEnumerable<string> Split(string patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns))
            return [];

        return patterns
            .Split(
                "||",
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern));
    }

    public static Match Match(string template, string line)
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

        return Regex.Match(
            line,
            "^" + pattern + "$",
            RegexOptions.IgnoreCase);
    }

    public static string Add(string existing, string pattern)
    {
        var patterns = Split(existing).ToList();

        if (!patterns.Any(p =>
                p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            patterns.Add(pattern.Trim());
        }

        return string.Join(" || ", patterns);
    }
}

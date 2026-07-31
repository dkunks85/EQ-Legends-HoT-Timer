using System.Globalization;
using System.Text.RegularExpressions;

namespace EQSpellTimer;

internal readonly record struct LogMessage(DateTime Timestamp, string Text);

internal static partial class LogMessageParser
{
    public static LogMessage Parse(string raw)
    {
        var match = TimestampRegex().Match(raw);

        var timestamp =
            match.Success &&
            DateTime.TryParseExact(
                match.Groups["stamp"].Value,
                "ddd MMM dd HH:mm:ss yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
                ? parsed
                : DateTime.Now;

        var text = TimestampRegex().Replace(raw, string.Empty).Trim();
        return new LogMessage(timestamp, text);
    }

    public static bool TryReadCast(
        string line,
        out string caster,
        out string spell)
    {
        var match = YouCastRegex().Match(line);

        if (match.Success)
        {
            caster = "You";
            spell = CleanSpellName(match.Groups["spell"].Value);
            return true;
        }

        match = OtherCastRegex().Match(line);

        if (match.Success)
        {
            caster = match.Groups["caster"].Value.Trim();
            spell = CleanSpellName(match.Groups["spell"].Value);
            return true;
        }

        caster = string.Empty;
        spell = string.Empty;
        return false;
    }

    private static string CleanSpellName(string value) =>
        value.Trim().TrimEnd('.');

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
}

using System.Text.Json.Serialization;

namespace EQSpellTimer;

public sealed class SpellDefinition
{
    public string Name { get; set; } = "New Spell";
    public string Category { get; set; } = "Buff";
    public int DurationSeconds { get; set; } = 60;
    public string DetectionMode { get; set; } = "Landing Message";
    public string MatchName { get; set; } = "";
    public int TickDelaySeconds { get; set; }
    public string LandingPattern { get; set; } = "";
    public string FadePattern { get; set; } = "";

    // UI-friendly landing fields. The parser still reads LandingPattern,
    // but users no longer need to type || or {target}.
    [JsonIgnore]
    public string SelfLandingPattern
    {
        get => SplitLandingPatterns()
            .FirstOrDefault(pattern =>
                !pattern.Contains(
                    "{target}",
                    StringComparison.OrdinalIgnoreCase))
            ?? "";

        set => SetLandingPattern(
            isTargetPattern: false,
            value);
    }

    [JsonIgnore]
    public string TargetLandingPattern
    {
        get
        {
            var pattern = SplitLandingPatterns()
                .FirstOrDefault(pattern =>
                    pattern.Contains(
                        "{target}",
                        StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(pattern))
                return "";

            var markerIndex = pattern.IndexOf(
                "{target}",
                StringComparison.OrdinalIgnoreCase);

            return pattern
                .Remove(markerIndex, "{target}".Length)
                .TrimStart();
        }

        set => SetLandingPattern(
            isTargetPattern: true,
            value);
    }

    public bool Enabled { get; set; } = true;

    private IEnumerable<string> SplitLandingPatterns()
    {
        return (LandingPattern ?? "")
            .Split(
                "||",
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
    }

    private void SetLandingPattern(
        bool isTargetPattern,
        string? value)
    {
        var patterns = SplitLandingPatterns().ToList();

        patterns.RemoveAll(pattern =>
            pattern.Contains(
                "{target}",
                StringComparison.OrdinalIgnoreCase) ==
            isTargetPattern);

        var cleaned = value?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            if (isTargetPattern)
            {
                cleaned = cleaned
                    .Replace(
                        "{target}",
                        "",
                        StringComparison.OrdinalIgnoreCase)
                    .TrimStart();

                cleaned = "{target} " + cleaned;
            }

            patterns.Add(cleaned);
        }

        LandingPattern = string.Join(" || ", patterns);
    }

    [JsonIgnore]
    public string Duration
    {
        get => $"{DurationSeconds / 60}:{DurationSeconds % 60:00}";
        set => DurationSeconds = DurationParser.Parse(value, DurationSeconds);
    }
}

public sealed class AppSettings
{
    public string LogPath { get; set; } = "";
    public bool LearnHotDurations { get; set; } = true;
    public int WindowWidth { get; set; } = 940;
    public int WindowHeight { get; set; } = 760;
}

public sealed class PendingCast
{
    public required SpellDefinition Spell { get; init; }
    public required string CastName { get; init; }
    public required string BaseName { get; init; }
    public required string Caster { get; init; }
    public required DateTime CastTime { get; init; }
    public DateTime Expires { get; init; }
}

public sealed class ActiveTimer
{
    public required string Key { get; set; }
    public required string Spell { get; init; }
    public required string BaseName { get; init; }
    public required string Target { get; set; }
    public required string Source { get; init; }
    public required string Category { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; set; }
    public required double Duration { get; set; }
    public bool TickSynced { get; set; }
    public DateTime? FirstTick { get; set; }
}

public static class DurationParser
{
    public static int Parse(string? value, int fallback = 60)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        value = value.Trim();

        if (int.TryParse(value, out var seconds))
            return Math.Max(1, seconds);

        var parts = value.Split(':');

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var minutes) &&
            int.TryParse(parts[1], out seconds))
        {
            return Math.Max(1, minutes * 60 + seconds);
        }

        return fallback;
    }
}

using System.Text.RegularExpressions;

namespace EQSpellTimer;

public static partial class SpellNames
{
    private static readonly HashSet<string> HotFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Budding Heal", "Sprouting Heal", "Flowering Heal", "Blooming Heal", "Blossoming Heal", "Efflorescing Heal",
        "Snails Healing", "Tortoises Healing", "Slugs Healing",
        "Echo of Health", "Echoing Light", "Renewing Echo",
        "Celestial Echo", "Sacred Echo"
    };

    [GeneratedRegex(@"\s+(?:I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII|XIII|XIV|XV|\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RankRegex();

    public static string Base(string? spellName)
    {
        var value = (spellName ?? "").Trim().TrimEnd('.');
        return RankRegex().Replace(value, "").Trim();
    }

    public static bool IsSupportedHot(string spellName) => HotFamilies.Contains(Base(spellName));
}

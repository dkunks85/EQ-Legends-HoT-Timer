using System.Text.Json;

namespace EQSpellTimer;

public sealed class ConfigStore
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string AppDirectory { get; } = AppContext.BaseDirectory;
    public string SpellsPath => Path.Combine(AppDirectory, "spells.json");
    public string SettingsPath => Path.Combine(AppDirectory, "settings.json");

    public List<SpellDefinition> LoadSpells()
    {
        try
        {
            if (!File.Exists(SpellsPath))
                return Defaults();

            var spells =
                JsonSerializer.Deserialize<List<SpellDefinition>>(
                    File.ReadAllText(SpellsPath),
                    _json)
                ?? Defaults();

            foreach (var spell in spells)
            {
                spell.Name = string.IsNullOrWhiteSpace(spell.Name)
                    ? "New Spell"
                    : spell.Name.Trim();

                spell.MatchName = string.IsNullOrWhiteSpace(spell.MatchName)
                    ? SpellNames.Base(spell.Name)
                    : spell.MatchName.Trim();

                spell.Category = string.IsNullOrWhiteSpace(spell.Category)
                    ? "Buff"
                    : spell.Category;

                spell.DetectionMode =
                    string.IsNullOrWhiteSpace(spell.DetectionMode)
                        ? "Landing Message"
                        : spell.DetectionMode;

                spell.DurationSeconds =
                    Math.Max(1, spell.DurationSeconds);

                if (SpellNames.Base(spell.Name).Equals(
                        "Alacrity",
                        StringComparison.OrdinalIgnoreCase))
                {
                    spell.Category = "Buff";
                    spell.DetectionMode = "Landing Message";
                    spell.MatchName = "Alacrity";

                    // Upgrade old self-only configurations.
                    if (string.IsNullOrWhiteSpace(spell.LandingPattern) ||
                        spell.LandingPattern.Equals(
                            "You feel much faster.",
                            StringComparison.OrdinalIgnoreCase) ||
                        spell.LandingPattern.Equals(
                            "{target} feels much faster.",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        spell.LandingPattern =
                            "You feel much faster. || {target} feels much faster.";
                    }
                }
            }

            SaveSpells(spells);
            return spells;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Could not load spells: {ex}");

            return Defaults();
        }
    }

    public void SaveSpells(IEnumerable<SpellDefinition> spells) => File.WriteAllText(SpellsPath, JsonSerializer.Serialize(spells, _json));

    public AppSettings LoadSettings()
    {
        try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), _json) ?? new() : new(); }
        catch { return new(); }
    }

    public void SaveSettings(AppSettings settings) => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _json));

    public static List<SpellDefinition> Defaults() =>
    [
        Hot("Budding Heal"), Hot("Sprouting Heal"), Hot("Flowering Heal"), Hot("Blooming Heal"), Hot("Blossoming Heal"), Hot("Efflorescing Heal"),
        Hot("Snails Healing"), Hot("Tortoises Healing"), Hot("Slugs Healing"),
        new() { Name="Alacrity", MatchName="Alacrity", Category="Buff", DurationSeconds=180, DetectionMode="Landing Message", LandingPattern="You feel much faster.", Enabled=true }
    ];

    private static SpellDefinition Hot(string name) => new() { Name=name, MatchName=name, Category="HoT", DurationSeconds=27, DetectionMode="Auto HoT Family", Enabled=true };
}

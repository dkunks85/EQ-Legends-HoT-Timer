using System.Text.Json;

namespace EQSpellTimer;

internal sealed class HotDurationStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private Dictionary<string, List<int>> _observations =
        new(StringComparer.OrdinalIgnoreCase);

    public HotDurationStore(string appDirectory)
    {
        _path = Path.Combine(appDirectory, "hot-durations.json");
        Load();
    }

    public int GetDuration(string exactSpellName, int fallbackSeconds)
    {
        if (!_observations.TryGetValue(exactSpellName, out var values) ||
            values.Count == 0)
        {
            return Math.Max(1, fallbackSeconds);
        }

        var ordered = values.OrderBy(value => value).ToList();
        return ordered[ordered.Count / 2];
    }

    public int Record(string exactSpellName, int observedSeconds)
    {
        observedSeconds = Math.Max(1, observedSeconds);

        if (!_observations.TryGetValue(exactSpellName, out var values))
        {
            values = [];
            _observations[exactSpellName] = values;
        }

        values.Add(observedSeconds);

        // Keep recent observations so patches or rank changes can adapt.
        while (values.Count > 9)
            values.RemoveAt(0);

        Save();
        return GetDuration(exactSpellName, observedSeconds);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var loaded =
                JsonSerializer.Deserialize<Dictionary<string, List<int>>>(
                    File.ReadAllText(_path),
                    _json);

            if (loaded is not null)
            {
                _observations = new Dictionary<string, List<int>>(
                    loaded,
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _observations =
                new Dictionary<string, List<int>>(
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(_observations, _json));
    }
}

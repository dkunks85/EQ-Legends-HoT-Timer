using System.Text.Json;

namespace EQSpellTimer;

internal sealed class HotDurationStore
{
    private const int MinimumValidSeconds = 5;
    private const int MaximumValidSeconds = 600;

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
        fallbackSeconds = Math.Max(MinimumValidSeconds, fallbackSeconds);

        if (!_observations.TryGetValue(exactSpellName, out var values))
            return fallbackSeconds;

        var valid = values
            .Where(IsValidObservation)
            .OrderBy(value => value)
            .ToList();

        return valid.Count == 0
            ? fallbackSeconds
            : valid[valid.Count / 2];
    }

    public int Record(string exactSpellName, int observedSeconds)
    {
        if (!IsValidObservation(observedSeconds))
            return GetDuration(exactSpellName, 27);

        if (!_observations.TryGetValue(exactSpellName, out var values))
        {
            values = [];
            _observations[exactSpellName] = values;
        }

        values.Add(observedSeconds);
        values.RemoveAll(value => !IsValidObservation(value));

        while (values.Count > 9)
            values.RemoveAt(0);

        Save();
        return GetDuration(exactSpellName, observedSeconds);
    }

    private static bool IsValidObservation(int seconds) =>
        seconds >= MinimumValidSeconds &&
        seconds <= MaximumValidSeconds;

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

            if (loaded is null)
                return;

            _observations = new Dictionary<string, List<int>>(
                loaded,
                StringComparer.OrdinalIgnoreCase);

            var changed = false;

            foreach (var values in _observations.Values)
                changed |= values.RemoveAll(v => !IsValidObservation(v)) > 0;

            foreach (var key in _observations
                         .Where(pair => pair.Value.Count == 0)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _observations.Remove(key);
                changed = true;
            }

            if (changed)
                Save();
        }
        catch
        {
            _observations = new Dictionary<string, List<int>>(
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

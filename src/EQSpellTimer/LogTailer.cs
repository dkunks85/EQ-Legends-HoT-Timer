namespace EQSpellTimer;

public sealed class LogTailer : IDisposable
{
    private FileSystemWatcher? _watcher;
    private FileStream? _stream;
    private StreamReader? _reader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public event Action<string>? LineReceived;
    public event Action<Exception>? Error;
    public bool IsRunning => _watcher is not null;

    public async Task StartAsync(string path, bool startAtEnd = true)
    {
        Stop();
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (startAtEnd) _stream.Seek(0, SeekOrigin.End);
        _reader = new StreamReader(_stream);
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += async (_, _) => await DrainAsync();
        _watcher.Renamed += async (_, _) => await DrainAsync();
        await DrainAsync();
    }

    private async Task DrainAsync()
    {
        if (_reader is null || !await _gate.WaitAsync(0)) return;
        try
        {
            while (true)
            {
                var line = await _reader.ReadLineAsync();
                if (line is null) break;
                LineReceived?.Invoke(line);
            }
        }
        catch (Exception ex) { Error?.Invoke(ex); }
        finally { _gate.Release(); }
    }

    public void Stop()
    {
        _watcher?.Dispose(); _watcher = null;
        _reader?.Dispose(); _reader = null;
        _stream?.Dispose(); _stream = null;
    }

    public void Dispose() { Stop(); _gate.Dispose(); }
}

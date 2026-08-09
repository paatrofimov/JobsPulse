using System.Text.Json;
using System.Text.Json.Serialization;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Infrastructure;

public sealed class FileWatchlistProvider : IWatchlistProvider, IDisposable
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptionsFactory.CreateJsonOptions(opts =>
    {
        opts.WriteIndented = true;
        opts.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    private readonly string _path;
    private readonly ILog ctxLog;
    private readonly FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private volatile Watchlist _current = new();
    private DateTimeOffset _lastReload = DateTimeOffset.MinValue;
    private bool _selfWrite;

    public FileWatchlistProvider(string path, TimeProvider clock, ILog log)
    {
        _path = Path.GetFullPath(path);
        Clock = clock;

        ctxLog = log.ForContext<FileWatchlistProvider>();

        _current = Load() ?? new Watchlist();

        var dir = Path.GetDirectoryName(_path);
        if (dir is null) return;

        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
    }

    private TimeProvider Clock { get; }

    public Watchlist Current => _current;

    public async Task<WatchEntry> AddAsync(WatchEntry entry, CancellationToken ct)
    {
        await MutateAsync(list =>
        {
            var others = list.Entries.Where(e => !e.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            return list with { Entries = [.. others, entry] };
        }, ct);

        return entry;
    }

    public async Task<bool> RemoveAsync(string entryId, CancellationToken ct)
    {
        var existed = _current.Entries.Any(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase));
        if (!existed) return false;

        await MutateAsync(list => list with
        {
            Entries = [.. list.Entries.Where(e => !e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase))]
        }, ct);

        return true;
    }

    public async Task<bool> SetEnabledAsync(string entryId, bool enabled, CancellationToken ct)
    {
        if (!_current.Entries.Any(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase))) return false;

        await MutateAsync(list => list with
        {
            Entries =
            [
                .. list.Entries
                    .Select(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase) ? e with { Enabled = enabled } : e)
            ]
        }, ct);

        return true;
    }

    private async Task MutateAsync(Func<Watchlist, Watchlist> mutate, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var updated = mutate(_current) with { Version = _current.Version + 1 };

            _selfWrite = true;

            // Writing via temp file for atomic move
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new Root { Watchlist = updated }, Json), ct);
            File.Move(temp, _path, overwrite: true);

            _current = updated;
        }
        finally
        {
            _selfWrite = false;
            _writeLock.Release();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_selfWrite) return;

        var now = Clock.GetUtcNow();
        if (now - _lastReload < DebounceWindow) return;
        _lastReload = now;

        var reloaded = Load();
        if (reloaded is null)
        {
            ctxLog.Error("watchlist.json could not be read — using previous version {Version}",
                _current.Version);
            return;
        }

        _current = reloaded;
        ctxLog.Info("watchlist reloaded: version {Version}, entries {Count}",
            reloaded.Version, reloaded.Entries.Count);
    }

    private Watchlist? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new Watchlist();

            var content = ReadWithRetry();
            var root = JsonSerializer.Deserialize<Root>(content, Json);

            return Validate(root?.Watchlist);
        }
        catch (Exception ex)
        {
            ctxLog.Error(ex, "Failed to read path {Path}", _path);
            return null;
        }
    }

    private string ReadWithRetry()
    {
        for (var attempt = 0;; attempt++)
        {
            try
            {
                return File.ReadAllText(_path);
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100);
            }
        }
    }

    private Watchlist? Validate(Watchlist? list)
    {
        if (list is null) return null;

        var bad = list.Entries
            .Where(e => string.IsNullOrWhiteSpace(e.Id)
                        || string.IsNullOrWhiteSpace(e.VacancySourceId)
                        || string.IsNullOrWhiteSpace(e.BoardId))
            .ToList();

        if (bad.Count > 0)
        {
            ctxLog.Error("Watchlist has {Count} invalid entries — version is rejected", bad.Count);
            return null;
        }

        var duplicates = list.Entries
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            ctxLog.Error("Duplicated watchlist ids: {Ids}", string.Join(", ", duplicates));
            return null;
        }

        return list;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _writeLock.Dispose();
    }

    private sealed class Root
    {
        public Watchlist Watchlist { get; set; } = new();
    }
}
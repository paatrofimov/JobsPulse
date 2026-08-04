using System.Text.Json;
using System.Text.Json.Serialization;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;

namespace JobsPulse.Host.Infrastructure;

/// <summary>
/// Watchlist в JSON-файле с горячей перезагрузкой и обратной записью (бот тоже правит этот файл).
///
/// Три вещи, ради которых это не просто File.ReadAllText:
///  1. Файл могут прочитать в момент записи — тогда мы оставляем предыдущую ВАЛИДНУЮ версию.
///     Битый watchlist никогда не применяется.
///  2. FileSystemWatcher на Windows шлёт два события на одно сохранение — нужен дебаунс.
///  3. Свои же записи не должны вызывать перезагрузку по кругу.
///
/// На этапе 2 этот класс заменяется реализацией поверх БД. Контракт IWatchlistProvider не меняется.
/// </summary>
public sealed class FileWatchlistProvider : IWatchlistProvider, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    private readonly string _path;
    private readonly ILogger<FileWatchlistProvider> _log;
    private readonly FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<Action<Watchlist>> _listeners = [];
    private readonly Lock _listenerLock = new();

    private volatile Watchlist _current = new();
    private DateTimeOffset _lastReload = DateTimeOffset.MinValue;
    private bool _selfWrite;

    public FileWatchlistProvider(string path, TimeProvider clock, ILogger<FileWatchlistProvider> log)
    {
        _path = Path.GetFullPath(path);
        Clock = clock;
        _log = log;

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
            Entries = list.Entries.Where(e => !e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase)).ToList()
        }, ct);

        return true;
    }

    public async Task<bool> SetEnabledAsync(string entryId, bool enabled, CancellationToken ct)
    {
        if (!_current.Entries.Any(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase))) return false;

        await MutateAsync(list => list with
        {
            Entries = list.Entries
                .Select(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase) ? e with { Enabled = enabled } : e)
                .ToList()
        }, ct);

        return true;
    }

    public Task MarkSeededAsync(string entryId, string filterHash, CancellationToken ct) =>
        MutateAsync(list => list with
        {
            Entries = list.Entries
                .Select(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase)
                    ? e with { SeededAt = Clock.GetUtcNow(), SeededFilterHash = filterHash }
                    : e)
                .ToList()
        }, ct);

    public IDisposable OnChange(Action<Watchlist> listener)
    {
        lock (_listenerLock) _listeners.Add(listener);
        return new Subscription(() => { lock (_listenerLock) _listeners.Remove(listener); });
    }

    private async Task MutateAsync(Func<Watchlist, Watchlist> mutate, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var updated = mutate(_current) with { Version = _current.Version + 1 };

            _selfWrite = true;

            // Пишем через временный файл: читатель никогда не увидит полузаписанный JSON.
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new Root { Watchlist = updated }, Json), ct);
            File.Move(temp, _path, overwrite: true);

            _current = updated;
            Notify(updated);
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

        // Дебаунс: одно сохранение файла порождает несколько событий.
        var now = Clock.GetUtcNow();
        if (now - _lastReload < DebounceWindow) return;
        _lastReload = now;

        var reloaded = Load();
        if (reloaded is null)
        {
            _log.LogError("watchlist.json не прочитался — продолжаю работать на предыдущей версии {Version}",
                _current.Version);
            return;
        }

        _current = reloaded;
        _log.LogInformation("watchlist перезагружен: версия {Version}, записей {Count}",
            reloaded.Version, reloaded.Entries.Count);

        Notify(reloaded);
    }

    private Watchlist? Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Watchlist();

            // Небольшая задержка: редактор мог ещё не отпустить файл.
            var content = ReadWithRetry();
            var root = JsonSerializer.Deserialize<Root>(content, Json);

            return Validate(root?.Watchlist);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Не удалось прочитать {Path}", _path);
            return null;
        }
    }

    private string ReadWithRetry()
    {
        for (var attempt = 0; ; attempt++)
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
                        || string.IsNullOrWhiteSpace(e.Source)
                        || string.IsNullOrWhiteSpace(e.Board))
            .ToList();

        if (bad.Count > 0)
        {
            _log.LogError("В watchlist {Count} некорректных записей — версия отклонена целиком", bad.Count);
            return null;
        }

        var duplicates = list.Entries
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            _log.LogError("Дублирующиеся Id в watchlist: {Ids}", string.Join(", ", duplicates));
            return null;
        }

        return list;
    }

    private void Notify(Watchlist list)
    {
        Action<Watchlist>[] snapshot;
        lock (_listenerLock) snapshot = _listeners.ToArray();

        foreach (var listener in snapshot)
        {
            try { listener(list); }
            catch (Exception ex) { _log.LogWarning(ex, "Подписчик на изменение watchlist упал"); }
        }
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

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

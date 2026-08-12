using System.Text.Json;
using System.Text.Json.Serialization;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Infrastructure;

/// <summary>
/// One-shot import of the retired `watchlist.json` into the database, so an existing installation keeps its boards
/// after the move to PostgreSQL. Runs only while there is no watchlist at all; after that the file is dead weight
/// and the database is the only source of truth.
/// </summary>
public static class LegacyWatchlistImporter
{
    private const string ImportedName = "default";

    public static async Task ImportAsync(
        IWatchlistStorage watchlists,
        string path,
        ILog log,
        CancellationToken ct)
    {
        var ctxLog = log.ForContext(nameof(LegacyWatchlistImporter));

        var existing = await watchlists.GetAllAsync(ct);
        if (existing.Count > 0)
            return;

        if (!File.Exists(path))
        {
            ctxLog.Info("No watchlists yet — create one with /watchlist_add");
            return;
        }

        LegacyRoot? root;
        try
        {
            root = JsonSerializer.Deserialize<LegacyRoot>(
                await File.ReadAllTextAsync(path, ct), JsonSerializerOptionsFactory.Instance);
        }
        catch (Exception ex)
        {
            ctxLog.Error(ex, "Legacy watchlist file {Path} could not be read — nothing is imported", path);
            return;
        }

        var legacy = root?.Watchlist;
        if (legacy is null || legacy.Entries.Count == 0)
        {
            ctxLog.Info("Legacy watchlist file {Path} has no entries — nothing is imported", path);
            return;
        }

        // The legacy file predates users, so the imported watchlist belongs to nobody - a system one.
        var created = await watchlists.CreateAsync(
            ImportedName, legacy.DefaultFilter ?? new FilterSpec(), ownerUserId: null, ct);
        if (created is null)
        {
            ctxLog.Error("Watchlist '{Watchlist}' could not be created — legacy import is skipped", ImportedName);
            return;
        }

        var imported = 0;

        foreach (var entry in legacy.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Board))
                continue;

            var added = await watchlists.AddEntryAsync(
                created.Id,
                entry.Source!,
                entry.Board!,
                string.IsNullOrWhiteSpace(entry.CompanyName) ? entry.Board! : entry.CompanyName!,
                // The legacy file predates source-specific configuration and only ever held slug-addressed boards.
                configuration: null,
                ct);

            if (added is null)
                continue;

            if (!entry.Enabled)
                await watchlists.SetEntryEnabledAsync(added.Id, false, ct);

            imported++;
        }

        ctxLog.Warn(
            "Legacy watchlist imported from {Path} into watchlist '{Watchlist}' (id {Id}): {Count} boards. "
            + "The file is not read again — manage the watchlist through the bot from now on",
            path, created.Name, created.Id, imported);
    }

    private sealed record LegacyRoot
    {
        [JsonPropertyName("watchlist")] public LegacyWatchlist? Watchlist { get; init; }
    }

    private sealed record LegacyWatchlist
    {
        [JsonPropertyName("defaultFilter")] public FilterSpec? DefaultFilter { get; init; }

        [JsonPropertyName("entries")] public List<LegacyEntry> Entries { get; init; } = [];
    }

    private sealed record LegacyEntry
    {
        [JsonPropertyName("Source")] public string? Source { get; init; }

        [JsonPropertyName("Board")] public string? Board { get; init; }

        [JsonPropertyName("companyName")] public string? CompanyName { get; init; }

        [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    }
}

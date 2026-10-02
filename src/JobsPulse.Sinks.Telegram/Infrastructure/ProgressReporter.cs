using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Gathers the traversal progress from the database - the run history of the jobs, the poll stamp of every board, the
/// registry and the discovery checkpoints - and hands it to <see cref="ProgressFormatter"/>. The jobs run on GitHub
/// runners and the bot on another host, so nothing in the memory of this process knows about them. One place, so the
/// admin screen and the <c>/progress</c> command can never drift apart or read different numbers.
/// </summary>
public sealed class ProgressReporter(
    IJobRunHistoryStorage history,
    IWatchlistStorage watchlists,
    IBoardRegistryStorage boardRegistry,
    IBoardPollStateStorage pollState,
    IBoardDiscoveryService discovery,
    TimeProvider clock)
{
    /// <summary>The registry sweep walks the whole registry in about a day - a board older than that is lagging.</summary>
    private static readonly TimeSpan RegistryFreshWindow = TimeSpan.FromDays(1);

    /// <summary>Larger than any registry this system has had; the query is one sequential read either way.</summary>
    private const int RegistryReadLimit = 1_000_000;

    public async Task<string> RenderAsync(BotLanguage language, CancellationToken ct) =>
        ProgressFormatter.Render(await ReadAsync(ct), language);

    public async Task<ProgressSnapshot> ReadAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var runs = await history.ListRecentAsync(ct);
        var plan = WatchlistPlan.Build(await watchlists.GetEnabledAsync(ct));
        var registry = await boardRegistry.ListAsync(null, RegistryReadLimit, ct);
        var stamps = await pollState.LoadAsync(ct);

        var watched = plan.Boards
            .Select(b => (b.SourceId, BoardKey: $"{b.SourceId}/{b.BoardId}"))
            .ToList();

        var watchedKeys = watched
            .Select(b => b.BoardKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var swept = registry
            .Where(b => b.IsActive)
            .Select(b => (b.SourceId, BoardKey: $"{b.SourceId}/{b.BoardId}"))
            .Where(b => !watchedKeys.Contains(b.BoardKey))
            .ToList();

        // Polling stamps its boards with the start of its cycle, and only once the cycle is over - so «fresh» is
        // measured from the start of the last run that has finished, not of one still walking.
        var lastPolling = runs.FirstOrDefault(r =>
            r.Role == JobRunRoles.Polling && r.Outcome != JobRunOutcome.Running);

        return new ProgressSnapshot
        {
            Now = now,
            Runs = runs,
            Watchlist = BoardCoverage.Compute(watched, stamps, lastPolling?.StartedAt ?? now - RegistryFreshWindow),
            Registry = BoardCoverage.Compute(swept, stamps, now - RegistryFreshWindow),
            RegistryInactive = registry.Count(b => !b.IsActive),
            KnownBySource = await boardRegistry.CountBySourceAsync(ct),
            Discovery = await discovery.GetProgressAsync(ct)
        };
    }
}

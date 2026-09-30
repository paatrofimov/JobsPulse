using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>Everything the progress screen shows, read from the database in one go by <c>ProgressReporter</c>.</summary>
public sealed record ProgressSnapshot
{
    public DateTimeOffset Now { get; init; }

    /// <summary>Runs of the one-shot jobs over the history retention, newest first.</summary>
    public IReadOnlyList<JobRun> Runs { get; init; } = [];

    /// <summary>Boards of the enabled watchlists; fresh = stamped since the last finished polling run started.</summary>
    public BoardCoverage Watchlist { get; init; } = BoardCoverage.Empty;

    /// <summary>Active registry boards outside the watchlists; fresh = stamped within the last day.</summary>
    public BoardCoverage Registry { get; init; } = BoardCoverage.Empty;

    /// <summary>Registry boards switched off because they stopped answering.</summary>
    public int RegistryInactive { get; init; }

    /// <summary>Every registry row by source - what discovery has found so far.</summary>
    public IReadOnlyDictionary<string, int> KnownBySource { get; init; } = new Dictionary<string, int>();

    public DiscoveryProgress Discovery { get; init; } = DiscoveryProgress.None;
}

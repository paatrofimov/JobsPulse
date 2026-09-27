namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// What a registry cycle reads before it walks: the enabled watchlists, the registry boards worth polling and when
/// each board was polled last. Loaded once per sweep - reloading the whole registry and poll state before every
/// slice was the bulk of what the job pulled from the database. <see cref="Polled"/> is kept current in memory as
/// slices are stamped.
/// </summary>
public sealed record RegistrySweepState(
    IReadOnlyList<Watchlist> Enabled,
    WatchlistPlan Plan,
    IReadOnlyList<RegisteredBoard> Boards,
    Dictionary<string, DateTimeOffset> Polled);

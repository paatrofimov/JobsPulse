namespace JobsPulse.Core.Infrastructure;

/// <summary>
/// The delivery window - the unit a notification message is built from (`Delivery:GroupChangesWithinMinutes`).
/// Windows are aligned to the epoch rather than to a batch, so the sender and the formatter agree on where one ends
/// without passing anything around: `OutboxDispatcher` holds an item back until its window is closed and
/// `MessageFormatter` groups by the very same boundary. Two different floors would mean a window delivered in
/// halves, which is the whole failure this type exists to rule out.
/// </summary>
public static class DeliveryWindow
{
    public static TimeSpan Of(int minutes) => TimeSpan.FromMinutes(Math.Max(1, minutes));

    /// <summary>The start of the window an instant falls into.</summary>
    public static DateTimeOffset Floor(DateTimeOffset time, TimeSpan window)
    {
        var ticks = time.UtcDateTime.Ticks / window.Ticks * window.Ticks;

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>
    /// The first <paramref name="max"/> of <paramref name="oldestFirst"/> cut back to whole windows: when the cap
    /// falls inside a window, that window is left for the next batch instead of being sent in two messages under the
    /// same header. Only a single window bigger than the cap is taken in parts - there is no other way to send it.
    /// <paramref name="oldestFirst"/> must hold up to <c>max + 1</c> items, the extra one tells whether the cap cut.
    /// </summary>
    public static IReadOnlyList<T> TakeWholeWindows<T>(
        IReadOnlyList<T> oldestFirst,
        Func<T, DateTimeOffset> createdAt,
        int max,
        TimeSpan window)
    {
        if (oldestFirst.Count <= max)
            return oldestFirst;

        var cut = Floor(createdAt(oldestFirst[max]), window);
        var whole = oldestFirst.Take(max).Where(x => Floor(createdAt(x), window) < cut).ToList();

        return whole.Count > 0 ? whole : [.. oldestFirst.Take(max)];
    }
}

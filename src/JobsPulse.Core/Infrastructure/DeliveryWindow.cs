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
}

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Slices a list into pages. The page number is taken by reference and clamped in place on purpose: a button lives
/// in a message that is never deleted, so a tap can always arrive with a page number a shorter list no longer has.
/// Clamping only the slice - and building the keyboard from the number the user tapped - is what renders an empty
/// screen with a «page 4 of 2» label under it; clamping the caller's variable makes the two agree by construction.
/// </summary>
public static class Pager
{
    public static List<T> Slice<T>(IReadOnlyList<T> items, ref int page, out int totalPages) =>
        Slice(items, ref page, KeyboardBuilder.PageSize, out totalPages);

    /// <summary>
    /// A list whose rows are text rather than buttons is not bound by the keyboard size - it takes its own page size.
    /// </summary>
    public static List<T> Slice<T>(IReadOnlyList<T> items, ref int page, int pageSize, out int totalPages)
    {
        totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)pageSize));
        page = Math.Clamp(page, 0, totalPages - 1);

        return [.. items.Skip(page * pageSize).Take(pageSize)];
    }
}

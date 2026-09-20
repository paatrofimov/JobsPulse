namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// How much moved on one board inside a window: vacancies that opened, changed and closed. Counted from
/// <c>seen_vacancy</c> - the outbox is purged within a day and cannot answer it - so the numbers are global to the
/// board, not per watchlist.
///
/// A single vacancy may count twice (opened and then changed in the same window): the measure is events, not posts.
/// That is the point - a board that keeps rewriting its postings is exactly as interesting as one that keeps adding
/// them, and an implausible rate is usually a source bug rather than a hiring spree.
/// </summary>
/// <param name="Months">Length of the window in months, so the rate is comparable between boards.</param>
public sealed record BoardActivity(int Opened, int Changed, int Closed, double Months)
{
    public static readonly BoardActivity None = new(0, 0, 0, 1);

    public int Events => Opened + Changed + Closed;

    /// <summary>Events per month - the indicator the «hottest first» ordering is built on.</summary>
    public double PerMonth => Months <= 0 ? Events : Events / Months;
}

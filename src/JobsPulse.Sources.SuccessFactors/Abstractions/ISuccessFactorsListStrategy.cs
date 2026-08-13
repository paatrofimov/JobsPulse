using JobsPulse.Sources.SuccessFactors.Models;

namespace JobsPulse.Sources.SuccessFactors.Abstractions;

/// <summary>
/// One way of reading the vacancies of a SuccessFactors career site. There is more than one on purpose: SuccessFactors
/// is not one product but several generations of career site with nothing in common but the tenant behind them, and
/// even one generation answers differently depending on how the customer configured it. A new flavour is a new
/// implementation of this, not a branch inside the source.
///
/// Ordered by <see cref="Priority"/>: the source tries the strategies that can serve a board in that order and stops
/// at the first that returns a whole board, so a cheap-and-exact strategy shields an expensive-or-approximate one.
/// </summary>
public interface ISuccessFactorsListStrategy
{
    /// <summary>Short name for the log - the strategies differ in what the data is worth.</summary>
    string Name { get; }

    /// <summary>Lower is tried first.</summary>
    int Priority { get; }

    /// <summary>Whether this strategy can address the board at all.</summary>
    bool CanServe(SuccessFactorsBoardConfig config);

    Task<SuccessFactorsFetch<SuccessFactorsListing>> FetchAsync(
        SuccessFactorsBoardConfig config,
        bool includeDescriptions,
        CancellationToken ct);
}

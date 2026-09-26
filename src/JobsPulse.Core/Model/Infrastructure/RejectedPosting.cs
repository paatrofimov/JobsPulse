namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// A posting whose detail was read and that passed no storage filter: while its list data (<see cref="ListHash"/>)
/// and the filter set (<see cref="FilterHash"/>) stay the same, the verdict cannot change and the detail is not asked
/// again.
/// </summary>
public readonly record struct RejectedPosting(string PostId, string ListHash, string FilterHash);

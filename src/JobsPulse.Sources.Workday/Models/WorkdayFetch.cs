namespace JobsPulse.Sources.Workday.Models;

/// <summary>
/// <see cref="NotFound"/> means the board really is not there (HTTP 404 for a missing site, 422 for a missing
/// tenant). Everything else - 5xx, a timeout, a json that no longer deserializes - is a <see cref="Error"/>, so a
/// broken contract is never reported as a board that stopped existing.
/// </summary>
public readonly record struct WorkdayFetch<T>(T? Value, bool NotFound, string? Error) where T : class
{
    public bool Success => Value is not null;

    public static WorkdayFetch<T> Ok(T value) => new(value, false, null);
    public static WorkdayFetch<T> Missing() => new(null, true, "board is missing");
    public static WorkdayFetch<T> Failure(string error) => new(null, false, error);
}

namespace JobsPulse.Sources.HeadHunter.Models;

/// <summary>
/// <see cref="NotFound"/> means the employer or vacancy really is not there - HTTP 404, or the `bad_argument` the
/// vacancy search answers for an employer id it does not know. Everything else is an <see cref="Error"/>, so a
/// throttled or refused api can never be reported as an employer that stopped existing and close a whole board.
///
/// <see cref="Forbidden"/> is the one failure worth telling apart: it is what the api answers when it decides the
/// caller looks like a bot or when an endpoint has stopped being public. Retrying it is waste, and the fix is a token -
/// see `IHeadHunterAuthorization`.
/// </summary>
public readonly record struct HeadHunterFetch<T>(T? Value, bool NotFound, bool Forbidden, string? Error)
    where T : class
{
    public bool Success => Value is not null;

    public static HeadHunterFetch<T> Ok(T value) => new(value, false, false, null);

    public static HeadHunterFetch<T> Missing() => new(null, true, false, "not found");

    public static HeadHunterFetch<T> Failure(string error) => new(null, false, false, error);

    public static HeadHunterFetch<T> Refused(string error) => new(null, false, true, error);
}

namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// <see cref="NotFound"/> means the site really is not there. Everything else - a customer web application firewall
/// answering 403 or 503, a timeout, a body that is not the document it should be - is an <see cref="Error"/>, so a
/// broken contract is never reported as a board that stopped existing and cannot close a whole board's vacancies.
///
/// <see cref="Truncated"/> is the third answer this source needs and the other ATS do not: the feed is one document
/// per board and a large one arrives cut off - either because the site cut it or because it did not fit the byte
/// budget. Such a list is not the whole board, so it must never be committed, and it is the signal to try the
/// fallback strategy rather than to give up.
/// </summary>
public readonly record struct SuccessFactorsFetch<T>(T? Value, bool NotFound, bool Truncated, string? Error)
    where T : class
{
    public bool Success => Value is not null;

    public static SuccessFactorsFetch<T> Ok(T value) => new(value, false, false, null);

    public static SuccessFactorsFetch<T> Missing() => new(null, true, false, "site is missing");

    public static SuccessFactorsFetch<T> Failure(string error) => new(null, false, false, error);

    public static SuccessFactorsFetch<T> TooLarge(string error) => new(null, false, true, error);
}

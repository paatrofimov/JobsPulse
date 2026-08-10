namespace JobsPulse.Sources.Ashby.Models;

public readonly record struct AshbyFetch<T>(T? Value, bool NotFound, string? Error) where T : class
{
    public bool Success => Value is not null;

    public static AshbyFetch<T> Ok(T value) => new(value, false, null);
    public static AshbyFetch<T> Missing() => new(null, true, "board is missing");
    public static AshbyFetch<T> Failure(string error) => new(null, false, error);
}

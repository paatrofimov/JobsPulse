namespace JobsPulse.Sources.Lever.Models;

public readonly record struct LeverFetch<T>(T? Value, bool NotFound, string? Error) where T : class
{
    public bool Success => Value is not null;

    public static LeverFetch<T> Ok(T value) => new(value, false, null);
    public static LeverFetch<T> Missing() => new(null, true, "board is missing");
    public static LeverFetch<T> Failure(string error) => new(null, false, error);
}

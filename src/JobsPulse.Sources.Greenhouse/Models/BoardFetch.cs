namespace JobsPulse.Sources.Greenhouse.Models;

public readonly record struct BoardFetch<T>(T? Value, bool NotFound, string? Error) where T : class
{
    public bool Success => Value is not null;

    public static BoardFetch<T> Ok(T value) => new(value, false, null);
    public static BoardFetch<T> Missing() => new(null, true, "board is missing");
    public static BoardFetch<T> Failure(string error) => new(null, false, error);
}
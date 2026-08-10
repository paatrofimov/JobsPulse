namespace JobsPulse.Sources.SmartRecruiters.Models;

public readonly record struct SmartRecruitersFetch<T>(T? Value, bool NotFound, string? Error) where T : class
{
    public bool Success => Value is not null;

    public static SmartRecruitersFetch<T> Ok(T value) => new(value, false, null);
    public static SmartRecruitersFetch<T> Missing() => new(null, true, "board is missing");
    public static SmartRecruitersFetch<T> Failure(string error) => new(null, false, error);
}

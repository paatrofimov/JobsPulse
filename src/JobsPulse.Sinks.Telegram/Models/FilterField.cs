namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>One field of a filter - title, location or text - as the wanted and the excluded words it holds.</summary>
public readonly record struct FilterField(IReadOnlyList<string> Wanted, IReadOnlyList<string> Excluded)
{
    public static readonly FilterField Empty = new([], []);

    public bool IsEmpty => Wanted.Count == 0 && Excluded.Count == 0;
}

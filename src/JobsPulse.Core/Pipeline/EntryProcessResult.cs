namespace JobsPulse.Core.Pipeline;

/// <param name="BoardMissing">The board answered 404 - the caller decides what to do with the dead board.</param>
public readonly record struct EntryProcessResult(EntryReport Report, bool BoardMissing)
{
    public static readonly EntryProcessResult Missing = new(EntryReport.Failure(), true);

    public static EntryProcessResult Failed() => new(EntryReport.Failure(), false);
}

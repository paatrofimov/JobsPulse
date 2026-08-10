namespace JobsPulse.Core.Pipeline;

/// <param name="BoardMissing">The board answered 404 - the caller decides what to do with the dead board.</param>
public readonly record struct BoardProcessResult(BoardReport Report, bool BoardMissing)
{
    public static readonly BoardProcessResult Missing = new(BoardReport.Failure(), true);

    public static BoardProcessResult Failed() => new(BoardReport.Failure(), false);
}

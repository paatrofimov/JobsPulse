namespace JobsPulse.Core.Abstractions;

public interface IPollingTrigger
{
    void RequestImmediateRun();

    Task WaitAsync(TimeSpan period, CancellationToken ct);
}

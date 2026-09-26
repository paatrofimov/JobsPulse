namespace JobsPulse.Core.Abstractions;

public interface IPollingTrigger
{
    /// <summary>The polling cycle runs outside this process (GitHub Actions), so a request only asks for a run.</summary>
    bool IsRemote { get; }

    void RequestImmediateRun();

    Task WaitAsync(TimeSpan period, CancellationToken ct);
}

using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>The polite pause every discovery pass keeps between two units of work.</summary>
public static class DiscoveryPause
{
    public static async Task WaitAsync(ILog log, long? milliseconds, string between, CancellationToken ct)
    {
        if (milliseconds is null or <= 0)
            return;

        var pause = TimeSpan.FromMilliseconds(milliseconds.Value);

        log.Debug("Pausing {Pause} between {Between}", pause, between);

        await Task.Delay(pause, ct);
    }
}

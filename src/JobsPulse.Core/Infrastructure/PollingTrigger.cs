using JobsPulse.Core.Abstractions;

namespace JobsPulse.Core.Infrastructure;

public sealed class PollingTrigger : IPollingTrigger
{
    private readonly Lock sync = new();
    private TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool pending;

    public void RequestImmediateRun()
    {
        TaskCompletionSource toComplete;

        lock (sync)
        {
            // Latch the request: a wake-up raised while the cycle is running is not lost,
            // the waiter returns immediately on the next wait.
            if (pending)
                return;

            pending = true;
            toComplete = signal;
        }

        toComplete.TrySetResult();
    }

    public async Task WaitAsync(TimeSpan period, CancellationToken ct)
    {
        Task wake;

        lock (sync)
        {
            if (pending)
            {
                pending = false;
                signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return;
            }

            wake = signal.Task;
        }

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(period, delayCts.Token);
        var completed = await Task.WhenAny(wake, delay);

        if (completed == delay)
        {
            await delay;
            return;
        }

        // The wake-up won -- stop the timer.
        await delayCts.CancelAsync();

        lock (sync)
        {
            pending = false;
            signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        ct.ThrowIfCancellationRequested();
    }
}

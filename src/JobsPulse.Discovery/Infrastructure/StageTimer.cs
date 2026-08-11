using System.Diagnostics;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>
/// A discovery stage takes minutes and produces nothing until it is over, so the log is the only progress bar there
/// is. Every stage announces itself when it starts and reports how long it took when it ends.
/// </summary>
public sealed class StageTimer : IDisposable
{
    private readonly ILog log;
    private readonly string stage;
    private readonly Stopwatch watch;

    private string outcome = "finished";

    private StageTimer(ILog log, string stage)
    {
        this.log = log;
        this.stage = stage;
        watch = Stopwatch.StartNew();

        log.Info("Stage '{Stage}' has started", stage);
    }

    public static StageTimer Start(ILog log, string stage) => new(log, stage);

    public TimeSpan Elapsed => watch.Elapsed;

    /// <summary>Replaces the closing word, so a stage that gave up does not read as a stage that succeeded.</summary>
    public void Outcome(string value) => outcome = value;

    public void Dispose() =>
        log.Info("Stage '{Stage}' {Outcome} in {Elapsed}", stage, outcome, watch.Elapsed);
}

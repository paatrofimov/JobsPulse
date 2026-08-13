using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Infrastructure;

/// <summary>
/// The counters behind <see cref="ITraversalProgressTracker"/>. One lock over a small dictionary: `UnitFinished` is
/// called from every concurrent board task of a cycle, so it has to be cheap and thread-safe, and a lock over a few
/// integers is cheaper than the fetch that just finished by orders of magnitude.
/// </summary>
public sealed class TraversalProgressTracker(TimeProvider clock) : ITraversalProgressTracker
{
    private readonly Lock sync = new();
    private readonly Dictionary<TraversalKind, CycleState> cycles = new();

    public void CycleStarted(TraversalKind kind, IReadOnlyList<TraversalSourceUnits> plan)
    {
        lock (sync)
        {
            var state = new CycleState
            {
                IsRunning = true,
                StartedAt = clock.GetUtcNow(),
                FinishedAt = null
            };

            foreach (var units in plan)
                state.Sources[units.SourceId] = SourceCounters.FromPlan(units);

            cycles[kind] = state;
        }
    }

    public void UnitFinished(TraversalKind kind, string sourceId, bool failed)
    {
        lock (sync)
        {
            if (!cycles.TryGetValue(kind, out var state))
                return;

            // A board of a source the plan did not mention still has to be counted - config may have drifted.
            if (!state.Sources.TryGetValue(sourceId, out var counters))
            {
                counters = new SourceCounters();
                state.Sources[sourceId] = counters;
            }

            counters.Done++;

            if (failed)
                counters.Failed++;
        }
    }

    public void CycleFinished(TraversalKind kind, IReadOnlyList<TraversalSourceUnits> coverage)
    {
        lock (sync)
        {
            if (!cycles.TryGetValue(kind, out var state))
            {
                state = new CycleState { StartedAt = clock.GetUtcNow() };
                cycles[kind] = state;
            }

            state.IsRunning = false;
            state.FinishedAt = clock.GetUtcNow();

            foreach (var units in coverage)
            {
                if (!state.Sources.TryGetValue(units.SourceId, out var counters))
                {
                    counters = new SourceCounters();
                    state.Sources[units.SourceId] = counters;
                }

                counters.DatasetTotal = units.DatasetTotal;
                counters.DatasetCovered = units.DatasetCovered;
            }
        }
    }

    public IReadOnlyList<TraversalProgress> Snapshot()
    {
        lock (sync)
        {
            return
            [
                .. Enum.GetValues<TraversalKind>()
                    .Select(kind => cycles.TryGetValue(kind, out var state)
                        ? state.ToSnapshot(kind)
                        : new TraversalProgress { Kind = kind })
            ];
        }
    }

    private sealed class CycleState
    {
        public bool IsRunning { get; set; }

        public DateTimeOffset? StartedAt { get; init; }

        public DateTimeOffset? FinishedAt { get; set; }

        public Dictionary<string, SourceCounters> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TraversalProgress ToSnapshot(TraversalKind kind) => new()
        {
            Kind = kind,
            IsRunning = IsRunning,
            StartedAt = StartedAt,
            FinishedAt = FinishedAt,
            Sources =
            [
                .. Sources
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value.ToSnapshot(pair.Key))
            ]
        };
    }

    private sealed class SourceCounters
    {
        public int Planned { get; init; }

        public int Done { get; set; }

        public int Failed { get; set; }

        public int DatasetTotal { get; set; }

        public int DatasetCovered { get; set; }

        public static SourceCounters FromPlan(TraversalSourceUnits units) => new()
        {
            Planned = units.Planned,
            DatasetTotal = units.DatasetTotal,
            DatasetCovered = units.DatasetCovered
        };

        public TraversalSourceProgress ToSnapshot(string sourceId) => new()
        {
            SourceId = sourceId,
            Planned = Planned,
            Done = Done,
            Failed = Failed,
            DatasetTotal = DatasetTotal,
            DatasetCovered = DatasetCovered
        };
    }
}

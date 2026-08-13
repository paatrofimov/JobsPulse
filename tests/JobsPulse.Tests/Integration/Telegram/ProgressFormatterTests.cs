using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>
/// The admin progress block. It is rendered from state that is empty most of the time - a fresh process, an
/// unreachable crawl index, a registry nobody has swept yet - so «renders without dividing by zero» is as much the
/// point as the numbers themselves.
/// </summary>
public sealed class ProgressFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Render_should_report_a_running_cycle_with_its_percentages()
    {
        var html = ProgressFormatter.Render(
            [
                new TraversalProgress
                {
                    Kind = TraversalKind.Watchlist,
                    IsRunning = true,
                    StartedAt = Now.AddMinutes(-2),
                    Sources =
                    [
                        Source("greenhouse", planned: 10, done: 4, failed: 1, total: 20, covered: 15),
                        Source("lever", planned: 2, done: 2, failed: 0, total: 4, covered: 4)
                    ]
                }
            ],
            DiscoveryProgress.None,
            new Dictionary<string, int>(),
            Now);

        html.Should().Contain("Watchlist boards");
        html.Should().Contain("running for 2m 0s");
        html.Should().Contain("cycle: <b>6</b> of 12 (50%)");
        html.Should().Contain("errors <b>1</b>");
        html.Should().Contain("dataset: <b>19</b> of 24 boards (79%)");
        html.Should().Contain("greenhouse");
        html.Should().Contain("of 20 (75%)");
        html.Should().Contain("lever");
    }

    [Test]
    public void Render_should_survive_a_process_that_has_traversed_nothing()
    {
        var html = ProgressFormatter.Render(
            [
                new TraversalProgress { Kind = TraversalKind.Watchlist },
                new TraversalProgress { Kind = TraversalKind.Registry }
            ],
            DiscoveryProgress.None,
            new Dictionary<string, int>(),
            Now);

        html.Should().Contain("not started yet");
        html.Should().Contain("nothing mined yet");
        html.Should().NotContain("%");
    }

    [Test]
    public void Render_should_name_a_source_that_is_only_in_the_registry()
    {
        var html = ProgressFormatter.Render(
            [
                new TraversalProgress
                {
                    Kind = TraversalKind.Registry,
                    StartedAt = Now.AddMinutes(-30),
                    FinishedAt = Now.AddMinutes(-25),
                    Sources = [Source("greenhouse", planned: 50, done: 50, failed: 0, total: 900, covered: 300)]
                }
            ],
            DiscoveryProgress.None,
            new Dictionary<string, int> { ["greenhouse"] = 1200, ["workday"] = 40 },
            Now);

        html.Should().Contain("idle, last cycle finished 25m 0s ago");
        html.Should().Contain("registry 1200");
        html.Should().Contain("workday");
        html.Should().Contain("not swept yet, registry 40");
    }

    [Test]
    public void Render_should_report_the_mined_share_of_the_crawl_dataset()
    {
        var discovery = new DiscoveryProgress
        {
            IsRunning = true,
            CollectionsTotal = 100,
            ProcessedBySource = new Dictionary<string, int> { ["greenhouse"] = 25, ["lever"] = 100 }
        };

        var html = ProgressFormatter.Render([], discovery, new Dictionary<string, int>(), Now);

        html.Should().Contain("mining now");
        html.Should().Contain("published indexes: <b>100</b>");
        html.Should().Contain("<b>25</b> of 100 (25%)");
        html.Should().Contain("<b>100</b> of 100 (100%)");
    }

    /// <summary>An index that did not answer must cost the total, not the whole screen.</summary>
    [Test]
    public void Render_should_omit_the_percentage_when_the_index_total_is_unknown()
    {
        var discovery = new DiscoveryProgress
        {
            CollectionsTotal = 0,
            ProcessedBySource = new Dictionary<string, int> { ["greenhouse"] = 25 }
        };

        var html = ProgressFormatter.Render([], discovery, new Dictionary<string, int>(), Now);

        html.Should().Contain("published index count is unavailable");
        html.Should().Contain("<b>25</b>");
        html.Should().NotContain("%");
    }

    private static TraversalSourceProgress Source(
        string sourceId,
        int planned,
        int done,
        int failed,
        int total,
        int covered) => new()
    {
        SourceId = sourceId,
        Planned = planned,
        Done = done,
        Failed = failed,
        DatasetTotal = total,
        DatasetCovered = covered
    };
}

using FluentAssertions;
using JobsPulse.Core.Pipeline;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>Since when the changes a cycle found piled up - the earliest previous traversal of its boards.</summary>
public sealed class CycleReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void EarliestPoll_should_skip_boards_walked_for_the_first_time()
    {
        var polled = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenhouse/a"] = Now.AddHours(-3),
            ["greenhouse/b"] = Now.AddDays(-1)
        };

        CycleReport.EarliestPoll(["GREENHOUSE/A", "greenhouse/b", "greenhouse/new"], polled)
            .Should().Be(Now.AddDays(-1));
        CycleReport.EarliestPoll(["greenhouse/new"], polled).Should().BeNull();
    }

    [Test]
    public void Combine_should_keep_the_earliest_start_of_its_slices()
    {
        var combined = CycleReport.Combine(
        [
            new CycleReport(2, 0, 0, 0, 0, 0, Now.AddDays(-2)),
            new CycleReport(2, 0, 0, 0, 0, 0),
            new CycleReport(2, 0, 0, 0, 0, 0, Now.AddDays(-4))
        ]);

        combined.BoardsProcessed.Should().Be(6);
        combined.ChangesSince.Should().Be(Now.AddDays(-4));
        CycleReport.Combine([]).ChangesSince.Should().BeNull();
    }
}

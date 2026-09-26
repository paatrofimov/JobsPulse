using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Lever.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Lever;

/// <summary>
/// Live tests against the Lever API. The sites are the ones the production logs showed: one no instance knows, and
/// one that exists on the global instance without a single posting.
/// </summary>
public sealed class LeverBoardSourceTests
{
    private static TimeSpan RequestTimeout => TimeSpan.FromMinutes(1);

    private static SourceTarget Target(string boardId) => new()
    {
        SourceId = LeverMapper.SourceId,
        BoardId = boardId,
    };

    /// <summary>404 on every instance is a dead board - it must be reported missing, so its entries get disabled.</summary>
    [Test]
    public async Task TraverseTarget_should_report_a_site_no_instance_knows_as_missing()
    {
        using var host = new LeverTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var result = await host.Source.TraverseTargetAsync(Target("skypointcloud"), cts.Token);

        result.BoardMissing.Should().BeTrue();
        result.IsComplete.Should().BeFalse();
    }

    /// <summary>An empty board on its home instance is complete, and the instance is remembered for the next poll.</summary>
    [Test]
    public async Task TraverseTarget_should_remember_the_instance_of_an_empty_site()
    {
        using var host = new LeverTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var result = await host.Source.TraverseTargetAsync(Target("3pillarglobal"), cts.Token);

        result.Error.Should().BeNull();
        result.BoardMissing.Should().BeFalse();
        result.IsComplete.Should().BeTrue();
        result.Vacancies.Should().BeEmpty();

        host.Regions.TryGet("3pillarglobal", out var region).Should().BeTrue();
        region.Id.Should().Be("global");
    }
}

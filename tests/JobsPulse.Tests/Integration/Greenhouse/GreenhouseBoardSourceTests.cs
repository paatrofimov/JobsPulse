using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Greenhouse;

public sealed class GreenhouseBoardSourceTests : IntegrationTestBase
{
    public static IEnumerable<SourceTarget> Targets =>
    [
        new() { SourceId = "greenhouse", BoardId = "nebius", IncludeDescriptions = false },
        new() { SourceId = "greenhouse", BoardId = "nebius", IncludeDescriptions = true },
    ];

    [TestCaseSource(nameof(Targets))]
    public async Task TraverseTarget_should_traverse(SourceTarget sourceTarget)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);
        var source = GetVacancySource(sourceTarget.SourceId);

        var result = await source.TraverseTargetAsync(
            sourceTarget,
            cts.Token);

        TestContext.Progress.WriteLine(
            $"{sourceTarget}: complete={result.IsComplete}, " +
            $"boardMissing={result.BoardMissing}, " +
            $"vacancies={result.Vacancies.Count}, " +
            $"error={result.Error ?? "-"}");

        foreach (var vacancy in result.Vacancies)
        {
            TestContext.Progress.WriteLine(VacancyToString(vacancy));

            if (sourceTarget.IncludeDescriptions)
                vacancy.Description.Should().NotBeNull();
        }

        result.Error.Should().BeNull();
        result.BoardMissing.Should().BeFalse();
        result.IsComplete.Should().BeTrue();
        result.Vacancies.Should().NotBeEmpty();
    }
}
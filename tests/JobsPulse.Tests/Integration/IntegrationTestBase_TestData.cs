using JobsPulse.Core.Model.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration;

[TestFixture]
public abstract partial class IntegrationTestBase
{
    protected async Task<SourceTraverseResult> FetchRealVacanciesAsync(SourceTarget sourceTarget)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);

        var source = GetVacancySource(sourceTarget.SourceId);

        var result = await source.TraverseTargetAsync(
            sourceTarget,
            cts.Token);

        return result;
    }
}
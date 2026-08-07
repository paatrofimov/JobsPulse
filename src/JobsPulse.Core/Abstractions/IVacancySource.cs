using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IVacancySource
{
    Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct);
}
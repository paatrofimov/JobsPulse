using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IVacancySource
{
    Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct);

    /// <summary>
    /// The source reads per-posting details through `DetailSelector` and honours <see cref="SourceTarget.Rejected"/>
    /// - only then is it worth a query to load it.
    /// </summary>
    bool SelectsDetails => false;
}
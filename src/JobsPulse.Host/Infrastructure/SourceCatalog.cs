using JobsPulse.Core.Abstractions;

namespace JobsPulse.Host.Infrastructure;

public sealed class SourceCatalog(IServiceProvider services, IEnumerable<string> sourceIds) : ISourceCatalog
{
    private readonly HashSet<string> _ids = new(sourceIds, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SourceIds => _ids;

    public IVacancySource? GetSource(string sourceId) =>
        _ids.Contains(sourceId) ? services.GetKeyedService<IVacancySource>(sourceId) : null;

    public IBoardResolver? GetResolver(string sourceId) =>
        _ids.Contains(sourceId) ? services.GetKeyedService<IBoardResolver>(sourceId) : null;
}
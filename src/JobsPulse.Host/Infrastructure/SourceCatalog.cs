using JobsPulse.Core.Abstractions;

namespace JobsPulse.Host.Infrastructure;

/// <summary>
/// Мост между ядром и keyed-регистрациями DI.
/// Именно здесь заканчивается знание системы о конкретных ATS: ядро видит только строковые SourceId.
/// </summary>
public sealed class SourceCatalog(IServiceProvider services, IEnumerable<string> sourceIds) : ISourceCatalog
{
    private readonly HashSet<string> _ids = new(sourceIds, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SourceIds => _ids;

    public IVacancySource? GetSource(string sourceId) =>
        _ids.Contains(sourceId) ? services.GetKeyedService<IVacancySource>(sourceId) : null;

    public IBoardResolver? GetResolver(string sourceId) =>
        _ids.Contains(sourceId) ? services.GetKeyedService<IBoardResolver>(sourceId) : null;
}

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Реестр подключённых ATS. Ядро не знает про конкретные реализации — только про SourceId.
/// Реализация живёт в хосте и резолвит keyed-сервисы из DI.
/// </summary>
public interface ISourceCatalog
{
    IReadOnlyCollection<string> SourceIds { get; }

    IVacancySource? GetSource(string sourceId);

    IBoardResolver? GetResolver(string sourceId);
}

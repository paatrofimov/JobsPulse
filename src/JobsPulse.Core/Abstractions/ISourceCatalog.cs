namespace JobsPulse.Core.Abstractions;

public interface ISourceCatalog
{
    IReadOnlyCollection<string> SourceIds { get; }

    IVacancySource? GetSource(string sourceId);

    IBoardResolver? GetResolver(string sourceId);
}
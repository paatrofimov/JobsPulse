using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record SourceTarget
{
    public required string SourceId { get; init; }
    public required string BoardId { get; init; }

    /// <summary>
    /// Source-specific board parameters as json - see <see cref="BoardCandidate.Configuration"/>. A source that
    /// needs more than <see cref="BoardId"/> reads them from here instead of parsing the id.
    /// </summary>
    public string? Configuration { get; init; }

    /// <summary>Open vacancies already stored for the board, by post id - lets a source skip unchanged details.</summary>
    public IReadOnlyDictionary<string, Vacancy> Known { get; init; } = new Dictionary<string, Vacancy>();

    /// <summary>Some filter reads descriptions, so every posting needs its detail.</summary>
    public bool NeedsDescription { get; init; }

    /// <summary>
    /// Whether a list-only vacancy can pass the storage filters by its title. Null means «maybe» for everything.
    /// </summary>
    public Func<Vacancy, bool>? MayBeStored { get; init; }
}

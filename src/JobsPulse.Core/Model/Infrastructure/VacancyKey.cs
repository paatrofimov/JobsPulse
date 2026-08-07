namespace JobsPulse.Core.Model.Infrastructure;

public readonly record struct VacancyKey(string SourceId, string BoardId, string PostId)
{
    public override string ToString() => $"{SourceId}/{BoardId}/{PostId}";
}
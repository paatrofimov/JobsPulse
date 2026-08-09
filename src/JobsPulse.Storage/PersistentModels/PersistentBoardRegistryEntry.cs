namespace JobsPulse.Storage.PersistentModels;

public class PersistentBoardRegistryEntry
{
    public long Id { get; set; }

    public required string SourceId { get; set; }
    public required string BoardId { get; set; }

    public string? DisplayName { get; set; }
    public string? BoardUrl { get; set; }
    public int JobCount { get; set; }

    public required string DiscoveredVia { get; set; }

    public DateTimeOffset DiscoveredAt { get; set; }
    public DateTimeOffset? LastValidatedAt { get; set; }

    public bool IsActive { get; set; }
}

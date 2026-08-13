namespace JobsPulse.Sources.SuccessFactors.Models;

/// <summary>
/// One tile of the html job list. Only the id and the url are structural - they are attributes the platform writes on
/// every tile. Everything else is a cell the customer chose to put on the tile in Career Site Builder and may simply
/// not be there: SAP's own site renders nothing but the title.
/// </summary>
public sealed record JobTileDto
{
    public required string Id { get; init; }

    public required string Url { get; init; }

    public string? Title { get; init; }

    public string? Location { get; init; }
}

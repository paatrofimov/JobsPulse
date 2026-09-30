namespace JobsPulse.Sources.Greenhouse.Options;

public sealed class GreenhouseOptions
{
    public const string SectionName = "Sources:Greenhouse";

    public string BaseUrl { get; set; } = "https://boards-api.greenhouse.io/v1/boards/";

    /// <summary>
    /// Descriptions on every poll. Off: they are asked only while a watchlist filter has description rules.
    /// </summary>
    public bool IncludeContentOnPoll { get; set; }

    public int MaxSlugGuesses { get; set; } = 8;
}
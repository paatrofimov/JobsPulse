namespace JobsPulse.Sources.Greenhouse.Options;

public sealed class GreenhouseOptions
{
    public const string SectionName = "Sources:Greenhouse";

    public string BaseUrl { get; set; } = "https://boards-api.greenhouse.io/v1/boards/";

    public bool IncludeContentOnPoll { get; set; }

    public int MaxSlugGuesses { get; set; } = 8;
}
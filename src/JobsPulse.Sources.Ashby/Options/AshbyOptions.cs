namespace JobsPulse.Sources.Ashby.Options;

public sealed class AshbyOptions
{
    public const string SectionName = "Sources:Ashby";

    public string BaseUrl { get; set; } = "https://api.ashbyhq.com/posting-api/job-board/";

    /// <summary>Unlisted postings must not be shown publicly, so they are dropped by default.</summary>
    public bool IncludeUnlisted { get; set; }

    public int MaxSlugGuesses { get; set; } = 8;
}

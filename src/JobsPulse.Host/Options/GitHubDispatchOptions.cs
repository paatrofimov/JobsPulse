namespace JobsPulse.Host.Options;

public sealed class GitHubDispatchOptions
{
    public const string SectionName = "GitHubDispatch";

    public string? Token { get; set; }

    public string Repository { get; set; } = "paatrofimov/JobsPulse";

    public string Workflow { get; set; } = "polling.yml";

    public string Ref { get; set; } = "master";

    public int CooldownSeconds { get; set; } = 120;
}

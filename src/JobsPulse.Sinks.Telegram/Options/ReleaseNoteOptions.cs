namespace JobsPulse.Sinks.Telegram.Options;

public sealed class ReleaseNoteOptions
{
    public const string SectionName = "ReleaseNote";

    /// <summary>The deployed version, the short commit sha.</summary>
    public string? Version { get; set; }

    /// <summary>One change per line, the commit subjects since the previous deploy.</summary>
    public string? Changes { get; set; }
}

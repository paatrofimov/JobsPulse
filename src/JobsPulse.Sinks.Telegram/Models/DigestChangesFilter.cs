namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>Which vacancies a digest's changes screen lists.</summary>
public enum DigestChangesFilter
{
    /// <summary>Every vacancy that changed.</summary>
    All,

    /// <summary>The vacancies that opened in the period, whatever happened to them afterwards.</summary>
    Opened,

    /// <summary>The vacancies that closed, aged out or were filtered away in the period.</summary>
    Closed
}

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// Where a job search is aimed - the slices of the shortlist. Finer than <see cref="LocationRegion"/> where it
/// matters for applying (Europe split in two, the USA apart from the rest of the Americas). A vacancy may be in
/// several areas: «EMEA» is both halves of Europe.
/// </summary>
public enum FocusArea
{
    WesternEurope,
    EasternEurope,
    Usa,
    Asia
}

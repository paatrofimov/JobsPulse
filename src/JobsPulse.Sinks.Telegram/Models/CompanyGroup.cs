using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// One slice of a company list - the unit the list is rendered by. The label is the source the companies are watched
/// through or the region they hire in, depending on the grouping the reader chose; the list itself does not care.
/// </summary>
public sealed record CompanyGroup(string Label, IReadOnlyList<WatchlistEntry> Entries);

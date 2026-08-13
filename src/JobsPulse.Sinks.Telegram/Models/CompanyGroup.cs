using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>The companies of one source inside a company list - the unit the list is rendered by.</summary>
public sealed record CompanyGroup(string SourceId, IReadOnlyList<WatchlistEntry> Entries);

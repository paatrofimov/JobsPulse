using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Storage.PersistentModels;

public static class PersistencyExtensions
{
    public static Vacancy ToDomainModel(this PersistentSeenVacancy persistentVacancy)
    {
        return new Vacancy()
        {
            BoardId = persistentVacancy.BoardId,
            SourceId = persistentVacancy.SourceId,
            Url = persistentVacancy.Url,
            PostId = persistentVacancy.PostId,
            Title = persistentVacancy.Title,
            Offices = persistentVacancy.Offices,
            Location = persistentVacancy.Location,
            FirstSeenAt = persistentVacancy.FirstSeenAt,
            FirstPublishedAt = persistentVacancy.FirstPublishedAt,
            UpdatedAt = persistentVacancy.UpdatedAt,
            ContentHash = persistentVacancy.ContentHash,
            GroupId = persistentVacancy.GroupId,
        };
    }

    public static OutboxItem ToDomainModel(this PersistentOutboxItem persistentItem)
    {
        return new OutboxItem
        {
            Id = persistentItem.Id,
            DedupKey = persistentItem.DedupKey,
            ChangeKind = persistentItem.ChangeKind,
            CompanyName = persistentItem.CompanyName,
            WatchlistId = persistentItem.WatchlistId,
            WatchlistName = persistentItem.WatchlistName,
            Discovered = persistentItem.Discovered,
            Attempts = persistentItem.Attempts,
            Vacancy = JsonSerializer.Deserialize<Vacancy>(
                          persistentItem.VacancyPayload, JsonSerializerOptionsFactory.Instance
                      )
                      ?? throw new ArgumentNullException(nameof(persistentItem.VacancyPayload)),
        };
    }

    public static Watchlist ToDomainModel(this PersistentWatchlist persistentWatchlist)
    {
        return new Watchlist
        {
            Id = persistentWatchlist.Id,
            Name = persistentWatchlist.Name,
            Enabled = persistentWatchlist.Enabled,
            Filter = persistentWatchlist.Filter.ToFilterSpec(),
            IntervalMinutesOverride = persistentWatchlist.IntervalMinutesOverride,
            // Manual boards first, discovered ones after them - every listing inherits this order.
            Entries =
            [
                .. persistentWatchlist.Entries
                    .OrderBy(e => e.Origin)
                    .ThenBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
                    .Select(e => e.ToDomainModel())
            ]
        };
    }

    public static WatchlistEntry ToDomainModel(this PersistentWatchlistEntry persistentEntry)
    {
        return new WatchlistEntry
        {
            Id = persistentEntry.Id,
            WatchlistId = persistentEntry.WatchlistId,
            VacancySourceId = persistentEntry.SourceId,
            BoardId = persistentEntry.BoardId,
            CompanyName = persistentEntry.CompanyName,
            Configuration = persistentEntry.Configuration,
            Enabled = persistentEntry.Enabled,
            Origin = persistentEntry.Origin
        };
    }

    public static WatchlistMatch ToDomainModel(this PersistentWatchlistVacancy persistentMatch)
    {
        return new WatchlistMatch
        {
            WatchlistId = persistentMatch.WatchlistId,
            SourceId = persistentMatch.SourceId,
            BoardId = persistentMatch.BoardId,
            PostId = persistentMatch.PostId,
            ContentHash = persistentMatch.ContentHash,
            FilterHash = persistentMatch.FilterHash ?? string.Empty
        };
    }

    public static string ToJson(this FilterSpec filter) =>
        JsonSerializer.Serialize(filter, JsonSerializerOptionsFactory.Instance);

    private static FilterSpec ToFilterSpec(this string json) =>
        JsonSerializer.Deserialize<FilterSpec>(json, JsonSerializerOptionsFactory.Instance) ?? new FilterSpec();
}

using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;

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
            Attempts = persistentItem.Attempts,
            Vacancy = JsonSerializer.Deserialize<Vacancy>(
                          persistentItem.VacancyPayload, JsonSerializerOptionsFactory.Instance
                      )
                      ?? throw new ArgumentNullException(nameof(persistentItem.VacancyPayload)),
        };
    }
}
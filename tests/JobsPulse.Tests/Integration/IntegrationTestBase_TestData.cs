using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Storage.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration;

[TestFixture]
public abstract partial class IntegrationTestBase
{
    protected async Task<SourceTraverseResult> FetchRealVacanciesAsync(SourceTarget sourceTarget)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);

        var source = GetVacancySource(sourceTarget.SourceId);

        var result = await source.TraverseTargetAsync(
            sourceTarget,
            cts.Token);

        return result;
    }

    protected static OutboxItem[] MapVacanciesToMockOutboxes(IReadOnlyList<Vacancy> vacancies, VacancyChangeKind? changeKind = null)
    {
        return vacancies
            .Select((v, ind) =>
            {
                changeKind ??= (VacancyChangeKind)Random.Shared.Next(0, 3);
                var hash = VacancyHasher.Compute(v);
                var companyIndex = Random.Shared.Next(0, 3);

                v = v with { ContentHash = hash };

                return new OutboxItem()
                {
                    Id = ind,
                    ChangeKind = changeKind.Value,
                    CompanyName = $"TEST COMPANY {companyIndex}",
                    Vacancy = v,
                    DedupKey = v.ToDedupKey(changeKind.Value, hash),
                };
            })
            .ToArray();
    }

    protected async Task<IReadOnlyList<Vacancy>> ReadAllSeenVacanciesAsync()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        return [.. db.SeenVacancies.Select(o => o.ToDomainModel())];
    }

    protected async Task<IReadOnlyList<Vacancy>> ReadClosedSeenVacanciesAsync()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        return [.. db.SeenVacancies.Where(v => v.ClosedAt != null).Select(o => o.ToDomainModel())];
    }

    protected async Task<IReadOnlyList<OutboxItem>> ReadAllOutboxesAsync()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        return [.. db.Outbox.Select(o => o.ToDomainModel())];
    }

    protected async Task<(StateCommitResult, IReadOnlyList<Vacancy>, IReadOnlyList<OutboxItem>)> InsertNewVacanciesAsync(int take)
    {
        var sourceTarget = new SourceTarget() { SourceId = "greenhouse", BoardId = "nebius", IncludeDescriptions = true };
        var result = await FetchRealVacanciesAsync(sourceTarget);

        var vacancies = result.Vacancies.Take(take).ToList();

        var outboxes = MapVacanciesToMockOutboxes(vacancies, changeKind: VacancyChangeKind.New);

        var stateCommit = BuildStateCommit(outboxes, vacancies);

        var commitResult = await CommitAsync(stateCommit);

        return (commitResult, vacancies, outboxes);
    }

    protected async Task<Vacancy> ReadVacancyAsync(VacancyKey key)
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        return db.SeenVacancies.Single(v =>
                v.SourceId == key.SourceId && v.BoardId == key.BoardId && v.PostId == key.PostId
            )
            .ToDomainModel();
    }

    protected async Task<StateCommitResult> CommitAsync(StateCommit stateCommit)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);
        return await StateStore.CommitAsync(stateCommit, cts.Token);
    }

    protected static StateCommit BuildStateCommit(OutboxItem[] notifications, IReadOnlyList<Vacancy> vacancies)
    {
        return new StateCommit()
        {
            SourceId = "greenhouse",
            BoardId = "nebius",
            Notifications = notifications,
            Upserts = vacancies,
            ClosedPostIds = [],
        };
    }
}
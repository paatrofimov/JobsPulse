using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Storage;

public sealed class StateStoreTests : IntegrationTestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox, seen_vacancy RESTART IDENTITY CASCADE");
    }

    [Test]
    public async Task StateStore_should_commit_new_vacancies()
    {
        var (stateCommitResult, vacancies, outboxes) = await InsertNewVacanciesAsync(take: 10);

        stateCommitResult.UpsertVacanciesAffectedRows.Should().Be(10);
        stateCommitResult.OutboxAffectedRows.Should().Be(10);
        stateCommitResult.CloseVacanciesAffectedRows.Should().Be(0);

        var allSeenVacancies = await ReadAllSeenVacanciesAsync();
        AssertEquivalentVacancies(allSeenVacancies, vacancies);

        foreach (var vacancy in allSeenVacancies)
        {
            vacancy.ContentHash.Should().BeEquivalentTo(VacancyHasher.Compute(vacancy));
        }

        var closedSeenVacancies = await ReadClosedSeenVacanciesAsync();
        closedSeenVacancies.Should().HaveCount(0);

        var allOutboxes = await ReadAllOutboxesAsync();
        AssertEquivalencyOutboxes(allOutboxes, outboxes);

        foreach (var outboxItem in allOutboxes)
        {
            var vacancy = outboxItem.Vacancy;
            vacancy.ContentHash.Should().BeEquivalentTo(VacancyHasher.Compute(vacancy));
        }
    }


    [Test]
    public async Task StateStore_should_update_vacancy()
    {
        var (_, vacancies, __) = await InsertNewVacanciesAsync(take: 10);

        var firstVacancy = vacancies[0];
        firstVacancy = firstVacancy with
        {
            Title = "Updated title", Location = "Updated location",
            Url = "Updated url", Offices = ["Updated office"],
        };

        var changeOutboxes = MapVacanciesToMockOutboxes([firstVacancy], changeKind: VacancyChangeKind.Updated);

        var updateStateCommit = BuildStateCommit(changeOutboxes, [firstVacancy]);

        var updateResult = await CommitAsync(updateStateCommit);

        updateResult.UpsertVacanciesAffectedRows.Should().Be(1);
        updateResult.OutboxAffectedRows.Should().Be(1);
        updateResult.CloseVacanciesAffectedRows.Should().Be(0);

        var closedSeenVacancies = await ReadClosedSeenVacanciesAsync();
        closedSeenVacancies.Should().HaveCount(0);

        var updatedVacancy = await ReadVacancyAsync(firstVacancy.Key);

        updatedVacancy.Should().BeEquivalentTo(firstVacancy, ConfigureVacancyComparisonOpts);

        updatedVacancy.ContentHash.Should().NotBeEquivalentTo(firstVacancy.ContentHash);
        updatedVacancy.ContentHash.Should().BeEquivalentTo(VacancyHasher.Compute(updatedVacancy));

        updatedVacancy.UpdatedAt!.Value.Should().BeAfter(firstVacancy.UpdatedAt!.Value);

        var allOutboxes = await ReadAllOutboxesAsync();
        var vacancyOutboxes = allOutboxes.Where(o => o.Vacancy.Key == firstVacancy.Key).ToArray();
        vacancyOutboxes.Should().HaveCount(2);
        vacancyOutboxes.Should().ContainSingle(o => o.ChangeKind == VacancyChangeKind.New);
        vacancyOutboxes.Should().ContainSingle(o => o.ChangeKind == VacancyChangeKind.Updated);
    }
}
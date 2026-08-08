using FluentAssertions;
using JobsPulse.Core.Model.Domain;
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
        var (stateCommitResult, vacancies, outboxes) = await InsertVacanciesAsync(take: 10, changeKind: VacancyChangeKind.New);

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
            outboxItem.ChangeKind.Should().Be(VacancyChangeKind.New);

            var vacancy = outboxItem.Vacancy;
            vacancy.ContentHash.Should().BeEquivalentTo(VacancyHasher.Compute(vacancy));
        }
    }

    [Test]
    public async Task StateStore_should_update_vacancy()
    {
        var (_, vacancies, __) = await InsertVacanciesAsync(take: 10, changeKind: VacancyChangeKind.New);

        var firstVacancy = vacancies[0];
        firstVacancy = firstVacancy with
        {
            Title = "Updated title", Location = "Updated location",
            Url = "Updated url", Offices = ["Updated office"],
        };

        var changeOutboxes = MapVacanciesToMockOutboxes([firstVacancy], changeKind: VacancyChangeKind.Updated);

        var updateStateCommit = BuildStateCommit(changeOutboxes, vacancies: [firstVacancy], closed: []);

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

    [Test]
    public async Task Should_deduplicate_unchanged_outboxes_and_vacancies()
    {
        var (_, vacancies, __) = await InsertVacanciesAsync(take: 1, changeKind: VacancyChangeKind.Updated);

        var firstVacancy = vacancies[0];

        var now = DateTimeOffset.UtcNow;

        // changing fields excluded from hash calculation
        firstVacancy = firstVacancy with { Description = "new description", UpdatedAt = now, FirstSeenAt = now, FirstPublishedAt = now };

        var outboxes = MapVacanciesToMockOutboxes([firstVacancy], changeKind: VacancyChangeKind.Updated);

        var stateCommit = BuildStateCommit(outboxes, vacancies: [firstVacancy], closed: []);

        var commitResult = await CommitAsync(stateCommit);

        commitResult.UpsertVacanciesAffectedRows.Should().Be(0);
        commitResult.CloseVacanciesAffectedRows.Should().Be(0);
        commitResult.OutboxAffectedRows.Should().Be(0);

        var allOutboxes = await ReadAllOutboxesAsync();
        allOutboxes.Should().HaveCount(1); // updated outbox should be deduplicated because vacancy ans change kind are the same
        allOutboxes.Single().ChangeKind.Should().Be(VacancyChangeKind.Updated);

        var seenVacancies = await ReadAllSeenVacanciesAsync();
        seenVacancies.Should().HaveCount(1);
        var vacancy = seenVacancies.Single();
        vacancy.UpdatedAt.Should().BeBefore(now); // and vacancy should not be updated because hash is the same
    }

    [Test]
    public async Task Should_close_vacancies()
    {
        var (_, vacancies, __) = await InsertVacanciesAsync(take: 1, changeKind: VacancyChangeKind.New);

        var firstVacancy = vacancies[0];
        var outboxes = MapVacanciesToMockOutboxes([firstVacancy], changeKind: VacancyChangeKind.Closed);

        var stateCommit = BuildStateCommit(outboxes, vacancies: [], closed: [firstVacancy.PostId]);

        var commitResult = await CommitAsync(stateCommit);

        commitResult.UpsertVacanciesAffectedRows.Should().Be(0);
        commitResult.CloseVacanciesAffectedRows.Should().Be(1);
        commitResult.OutboxAffectedRows.Should().Be(1);

        var seenVacancies = await ReadAllSeenVacanciesAsync();
        seenVacancies.Should().HaveCount(1);

        var allOutboxes = await ReadAllOutboxesAsync();
        allOutboxes.Should().HaveCount(2);
        allOutboxes.Should().ContainSingle(o => o.ChangeKind == VacancyChangeKind.New);
        allOutboxes.Should().ContainSingle(o => o.ChangeKind == VacancyChangeKind.Closed);

        // same closure should be deduplicated via closed_at filter
        var commitResult2 = await CommitAsync(stateCommit);
        commitResult2.UpsertVacanciesAffectedRows.Should().Be(0);
        commitResult2.CloseVacanciesAffectedRows.Should().Be(0);
        commitResult2.OutboxAffectedRows.Should().Be(0);
    }

    [Test]
    public async Task Should_load_seen_unclosed_vacancies()
    {
        var (_, vacancies, __) = await InsertVacanciesAsync(take: 4, changeKind: VacancyChangeKind.New);

        var dict = await LoadSeenVacanciesAsync();
        dict.Values.Should().HaveCount(4);

        var stateCommit = BuildStateCommit(notifications: [], vacancies: [], closed: [vacancies[0].PostId, vacancies[2].PostId]);
        await CommitAsync(stateCommit);

        var dict2 = await LoadSeenVacanciesAsync();
        dict2.Values.Should().HaveCount(2);
        dict2.Values.Should().ContainSingle(v => v.PostId == vacancies[1].PostId);
        dict2.Values.Should().ContainSingle(v => v.PostId == vacancies[3].PostId);

        var stateCommit2 = BuildStateCommit(notifications: [], vacancies: [], closed: [vacancies[1].PostId]);
        await CommitAsync(stateCommit2);

        var dict3 = await LoadSeenVacanciesAsync();
        dict3.Values.Should().HaveCount(1);
    }
}
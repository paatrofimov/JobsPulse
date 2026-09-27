using System.Text.Json;
using FluentAssertions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Storage;

/// <summary>A leased batch never carries half a delivery window - on a real database, with real `created_at` stamps.</summary>
public sealed class OutboxStorageTests : IntegrationTestBase
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly DateTimeOffset W1 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset W2 = W1 + Window;
    private static readonly DateTimeOffset Later = W1.AddDays(1);

    [SetUp]
    public async Task SetUp()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox RESTART IDENTITY CASCADE");
    }

    [Test]
    public async Task ReadAndLease_should_leave_a_window_the_cap_cuts_through_for_the_next_batch()
    {
        await InsertAsync(W1, W1.AddMinutes(1), W2, W2.AddMinutes(1), W2.AddMinutes(2));

        var first = await OutboxStorage.ReadAndLeaseAsync(3, Later, Window, CancellationToken.None);
        var second = await OutboxStorage.ReadAndLeaseAsync(3, Later, Window, CancellationToken.None);
        var third = await OutboxStorage.ReadAndLeaseAsync(3, Later, Window, CancellationToken.None);

        first.Select(i => i.CreatedAt).Should().Equal(W1, W1.AddMinutes(1));
        second.Select(i => i.CreatedAt).Should().Equal(W2, W2.AddMinutes(1), W2.AddMinutes(2));
        third.Should().BeEmpty();
    }

    [Test]
    public async Task ReadAndLease_should_split_only_a_single_window_bigger_than_the_cap()
    {
        await InsertAsync(W2, W2.AddMinutes(1), W2.AddMinutes(2));

        var first = await OutboxStorage.ReadAndLeaseAsync(2, Later, Window, CancellationToken.None);

        first.Should().HaveCount(2);
    }

    [Test]
    public async Task ReadAndLease_should_keep_the_open_window_behind_the_cutoff()
    {
        await InsertAsync(W1, W2.AddMinutes(1));

        var leased = await OutboxStorage.ReadAndLeaseAsync(10, W2, Window, CancellationToken.None);

        leased.Select(i => i.CreatedAt).Should().Equal(W1);
    }

    private async Task InsertAsync(params DateTimeOffset[] stamps)
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();

        for (var i = 0; i < stamps.Length; i++)
        {
            var vacancy = new Vacancy
            {
                SourceId = "test",
                BoardId = "board",
                PostId = $"post-{i}",
                Title = $"Engineer {i}",
                Url = $"https://example.com/{i}"
            };

            db.Outbox.Add(new PersistentOutboxItem
            {
                DedupKey = $"test|{i}",
                ChangeKind = VacancyChangeKind.New,
                CompanyName = "Acme",
                VacancyPayload = JsonSerializer.Serialize(vacancy, JsonSerializerOptionsFactory.Instance),
                CreatedAt = stamps[i]
            });
        }

        await db.SaveChangesAsync();
    }
}

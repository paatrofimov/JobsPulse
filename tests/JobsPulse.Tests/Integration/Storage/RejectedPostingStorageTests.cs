using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Storage;

public sealed class RejectedPostingStorageTests : IntegrationTestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE rejected_posting RESTART IDENTITY");
    }

    [Test]
    public async Task Save_should_upsert_and_remove_by_the_difference()
    {
        await RejectedPostings.SaveAsync(
            "test", "board",
            [new RejectedPosting("1", "list-1", "filters"), new RejectedPosting("2", "list-2", "filters")],
            [],
            CancellationToken.None);

        await RejectedPostings.SaveAsync(
            "test", "board",
            [new RejectedPosting("2", "list-2b", "filters")],
            ["1"],
            CancellationToken.None);

        var stored = await RejectedPostings.LoadAsync("test", "board", CancellationToken.None);

        stored.Should().ContainSingle();
        stored["2"].Should().Be(new RejectedPosting("2", "list-2b", "filters"));
    }

    [Test]
    public async Task Load_should_read_one_board_only()
    {
        await RejectedPostings.SaveAsync(
            "test", "other", [new RejectedPosting("1", "list", "filters")], [], CancellationToken.None);

        var stored = await RejectedPostings.LoadAsync("test", "board", CancellationToken.None);

        stored.Should().BeEmpty();
    }
}

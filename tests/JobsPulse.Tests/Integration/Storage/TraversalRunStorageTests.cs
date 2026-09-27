using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Storage;

/// <summary>What the dispatchers of both jobs read to decide whether the open window is still being filled.</summary>
public sealed class TraversalRunStorageTests : IntegrationTestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE traversal_run RESTART IDENTITY");
    }

    [Test]
    public async Task AnyActive_should_see_a_started_run_until_it_is_finished()
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-3);

        var id = await TraversalRuns.StartAsync("Registry", CancellationToken.None);
        var whileWalking = await TraversalRuns.AnyActiveAsync(since, CancellationToken.None);

        await TraversalRuns.FinishAsync(id, CancellationToken.None);
        var afterFinish = await TraversalRuns.AnyActiveAsync(since, CancellationToken.None);

        whileWalking.Should().BeTrue();
        afterFinish.Should().BeFalse();
    }

    [Test]
    public async Task AnyActive_should_ignore_a_run_whose_heartbeat_is_stale()
    {
        await TraversalRuns.StartAsync("Polling", CancellationToken.None);

        // A runner killed mid-walk never finishes its row.
        var active = await TraversalRuns.AnyActiveAsync(DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        active.Should().BeFalse();
    }
}

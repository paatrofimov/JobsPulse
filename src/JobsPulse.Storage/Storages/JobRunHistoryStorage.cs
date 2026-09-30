using System.Text.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;

namespace JobsPulse.Storage.Storages;

/// <summary>Pure EF over a small table: a dozen runs a day, a month of them kept.</summary>
internal class JobRunHistoryStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    TimeProvider clock) : IJobRunHistoryStorage
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>An error is a log line, not a stack trace - the log holds the rest.</summary>
    private const int MaxErrorLength = 500;

    public async Task<long> StartAsync(string role, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = clock.GetUtcNow();
        var threshold = now - Retention;

        await db.JobRuns
            .Where(x => x.StartedAt < threshold)
            .ExecuteDeleteAsync(ct);

        var row = new PersistentJobRun
        {
            Role = role,
            StartedAt = now,
            Outcome = JobRunOutcome.Running.ToString()
        };

        db.JobRuns.Add(row);
        await db.SaveChangesAsync(ct);

        return row.Id;
    }

    public async Task FinishAsync(
        long id,
        JobRunOutcome outcome,
        string? error,
        JobRunSummary summary,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = clock.GetUtcNow();
        var outcomeName = outcome.ToString();
        var trimmed = error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;
        var json = JsonSerializer.Serialize(summary, JsonSerializerOptionsFactory.Instance);

        await db.JobRuns
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.FinishedAt, now)
                .SetProperty(x => x.Outcome, outcomeName)
                .SetProperty(x => x.Error, trimmed)
                .SetProperty(x => x.Summary, json), ct);
    }

    public async Task<IReadOnlyList<JobRun>> ListRecentAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var threshold = clock.GetUtcNow() - Retention;

        var rows = await db.JobRuns
            .AsNoTracking()
            .Where(x => x.StartedAt >= threshold)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(ct);

        return rows.Select(ToDomainModel).ToList();
    }

    private static JobRun ToDomainModel(PersistentJobRun row) => new()
    {
        Id = row.Id,
        Role = row.Role,
        StartedAt = row.StartedAt,
        FinishedAt = row.FinishedAt,
        Outcome = Enum.TryParse<JobRunOutcome>(row.Outcome, out var outcome) ? outcome : JobRunOutcome.Failed,
        Error = row.Error,
        Summary = row.Summary is { } json
            ? JsonSerializer.Deserialize<JobRunSummary>(json, JsonSerializerOptionsFactory.Instance) ?? JobRunSummary.None
            : JobRunSummary.None
    };
}

using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobsPulse.Storage.Storages;

internal class DiscoveryCheckpointStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    NpgsqlDataSource dataSource) : IDiscoveryCheckpointStorage
{
    public async Task<IReadOnlyList<DiscoveryCheckpoint>> GetLatestAsync(int count, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.DiscoveryCheckpoints
            .AsNoTracking()
            .OrderByDescending(x => x.Iteration)
            .Take(Math.Max(1, count))
            .ToListAsync(ct);

        return rows.Select(ToDomainModel).ToList();
    }

    public async Task SaveAsync(DiscoveryCheckpoint checkpoint, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();

        // The same iteration is rewritten every few minutes, so the write is an upsert on the iteration number.
        cmd.CommandText =
            """
            INSERT INTO discovery_checkpoint
                (iteration, is_full, started_at, updated_at, finished_at, started_from_collection_id,
                 resume_from_collection_id, collections_total, collections_done, collections_processed,
                 collections_failed, records_seen, tokens_found, boards_added)
            VALUES
                (@iteration, @full, @started_at, @updated_at, @finished_at, @started_from,
                 @resume_from, @total, @done, @processed,
                 @failed, @records, @tokens, @added)
            ON CONFLICT (iteration) DO UPDATE SET
                is_full                   = EXCLUDED.is_full,
                updated_at                = EXCLUDED.updated_at,
                finished_at               = EXCLUDED.finished_at,
                started_from_collection_id = EXCLUDED.started_from_collection_id,
                resume_from_collection_id = EXCLUDED.resume_from_collection_id,
                collections_total         = EXCLUDED.collections_total,
                collections_done          = EXCLUDED.collections_done,
                collections_processed     = EXCLUDED.collections_processed,
                collections_failed        = EXCLUDED.collections_failed,
                records_seen              = EXCLUDED.records_seen,
                tokens_found              = EXCLUDED.tokens_found,
                boards_added              = EXCLUDED.boards_added
            """;

        cmd.Parameters.AddWithValue("iteration", checkpoint.Iteration);
        cmd.Parameters.AddWithValue("full", checkpoint.Full);
        cmd.Parameters.AddWithValue("started_at", checkpoint.StartedAt);
        cmd.Parameters.AddWithValue("updated_at", checkpoint.UpdatedAt);
        cmd.Parameters.AddWithValue("finished_at", (object?)checkpoint.FinishedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("started_from", (object?)checkpoint.StartedFromCollectionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("resume_from", (object?)checkpoint.ResumeFromCollectionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("total", checkpoint.CollectionsTotal);
        cmd.Parameters.AddWithValue("done", checkpoint.CollectionsDone);
        cmd.Parameters.AddWithValue("processed", checkpoint.CollectionsProcessed);
        cmd.Parameters.AddWithValue("failed", checkpoint.CollectionsFailed);
        cmd.Parameters.AddWithValue("records", checkpoint.RecordsSeen);
        cmd.Parameters.AddWithValue("tokens", checkpoint.TokensFound);
        cmd.Parameters.AddWithValue("added", checkpoint.BoardsAdded);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DiscoveryCheckpoint ToDomainModel(PersistentDiscoveryCheckpoint row) => new()
    {
        Iteration = row.Iteration,
        Full = row.IsFull,
        StartedAt = row.StartedAt,
        UpdatedAt = row.UpdatedAt,
        FinishedAt = row.FinishedAt,
        StartedFromCollectionId = row.StartedFromCollectionId,
        ResumeFromCollectionId = row.ResumeFromCollectionId,
        CollectionsTotal = row.CollectionsTotal,
        CollectionsDone = row.CollectionsDone,
        CollectionsProcessed = row.CollectionsProcessed,
        CollectionsFailed = row.CollectionsFailed,
        RecordsSeen = row.RecordsSeen,
        TokensFound = row.TokensFound,
        BoardsAdded = row.BoardsAdded
    };
}

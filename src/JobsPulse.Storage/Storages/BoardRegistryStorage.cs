using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Storage.Storages;

internal class BoardRegistryStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    NpgsqlDataSource dataSource,
    ILog log) : IBoardRegistryStorage
{
    private readonly ILog ctxLog = log.ForContext<BoardRegistryStorage>();

    public async Task<int> UpsertAsync(IReadOnlyList<RegisteredBoard> boards, CancellationToken ct)
    {
        if (boards.Count == 0)
            return 0;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();

        // Insert wins only for unknown boards; a known one keeps its discovery origin and just gets refreshed.
        cmd.CommandText =
            """
            INSERT INTO board_registry
                (source_id, board_id, display_name, board_url, job_count,
                 discovered_via, discovered_at, last_validated_at, is_active)
            VALUES
                (@source, @board, @name, @url, @jobs,
                 @via, @discovered_at, @validated_at, @active)
            ON CONFLICT (source_id, board_id) DO UPDATE SET
                display_name      = EXCLUDED.display_name,
                board_url         = EXCLUDED.board_url,
                job_count         = EXCLUDED.job_count,
                last_validated_at = EXCLUDED.last_validated_at,
                is_active         = EXCLUDED.is_active
            RETURNING (xmax = 0) AS inserted
            """;

        var inserted = 0;

        foreach (var board in boards)
        {
            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("source", board.SourceId);
            cmd.Parameters.AddWithValue("board", board.BoardId);
            cmd.Parameters.AddWithValue("name", (object?)board.DisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("url", (object?)board.BoardUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("jobs", board.JobCount);
            cmd.Parameters.AddWithValue("via", board.DiscoveredVia);
            cmd.Parameters.AddWithValue("discovered_at", board.DiscoveredAt);
            cmd.Parameters.AddWithValue("validated_at", (object?)board.LastValidatedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("active", board.IsActive);

            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is true)
                inserted++;
        }

        ctxLog.Info("Board registry upsert: {Total} boards, {Inserted} new", boards.Count, inserted);

        return inserted;
    }

    public async Task<IReadOnlyList<RegisteredBoard>> ListAsync(string? sourceId, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var query = db.BoardRegistry.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(sourceId))
            query = query.Where(x => x.SourceId == sourceId);

        var rows = await query
            .OrderByDescending(x => x.JobCount)
            .ThenBy(x => x.BoardId)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(ToDomainModel).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> CountBySourceAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var counts = await db.BoardRegistry
            .AsNoTracking()
            .GroupBy(x => x.SourceId)
            .Select(g => new
            {
                Source = g.Key,
                Count = g.Count()
            })
            .ToListAsync(ct);

        return counts.ToDictionary(x => x.Source, x => x.Count, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyCollection<string>> GetKnownBoardIdsAsync(string sourceId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.BoardRegistry
            .AsNoTracking()
            .Where(x => x.SourceId == sourceId)
            .Select(x => x.BoardId)
            .ToListAsync(ct);
    }

    public async Task<bool> RemoveAsync(string sourceId, string boardId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var affected = await db.BoardRegistry
            .Where(x => x.SourceId == sourceId && x.BoardId == boardId)
            .ExecuteDeleteAsync(ct);

        return affected > 0;
    }

    public async Task<IReadOnlyCollection<string>> GetProcessedCrawlsAsync(string sourceId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.CrawlIndexState
            .AsNoTracking()
            .Where(x => x.SourceId == sourceId)
            .Select(x => x.CollectionId)
            .ToListAsync(ct);
    }

    public async Task MarkCrawlProcessedAsync(CrawlIndexProgress progress, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText =
            """
            INSERT INTO crawl_index_state
                (source_id, collection_id, records_seen, tokens_found, boards_added, processed_at)
            VALUES
                (@source, @collection, @records, @tokens, @added, @processed_at)
            ON CONFLICT (source_id, collection_id) DO UPDATE SET
                records_seen = EXCLUDED.records_seen,
                tokens_found = EXCLUDED.tokens_found,
                boards_added = EXCLUDED.boards_added,
                processed_at = EXCLUDED.processed_at
            """;

        cmd.Parameters.AddWithValue("source", progress.SourceId);
        cmd.Parameters.AddWithValue("collection", progress.CollectionId);
        cmd.Parameters.AddWithValue("records", progress.RecordsSeen);
        cmd.Parameters.AddWithValue("tokens", progress.TokensFound);
        cmd.Parameters.AddWithValue("added", progress.BoardsAdded);
        cmd.Parameters.AddWithValue("processed_at", progress.ProcessedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static RegisteredBoard ToDomainModel(PersistentBoardRegistryEntry row) => new()
    {
        SourceId = row.SourceId,
        BoardId = row.BoardId,
        DisplayName = row.DisplayName,
        BoardUrl = row.BoardUrl,
        JobCount = row.JobCount,
        DiscoveredVia = row.DiscoveredVia,
        DiscoveredAt = row.DiscoveredAt,
        LastValidatedAt = row.LastValidatedAt,
        IsActive = row.IsActive
    };
}

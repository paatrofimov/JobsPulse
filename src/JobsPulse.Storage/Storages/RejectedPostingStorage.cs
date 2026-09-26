using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobsPulse.Storage.Storages;

internal class RejectedPostingStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    NpgsqlDataSource dataSource,
    TimeProvider clock) : IRejectedPostingStorage
{
    public async Task<IReadOnlyDictionary<string, RejectedPosting>> LoadAsync(
        string sourceId,
        string boardId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.RejectedPostings
            .AsNoTracking()
            .Where(x => x.SourceId == sourceId && x.BoardId == boardId)
            .Select(x => new RejectedPosting(x.PostId, x.ListHash, x.FilterHash))
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.PostId, StringComparer.Ordinal);
    }

    public async Task SaveAsync(
        string sourceId,
        string boardId,
        IReadOnlyList<RejectedPosting> upserts,
        IReadOnlyList<string> removals,
        CancellationToken ct)
    {
        if (upserts.Count == 0 && removals.Count == 0)
            return;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        if (removals.Count > 0)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                DELETE FROM rejected_posting
                WHERE source_id = @source AND board_id = @board AND post_id = ANY(@post_ids)
                """;

            cmd.Parameters.AddWithValue("source", sourceId);
            cmd.Parameters.AddWithValue("board", boardId);
            cmd.Parameters.AddWithValue("post_ids", removals.ToArray());

            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string sql =
            """
            INSERT INTO rejected_posting (source_id, board_id, post_id, list_hash, filter_hash, rejected_at)
            VALUES (@source, @board, @post, @list_hash, @filter_hash, @now)
            ON CONFLICT (source_id, board_id, post_id) DO UPDATE SET
                list_hash   = EXCLUDED.list_hash,
                filter_hash = EXCLUDED.filter_hash,
                rejected_at = EXCLUDED.rejected_at
            """;

        var now = clock.GetUtcNow();

        await NpgsqlBatchExecutor.ExecuteAsync(connection, tx, sql, upserts, (p, rejected) =>
        {
            p.AddWithValue("source", sourceId);
            p.AddWithValue("board", boardId);
            p.AddWithValue("post", rejected.PostId);
            p.AddWithValue("list_hash", rejected.ListHash);
            p.AddWithValue("filter_hash", rejected.FilterHash);
            p.AddWithValue("now", now);
        }, ct);

        await tx.CommitAsync(ct);
    }
}

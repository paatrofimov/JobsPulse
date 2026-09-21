using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace JobsPulse.Storage.Storages;

internal class BoardPollStateStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    NpgsqlDataSource dataSource) : IBoardPollStateStorage
{
    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> LoadAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.BoardPollState
            .AsNoTracking()
            .Select(x => new
            {
                x.SourceId,
                x.BoardId,
                x.LastPolledAt
            })
            .ToListAsync(ct);

        var map = new Dictionary<string, DateTimeOffset>(rows.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
            map[$"{row.SourceId}/{row.BoardId}"] = row.LastPolledAt;

        return map;
    }

    public async Task StampAsync(IReadOnlyList<BoardPollStamp> stamps, CancellationToken ct)
    {
        if (stamps.Count == 0)
            return;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();

        // One statement for the whole slice: a cycle stamps up to a few hundred boards and a command per board
        // would cost more round trips than the fetch that produced them.
        cmd.CommandText =
            """
            INSERT INTO board_poll_state (source_id, board_id, last_polled_at)
            SELECT s, b, p
            FROM unnest(@sources, @boards, @stamps) AS t(s, b, p)
            ON CONFLICT (source_id, board_id) DO UPDATE SET
                last_polled_at = EXCLUDED.last_polled_at
            """;

        cmd.Parameters.Add(
            new NpgsqlParameter("sources", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = stamps.Select(s => s.SourceId).ToArray()
            });

        cmd.Parameters.Add(
            new NpgsqlParameter("boards", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = stamps.Select(s => s.BoardId).ToArray()
            });

        cmd.Parameters.Add(
            new NpgsqlParameter("stamps", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            {
                Value = stamps.Select(s => s.PolledAt).ToArray()
            });

        await cmd.ExecuteNonQueryAsync(ct);
    }
}

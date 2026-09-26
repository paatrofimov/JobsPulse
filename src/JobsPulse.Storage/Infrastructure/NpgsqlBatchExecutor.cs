using Npgsql;

namespace JobsPulse.Storage.Infrastructure;

internal static class NpgsqlBatchExecutor
{
    private const int ChunkSize = 500;

    /// <summary>
    /// Runs one parameterized statement per row, <see cref="ChunkSize"/> statements per round trip. Row-by-row
    /// execution cost one network round trip per vacancy, which dominates a commit against a remote database.
    /// Returns the total number of affected rows.
    /// </summary>
    public static async Task<int> ExecuteAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string sql,
        IReadOnlyList<T> rows,
        Action<NpgsqlParameterCollection, T> bind,
        CancellationToken ct)
    {
        var affected = 0;

        foreach (var chunk in rows.Chunk(ChunkSize))
        {
            await using var batch = new NpgsqlBatch(connection, tx);

            foreach (var row in chunk)
            {
                var command = new NpgsqlBatchCommand(sql);
                bind(command.Parameters, row);
                batch.BatchCommands.Add(command);
            }

            affected += await batch.ExecuteNonQueryAsync(ct);
        }

        return affected;
    }
}

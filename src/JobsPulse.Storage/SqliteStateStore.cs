using System.Text.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Pipeline;
using Microsoft.Data.Sqlite;

namespace JobsPulse.Storage;

/// <summary>
/// Состояние борда + постановка уведомлений. Ключевое свойство — CommitAsync атомарен:
/// либо вакансия помечена виденной И уведомление в очереди, либо ни то, ни другое.
/// </summary>
public sealed class SqliteStateStore(SqliteConnectionFactory factory, TimeProvider clock) : IStateStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Schema.Sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, SeenVacancy>> LoadSeenAsync(
        string sourceId, string boardKey, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT external_id, content_hash, title, location, url, updated_at, last_seen_at
            FROM seen_vacancy
            WHERE source_id = $source AND board_key = $board AND closed_at IS NULL
            """;
        cmd.Parameters.AddWithValue("$source", sourceId);
        cmd.Parameters.AddWithValue("$board", boardKey);

        var result = new Dictionary<string, SeenVacancy>(StringComparer.Ordinal);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var externalId = reader.GetString(0);
            result[externalId] = new SeenVacancy
            {
                ExternalId = externalId,
                ContentHash = reader.GetString(1),
                Title = reader.GetString(2),
                Location = reader.IsDBNull(3) ? null : reader.GetString(3),
                Url = reader.GetString(4),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(5)),
                LastSeenAt = DateTimeOffset.Parse(reader.GetString(6))
            };
        }

        return result;
    }

    public async Task CommitAsync(StateCommit commit, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToString("O");

        await using var connection = await factory.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await UpsertAsync(connection, tx, commit, now, ct);
        await CloseAsync(connection, tx, commit, now, ct);
        await EnqueueAsync(connection, tx, commit, now, ct);

        await tx.CommitAsync(ct);
    }

    private static async Task UpsertAsync(
        SqliteConnection connection, SqliteTransaction tx, StateCommit commit, string now, CancellationToken ct)
    {
        if (commit.Upserts.Count == 0) return;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO seen_vacancy
                (source_id, board_key, external_id, group_id, content_hash, title, location, url,
                 updated_at, first_seen_at, last_seen_at, closed_at)
            VALUES ($source, $board, $external, $group, $hash, $title, $location, $url,
                    $updated, $now, $now, NULL)
            ON CONFLICT (source_id, board_key, external_id) DO UPDATE SET
                content_hash = excluded.content_hash,
                title        = excluded.title,
                location     = excluded.location,
                url          = excluded.url,
                updated_at   = excluded.updated_at,
                last_seen_at = excluded.last_seen_at,
                closed_at    = NULL
            """;

        foreach (var v in commit.Upserts)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$source", v.SourceId);
            cmd.Parameters.AddWithValue("$board", v.BoardKey);
            cmd.Parameters.AddWithValue("$external", v.ExternalId);
            cmd.Parameters.AddWithValue("$group", (object?)v.GroupId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hash", VacancyHasher.Compute(v));
            cmd.Parameters.AddWithValue("$title", v.Title);
            cmd.Parameters.AddWithValue("$location", (object?)v.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$url", v.Url);
            cmd.Parameters.AddWithValue("$updated", v.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$now", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task CloseAsync(
        SqliteConnection connection, SqliteTransaction tx, StateCommit commit, string now, CancellationToken ct)
    {
        if (commit.Closed.Count == 0) return;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            UPDATE seen_vacancy SET closed_at = $now
            WHERE source_id = $source AND board_key = $board AND external_id = $external
            """;

        foreach (var externalId in commit.Closed)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$source", commit.SourceId);
            cmd.Parameters.AddWithValue("$board", commit.BoardKey);
            cmd.Parameters.AddWithValue("$external", externalId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task EnqueueAsync(
        SqliteConnection connection, SqliteTransaction tx, StateCommit commit, string now, CancellationToken ct)
    {
        if (commit.Notifications.Count == 0) return;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        // ON CONFLICT DO NOTHING по dedup_key: повторная постановка того же изменения игнорируется.
        cmd.CommandText =
            """
            INSERT INTO outbox (dedup_key, chat_id, silent, kind, company_name, payload, status, created_at)
            VALUES ($dedup, $chat, $silent, $kind, $company, $payload, 'pending', $now)
            ON CONFLICT (dedup_key) DO NOTHING
            """;

        foreach (var item in commit.Notifications)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$dedup", item.DedupKey);
            cmd.Parameters.AddWithValue("$chat", item.ChatId);
            cmd.Parameters.AddWithValue("$silent", item.Silent ? 1 : 0);
            cmd.Parameters.AddWithValue("$kind", item.Kind.ToString());
            cmd.Parameters.AddWithValue("$company", item.CompanyName);
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item.Vacancy, Json));
            cmd.Parameters.AddWithValue("$now", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}

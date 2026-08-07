using System.Text.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace JobsPulse.Storage.Storages;

/// <summary>
/// Состояние борда + постановка уведомлений. Ключевое свойство — CommitAsync атомарен:
/// либо вакансия помечена виденной И уведомление в очереди, либо ни то, ни другое.
/// </summary>
internal class StateStore(
    IDbContextFactory<JobsPulseDbContext> factory,
    NpgsqlDataSource dataSource,
    TimeProvider clock) : IStateStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, Vacancy>> LoadSeenAsync(
        string sourceId,
        string boardId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.SeenVacancies
            .AsNoTracking()
            .Where(x =>
                x.SourceId == sourceId &&
                x.BoardId == boardId &&
                x.ClosedAt == null)
            .Select(x => new
            {
                x.PostId,
                x.VacancyPayload
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            x => x.PostId,
            x => JsonSerializer.Deserialize<Vacancy>(x.VacancyPayload, Json)
                 ?? throw new InvalidOperationException($"Failed to deserialize vacancy {x.PostId}"),
            StringComparer.Ordinal);
    }

    public async Task CommitAsync(StateCommit commit, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await UpsertSeenVacanciesAsync(connection, tx, commit, now, ct);
        await CloseSeenVacanciesAsync(connection, tx, commit, now, ct);
        await EnqueueOutboxAsync(connection, tx, commit, now, ct);

        await tx.CommitAsync(ct);
    }

    private static async Task UpsertSeenVacanciesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (commit.Upserts.Count == 0)
            return;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO seen_vacancy
                (source_id, board_id, post_id, group_id, content_hash, title, location, url,
                 first_seen_at, closed_at)
            VALUES
                (@source, @board, @post, @group, @hash, @title, @location, @url,
                 @now, NULL)
            ON CONFLICT (source_id, board_id, post_id) DO UPDATE SET
                content_hash = EXCLUDED.content_hash,
                title        = EXCLUDED.title,
                location     = EXCLUDED.location,
                url          = EXCLUDED.url,
                last_seen_at = EXCLUDED.last_seen_at,
                closed_at    = NULL
            """;

        foreach (var v in commit.Upserts)
        {
            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("source", v.SourceId);
            cmd.Parameters.AddWithValue("board", v.BoardId);
            cmd.Parameters.AddWithValue("post", v.PostId);
            cmd.Parameters.AddWithValue("group", (object?)v.GroupId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("hash", VacancyHasher.Compute(v));
            cmd.Parameters.AddWithValue("title", v.Title);
            cmd.Parameters.AddWithValue("location", (object?)v.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("url", v.Url);
            cmd.Parameters.AddWithValue("now", now);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task CloseSeenVacanciesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (commit.ClosedPostIds.Count == 0)
            return;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            UPDATE seen_vacancy
            SET closed_at = @now
            WHERE source_id = @source
              AND board_id = @board
              AND post_id = ANY(@post_ids)
            """;

        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("source", commit.SourceId);
        cmd.Parameters.AddWithValue("board", commit.BoardId);
        cmd.Parameters.AddWithValue(
            "post_ids",
            commit.ClosedPostIds.ToArray());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnqueueOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (commit.Notifications.Count == 0)
            return;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText =
            """
            INSERT INTO outbox
                (dedup_key, kind, company_name, payload, status, created_at)
            VALUES
                (@dedup, @kind, @company, @payload, 'pending', @now)
            ON CONFLICT (dedup_key) DO NOTHING
            """;

        foreach (var item in commit.Notifications)
        {
            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("dedup", item.DedupKey);
            cmd.Parameters.AddWithValue("kind", item.ChangeKind.ToString());
            cmd.Parameters.AddWithValue("company", item.CompanyName);

            cmd.Parameters.Add(
                new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
                {
                    Value = JsonSerializer.Serialize(item.Vacancy, Json)
                });

            cmd.Parameters.AddWithValue("now", now);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
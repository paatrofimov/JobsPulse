using System.Text.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Storage.Storages;

internal class StateStore(
    IDbContextFactory<JobsPulseDbContext> factory,
    NpgsqlDataSource dataSource,
    TimeProvider clock,
    ILog log) : IStateStore
{
    private readonly ILog ctxLog = log.ForContext<StateStore>();

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
            .ToListAsync(ct);

        return rows.ToDictionary(
            x => x.PostId,
            x => x.ToDomainModel(),
            StringComparer.Ordinal
        );
    }

    public async Task<StateCommitResult> CommitAsync(StateCommit commit, CancellationToken ct)
    {
        if (commit.Upserts.Count == 0 &&
            commit.ClosedPostIds.Count == 0 &&
            commit.Notifications.Count == 0)
        {
            ctxLog.Warn("Nothing to commit");
            return StateCommitResult.Empty;
        }

        var now = clock.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var upserts = await UpsertSeenVacanciesAsync(connection, tx, commit, now, ct);
        var closures = await CloseSeenVacanciesAsync(connection, tx, commit, now, ct);
        var outboxes = await EnqueueOutboxAsync(connection, tx, commit, now, ct);

        await tx.CommitAsync(ct);

        return new StateCommitResult(upserts, closures, outboxes);
    }

    private async Task<int> UpsertSeenVacanciesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (commit.Upserts.Count == 0)
            return 0;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText =
            """
            INSERT INTO seen_vacancy
                (source_id, board_id, post_id, group_id, content_hash,
                 title, location, url,
                 first_seen_at, first_published_at, updated_at, closed_at, offices)
            VALUES
                (@source, @board, @post, @group, @hash,
                 @title, @location, @url,
                 @first_seen_at, @first_published_at, @now, NULL, @offices)
            ON CONFLICT (source_id, board_id, post_id) DO UPDATE SET
                group_id           = EXCLUDED.group_id,
                content_hash       = EXCLUDED.content_hash,
                title              = EXCLUDED.title,
                location           = EXCLUDED.location,
                url                = EXCLUDED.url,
                updated_at         = EXCLUDED.updated_at,
                closed_at          = NULL,
                offices            = EXCLUDED.offices,
                first_published_at = COALESCE(
                    seen_vacancy.first_published_at,
                    EXCLUDED.first_published_at)
            WHERE seen_vacancy.content_hash IS DISTINCT FROM EXCLUDED.content_hash
            """;

        var affectedRowsTotal = 0;

        foreach (var vacancy in commit.Upserts)
        {
            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("source", vacancy.SourceId);
            cmd.Parameters.AddWithValue("board", vacancy.BoardId);
            cmd.Parameters.AddWithValue("post", vacancy.PostId);
            cmd.Parameters.AddWithValue("group", (object?)vacancy.GroupId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("hash", VacancyHasher.Compute(vacancy));
            cmd.Parameters.AddWithValue("title", vacancy.Title);
            cmd.Parameters.AddWithValue("location", (object?)vacancy.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("url", vacancy.Url);
            cmd.Parameters.AddWithValue("first_seen_at", (object?)vacancy.FirstSeenAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue(
                "first_published_at",
                (object?)vacancy.FirstPublishedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("now", now);
            cmd.Parameters.AddWithValue("offices", vacancy.Offices.ToArray());

            var affectedRows = await cmd.ExecuteNonQueryAsync(ct);

            ctxLog.Debug(
                "Upsertion to seen_vacancy table of vacancy '{VacancyKey}' affected {Affected} rows",
                vacancy.Key,
                affectedRows);

            affectedRowsTotal += affectedRows;
        }

        return affectedRowsTotal;
    }

    private async Task<int> CloseSeenVacanciesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (commit.ClosedPostIds.Count == 0)
            return 0;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText =
            """
            UPDATE seen_vacancy
            SET closed_at = @now
            WHERE source_id = @source
              AND board_id = @board
              AND post_id = ANY(@post_ids)
              AND closed_at IS NULL
            """;

        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("source", commit.SourceId);
        cmd.Parameters.AddWithValue("board", commit.BoardId);
        cmd.Parameters.AddWithValue("post_ids", commit.ClosedPostIds.ToArray());

        var affectedRows = await cmd.ExecuteNonQueryAsync(ct);

        ctxLog.Debug(
            "Closing {ClosedCount} vacancies in seen_vacancy affected {AffectedRows} rows",
            commit.ClosedPostIds.Count,
            affectedRows);

        return affectedRows;
    }

    private async Task<int> EnqueueOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (commit.Notifications.Count == 0)
            return 0;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText =
            """
            INSERT INTO outbox
            (
                dedup_key,
                change_kind,
                company_name,
                vacancy_payload,
                status,
                attempts,
                created_at
            )
            VALUES
            (
                @dedup,
                @kind,
                @company,
                @payload,
                @status,
                0,
                @now
            )
            ON CONFLICT (dedup_key) DO NOTHING
            """;

        var affectedRowsTotal = 0;

        foreach (var item in commit.Notifications)
        {
            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("dedup", item.DedupKey);
            cmd.Parameters.AddWithValue("kind", (int)item.ChangeKind);
            cmd.Parameters.AddWithValue("company", item.CompanyName);

            cmd.Parameters.Add(
                new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
                {
                    Value = JsonSerializer.Serialize(item.Vacancy, JsonSerializerOptionsFactory.Instance)
                });

            cmd.Parameters.AddWithValue(
                "status",
                (int)PersistentOutboxStatus.Pending);

            cmd.Parameters.AddWithValue("now", now);

            var affectedRows = await cmd.ExecuteNonQueryAsync(ct);

            ctxLog.Debug(
                "Insertion to outbox table of vacancy '{VacancyKey}' affected {Affected} rows",
                item.Vacancy.Key,
                affectedRows);

            affectedRowsTotal += affectedRows;
        }

        return affectedRowsTotal;
    }
}
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

    public async Task<IReadOnlyList<WatchlistMatch>> LoadMatchesAsync(
        string sourceId,
        string boardId,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.WatchlistVacancies
            .AsNoTracking()
            .Where(x => x.SourceId == sourceId && x.BoardId == boardId)
            .ToListAsync(ct);

        return [.. rows.Select(x => x.ToDomainModel())];
    }

    public async Task<IReadOnlyDictionary<long, int>> CountMatchesByWatchlistAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var counts = await db.WatchlistVacancies
            .AsNoTracking()
            .GroupBy(x => x.WatchlistId)
            .Select(g => new
            {
                WatchlistId = g.Key,
                Count = g.Count()
            })
            .ToListAsync(ct);

        return counts.ToDictionary(x => x.WatchlistId, x => x.Count);
    }

    public async Task<IReadOnlyList<SeenVacancySnapshot>> LoadAllAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.SeenVacancies
            .AsNoTracking()
            .OrderBy(x => x.SourceId)
            .ThenBy(x => x.BoardId)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        return rows
            .Select(x => new SeenVacancySnapshot
            {
                Vacancy = x.ToDomainModel(),
                ClosedAt = x.ClosedAt
            })
            .ToList();
    }

    public async Task<IReadOnlyList<Vacancy>> LoadMatchedVacanciesAsync(
        long watchlistId,
        int limit,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // The match layer holds the keys only, so the payload comes from the open seen_vacancy rows behind them.
        var rows = await db.WatchlistVacancies
            .AsNoTracking()
            .Where(m => m.WatchlistId == watchlistId)
            .Join(
                db.SeenVacancies.AsNoTracking().Where(v => v.ClosedAt == null),
                m => new
                {
                    m.SourceId,
                    m.BoardId,
                    m.PostId
                },
                v => new
                {
                    v.SourceId,
                    v.BoardId,
                    v.PostId
                },
                (_, v) => v)
            .OrderByDescending(v => v.FirstPublishedAt ?? v.UpdatedAt)
            .ThenBy(v => v.Title)
            .Take(limit)
            .ToListAsync(ct);

        return [.. rows.Select(x => x.ToDomainModel())];
    }

    public async Task<IReadOnlyList<SeenVacancySnapshot>> LoadStaleFilterAsync(
        IReadOnlyList<string> knownFilterHashes,
        int limit,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.SeenVacancies
            .AsNoTracking()
            .Where(x =>
                x.ClosedAt == null &&
                (x.FilterHash == null || !knownFilterHashes.Contains(x.FilterHash)))
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(x => new SeenVacancySnapshot
            {
                Vacancy = x.ToDomainModel(),
                ClosedAt = x.ClosedAt
            })
            .ToList();
    }

    public async Task<int> DeleteAsync(IReadOnlyList<VacancyKey> keys, CancellationToken ct)
    {
        return await ExecuteByBoardAsync(
            keys,
            """
            DELETE FROM seen_vacancy
            WHERE source_id = @source AND board_id = @board AND post_id = ANY(@post_ids)
            """,
            _ => { },
            ct);
    }

    public async Task<int> SetFilterHashAsync(IReadOnlyList<VacancyKey> keys, string filterHash, CancellationToken ct)
    {
        return await ExecuteByBoardAsync(
            keys,
            """
            UPDATE seen_vacancy
            SET filter_hash = @filter_hash
            WHERE source_id = @source AND board_id = @board AND post_id = ANY(@post_ids)
            """,
            cmd => cmd.Parameters.AddWithValue("filter_hash", filterHash),
            ct);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountOpenByBoardAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var counts = await db.SeenVacancies
            .AsNoTracking()
            .Where(x => x.ClosedAt == null)
            .GroupBy(x => new
            {
                x.SourceId,
                x.BoardId
            })
            .Select(g => new
            {
                g.Key.SourceId,
                g.Key.BoardId,
                Count = g.Count()
            })
            .ToListAsync(ct);

        return counts.ToDictionary(
            x => $"{x.SourceId}/{x.BoardId}",
            x => x.Count,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Composite keys cannot be passed as one array - statements are grouped per board instead.</summary>
    private async Task<int> ExecuteByBoardAsync(
        IReadOnlyList<VacancyKey> keys,
        string sql,
        Action<NpgsqlCommand> bindExtra,
        CancellationToken ct)
    {
        if (keys.Count == 0)
            return 0;

        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var affectedTotal = 0;

        foreach (var board in keys.GroupBy(k => (k.SourceId, k.BoardId)))
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            cmd.Parameters.AddWithValue("source", board.Key.SourceId);
            cmd.Parameters.AddWithValue("board", board.Key.BoardId);
            cmd.Parameters.AddWithValue("post_ids", board.Select(k => k.PostId).ToArray());
            bindExtra(cmd);

            affectedTotal += await cmd.ExecuteNonQueryAsync(ct);
        }

        return affectedTotal;
    }

    public async Task<PurgeResult> PurgeAllAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var outboxDeleted = await ExecuteAsync(connection, tx, "DELETE FROM outbox", ct);
        // The match layer is derived state - it is wiped with the vacancies, the watchlists themselves are config.
        var matchesDeleted = await ExecuteAsync(connection, tx, "DELETE FROM watchlist_vacancy", ct);
        var vacanciesDeleted = await ExecuteAsync(connection, tx, "DELETE FROM seen_vacancy", ct);
        var boardsDeleted = await ExecuteAsync(connection, tx, "DELETE FROM board_registry", ct);
        var crawIndexStateDeleted = await ExecuteAsync(connection, tx, "DELETE FROM crawl_index_state", ct);

        await tx.CommitAsync(ct);

        ctxLog.Warn(
            "Purged state: {Vacancies} seen_vacancy rows, {Matches} watchlist_vacancy rows, {Outbox} outbox rows, "
            + "{Boards} board_registry rows, {CrawlIndexState} crawl_index_state rows",
            vacanciesDeleted,
            matchesDeleted,
            outboxDeleted,
            boardsDeleted,
            crawIndexStateDeleted);

        return new PurgeResult(vacanciesDeleted, outboxDeleted, boardsDeleted, crawIndexStateDeleted, matchesDeleted);
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string sql,
        CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<StateCommitResult> CommitAsync(StateCommit commit, CancellationToken ct)
    {
        if (commit.IsEmpty)
            return StateCommitResult.Empty;

        var now = clock.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var upserts = await UpsertSeenVacanciesAsync(connection, tx, commit, now, ct);
        var closures = await CloseSeenVacanciesAsync(connection, tx, commit, now, ct);

        // The match layer and the notifications must land together with the state that produced them.
        var matches = await UpsertMatchesAsync(connection, tx, commit, now, ct);
        matches += await RemoveMatchesAsync(connection, tx, commit, ct);

        var outboxes = await EnqueueOutboxAsync(connection, tx, commit, now, ct);

        await tx.CommitAsync(ct);

        return new StateCommitResult(upserts, closures, outboxes, matches);
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
                (source_id, board_id, post_id, group_id, content_hash, filter_hash,
                 title, location, url,
                 first_seen_at, first_published_at, updated_at, closed_at, offices)
            VALUES
                (@source, @board, @post, @group, @hash, @filter_hash,
                 @title, @location, @url,
                 @first_seen_at, @first_published_at, @updated_at, NULL, @offices)
            ON CONFLICT (source_id, board_id, post_id) DO UPDATE SET
                group_id           = EXCLUDED.group_id,
                content_hash       = EXCLUDED.content_hash,
                filter_hash        = EXCLUDED.filter_hash,
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
            cmd.Parameters.AddWithValue("filter_hash", (object?)commit.FilterHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("title", vacancy.Title);
            cmd.Parameters.AddWithValue("location", (object?)vacancy.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("url", vacancy.Url);
            cmd.Parameters.AddWithValue("first_seen_at", (object?)vacancy.FirstSeenAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue(
                "first_published_at",
                (object?)vacancy.FirstPublishedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("updated_at", (object?)vacancy.UpdatedAt ?? DBNull.Value);
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

    private async Task<int> UpsertMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (commit.MatchUpserts.Count == 0)
            return 0;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText =
            """
            INSERT INTO watchlist_vacancy
                (watchlist_id, source_id, board_id, post_id, content_hash, filter_hash, matched_at)
            VALUES
                (@watchlist, @source, @board, @post, @hash, @filter_hash, @matched_at)
            ON CONFLICT (watchlist_id, source_id, board_id, post_id) DO UPDATE SET
                content_hash = EXCLUDED.content_hash,
                filter_hash  = EXCLUDED.filter_hash,
                matched_at   = EXCLUDED.matched_at
            WHERE watchlist_vacancy.content_hash IS DISTINCT FROM EXCLUDED.content_hash
               OR watchlist_vacancy.filter_hash IS DISTINCT FROM EXCLUDED.filter_hash
            """;

        var affectedRowsTotal = 0;

        foreach (var match in commit.MatchUpserts)
        {
            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("watchlist", match.WatchlistId);
            cmd.Parameters.AddWithValue("source", match.SourceId);
            cmd.Parameters.AddWithValue("board", match.BoardId);
            cmd.Parameters.AddWithValue("post", match.PostId);
            cmd.Parameters.AddWithValue("hash", match.ContentHash);
            cmd.Parameters.AddWithValue("filter_hash", match.FilterHash);
            cmd.Parameters.AddWithValue("matched_at", now);

            affectedRowsTotal += await cmd.ExecuteNonQueryAsync(ct);
        }

        return affectedRowsTotal;
    }

    private async Task<int> RemoveMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        StateCommit commit,
        CancellationToken ct)
    {
        if (commit.MatchRemovals.Count == 0)
            return 0;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText =
            """
            DELETE FROM watchlist_vacancy
            WHERE watchlist_id = @watchlist
              AND source_id = @source
              AND board_id = @board
              AND post_id = ANY(@post_ids)
            """;

        var affectedRowsTotal = 0;

        // One statement per watchlist - the composite key cannot be passed as a single array.
        foreach (var group in commit.MatchRemovals.GroupBy(m => m.WatchlistId))
        {
            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("watchlist", group.Key);
            cmd.Parameters.AddWithValue("source", commit.SourceId);
            cmd.Parameters.AddWithValue("board", commit.BoardId);
            cmd.Parameters.AddWithValue("post_ids", group.Select(m => m.PostId).ToArray());

            var affectedRows = await cmd.ExecuteNonQueryAsync(ct);

            ctxLog.Debug(
                "Removed {Affected} watchlist_vacancy rows of watchlist {Watchlist} on board {Source}/{Board}",
                affectedRows, group.Key, commit.SourceId, commit.BoardId);

            affectedRowsTotal += affectedRows;
        }

        return affectedRowsTotal;
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
                watchlist_id,
                watchlist_name,
                discovered,
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
                @watchlist,
                @watchlist_name,
                @discovered,
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
            cmd.Parameters.AddWithValue("watchlist", (object?)item.WatchlistId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("watchlist_name", (object?)item.WatchlistName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("discovered", item.Discovered);

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
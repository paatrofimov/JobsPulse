namespace JobsPulse.Storage;

internal static class Schema
{
    /// <summary>
    /// Одна БД на состояние и очередь — намеренно, чтобы обновление состояния и постановку
    /// уведомления можно было выполнить одной транзакцией.
    /// </summary>
    public const string Sql =
        """
        CREATE TABLE IF NOT EXISTS seen_vacancy (
            source_id    TEXT NOT NULL,
            board_key    TEXT NOT NULL,
            external_id  TEXT NOT NULL,
            group_id     TEXT NULL,
            content_hash TEXT NOT NULL,
            title        TEXT NOT NULL,
            location     TEXT NULL,
            url          TEXT NOT NULL,
            updated_at   TEXT NOT NULL,
            first_seen_at TEXT NOT NULL,
            last_seen_at TEXT NOT NULL,
            closed_at    TEXT NULL,
            PRIMARY KEY (source_id, board_key, external_id)
        );

        CREATE INDEX IF NOT EXISTS ix_seen_board
            ON seen_vacancy (source_id, board_key) WHERE closed_at IS NULL;

        CREATE TABLE IF NOT EXISTS outbox (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            dedup_key     TEXT NOT NULL UNIQUE,
            chat_id       TEXT NOT NULL,
            silent        INTEGER NOT NULL DEFAULT 0,
            kind          TEXT NOT NULL,
            company_name  TEXT NOT NULL,
            payload       TEXT NOT NULL,
            status        TEXT NOT NULL DEFAULT 'pending',
            attempts      INTEGER NOT NULL DEFAULT 0,
            next_attempt_at TEXT NULL,
            last_error    TEXT NULL,
            created_at    TEXT NOT NULL,
            sent_at       TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_outbox_pending
            ON outbox (status, next_attempt_at);
        """;
}

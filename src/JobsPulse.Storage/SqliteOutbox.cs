using System.Text.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;

namespace JobsPulse.Storage;

/// <summary>
/// Очередь доставки. Аренда (lease) помечает элементы «в работе», чтобы два диспетчера
/// не отправили одно и то же. Гарантия at-least-once: при падении между отправкой
/// и MarkSent сообщение уйдёт повторно — это осознанно лучше, чем потерять его.
/// </summary>
public sealed class SqliteOutbox(SqliteConnectionFactory factory, TimeProvider clock) : IOutbox
{
    public async Task<IReadOnlyList<OutboxItem>> LeaseAsync(int max, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await using var connection = await factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await using var select = connection.CreateCommand();
        select.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
        select.CommandText =
            """
            SELECT id, dedup_key, chat_id, silent, kind, company_name, payload, attempts
            FROM outbox
            WHERE status = 'pending'
              AND (next_attempt_at IS NULL OR next_attempt_at <= $now)
            ORDER BY id
            LIMIT $max
            """;
        select.Parameters.AddWithValue("$now", now.ToString("O"));
        select.Parameters.AddWithValue("$max", max);

        var items = new List<OutboxItem>();
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var vacancy = JsonSerializer.Deserialize<Vacancy>(reader.GetString(6), SqliteStateStore.Json);
                if (vacancy is null) continue;

                items.Add(new OutboxItem
                {
                    Id = reader.GetInt64(0),
                    DedupKey = reader.GetString(1),
                    ChatId = reader.GetString(2),
                    Silent = reader.GetInt32(3) != 0,
                    Kind = Enum.Parse<ChangeKind>(reader.GetString(4)),
                    CompanyName = reader.GetString(5),
                    Vacancy = vacancy,
                    Attempts = reader.GetInt32(7)
                });
            }
        }

        if (items.Count > 0)
        {
            await using var lease = connection.CreateCommand();
            lease.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
            lease.CommandText =
                $"UPDATE outbox SET status = 'leased' WHERE id IN ({string.Join(",", items.Select(i => i.Id))})";
            await lease.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return items;
    }

    public async Task MarkSentAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;

        await using var connection = await factory.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            $"UPDATE outbox SET status = 'sent', sent_at = $now WHERE id IN ({string.Join(",", ids)})";
        cmd.Parameters.AddWithValue("$now", clock.GetUtcNow().ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(IReadOnlyList<long> ids, TimeSpan retryAfter, string error, CancellationToken ct)
    {
        if (ids.Count == 0) return;

        await using var connection = await factory.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();

        // Возвращаем в pending с отложенным повтором. Порог попыток проверяет диспетчер:
        // ему известна конфигурация MaxAttempts.
        cmd.CommandText =
            $"""
             UPDATE outbox
             SET status = 'pending',
                 attempts = attempts + 1,
                 next_attempt_at = $next,
                 last_error = $error
             WHERE id IN ({string.Join(",", ids)})
             """;
        cmd.Parameters.AddWithValue("$next", clock.GetUtcNow().Add(retryAfter).ToString("O"));
        cmd.Parameters.AddWithValue("$error", error);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Увести в dead-letter элементы, исчерпавшие попытки.</summary>
    public async Task DeadLetterAsync(int maxAttempts, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE outbox SET status = 'dead' WHERE status = 'pending' AND attempts >= $max";
        cmd.Parameters.AddWithValue("$max", maxAttempts);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

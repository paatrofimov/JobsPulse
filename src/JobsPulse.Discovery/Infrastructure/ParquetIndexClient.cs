using System.Data;
using System.Diagnostics;
using DuckDB.NET.Data;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>
/// DuckDB over `httpfs`: the remote parquet files are queried in place, so only the footers and the column chunks
/// that survive the predicate ever cross the network. Nothing is downloaded and nothing is written to disk.
/// </summary>
public sealed class ParquetIndexClient(
    IOptionsMonitor<DiscoveryOptions> options,
    ILog log) : IParquetIndexClient, IDisposable
{
    private readonly ILog ctxLog = log.ForContext<ParquetIndexClient>();

    // One in-memory database, one connection - queries are serialized, exactly like the http reader paces requests.
    private readonly SemaphoreSlim gate = new(1, 1);

    private DuckDBConnection? connection;

    public async Task<IReadOnlyList<string>> ProbeFilesAsync(ParquetFileProbe probe, CancellationToken ct)
    {
        if (probe.Files.Count == 0 || probe.Tlds.Count == 0)
            return [];

        var matched = new List<string>();

        await ExecuteAsync(
            ParquetIndexSql.Probe(probe),
            probe.Files.Count,
            reader =>
            {
                if (!reader.IsDBNull(0))
                    matched.Add(reader.GetString(0));
            },
            ct);

        return matched;
    }

    public async Task<long> ScanAsync(ParquetIndexQuery query, Action<string> onUrl, CancellationToken ct)
    {
        if (query.Files.Count == 0 || query.Targets.Count == 0)
            return 0;

        // The projected query parameters follow the host and the path head, in the order the sql put them.
        var queryKeys = ParquetIndexSql.QueryColumns(query);

        return await ExecuteAsync(
            ParquetIndexSql.BoardUrls(query),
            query.Files.Count,
            reader =>
            {
                var host = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (string.IsNullOrEmpty(host))
                    return;

                var path = reader.IsDBNull(1) ? "/" : reader.GetString(1);

                onUrl($"https://{host}{(path.StartsWith('/') ? path : "/" + path)}{Query(reader, queryKeys)}");
            },
            ct);
    }

    /// <summary>
    /// Puts the projected parameters back into the url, so a parser still reads one url and knows nothing about how
    /// the index stores it. Empty for every source that named no query key - which is all of them but SuccessFactors.
    /// </summary>
    private static string Query(DuckDBDataReader reader, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return string.Empty;

        var parts = new List<string>(keys.Count);

        for (var i = 0; i < keys.Count; i++)
        {
            var column = i + 2;

            if (reader.IsDBNull(column))
                continue;

            var value = reader.GetString(column);

            // regexp_extract answers with an empty string when the parameter was not in the url at all.
            if (!string.IsNullOrEmpty(value))
                parts.Add($"{keys[i]}={value}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    /// <summary>
    /// The data host throttles and drops range requests exactly like the cdx front-end does, so a failed query is
    /// the normal path and every attempt starts from a clean connection.
    /// </summary>
    /// <exception cref="ParquetIndexUnavailableException">Every attempt has failed.</exception>
    private async Task<long> ExecuteAsync(string sql, int files, Action<DuckDBDataReader> onRow, CancellationToken ct)
    {
        var opts = options.CurrentValue.Parquet;
        var attempts = Math.Max(1, opts.Retries + 1);

        await gate.WaitAsync(ct);

        try
        {
            var failure = "unknown";
            Exception? lastError = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    return await RunAsync(sql, files, onRow, opts, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    ctxLog.Debug("Gracefully finished with cancellation");
                    throw;
                }
                catch (Exception ex)
                {
                    failure = CrawlIndexFailure.Describe(ex);
                    lastError = ex;

                    // A broken connection is not reused - the next attempt starts from a clean one.
                    ResetConnection();
                }

                if (attempt == attempts)
                {
                    ctxLog.Warn(
                        lastError,
                        "Parquet query over {Files} file(s) has failed on the last attempt {Attempt}/{Attempts} ({Failure})",
                        files, attempt, attempts, failure);

                    break;
                }

                var delay = NextRetryDelay(opts, attempt);

                ctxLog.Warn(
                    lastError,
                    "Parquet query attempt {Attempt}/{Attempts} over {Files} file(s) has failed ({Failure}), retrying in {Delay}",
                    attempt, attempts, files, failure, delay);

                await Task.Delay(delay, ct);
            }

            throw new ParquetIndexUnavailableException(files, attempts, failure, lastError);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The DuckDB reader is synchronous, so the whole query runs on a pool thread; rows are handed over as they are
    /// read, so a result set of any size costs one row of memory here.
    /// </summary>
    private Task<long> RunAsync(
        string sql,
        int files,
        Action<DuckDBDataReader> onRow,
        ParquetIndexOptions opts,
        CancellationToken ct) =>
        Task.Run(
            () =>
            {
                var db = EnsureConnection(opts);

                using var command = db.CreateCommand();
                command.CommandText = sql;
                command.CommandTimeout = Math.Max(1, opts.QueryTimeoutSeconds);

                ctxLog.Debug("Running parquet query over {Files} file(s): {Sql}", files, sql);

                var watch = Stopwatch.StartNew();
                var rows = 0L;

                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    rows++;

                    onRow(reader);
                }

                ctxLog.Debug(
                    "Parquet query over {Files} file(s) answered {Rows} row(s) in {Elapsed} ms",
                    files, rows, watch.ElapsedMilliseconds);

                return rows;
            },
            ct);

    private DuckDBConnection EnsureConnection(ParquetIndexOptions opts)
    {
        if (connection is { State: ConnectionState.Open })
            return connection;

        var db = new DuckDBConnection("DataSource=:memory:");
        db.Open();

        Configure(db, opts);

        connection = db;

        return db;
    }

    private void Configure(DuckDBConnection db, ParquetIndexOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.ExtensionDirectory))
            Execute(db, $"SET extension_directory = '{opts.ExtensionDirectory.Replace("'", "''", StringComparison.Ordinal)}'", required: false);

        // Range requests over https are the whole point of the reader - without httpfs there is nothing to read.
        Execute(db, "INSTALL httpfs", required: false);
        Execute(db, "LOAD httpfs", required: true);

        Execute(db, $"SET threads = {Math.Max(1, opts.Threads)}", required: false);
        Execute(db, $"SET memory_limit = '{Math.Max(256, opts.MemoryLimitMb)}MB'", required: false);
        Execute(db, $"SET http_timeout = {Math.Max(1000, opts.HttpTimeoutMsec)}", required: false);
        Execute(db, $"SET http_retries = {Math.Max(0, opts.HttpRetries)}", required: false);
        Execute(db, $"SET http_retry_wait_ms = {Math.Max(0, opts.HttpRetryWaitMsec)}", required: false);
        Execute(db, "SET http_keep_alive = true", required: false);
        // Order of the distinct rows is irrelevant and preserving it only costs memory.
        Execute(db, "SET preserve_insertion_order = false", required: false);

        ctxLog.Info(
            "DuckDB reader is ready: {Threads} threads, {Memory} MB limit, http timeout {Timeout} ms, {Retries} retries",
            Math.Max(1, opts.Threads), Math.Max(256, opts.MemoryLimitMb), Math.Max(1000, opts.HttpTimeoutMsec),
            Math.Max(0, opts.HttpRetries));
    }

    /// <summary>A setting DuckDB does not know must not take the reader down - versions rename them.</summary>
    private void Execute(DuckDBConnection db, string sql, bool required)
    {
        try
        {
            using var command = db.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            if (required)
                throw;

            ctxLog.Debug(ex, "DuckDB has rejected '{Sql}' — continuing with its default", sql);
        }
    }

    private static TimeSpan NextRetryDelay(ParquetIndexOptions opts, int attempt)
    {
        var linear = TimeSpan.FromSeconds(Math.Max(1, opts.RetryDelaySeconds) * attempt);
        var max = TimeSpan.FromSeconds(Math.Max(1, opts.MaxRetryDelaySeconds));

        return linear > max ? max : linear;
    }

    private void ResetConnection()
    {
        try
        {
            connection?.Dispose();
        }
        catch (Exception ex)
        {
            ctxLog.Debug(ex, "Disposing the broken DuckDB connection has failed");
        }

        connection = null;
    }

    public void Dispose()
    {
        ResetConnection();
        gate.Dispose();
    }
}

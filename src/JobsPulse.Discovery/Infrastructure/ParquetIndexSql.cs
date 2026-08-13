using System.Text;
using JobsPulse.Discovery.Models;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>
/// The queries the columnar index is asked. Everything here exists to keep the amount of transferred bytes down,
/// because the files are read remotely over range requests.
///
/// The Common Crawl index files carry min/max statistics for exactly one useful column - `url_host_tld`. Neither
/// `url_surtkey` (which the table is sorted by) nor `url_host_name` has them, so a range predicate on the sort key
/// prunes nothing and the only cheap filter is the tld. Column widths are what is left to exploit, and they differ
/// by orders of magnitude: `url_host_tld` is a handful of values, `url_host_name` a few million, `url_path` is
/// unique per row. Hence <see cref="Probe"/> - narrow the file set on the cheap columns first, then pay for the wide
/// ones on the few files that are left. Measured on one crawl: 300 files → 137 after the tld probe → 4 after the
/// host probe, which is what turns a two hour scan into a few minutes.
/// </summary>
public static class ParquetIndexSql
{
    /// <summary>Which of the given files hold a row for the targets at all.</summary>
    public static string Probe(ParquetFileProbe probe)
    {
        var sql = new StringBuilder();

        sql.Append("SELECT DISTINCT filename FROM read_parquet([");
        AppendList(sql, probe.Files);
        sql.Append("], filename = true)")
            .Append(" WHERE fetch_status = ").Append(probe.FetchStatus)
            .Append(" AND url_host_tld IN (");
        AppendList(sql, probe.Tlds);
        sql.Append(')');

        // Exact hosts and whole-domain ones are one predicate: a file matching either of them has to be read.
        var hosts = probe.Hosts ?? [];
        var suffixes = probe.HostSuffixes ?? [];

        if (hosts.Count > 0 || suffixes.Count > 0)
        {
            sql.Append(" AND (");

            if (hosts.Count > 0)
            {
                sql.Append("url_host_name IN (");
                AppendList(sql, hosts);
                sql.Append(')');
            }

            for (var i = 0; i < suffixes.Count; i++)
            {
                if (i > 0 || hosts.Count > 0)
                    sql.Append(" OR ");

                AppendHostSuffix(sql, suffixes[i]);
            }

            sql.Append(')');
        }

        return sql.ToString();
    }

    /// <summary>
    /// The board urls themselves. The posting id is cut off the path and the result is `DISTINCT`, so a board with
    /// 5000 job pages comes back as one row instead of 5000; every ATS is one `OR` group, so one pass answers for
    /// all of them.
    /// </summary>
    public static string BoardUrls(ParquetIndexQuery query)
    {
        var sql = new StringBuilder();

        sql.Append("SELECT DISTINCT url_host_name, regexp_extract(coalesce(url_path, '/'), '^(?:/[^/]*){0,")
            .Append(Math.Max(1, query.PathSegments))
            .Append("}') AS url_path_head");

        AppendQueryColumns(sql, query);

        sql.Append(" FROM read_parquet([");

        AppendList(sql, query.Files);

        sql.Append("])")
            .Append(" WHERE fetch_status = ").Append(query.FetchStatus)
            .Append(" AND url_host_tld IN (");
        AppendList(sql, query.Targets.Select(t => t.Tld).Distinct(StringComparer.Ordinal).ToList());
        sql.Append(')')
            .Append(" AND (");

        for (var i = 0; i < query.Targets.Count; i++)
        {
            var target = query.Targets[i];

            if (i > 0)
                sql.Append(" OR ");

            sql.Append('(');

            if (target.HostIsSuffix)
                AppendHostSuffix(sql, target.Host);
            else
                sql.Append("url_host_name = ").Append(Literal(target.Host));

            if (target.PathPrefix != "/")
                sql.Append(" AND url_path LIKE ").Append(Literal(target.PathPrefix + "%"));

            sql.Append(')');
        }

        sql.Append(')');

        return sql.ToString();
    }

    /// <summary>
    /// The query parameters some source builds its board id from, one projected column each - see
    /// <see cref="BoardIndexTarget.QueryKeys"/>. The parameter is extracted rather than `url_query` selected whole,
    /// because the whole query is unique per job page and `DISTINCT` would return a row per posting instead of a row
    /// per board - the same reason <see cref="BoardUrls"/> cuts `url_path` down to its leading segments.
    ///
    /// Every target of the query shares the column list, so a source that asks for no parameter simply reads null
    /// there. That keeps one query answering for every ATS at once, which is what makes the columnar pass affordable.
    /// </summary>
    public static IReadOnlyList<string> QueryColumns(ParquetIndexQuery query) =>
        query.Targets
            .SelectMany(t => t.QueryKeys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    private static void AppendQueryColumns(StringBuilder sql, ParquetIndexQuery query)
    {
        foreach (var key in QueryColumns(query))
        {
            // '(?:^|&)key=([^&]*)' - the value of one parameter, wherever it sits in the query.
            sql.Append(", regexp_extract(coalesce(url_query, ''), ")
                .Append(Literal("(?:^|&)" + Escape(key) + "=([^&]*)"))
                .Append(", 1) AS ")
                .Append(ColumnAlias(key));
        }
    }

    /// <summary>A parameter name is an identifier, but the alias still has to be one DuckDB accepts.</summary>
    private static string ColumnAlias(string key) =>
        "url_query_" + new string([.. key.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')]);

    private static string Escape(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c.ToString() : "\\" + c));

    /// <summary>
    /// Every subdomain of one domain. The leading '%' rules out a row group skip, but there is no alternative: an ATS
    /// that gives each tenant its own host has no host to be asked for by name.
    /// </summary>
    private static void AppendHostSuffix(StringBuilder sql, string suffix) =>
        sql.Append("url_host_name LIKE ").Append(Literal("%." + suffix));

    private static void AppendList(StringBuilder sql, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
                sql.Append(", ");

            sql.Append(Literal(values[i]));
        }
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

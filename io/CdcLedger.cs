using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// dbchange/ledgers/cdc-tracked.md (WP 2.5): the tables Change Data Capture tracks, one row per environment and table, with the date check
/// cdc read sys.tables.is_tracked_by_cdc there. The SSDT repository owns the file (V3_MILESTONES.md §6 item 5): classify reads its rows,
/// and check cdc --commit proposes it on a new branch, one environment's rows replaced and every other environment's kept. A repository
/// without the file lists no table. A row holds no capture instance and no column list: Classify refuses a column-list change on a tracked
/// table whichever columns its capture instance holds, and the names would leave the environment as text, which no aggregate query
/// returns.
/// </summary>
public static class CdcLedger
{
    /// <summary>The ledger's path from the repository root.</summary>
    public const string File = "dbchange/ledgers/cdc-tracked.md";

    private const string Header = "| Environment | Table | Measured |";

    private const string Rule = "|---|---|---|";

    private static readonly string Preamble = string.Join('\n',
        "# CDC-tracked tables",
        "",
        "The tables Change Data Capture tracks, one row per environment and table, with the date `dbchange check cdc --env <environment>`",
        "read `sys.tables.is_tracked_by_cdc` there; `--commit` proposes the rows on a new branch, for review, one environment's replaced",
        "and every other environment's kept. `dbchange classify` refuses a change that creates, drops or renames a column of a table",
        "listed here, and, with `--env`, a deploy plan that rebuilds one.",
        "");

    private static readonly Regex Date = new(@"\A\d{4}-\d{2}-\d{2}\z", RegexOptions.CultureInvariant);

    /// <summary>A row: the environment, the table in two parts, and the date check cdc read it, yyyy-MM-dd.</summary>
    public sealed record Row(EnvironmentName Environment, Name Table, string Measured) : IComparable<Row>
    {
        public int CompareTo(Row? other) =>
            other is null ? 1
            : Environment.CompareTo(other.Environment) is var e and not 0 ? e
            : Table.CompareTo(other.Table) is var t and not 0 ? t
            : string.CompareOrdinal(Measured, other.Measured);

        /// <summary>The row as the ledger writes it: | dev | [dbo].[Customer] | 2026-10-21 |.</summary>
        public override string ToString() => "| " + Environment + " | " + Table + " | " + Measured + " |";
    }

    /// <summary>The ledger's rows under <paramref name="repositoryRoot"/>; none where the file does not exist.</summary>
    public static Result<SortedArray<Row>> Read(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, File);
        if (!System.IO.File.Exists(path))
        {
            return SortedArray.Of<Row>([]);
        }

        try
        {
            return Parse(System.IO.File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("file.unreadable", File + " cannot be read: " + e.GetType().Name + ".",
                "Give this identity read access to " + File + ", and close any program that holds it open.");
        }
    }

    /// <summary>
    /// The rows of a ledger's text: each line that starts with a bar, the header and its rule aside, read as an environment's name, a table
    /// in two parts and a date; any other such line is file.ledger-row, by its line number, quoting nothing of it. The table cell is what
    /// stands between the second bar and the last but one, so a name holding a bar reads back as written.
    /// </summary>
    public static Result<SortedArray<Row>> Parse(string text)
    {
        var rows = new List<Row>();
        var lines = LineEndings.Lf(text).Split('\n');
        for (var at = 0; at < lines.Length; at++)
        {
            var line = lines[at].Trim();
            if (!line.StartsWith('|') || line == Header || line == Rule)
            {
                continue;
            }

            var (first, last) = (line.IndexOf('|', 1), line.Length > 1 ? line.LastIndexOf('|', line.Length - 2) : -1);
            var row = line.EndsWith('|') && first > 0 && last > first
                && EnvironmentName.Of(File, line[1..first].Trim()) is Result<EnvironmentName>.Ok(var environment)
                && TSql.NameParts(line[(first + 1)..last].Trim()) is [var schema, var table]
                && Name.Of(schema, table) is Result<Name>.Ok(var name)
                && line[(last + 1)..^1].Trim() is var measured && Date.IsMatch(measured)
                    ? new Row(environment, name, measured) : null;
            if (row is null)
            {
                return new Error("file.ledger-row", File + " line " + (at + 1).ToString(CultureInfo.InvariantCulture)
                        + " is not a row of an environment, a table named in two parts and a date; the line is not quoted.",
                    "Write the line as | dev | [dbo].[Customer] | 2026-10-21 |, or delete it and run dbchange check cdc for its"
                        + " environment.");
            }

            rows.Add(row);
        }

        return SortedArray.Of(rows);
    }

    /// <summary>
    /// The ledger's text: its preamble, its header, the rows kept (other environments' rows, as read), and a row for each table
    /// <paramref name="environment"/> tracks, measured on <paramref name="measured"/>, every row in the order of its environment and
    /// table.
    /// </summary>
    public static string Written(IEnumerable<Row> kept, EnvironmentName environment, IEnumerable<Name> tracked, string measured) =>
        Preamble + "\n" + Header + "\n" + Rule + "\n" + string.Concat(SortedArray.Of(kept.Where(r => r.Environment != environment)
            .Concat(tracked.Distinct().Select(t => new Row(environment, t, measured)))).Select(r => r + "\n"));
}

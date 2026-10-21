using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// The row-tier ledger, dbchange/ledgers/row-tiers.md (V3_MILESTONES.md WP 3.7; VALUES.md G5 and O12): one table of four columns, Date,
/// Environment, Table and Tier, each row one table's row tier in one environment as measured on its date, the UTC day. The tiers are v2's
/// (archive/v2/ssdt-agent/estate/row-tiers.md), a table's rows by order of magnitude. A measurement appends a row for each table whose tier
/// differs from the last row of its environment and table, or that has none, after the table's last row, so text after the table stays
/// after it; no row is edited. A ledger of another form is ledger.malformed, naming its line, and nothing is appended.
/// </summary>
public static partial class Profile
{
    /// <summary>The ledger's path from the repository root, as messages name it.</summary>
    public const string Ledger = "dbchange/ledgers/row-tiers.md";

    private const string Header = "| Date | Environment | Table | Tier |";

    private const string Separator = "|---|---|---|---|";

    /// <summary>The tiers, the smallest first: no row, then up to a thousand, a hundred thousand and a million rows, and more.</summary>
    public static readonly IReadOnlyList<string> TierNames = ["0", "1–1k", "1k–100k", "100k–1M", ">1M"];

    /// <summary>A pipe that separates two cells: one no backslash escapes.</summary>
    private static readonly Regex Cells = new(@"(?<!\\)\|", RegexOptions.CultureInvariant);

    /// <summary>The ledger as a measurement first writes it: what it holds, then its table's header.</summary>
    private static readonly string Template = string.Join('\n',
        "# Row tiers", "",
        "Each row is one table's row tier in one environment on the date it was measured, the UTC day, written by",
        "`dbchange profile --env <environment> --counts` from the rows sys.partitions keeps, which a login holding VIEW DEFINITION reads",
        "without reading a row. A tier is 0, 1–1k, 1k–100k, 100k–1M or >1M rows. A measurement appends a row for each table whose tier",
        "differs from the last row of its environment and table, or that has none; no row is edited. A table whose partitions the login",
        "cannot see gets no row.", "", Header, Separator, "");

    /// <summary>The tier of a table holding <paramref name="rows"/> rows.</summary>
    public static string TierOf(long rows) => rows switch
    {
        <= 0 => TierNames[0],
        <= 1_000 => TierNames[1],
        <= 100_000 => TierNames[2],
        <= 1_000_000 => TierNames[3],
        _ => TierNames[4],
    };

    /// <summary>
    /// The ledger's text, the template where <paramref name="ledger"/> is null, with a row dated <paramref name="date"/> appended for each
    /// table of <paramref name="tiers"/> whose tier differs from the last row of <paramref name="environment"/> and the table, or that has
    /// none; and those tables, in the order given.
    /// </summary>
    internal static Result<(string Text, IReadOnlyList<Name> Appended)> Appended(string? ledger, EnvironmentName environment,
        DateOnly date, IReadOnlyList<(Name Table, string Tier)> tiers)
    {
        var lines = LineEndings.Lf(ledger ?? Template).Split('\n').ToList();
        var header = lines.FindIndex(line => line.Trim() == Header);
        if (header < 0 || header + 1 == lines.Count || lines[header + 1].Replace(" ", "", StringComparison.Ordinal) != Separator)
        {
            return Malformed("holds no table headed " + Header);
        }

        var last = new Dictionary<(string Environment, string Table), string>();
        var end = header + 2;
        for (; end < lines.Count && lines[end].TrimStart().StartsWith('|'); end++)
        {
            var cells = Cells.Split(lines[end].Trim()).Select(cell => cell.Trim()).ToArray();
            if (cells is not ["", var dated, var named, var table, var tier, ""]
                || !DateOnly.TryParseExact(dated, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || EnvironmentName.Of(Ledger, named) is not Result<EnvironmentName>.Ok || table.Length == 0 || !TierNames.Contains(tier))
            {
                return Malformed("line " + (end + 1).ToString(CultureInfo.InvariantCulture)
                    + " is no row of a date, an environment, a table and a tier");
            }

            last[(named, table)] = tier;
        }

        var appended = tiers.Where(t => !last.TryGetValue((environment.ToString(), Written(t.Table)), out var was) || was != t.Tier)
            .ToList();
        lines.InsertRange(end, appended.Select(t => "| " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " | " + environment
            + " | " + Written(t.Table) + " | " + t.Tier + " |"));
        return (string.Join('\n', lines), [.. appended.Select(t => t.Table)]);
    }

    /// <summary>The ledger under the repository root as it stands, null where there is none; ledger.unreadable where the file system
    /// withholds it.</summary>
    internal static Result<string?> LedgerText(string root)
    {
        var path = Path.Combine(root, Ledger);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("ledger.unreadable", Ledger + " cannot be read: " + e.Message.TrimEnd('.') + ".",
                "Grant this identity read access to " + Ledger + ", and close any program that holds it open.");
        }
    }

    /// <summary>The tables measured, appended to the ledger under the repository root where their tier changed; the tables whose row was
    /// appended. The file is written only where a row is.</summary>
    private static Result<IReadOnlyList<Name>> Recorded(string root, EnvironmentName environment, DateOnly date,
        IReadOnlyList<(Name Table, string? Tier)> measured) =>
        LedgerText(root).Bind(ledger => Appended(ledger, environment, date,
            [.. measured.Where(m => m.Tier is not null).Select(m => (m.Table, m.Tier!))]))
        .Bind(written => written.Appended.Count == 0 ? Result.Ok(written.Appended)
            : Write.Text(Path.Combine(root, Ledger), written.Text).Map(_ => written.Appended));

    /// <summary>A table's name as a cell writes it: QUOTENAME's form, a pipe escaped.</summary>
    private static string Written(Name table) => table.ToString().Replace("|", "\\|", StringComparison.Ordinal);

    private static Error Malformed(string what) => new("ledger.malformed", Ledger + " " + what + ".",
        "Correct the ledger at that place, so each row reads | yyyy-MM-dd | environment | [schema].[table] | tier |.");
}

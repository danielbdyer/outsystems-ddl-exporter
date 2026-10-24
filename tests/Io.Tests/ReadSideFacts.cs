using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Budgets.Tests;
using DbChange.Kernel;

namespace DbChange.Io.Tests;

/// <summary>
/// What v3's Schema and v2's read-side catalog (ReadSide.readSchema, then CatalogCodec.serialize; ci/readside.sh) both say of a database,
/// one fact a line, sorted ordinally, so the two compare line for line and a difference names its object. A name is quoted as
/// QUOTENAME quotes it; an expression is stripped of its outer parentheses as v2's PhysicalSchema.normalizeDefault strips a default's
/// (archive/v2/src/Projection.Core/PhysicalSchema.fs). The facts are
/// <c>table [s].[t]</c>;
/// <c>column [s].[t].[c] &lt;ordinal&gt; &lt;type&gt; null|not-null[ primary-key][ identity][ rowversion][ variant]</c>, or, for a
/// computed column, <c>column [s].[t].[c] &lt;ordinal&gt; computed null|not-null</c> and
/// <c>computed [s].[t].[c] persisted|virtual &lt;expression&gt;</c>;
/// <c>default [s].[t].[c] &lt;expression&gt;</c>; <c>reference [s].[t].[c] -&gt; [s].[t]</c>, one a column of the foreign key;
/// <c>unique [s].[t] (c[ desc], ...)</c>, of a unique constraint or a unique index; <c>check [s].[t] &lt;expression&gt;</c>; and
/// <c>trigger [s].[t] &lt;name&gt; enabled|disabled</c>. A type is v2's: Integer, Boolean, Decimal(p[,s]), Text(n|max), Binary(n|max),
/// Guid, Date, Time or DateTime. <see cref="Header"/> says what the two readers do not share, and why.
/// </summary>
internal static class ReadSideFacts
{
    /// <summary>The committed facts' header: what both readers read but the facts leave out, each with its reason.</summary>
    public static readonly string[] Header =
    [
        "# The facts v3's Schema.Of and v2's read side (ci/readside.sh) share for the golden project with this",
        "# change, published under its pipeline profile. Io.Tests' ReadSideTests compares both with these lines.",
        "# Regenerate: DBCHANGE_READSIDE=$(bash ci/readside.sh) DBCHANGE_BLESS=1 dotnet test tests/Io.Tests",
        "#   --filter FullyQualifiedName~ReadSideTests",
        "# Left out, with the reason:",
        "# - a foreign key's name, parent columns, actions and NOCHECK: v2 names it FK_<table>_<column> and",
        "#   writes NoAction (ReadSide.fs);",
        "# - trust: DacFx's model holds none; WP 3.5 reads is_not_trusted from the environment (S9);",
        "# - the names of checks, uniques and defaults: v2 reads the names SQL Server generates;",
        "# - identity seed and increment, collation, SPARSE, ROWGUIDCOL, an alias and the exact storage type:",
        "#   v2 reads none of them;",
        "# - non-unique indexes, an index's filter and included columns, sequences, triggers' kinds;",
        "# - system versioning and a computed column's type: v2 reads neither.",
    ];

    /// <summary>The facts of a schema v3 read.</summary>
    public static SortedArray<string> Of(Schema schema)
    {
        var facts = new List<string>();
        foreach (var table in schema.Tables)
        {
            var t = table.Name.ToString();
            var key = table.Key?.Columns.Select(c => c.Column).ToHashSet() ?? [];
            facts.Add("table " + t);
            foreach (var column in table.Columns)
            {
                var c = t + "." + column.Name;
                var nullable = column.Nullable ? "null" : "not-null";
                facts.AddRange(column.Match<IEnumerable<string>>(
                    s => [Line(c, column.Ordinal, Type(s.Type), nullable, key.Contains(s.Name), s.Identity is not null, s.Type),
                        .. s.Default is { } d ? ["default " + c + " " + Stripped(d.Expression.Content)] : Array.Empty<string>()],
                    computed => new[]
                    {
                        "column " + c + " " + column.Ordinal + " computed " + nullable,
                        "computed " + c + " " + (computed.Persisted ? "persisted " : "virtual ") + Stripped(computed.Expression.Content),
                    },
                    g => [Line(c, column.Ordinal, Type(g.Type), nullable, key.Contains(g.Name), false, g.Type)]));
            }

            facts.AddRange(table.References.SelectMany(r => r.Legs.Select(l => "reference " + t + "." + l.Column + " -> " + r.Parent)));
            facts.AddRange(table.Uniques.Select(u =>
                "unique " + t + " (" + string.Join(", ", u.Columns.Select(k => k.Column.Base + (k.Descending ? " desc" : ""))) + ")"));
            facts.AddRange(table.Checks.Select(k => "check " + t + " " + Stripped(k.Expression.Content)));
            facts.AddRange(table.Triggers.Select(g => "trigger " + t + " " + g.Name.Base + (g.Disabled ? " disabled" : " enabled")));
        }

        return SortedArray.Of(facts);
    }

    /// <summary>The facts of the catalog v2's read side writes as JSON (archive/v2/src/Projection.Targets.Json/CatalogCodec.fs).</summary>
    public static SortedArray<string> Of(JsonNode catalog)
    {
        var kinds = catalog["modules"]!.AsArray().SelectMany(m => m!["kinds"]!.AsArray()).Select(k => k!).ToArray();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in kinds)
        {
            var t = Quoted((string)kind["physical"]!["schema"]!) + "." + Quoted((string)kind["physical"]!["table"]!);
            names[(string)kind["ssKey"]!] = t;
            foreach (var a in kind["attributes"]!.AsArray())
            {
                names[(string)a!["ssKey"]!] = (string)a["column"]!["columnName"]!;
            }
        }

        var facts = new List<string>();
        foreach (var kind in kinds)
        {
            var t = names[(string)kind["ssKey"]!];
            facts.Add("table " + t);
            foreach (var (a, i) in kind["attributes"]!.AsArray().Select((a, i) => (a!, i + 1)))
            {
                var c = t + "." + Quoted((string)a["column"]!["columnName"]!);
                var nullable = (bool)a["column"]!["isNullable"]! ? "null" : "not-null";
                if (a["computed"] is { } computed)
                {
                    facts.Add("column " + c + " " + i + " computed " + nullable);
                    facts.Add("computed " + c + " " + ((bool)computed["isPersisted"]! ? "persisted " : "virtual ")
                        + Stripped((string)computed["expression"]!));
                }
                else
                {
                    var storage = (string?)a["sqlStorage"]?["kind"];
                    facts.Add(string.Join(' ', new[]
                    {
                        "column " + c + " " + i + " " + V2Type(a), nullable, (bool)a["isPrimaryKey"]! ? "primary-key" : "",
                        (bool)a["isIdentity"]! ? "identity" : "", storage == "RowVersion" ? "rowversion" : "",
                        storage == "SqlVariant" ? "variant" : "",
                    }.Where(w => w.Length > 0)));
                }

                if (a["defaultValue"] is { } d)
                {
                    facts.Add("default " + c + " " + ((string)d["kind"]! == "BooleanLit" ? ((bool)d["value"]! ? "1" : "0")
                        : Stripped((string)d["value"]!)));
                }
            }

            facts.AddRange(kind["references"]!.AsArray().Select(r =>
                "reference " + t + "." + Quoted(names[(string)r!["sourceAttribute"]!]) + " -> " + names[(string)r["targetKind"]!]));
            facts.AddRange(kind["indexes"]!.AsArray().Where(x => (bool)x!["isUnique"]!).Select(x => "unique " + t + " (" + string.Join(", ",
                x!["columns"]!.AsArray().Select(k => names[(string)k!["attribute"]!] + ((string)k["direction"]! == "Descending" ? " desc" : "")))
                + ")"));
            facts.AddRange(kind["columnChecks"]!.AsArray().Select(k => "check " + t + " " + Stripped((string)k!["definition"]!)));
            facts.AddRange(kind["triggers"]!.AsArray().Select(g =>
                "trigger " + t + " " + (string)g!["name"]! + ((bool)g["isDisabled"]! ? " disabled" : " enabled")));
        }

        return SortedArray.Of(facts);
    }

    /// <summary>The committed facts of a sample change: tests/Golden/changes/&lt;change&gt;/read-side.txt, its header left out.</summary>
    public static SortedArray<string> Committed(string change) =>
        SortedArray.Of(File.ReadAllLines(Path(change)).Where(l => l.Length > 0 && !l.StartsWith('#')));

    /// <summary>The facts written as the committed file, under the header, with LF line endings.</summary>
    public static void Bless(string change, SortedArray<string> facts) => File.WriteAllText(Path(change), Written(facts));

    /// <summary>The committed file's text exactly as it is on disk, to compare with <see cref="Written"/> byte for byte.</summary>
    public static string Text(string change) => File.ReadAllText(Path(change));

    /// <summary>The text Bless writes for <paramref name="facts"/>: the header, then the facts, one a line, LF after each.</summary>
    public static string Written(SortedArray<string> facts) => string.Join('\n', Header.Concat(facts)) + "\n";

    /// <summary>
    /// An expression without its outer parentheses, removed in pairs while the first opens a group the last closes, as v2's
    /// PhysicalSchema.normalizeDefault removes them: SQL Server writes a default (N'Pending') as ((N'Pending')) and a check as
    /// ([Total]&gt;=(0)).
    /// </summary>
    internal static string Stripped(string expression)
    {
        var text = expression.Trim();
        while (text.Length >= 2 && text[0] == '(' && text[^1] == ')' && Closes(text))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    private static bool Closes(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0 && i < text.Length - 1)
            {
                return false;
            }
        }

        return true;
    }

    private static string Path(string change) =>
        System.IO.Path.Combine(Repository.Root, "tests", "Golden", "changes", change, "read-side.txt");

    private static string Quoted(string part) => "[" + part.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string Line(string column, int ordinal, string type, string nullable, bool key, bool identity, ColumnType stored) =>
        string.Join(' ', new[]
        {
            "column " + column + " " + ordinal + " " + type, nullable, key ? "primary-key" : "", identity ? "identity" : "",
            stored.Type.Base is "rowversion" or "timestamp" ? "rowversion" : "", stored.Type.Base == "sql_variant" ? "variant" : "",
        }.Where(w => w.Length > 0));

    /// <summary>A stored type as v2 names it, with the facets v2 reads: SqlStorageType's correspondence, for the types it knows.</summary>
    private static string Type(ColumnType type)
    {
        string Sized(string name) => name + "(" + (type.IsMax ? "max" : type.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ")";
        return type.Type.Base.ToLowerInvariant() switch
        {
            "tinyint" or "smallint" or "int" or "bigint" => "Integer",
            "bit" => "Boolean",
            "decimal" or "numeric" => FormattableString.Invariant($"Decimal({type.Precision},{type.Scale})"),
            "money" => "Decimal(19,4)",
            "smallmoney" => "Decimal(10,4)",
            "float" => FormattableString.Invariant($"Decimal({(type.Precision == 0 ? 53 : type.Precision)})"),
            "real" => "Decimal(24)",
            "char" or "varchar" or "nchar" or "nvarchar" => Sized("Text"),
            "xml" => "Text(max)",
            "text" => "Text(2147483647)",
            "ntext" => "Text(1073741823)",
            "sql_variant" => "Text(0)",
            "binary" or "varbinary" => Sized("Binary"),
            "image" => "Binary(2147483647)",
            "rowversion" or "timestamp" => "Binary(max)",
            "uniqueidentifier" => "Guid",
            "date" => "Date",
            "time" => "Time",
            "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "DateTime",
            var other => "Unmapped(" + other + ")",
        };
    }

    private static string V2Type(JsonNode a)
    {
        var type = (string)a["type"]!;
        var length = a["length"] is { } l ? ((long)l).ToString(System.Globalization.CultureInfo.InvariantCulture) : "max";
        return type switch
        {
            "Text" or "Binary" => type + "(" + length + ")",
            "Decimal" => "Decimal(" + (long)a["precision"]! + (a["scale"] is { } s ? "," + (long)s : "") + ")",
            _ => type,
        };
    }
}

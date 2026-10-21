using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// The one form of the allowlist that answers text (V3_MILESTONES.md WP 3.7 and §17 item 24; the Facsimile's P6;
/// archive/v2/TWIN_SUCCESSION.md Appendix D): a column's value-free shape, its signatures with their counts, the rare ones folded. A
/// signature is a value with each character put in its class on the server, by TRANSLATE and the REPLACEs after it: a lower-case ASCII
/// letter a, an upper-case one A, a digit 9 and a space _, so a date's or a phone number's format stays legible; ASCII punctuation as
/// itself, the structure a script parses on; and every other character x, a letter of another script, an accented letter, a symbol or a
/// control character. Runs of a, of A and of x collapse to one, so no word's length shows; digits do not, so 99/99/9999 stays a format. A
/// signature seen fewer than five times, or one holding no a, A, 9 or x, which is punctuation and spaces alone and so the value it came
/// from, is folded on the server into one count of rows and signatures, so no such value leaves it. NULL and the empty string have no
/// signature; the summary counts them. The form is committed here: a query is admitted as it only when ScriptDom writes it as the form for
/// the table and the column it reads, and <see cref="Answered"/> refuses an answer the form cannot give.
/// </summary>
public static partial class SqlServer
{
    /// <summary>The Facsimile's k: a signature seen fewer times is folded.</summary>
    public const int Fold = 5;

    /// <summary>The printable ASCII characters, U+0020 to U+007E in order: the characters the form puts in a class or keeps.</summary>
    internal static readonly string Printable = new([.. Enumerable.Range(0x20, 95).Select(c => (char)c)]);

    /// <summary>Each printable character's class, in <see cref="Printable"/>'s order: the committed class map.</summary>
    internal static readonly string Classes = new([.. Printable.Select(ClassOf)]);

    /// <summary>What the form selects and groups by last: a signature it shows, or NULL for the fold.</summary>
    private static readonly string ShownOrFolded =
        "CASE WHEN g.n >= " + Fold.ToString(CultureInfo.InvariantCulture) + " AND g.s LIKE N'%[aA9x]%' THEN g.s END";

    /// <summary>Each run the form collapses: the class symbol, the two characters it is marked with, and the pair a run leaves between
    /// two marks, none of which a signature holds.</summary>
    private static readonly (string Symbol, string Mark, string Between)[] Runs = [("a", "bc", "cb"), ("A", "BC", "CB"), ("x", "yz", "zy")];

    private static char ClassOf(char c) => c switch
    {
        >= 'a' and <= 'z' => 'a',
        >= 'A' and <= 'Z' => 'A',
        >= '0' and <= '9' => '9',
        ' ' => '_',
        _ => c,
    };

    /// <summary>
    /// The signature form for one column of one table, admitted: its statement, as ScriptDom writes it; the table and column it reads, as
    /// the target's catalog spells them; and its site. Only io/Profile makes one, from the catalog's names.
    /// </summary>
    public sealed class SignatureQuery
    {
        private SignatureQuery(string statement, Name table, string column) =>
            (Statement, Table, Column) = (statement, table, column);

        public string Statement { get; }

        public Name Table { get; }

        public string Column { get; }

        public string Site => Table + "." + Quoted(Column) + " signatures";

        /// <summary>The form for <paramref name="table"/> and <paramref name="column"/>, admitted as any text is.</summary>
        internal static Result<SignatureQuery> Of(Name table, string column) => Of(Form(table, column), table + "." + Quoted(column));

        /// <summary>The query <paramref name="text"/> holds, when it is the committed form; any other shape is aggregate-query.refused,
        /// naming <paramref name="site"/> and what the query is, and quoting none of it.</summary>
        internal static Result<SignatureQuery> Of(string text, string site) =>
            Allowlist.Signature(text, site).Map(admitted => new SignatureQuery(admitted.Text, admitted.Table, admitted.Column));

        /// <summary>
        /// The committed form's text: the column cast to nvarchar(max) under Latin1_General_BIN2, so a code page's string is read as its
        /// Unicode characters and the classes compare exactly, over the rows whose value is neither NULL nor empty; z.o, the characters
        /// outside printable ASCII, which the third TRANSLATE makes x; the signature, each run of a, A and x collapsed by marking the
        /// symbol with two characters and dropping what two marks leave between them; the signatures grouped and counted; and the fold,
        /// grouped again.
        /// </summary>
        internal static string Form(Name table, string column)
        {
            var read = "t." + Quoted(column);
            var signature = Runs.Aggregate("TRANSLATE(TRANSLATE(z.v, " + Literal(Printable) + ", " + Literal(Classes)
                + "), z.o, REPLICATE(N'x', DATALENGTH(z.o) / 2))",
                (inner, run) => "REPLACE(REPLACE(REPLACE(" + inner + ", N'" + run.Symbol + "', N'" + run.Mark + "'), N'" + run.Between
                    + "', N''), N'" + run.Mark + "', N'" + run.Symbol + "')");
            return "SELECT " + ShownOrFolded + " AS signature, SUM(g.n) AS [rows], COUNT_BIG(*) AS signatures\n"
                + "FROM (SELECT w.s, COUNT_BIG(*) AS n\n"
                + "  FROM (SELECT " + signature + " AS s\n"
                + "    FROM (SELECT y.v, REPLACE(TRANSLATE(y.v, " + Literal(Printable) + ", REPLICATE(N'a', 95)), N'a', N'') AS o\n"
                + "      FROM (SELECT CAST(" + read + " AS nvarchar(max)) COLLATE Latin1_General_BIN2 AS v\n"
                + "        FROM " + table + " AS t WHERE DATALENGTH(" + read + ") > 0) AS y) AS z) AS w\n"
                + "  GROUP BY w.s) AS g\n"
                + "GROUP BY " + ShownOrFolded + ";";
        }

        public override string ToString() => Site + ": " + Statement;
    }

    /// <summary>A signature the form showed, and the rows holding it; the most rows first, then ordinally.</summary>
    public sealed record Signature(string Text, long Rows) : IComparable<Signature>
    {
        public int CompareTo(Signature? other) =>
            other is null ? 1 : other.Rows.CompareTo(Rows) is var byRows and not 0 ? byRows : string.CompareOrdinal(Text, other.Text);
    }

    /// <summary>What the form answered: the signatures it showed, and the rows and signatures it folded.</summary>
    public sealed record Signatures(SortedArray<Signature> Shown, long FoldedRows, long FoldedSignatures);

    /// <summary>
    /// The signature form run against the target through the one statement path, after the synonym check, under the aggregate queries'
    /// timeout. A statement SQL Server refuses is the error Database.ErrorOf makes of it, its message withheld for a named environment.
    /// </summary>
    public static Result<Signatures> Measure(Database target, SignatureQuery query, QueryLog log) =>
        NoSynonym(target, query.Site, [query.Table.ToString()], log)
            .Bind(_ => Query(target, new Statement(query.Site, query.Statement) { Timeout = AggregateQueryTimeout }, log, Answered))
            .Bind(read => read);

    /// <summary>
    /// The form's rows read: each a signature, its rows and 1, or the fold's NULL, its rows and its signatures. A row of another shape, a
    /// signature seen fewer than five times or one that is no signature the form shows (<see cref="IsSignature"/>) is a defect in the form,
    /// internal.answer-type, its text withheld, so nothing the form should have folded leaves here.
    /// </summary>
    internal static Result<Signatures> Answered(IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var (shown, folded) = (new List<Signature>(), (Rows: 0L, Signatures: 0L, Seen: false));
        foreach (var row in rows)
        {
            var counts = row.Count == 3 ? (Integer(row[1]), Integer(row[2])) : default;
            if (counts is not (Result<long?>.Ok { Value: { } n }, Result<long?>.Ok { Value: { } m }))
            {
                return Unread("a row other than a signature, its rows and its count of signatures");
            }

            switch (row[0])
            {
                case null when !folded.Seen:
                    folded = (n, m, true);
                    break;
                case string text when n >= Fold && m == 1 && IsSignature(text):
                    shown.Add(new(text, n));
                    break;
                default:
                    return Unread("a signature seen fewer than five times, a second fold, or a text that is no signature");
            }
        }

        return new Signatures(SortedArray.Of(shown), folded.Rows, folded.Signatures);
    }

    /// <summary>
    /// Whether a text is a signature the form shows: the class symbols a, A, 9, _ and x and ASCII punctuation alone, no run of a, of A or
    /// of x, and an a, an A, a 9 or an x among them.
    /// </summary>
    internal static bool IsSignature(string text) =>
        text.Any(c => c is 'a' or 'A' or '9' or 'x')
        && text.All(c => c is 'a' or 'A' or '9' or 'x' || (c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c)))
        && Runs.All(run => !text.Contains(run.Symbol + run.Symbol, StringComparison.Ordinal));

    private static Error Unread(string what) => new("internal.answer-type",
        "The signature form answered " + what + ", which the committed form never gives; the answer is withheld.",
        "Report this answer and the profile command to dbchange's maintainers: the signature form is not the one committed.");

    /// <summary>A column of a user table, as the target's catalog spells it: its table, its name, its type and its collation, null for a
    /// type that has none.</summary>
    public sealed record CatalogColumn(Name Table, string Column, string Type, string? Collation);

    /// <summary>
    /// The column of a user table that <paramref name="table"/> and <paramref name="column"/> name, read from the target's catalog with
    /// both as parameters: names and a type, as Extract reads them, and no row. Null where the catalog shows this identity no such column.
    /// </summary>
    internal static Result<CatalogColumn?> ColumnOf(Database target, Name table, string column, QueryLog log) =>
        Query(target, new Statement("Column " + table + "." + Quoted(column),
            "SELECT s.name, o.name, c.name, TYPE_NAME(c.system_type_id), c.collation_name FROM sys.columns AS c "
            + "JOIN sys.objects AS o ON o.object_id = c.object_id JOIN sys.schemas AS s ON s.schema_id = o.schema_id "
            + "WHERE c.object_id = OBJECT_ID(@table, N'U') AND c.name = @column;")
        {
            Parameters = [("@table", table.ToString()), ("@column", column)],
        }, log, rows => rows)
        .Bind(rows => rows is [[string schema, string name, string own, string type, var collation]]
            ? Name.Of(schema, name).Map(held => (CatalogColumn?)new CatalogColumn(held, own, type, collation as string))
            : Result.Ok<CatalogColumn?>(null));

    /// <summary>The user tables the target's catalog shows this identity, by name: names alone, as Extract reads them, and no
    /// row.</summary>
    internal static Result<SortedArray<Name>> TablesOf(Database target, QueryLog log) =>
        Query(target, new Statement("Tables", "SELECT s.name, o.name FROM sys.objects AS o JOIN sys.schemas AS s "
            + "ON s.schema_id = o.schema_id WHERE o.type = 'U' AND o.is_ms_shipped = 0;"), log, rows => rows)
        .Bind(rows => Result.All(rows.Select(row => Name.Of((string)row[0]!, (string)row[1]!))))
        .Map(names => SortedArray.Of(names));

    /// <summary>A part of a name as QUOTENAME writes it: in brackets, a closing bracket doubled.</summary>
    internal static string Quoted(string part) => "[" + part.Replace("]", "]]", StringComparison.Ordinal) + "]";

    /// <summary>A Unicode string literal of <paramref name="text"/>, a quote doubled.</summary>
    private static string Literal(string text) => "N'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static partial class Allowlist
    {
        /// <summary>The functions that return a part of a string, or a string's characters kept in another order.</summary>
        private static readonly HashSet<string> Kept = new(StringComparer.OrdinalIgnoreCase) { "LEFT", "RIGHT", "SUBSTRING", "STUFF",
            "REVERSE", "TRIM", "LTRIM", "RTRIM", "STRING_SPLIT", "CONCAT", "FORMAT" };

        /// <summary>
        /// The signature form, when <paramref name="text"/> is one SELECT that ScriptDom writes as the committed form for the one table it
        /// reads, aliased t, and the column it reads through t; every other shape is aggregate-query.refused, naming
        /// <paramref name="site"/> and what the query is, and quoting none of it.
        /// </summary>
        public static Result<(string Text, Name Table, string Column)> Signature(string text, string site)
        {
            var parsed = TSql.Parse(text, out var errors);
            if ((errors.Count > 0 ? [] : ((TSqlScript)parsed).Batches.SelectMany(b => b.Statements).ToList())
                is not [SelectStatement select])
            {
                return Refused(site, errors.Count > 0 ? "a text that does not parse" : "other than one SELECT");
            }

            var written = TSql.Text(select);
            return Reads(select) is { } read && TSql.Parse(SignatureQuery.Form(read.Table, read.Column), out _) is TSqlScript form
                && TSql.Text(form.Batches[0].Statements[0]) == written
                ? (written, read.Table, read.Column)
                : Refused(site, Diagnosis(select));
        }

        private static Error Refused(string site, string what) => new("aggregate-query.refused",
            "The signature query of " + site + " is refused: " + what + "; a column's signatures are read only in the committed form.",
            "Read the column's shape with dbchange profile, which writes the committed form.");

        /// <summary>The table a query reads, its only one, named in two parts and aliased t, and the first column it reads through t;
        /// null where it reads other than that.</summary>
        private static (Name Table, string Column)? Reads(SelectStatement select)
        {
            var columns = new Columns();
            select.Accept(columns);
            return TSql.TableReferences(select) is [{ SchemaObject: { ServerIdentifier: null, DatabaseIdentifier: null } named } only]
                && named.SchemaIdentifier is { } schema && only.Alias?.Value == "t"
                && columns.Found.FirstOrDefault(c => c.MultiPartIdentifier?.Identifiers is [{ Value: "t" }, _]) is { } column
                && Name.Of(schema.Value, named.BaseIdentifier.Value) is Result<Name>.Ok { Value: var table }
                ? (table, column.MultiPartIdentifier.Identifiers[1].Value) : null;
        }

        /// <summary>The committed form's outermost select list and its fold, as ScriptDom writes them, which name no table or
        /// column.</summary>
        private static readonly Lazy<(IReadOnlyList<string> Items, string Fold)> Outermost = new(() =>
        {
            var form = (QuerySpecification)((SelectStatement)((TSqlScript)TSql.Parse(
                SignatureQuery.Form(Name.Of("dbo").Match(n => n, e => throw new InvalidOperationException(e.Message)), "c"), out _))
                .Batches[0].Statements[0]).QueryExpression;
            return ([.. form.SelectElements.Select(TSql.Text)],
                TSql.Text(((ExpressionGroupingSpecification)form.GroupByClause.GroupingSpecifications[0]).Expression));
        });

        /// <summary>
        /// What a refused signature query is, the first that holds: a part of a value kept; a TRANSLATE whose class map is not the
        /// committed one; a value read without the committed TRANSLATE; signatures without the committed fold; a select list other than the
        /// form's, which can return a value; or another query.
        /// </summary>
        private static string Diagnosis(SelectStatement select)
        {
            var calls = new Calls();
            select.Accept(calls);
            var maps = calls.Found.Where(c => c.FunctionName.Value.Equals("TRANSLATE", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Parameters is [_, StringLiteral from, StringLiteral to] ? (From: from.Value, To: to.Value) : default)
                .Where(m => m != default).ToList();
            var outer = select.QueryExpression as QuerySpecification;
            return calls.Names.FirstOrDefault(Kept.Contains) is { } part
                ? "a part of a value, kept by " + part.ToUpperInvariant()
                : maps.Any(m => m != (Printable, Classes)) ? "a TRANSLATE whose class map is not the committed one"
                : !maps.Contains((Printable, Classes)) ? "a value read without TRANSLATE to the committed character classes"
                : outer?.GroupByClause?.GroupingSpecifications is not [ExpressionGroupingSpecification { Expression: var fold }]
                    || TSql.Text(fold) != Outermost.Value.Fold
                ? "signatures without the committed fold of those seen fewer than five times or holding no letter, digit or other"
                    + " character"
                : !outer.SelectElements.Select(TSql.Text).SequenceEqual(Outermost.Value.Items)
                ? "a select list other than the signature, its rows and the count of signatures, which can return a value"
                : "a query other than the committed signature form";
        }

        private sealed class Columns : TSqlFragmentVisitor
        {
            public List<ColumnReferenceExpression> Found { get; } = [];

            public override void Visit(ColumnReferenceExpression node) => Found.Add(node);
        }

        /// <summary>The functions a tree calls: each call, and each function's name, LEFT and RIGHT among them, which ScriptDom reads as
        /// calls of their own.</summary>
        private sealed class Calls : TSqlFragmentVisitor
        {
            public List<FunctionCall> Found { get; } = [];

            public List<string> Names { get; } = [];

            public override void Visit(FunctionCall node)
            {
                Found.Add(node);
                Names.Add(node.FunctionName.Value);
            }

            public override void Visit(LeftFunctionCall node) => Names.Add("LEFT");

            public override void Visit(RightFunctionCall node) => Names.Add("RIGHT");
        }
    }
}

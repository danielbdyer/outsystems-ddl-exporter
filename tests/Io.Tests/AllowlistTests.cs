using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsCheck;
using Estate.Budgets.Tests;
using Estate.Cli;
using Estate.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// The aggregate-query allowlist (V3_MILESTONES.md WP 1.4, VALUES.md P2): a query is admitted only when it is one SELECT whose
/// outermost select list holds COUNT or COUNT_BIG of * or of DISTINCT a column, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over
/// LEN or DATALENGTH of a column, CASE WHEN EXISTS (…) THEN 1 ELSE 0 END, or an integer literal, with names of one or two parts; any
/// other form is refused, since the allowlist is closed. The committed corpus plants every form the work package names, and CsCheck
/// composes variants of them: nested, aliased, commented, cased, a forbidden form hidden inside an allowed one, and a boundary another
/// row's value sets through a join, a correlated EXISTS, a derived table or arithmetic.
/// </summary>
public sealed class AllowlistTests
{
    public static TheoryData<string> Corpus => new(Cases.Select(c => c.Label));

    /// <summary>The corpus under tests/Golden/aggregate-queries/: each case's kind (admit or refuse), its label and its text.</summary>
    internal static IReadOnlyList<(bool Admitted, string Label, string Text)> Cases { get; } = Read();

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(Corpus))]
    public void The_allowlist_admits_each_allowed_form_of_its_corpus_and_refuses_each_forbidden_one(string label)
    {
        var (admitted, _, text) = Cases.Single(c => c.Label == label);

        var query = SqlServer.AggregateQuery.Of(text, "corpus");

        Assert.True(admitted == query is Result<SqlServer.AggregateQuery>.Ok, (admitted ? "refused: " : "admitted: ") + label + "\n" + query.Match(q => q.Statement, e => e.Message));
        Assert.All(new[] { query }.OfType<Result<SqlServer.AggregateQuery>.Failed>(), r => Assert.Equal(("aggregate-query.refused", 9), (r.Error.Code, Contract.Exit(r.Error))));
    }

    /// <summary>Every form the work package names is planted, forbidden and allowed alike, so a corpus trimmed of one fails here.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_corpus_plants_every_form_the_work_package_names()
    {
        string[] refused = ["STRING_AGG", "MIN over a bare column", "MAX over a bare column", "AVG over a bare column", "SELECT INTO", "EXEC", "INSERT", "UPDATE", "DELETE",
            "MERGE", "TRUNCATE", "CREATE", "ALTER", "DROP", "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "OPENXML", "three-part", "four-part", "two statements",
            "a forbidden function inside an allowed one", "disguised by comments", "bucket boundary read from the data", "a join boundary read from the data",
            "a cross join boundary", "a derived table's column as a length's boundary", "a correlated EXISTS boundary", "written as an equality of their difference",
            "a value computed in a derived table's select list", "a value computed in the select list of IN's subquery"];
        string[] admitted = ["COUNT of every row", "COUNT_BIG of every row", "COUNT of DISTINCT", "COUNT_BIG of DISTINCT", "SUM of CASE", "MIN over LEN", "MAX over LEN",
            "MIN over DATALENGTH", "MAX over DATALENGTH", "CASE WHEN EXISTS", "an integer literal"];

        Assert.DoesNotContain(refused, form => !Cases.Any(c => !c.Admitted && c.Label.Contains(form, StringComparison.Ordinal)));
        Assert.DoesNotContain(admitted, form => !Cases.Any(c => c.Admitted && c.Label.Contains(form, StringComparison.Ordinal)));
        Assert.Equal(Cases.Count, Cases.Select(c => c.Label).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Variants of the corpus's forms, composed: one to three select items, a table, a predicate nested up to three deep, and a
    /// decoration of the statement, each allowed or forbidden, written in any case with any mix of spaces, line breaks and comments
    /// between tokens. A variant is admitted exactly when nothing forbidden went into it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Generated_variants_are_admitted_exactly_when_no_forbidden_form_is_planted()
    {
        var drawn = Variants.Array[400].Single();
        Assert.InRange(drawn.Count(v => v.Admitted), 40, 360);   // both kinds are drawn, so neither half of the property holds vacuously

        // A forbidden variant is refused for its form: it parses, so no variant passes by being unreadable.
        Variants.Sample(variant => SqlServer.AggregateQuery.Of(variant.Text, "variant").Match(_ => variant.Admitted, r => !variant.Admitted && !r.Message.Contains("does not parse", StringComparison.Ordinal)),
            iter: 2000, print: variant => (variant.Admitted ? "allowed but refused: " : "forbidden but admitted, or unparsed: ") + variant.Text);
    }

    /// <summary>A refusal says where the query breaks the allowlist and which form it is; it quotes none of the query's literals, which a caller may have taken from anywhere.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("SELECT MAX(Email) FROM dbo.Customer WHERE Email = N'planted-7f3a';", "line 1, column 8", "MAX over a bare column")]
    [InlineData("SELECT COUNT(*)\nFROM dbo.Customer\nWHERE STRING_AGG(Email, N'planted-7f3a') IS NULL;", "line 3, column 7", "STRING_AGG")]
    [InlineData("SELECT COUNT(*) FROM Orders.dbo.Customer WHERE Email = N'planted-7f3a';", "line 1, column 22", "a name of 3 parts")]
    [InlineData("SELECT COUNT(*) FROM dbo.Customer WHERE Email = N'planted-7f3a'; DELETE FROM dbo.Customer;", "2 statements", "one statement at a time")]
    public void A_refusal_names_the_form_and_its_place_and_quotes_no_literal(string text, string place, string form)
    {
        var error = Assert.IsType<Result<SqlServer.AggregateQuery>.Failed>(SqlServer.AggregateQuery.Of(text, "dbo.Customer.Email NotNull")).Error;

        Assert.Contains(place, error.Message, StringComparison.Ordinal);
        Assert.Contains(form, error.Message + error.Remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("planted", error.Message + error.Remedy, StringComparison.Ordinal);
    }

    /// <summary>What an admitted query runs is the statement the allowlist checked, as ScriptDom writes it back: no comment and no batch separator reaches SQL Server.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_admitted_query_runs_the_statement_it_was_checked_as_without_comments_or_a_batch_separator()
    {
        var query = Assert.IsType<Result<SqlServer.AggregateQuery>.Ok>(SqlServer.AggregateQuery.Of("/* one */ select count_big( * ) -- two\nfrom dbo.Customer ;\nGO\n", "dbo.Customer Presence")).Value;

        Assert.Equal("SELECT count_big(*)\nFROM   dbo.Customer", query.Statement);
        Assert.Equal("dbo.Customer Presence", query.Site);
    }

    /// <summary>
    /// A query M2's builders make as a ScriptDom tree is checked as the tree, without being written as text and read again: the same
    /// query built in code and read from text runs as one statement, byte for byte; and the refusal of a built tree, which carries no
    /// line and column, names the site and the node the allowlist stops at.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_query_built_as_a_tree_is_checked_as_the_tree_and_runs_as_the_same_query_read_from_text()
    {
        var built = Assert.IsType<Result<SqlServer.AggregateQuery>.Ok>(SqlServer.AggregateQuery.Of(Built(Call("COUNT_BIG", new ColumnReferenceExpression { ColumnType = ColumnType.Wildcard })), "dbo.Customer Rows")).Value;
        var read = Assert.IsType<Result<SqlServer.AggregateQuery>.Ok>(SqlServer.AggregateQuery.Of("SELECT COUNT_BIG(*) FROM dbo.Customer;", "dbo.Customer Rows")).Value;
        var refused = Assert.IsType<Result<SqlServer.AggregateQuery>.Failed>(SqlServer.AggregateQuery.Of(Built(Call("MAX", ColumnOf("Email"))), "dbo.Customer.Email Fits")).Error;

        Assert.Equal(read.Statement, built.Statement);
        Assert.Equal(("aggregate-query.refused", 9), (refused.Code, Contract.Exit(refused)));
        Assert.Equal("The query dbo.Customer.Email Fits is refused at its FunctionCall: MAX over a bare column.", refused.Message);
    }

    /// <summary>An object's name as ScriptDom reads it: its parts unquoted, a doubled bracket read as one, and no name past four parts.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("[dbo].[Cust]]omer]", "dbo|Cust]omer")]
    [InlineData("Orders.dbo.Customer", "Orders|dbo|Customer")]
    [InlineData("a.b.c.d.e", null)]
    [InlineData("dbo.", null)]
    public void An_object_s_name_reads_into_its_parts_unquoted(string name, string? parts) =>
        Assert.Equal(parts, TSql.NameParts(name) is { } read ? string.Join('|', read) : null);

    /// <summary>SELECT item FROM dbo.Customer, built in code as M2's builders build a query, so no node carries a line or a column.</summary>
    private static SelectStatement Built(ScalarExpression item) => new()
    {
        QueryExpression = new QuerySpecification
        {
            SelectElements = { new SelectScalarExpression { Expression = item } },
            FromClause = new FromClause
            {
                TableReferences = { new NamedTableReference { SchemaObject = new SchemaObjectName { Identifiers = { new Identifier { Value = "dbo" }, new Identifier { Value = "Customer" } } } } },
            },
        },
    };

    private static FunctionCall Call(string name, ScalarExpression parameter) => new() { FunctionName = new Identifier { Value = name }, Parameters = { parameter } };

    private static ColumnReferenceExpression ColumnOf(string name) => new()
    {
        ColumnType = ColumnType.Regular, MultiPartIdentifier = new MultiPartIdentifier { Identifiers = { new Identifier { Value = name } } },
    };

    private static List<(bool, string, string)> Read()
    {
        var cases = new List<(bool, string, string)>();
        foreach (var line in File.ReadAllLines(Path.Combine(Repository.Root, "tests", "Golden", "aggregate-queries", "allowlist.txt")))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                var (kind, label) = (line[4..].Split(':', 2)[0], line[4..].Split(':', 2)[1].Trim());
                cases.Add((kind == "admit" ? true : kind == "refuse" ? false : throw new InvalidDataException("a case is admit or refuse: " + line), label, ""));
            }
            else if (cases.Count > 0)
            {
                cases[^1] = (cases[^1].Item1, cases[^1].Item2, cases[^1].Item3 + line + "\n");
            }
        }

        return cases;
    }


    /// <summary>A generated query: its text, and whether nothing forbidden went into it.</summary>
    public sealed record Variant(string Text, bool Admitted);

    private static readonly Gen<string[]> Column = Gen.OneOfConst("Email", "Region", "c.Email", "c.Region", "[c].[Name]").Select(c => (string[])[c]);

    /// <summary>A name of three or four parts where a column goes.</summary>
    private static readonly Gen<string[]> Overnamed = Gen.OneOfConst("dbo.Customer.Email", "Orders.dbo.Customer.Email").Select(c => (string[])[c]);

    private static readonly Gen<string> Literal = Gen.OneOf(Gen.Int[0, 500].Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), Gen.OneOfConst("N'West'", "'x@example.invalid'"));

    /// <summary>A comparison the allowlist admits, over the columns given: null tests, lengths against literals, LIKE, TRY_CONVERT, IN, EXISTS, and = or &lt;&gt; against another column.</summary>
    private static Gen<string[]> Comparison(Gen<string[]> column) => Gen.OneOf(
        column.Select(c => (string[])[.. c, "IS", "NULL"]),
        Gen.Select(column, Literal).Select((c, n) => (string[])["LEN", "(", .. c, ")", ">", n]),
        column.Select(c => (string[])[.. c, "LIKE", "N'%@%'"]),
        column.Select(c => (string[])["TRY_CONVERT", "(", "int", ",", .. c, ")", "IS", "NULL"]),
        Gen.Select(column, Literal).Select((c, n) => (string[])[.. c, "IN", "(", n, ",", "0", ")"]),
        Gen.Select(column, Gen.OneOfConst("=", "<>", "!=")).Select((c, op) => (string[])[.. c, op, "a.Region"]),
        Gen.Const((string[])["EXISTS", "(", "SELECT", "1", "FROM", "dbo.Account", "AS", "a", "WHERE", "a.Id", "=", "c.AccountId", ")"]));

    /// <summary>
    /// A comparison the allowlist refuses: a name of too many parts, CONVERT, STRING_AGG inside LEN, a variable; and a boundary read
    /// from the data, a subquery's value, another column's or a length against a column, even written as an equality of a difference.
    /// </summary>
    private static readonly Gen<string[]> Forbidden = Gen.OneOf(
        Comparison(Overnamed).Where(c => c[0] != "EXISTS"),
        Column.Select(c => (string[])["LEN", "(", .. c, ")", ">", "(", "SELECT", "MAX", "(", "LEN", "(", "Email", ")", ")", "FROM", "dbo.Customer", ")"]),
        Column.Select(c => (string[])["CONVERT", "(", "int", ",", .. c, ")", ">", "0"]),
        Column.Select(c => (string[])["LEN", "(", "STRING_AGG", "(", .. c, ",", "N','", ")", ")", ">", "0"]),
        Column.Select(c => (string[])[.. c, ">", "@x"]),
        Gen.Select(Column, Gen.OneOfConst("<", "<=", ">", ">=", "!<", "!>")).Select((c, op) => (string[])[.. c, op, "a.Balance"]),
        Column.Select(c => (string[])[.. c, "BETWEEN", "1", "AND", "a.Balance"]),
        Column.Select(c => (string[])[.. c, "<=", "TRY_CONVERT", "(", "int", ",", "a.Phone", ")"]),
        Column.Select(c => (string[])["LEN", "(", .. c, ")", ">", "d.m"]),
        Column.Select(c => (string[])["ABS", "(", .. c, "-", "a.Balance", ")", "+", .. c, "-", "a.Balance", "=", "0"]),
        Column.Select(c => (string[])["NULLIF", "(", .. c, ",", "a.Balance", ")", "IS", "NULL"]));

    /// <summary>The predicate given, inside up to <paramref name="depth"/> admitted layers: NOT, AND or OR beside an admitted comparison, EXISTS around it.</summary>
    private static Gen<string[]> Nested(Gen<string[]> inner, int depth) => depth == 0 ? inner : Gen.OneOf(
        inner,
        Nested(inner, depth - 1).Select(p => (string[])["NOT", "(", .. p, ")"]),
        Gen.Select(Nested(inner, depth - 1), Comparison(Column), Gen.Bool, Gen.OneOfConst("AND", "OR")).Select((p, q, first, and) =>
            first ? (string[])["(", .. p, ")", and, "(", .. q, ")"] : ["(", .. q, ")", and, "(", .. p, ")"]),
        Nested(inner, depth - 1).Select(p => (string[])["EXISTS", "(", "SELECT", "1", "FROM", "dbo.Account", "AS", "a", "WHERE", .. p, ")"]));

    private static readonly Gen<string[]> Predicate = Nested(Comparison(Column), 3);

    /// <summary>A forbidden comparison, deep inside admitted layers: a forbidden form inside an allowed one.</summary>
    private static readonly Gen<string[]> ForbiddenPredicate = Nested(Forbidden, 3);

    /// <summary>The items that carry a predicate: SUM of CASE, and CASE WHEN EXISTS.</summary>
    private static Gen<string[]> Counting(Gen<string[]> predicate) => Gen.OneOf(
        predicate.Select(p => (string[])["SUM", "(", "CASE", "WHEN", .. p, "THEN", "1", "ELSE", "0", "END", ")"]),
        predicate.Select(p => (string[])["CASE", "WHEN", "EXISTS", "(", "SELECT", "1", "FROM", "dbo.Customer", "AS", "c", "WHERE", .. p, ")", "THEN", "1", "ELSE", "0", "END"]));

    /// <summary>The items that carry a column: COUNT or COUNT_BIG of DISTINCT it, MIN or MAX over its LEN or DATALENGTH.</summary>
    private static Gen<string[]> Measuring(Gen<string[]> column) => Gen.OneOf(
        Gen.Select(Gen.OneOfConst("COUNT", "COUNT_BIG"), column).Select((f, c) => (string[])[f, "(", "DISTINCT", .. c, ")"]),
        Gen.Select(Gen.OneOfConst("MIN", "MAX"), Gen.OneOfConst("LEN", "DATALENGTH"), column).Select((m, l, c) => (string[])[m, "(", l, "(", .. c, ")", ")"]));

    /// <summary>An item in up to two pairs of parentheses, with an alias or none.</summary>
    private static Gen<string[]> Written(Gen<string[]> item) =>
        Gen.Select(item, Gen.Int[0, 2], Gen.OneOfConst("", "AS n", "AS [rows]", "total")).Select((i, parentheses, alias) =>
            (string[])[.. Enumerable.Repeat("(", parentheses), .. i, .. Enumerable.Repeat(")", parentheses), .. alias.Split(' ', StringSplitOptions.RemoveEmptyEntries)]);

    private static readonly Gen<string[]> Item = Written(Gen.OneOf(
        Counting(Predicate),
        Measuring(Column),
        Gen.OneOfConst("COUNT", "COUNT_BIG").Select(f => (string[])[f, "(", "*", ")"]),
        Gen.Int[0, 9999].Select(n => (string[])[n.ToString(System.Globalization.CultureInfo.InvariantCulture)])));

    private static readonly Gen<string[]> ForbiddenItem = Written(Gen.OneOf(
        Counting(ForbiddenPredicate),
        Measuring(Overnamed),
        Gen.Select(Gen.OneOfConst("MIN", "MAX", "AVG", "SUM", "COUNT"), Column).Select((f, c) => (string[])[f, "(", .. c, ")"]),
        Column.Select(c => (string[])["STRING_AGG", "(", .. c, ",", "N','", ")"]),
        Column.Select(c => (string[])["MAX", "(", "LEN", "(", "STRING_AGG", "(", .. c, ",", "N','", ")", ")", ")"]),
        Column,
        Gen.Const((string[])["N'x'"])));

    private static readonly Gen<string[]> Table = Gen.OneOfConst("dbo.Customer AS c", "[dbo].[Customer] AS c", "Customer AS c", "dbo.Customer c",
        "dbo.Customer AS c LEFT OUTER JOIN dbo.Account AS a ON a.Id = c.AccountId", "dbo.Customer AS c CROSS JOIN ( SELECT Id , MAX ( LEN ( Name ) ) AS m FROM dbo.Account GROUP BY Id ) AS d")
        .Select(t => t.Split(' '));

    /// <summary>A table the allowlist refuses: a name of too many parts, a hint, a rowset function; a join on a boundary read from the data; a value computed in a derived table.</summary>
    private static readonly Gen<string[]> ForbiddenTable = Gen.OneOfConst(
        "Orders.dbo.Customer AS c", "Linked.Orders.dbo.Customer AS c", "dbo.Customer AS c WITH (NOLOCK)",
        "OPENROWSET ( 'SQLNCLI' , 'Server=elsewhere;Trusted_Connection=yes;' , 'SELECT 1' ) AS c", "OPENQUERY ( Linked , 'SELECT 1' ) AS c",
        "OPENDATASOURCE ( 'SQLNCLI' , 'Data Source=elsewhere' ).Orders.dbo.Customer AS c", "OPENXML ( @doc , N'/root' , 1 ) AS c",
        "dbo.Customer AS c INNER JOIN dbo.Account AS a ON c.Id <= a.Balance", "dbo.Customer AS c JOIN dbo.Account AS a ON c.Id BETWEEN 1 AND a.Balance",
        "dbo.Customer AS c INNER JOIN dbo.Account AS a ON a.Id = c.AccountId AND c.Id <= TRY_CONVERT ( int , a.Phone )",
        "dbo.Customer AS c INNER JOIN ( SELECT MAX ( LEN ( Email ) ) AS m FROM dbo.Customer ) AS d ON LEN ( c.Email ) > d.m",
        "( SELECT c.Id - a.Balance AS m FROM dbo.Customer AS c CROSS JOIN dbo.Account AS a ) AS c",
        "dbo.Customer AS c CROSS JOIN ( SELECT TOP 1 ABS ( Balance ) AS m FROM dbo.Account ) AS d").Select(t => t.Split(' '));

    /// <summary>A form that turns the query into something else, and whether it follows the select list (INTO) or the statement.</summary>
    private static readonly Gen<(string[] Tokens, bool AfterItems)> Decoration = Gen.OneOf(
        Gen.OneOfConst("INTO #leak", "INTO dbo.Leak").Select(into => (into.Split(' '), true)),
        Gen.OneOfConst("; SELECT 1", "; DELETE FROM dbo.Customer", "; EXEC sp_who", "OPTION ( MAXDOP 1 )", "FOR XML PATH", "UNION ALL SELECT 1", "\nGO\nSELECT 1")
            .Select(after => (after.Split(' '), false)));

    /// <summary>What may stand between two tokens: space, a line break, a tab, a block comment, or a line comment and its line break.</summary>
    private static readonly string[] Gaps = [" ", " ", " ", "\n", "\t", "  ", " /* note */ ", "/**/", " -- note\n", "\r\n"];

    /// <summary>
    /// A query of admitted forms, and in half the draws exactly one slot given a forbidden form instead: one of the select items, the
    /// table, the WHERE clause, or a decoration of the statement.
    /// </summary>
    private static readonly Gen<Variant> Variants = Gen.Select(
        Gen.Int[0, 7], Gen.Select(Item.Array[1, 3], Gen.Int[0, 2], ForbiddenItem), Gen.Select(Table, ForbiddenTable), Gen.Select(Predicate, ForbiddenPredicate, Gen.Bool),
        Decoration, Gen.Int[0, 1 << 20].Array[128]).Select((slot, items, tables, predicates, decoration, noise) =>
        {
            var (allowed, at, forbidden) = items;
            string[][] selected = [.. allowed.Select((item, i) => slot == 4 && i == at % allowed.Length ? forbidden : item)];
            var filtered = predicates.Item3 || slot == 6;
            string[] tokens =
            [
                "SELECT", .. selected.SelectMany((item, i) => i == 0 ? item : [",", .. item]),
                .. slot == 7 && decoration.AfterItems ? decoration.Tokens : [],
                "FROM", .. slot == 5 ? tables.Item2 : tables.Item1,
                .. filtered ? ["WHERE", .. slot == 6 ? predicates.Item2 : predicates.Item1] : Array.Empty<string>(),
                .. slot == 7 && !decoration.AfterItems ? decoration.Tokens : [],
            ];
            return new Variant(Written(tokens, noise), slot < 4);
        });

    /// <summary>The tokens with a gap the noise picks between each two, and each word in the case the noise picks; a literal, a variable or a temporary table as it is.</summary>
    private static string Written(IReadOnlyList<string> tokens, int[] noise) => string.Concat(tokens.Select((token, i) =>
    {
        var draw = noise[i % noise.Length];
        var cased = token.Contains('\'', StringComparison.Ordinal) || token.StartsWith('@') || token.StartsWith('#') ? token
            : (draw / Gaps.Length % 3) switch
            {
                0 => token.ToUpperInvariant(),
                1 => token.ToLowerInvariant(),
                _ => new string([.. token.Select((ch, k) => k % 2 == 0 ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch))]),
            };
        return (i == 0 ? "" : Gaps[draw % Gaps.Length]) + cased;
    }));
}

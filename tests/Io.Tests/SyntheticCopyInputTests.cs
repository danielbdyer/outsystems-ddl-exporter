using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// What synthetic-copy up reads before it reaches the local server (WP 3.5): the environment's existing data as a file,
/// dbchange.existing-data/1, which holds names and booleans and no value of any row; the change's declared witnesses,
/// dbchange/witnesses.sql, DML in blocks under header lines, each literal synthetic or the repository's own; and step 7's acceptance,
/// which requires the environment's answers one by one, every no included.
/// </summary>
public sealed class SyntheticCopyInputTests
{
    /// <summary>The literals a repository's model defines: a seeded name, a check's LIKE pattern and a default's bytes.</summary>
    private static readonly IReadOnlySet<string> Repository = new HashSet<string>(StringComparer.Ordinal)
    {
        "Acme Holdings", "ABC%", "0x0A0B",
    };

    /// <summary>An existing data holding each kind of precondition, a Fits and a Unique with their classes, a clean and an empty answer,
    /// and a constraint held disabled and one held untrusted.</summary>
    private static readonly ExistingData EveryKind = new(
    [
        State(On("Customer", new PreconditionKind.TableEmpty()), true, true),
        State(On("Customer", new PreconditionKind.NotNull(), "Email"), true, false),
        State(On("Customer", new PreconditionKind.Fits(Type(50, "SQL_Latin1_General_CP1_CI_AS"), Type(7, null)), "Region"), true, true)
            with { Classes = RowClasses.OverLength | RowClasses.OverBytes },
        State(On("CustomerAddress", new PreconditionKind.ForeignKey(Value(Name.Of("dbo", "Customer")),
            [new PreconditionKind.ForeignKey.Pair(Column("CustomerId"), Column("Id"))]), "CustomerId"), true, true),
        State(On("Product", new PreconditionKind.Unique("([CategoryId] IS NOT NULL)", [Column("CategoryId")]), "Code"), true, true)
            with { Classes = RowClasses.Exact | RowClasses.Case },
        State(On("Order", new PreconditionKind.Check("([Total]>=(0))"), "Total"), false, false),
    ],
    [
        new TrustState(Value(ElementKey.Of("ForeignKeyConstraint", Value(Name.Of("dbo", "FK_A")))), true, true),
        new TrustState(Value(ElementKey.Of("CheckConstraint", Value(Name.Of("dbo", "CK_B")))), false, true),
    ]);

    /// <summary>
    /// The file dbchange writes reads back as the existing data it was written from, each kind's fields and each class whole; and one
    /// existing data has one fingerprint, however its file orders its keys or spaces its text, since the name of the synthetic copy
    /// carries it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_existing_data_file_reads_back_as_the_existing_data_it_was_written_from_and_its_fingerprint_is_its_content_s()
    {
        var text = ExistingDataFile.Text(EveryKind);
        var reordered = "{\n  \"trust\": " + System.Text.Json.Nodes.JsonNode.Parse(text)!["trust"]!.ToJsonString()
            + ",\n  \"states\": " + System.Text.Json.Nodes.JsonNode.Parse(text)!["states"]!.ToJsonString()
            + ",\n  \"schema\": \"dbchange.existing-data/1\"\n}";

        var read = Value(ExistingDataFile.Parse(text, "existing-data.json"));
        var again = Value(ExistingDataFile.Parse(reordered, "existing-data.json"));

        Assert.Equal(EveryKind, read);
        Assert.Equal(EveryKind, again);
        Assert.Equal(ExistingDataFile.FingerprintOf(EveryKind), ExistingDataFile.FingerprintOf(again));
    }

    /// <summary>
    /// A file that breaks the schema is existing-data.malformed, naming the JSON path and what is wrong there and quoting no value: a key
    /// the schema does not name, another schema, a state no table can hold (a TableEmpty answered violating otherwise than populated, a
    /// violation on a table that holds no row, a class of another kind), a disabled constraint read as trusted, a precondition given
    /// twice, and text that is no JSON.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("{ \"schema\": \"dbchange.existing-data/1\", \"states\": [], \"Pa55!planted#7f3a\": 1 }",
        "$ holds a key the schema does not name.")]
    [InlineData("{ \"schema\": \"dbchange.existing-data/2\", \"states\": [] }", "$.schema is not dbchange.existing-data/1.")]
    [InlineData("{ \"schema\": \"dbchange.existing-data/1\", \"states\": [ { \"precondition\": { \"kind\": \"TableEmpty\", \"table\": "
        + "\"[dbo].[T]\" }, \"populated\": true, \"violating\": false } ] }",
        "$.states[0] answers a TableEmpty violating otherwise than populated, which BlockOnPossibleDataLoss reads as one.")]
    [InlineData("{ \"schema\": \"dbchange.existing-data/1\", \"states\": [ { \"precondition\": { \"kind\": \"NotNull\", \"table\": "
        + "\"[dbo].[T]\", \"columns\": [\"[C]\"] }, \"populated\": false, \"violating\": true } ] }",
        "$.states[0] answers a precondition violating on a table that holds no row.")]
    [InlineData("{ \"schema\": \"dbchange.existing-data/1\", \"states\": [ { \"precondition\": { \"kind\": \"NotNull\", \"table\": "
        + "\"[dbo].[T]\", \"columns\": [\"[C]\"] }, \"populated\": true, \"violating\": true, \"classes\": [\"exact\"] } ] }",
        "$.states[0].classes[0] is no class io/AggregateQueries.cs counts for the precondition's kind.")]
    [InlineData("{ \"schema\": \"dbchange.existing-data/1\", \"states\": [], \"trust\": [ { \"constraint\": \"[dbo].[FK_T]\", "
        + "\"type\": \"ForeignKeyConstraint\", \"disabled\": true, \"untrusted\": false } ] }",
        "$.trust[0] reads a disabled constraint as trusted, which SQL Server never holds.")]
    [InlineData("{ \"schema\": \"dbchange.existing-data/1\", \"states\": [ { \"precondition\": { \"kind\": \"TableEmpty\", \"table\": "
        + "\"[dbo].[T]\" }, \"populated\": false, \"violating\": false }, { \"precondition\": { \"kind\": \"TableEmpty\", \"table\": "
        + "\"[dbo].[T]\" }, \"populated\": true, \"violating\": true } ] }", "$.states gives a state of one precondition twice.")]
    [InlineData("{ \"schema\": ", "is not JSON at line 1")]
    public void An_existing_data_file_that_breaks_its_schema_is_malformed_naming_the_place_and_quoting_no_value(string text, string said)
    {
        var error = Failed(ExistingDataFile.Parse(text, "existing-data.json"), "existing-data.malformed");

        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        Assert.StartsWith("existing-data.json ", error.Message, StringComparison.Ordinal);
        PlantedValue.Password.AbsentFrom(error);
    }

    /// <summary>
    /// The declared witnesses are blocks under header lines: -- plants: names a table whose rows the block plants whole, -- witness:
    /// names a precondition as dbchange writes it, and each block holds the lines up to the next header, from the line after its own.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_declared_witnesses_are_blocks_under_header_lines_each_naming_a_table_or_a_precondition()
    {
        var blocks = Value(Witnesses.Parse(
            "-- plants: [dbo].[Account]\nINSERT INTO dbo.Account (Name) VALUES (N'wA');\n\n"
                + "-- witness: Unique on [dbo].[Account] ([Name])\nINSERT INTO dbo.Account (Name) VALUES (N'Acme Holdings');\n",
            Repository));

        Assert.Equal([(1, "[dbo].[Account]", null, 2), (4, null, "Unique on [dbo].[Account] ([Name])", 5)],
            blocks.Select(b => (b.Line, b.Plants?.ToString(), b.Witness, b.FirstLine)));
        Assert.Equal("INSERT INTO dbo.Account (Name) VALUES (N'Acme Holdings');\n", blocks[1].Text);
    }

    /// <summary>
    /// PAR-033: each statement or literal a declared witness may not carry is refused by its line of dbchange/witnesses.sql: a statement
    /// before any header, a header naming no table or no precondition, a block that does not parse, a second batch, a statement other
    /// than DML, an INSERT of EXEC's rows, a read through OPENROWSET, and a literal neither synthetic nor the repository's.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("INSERT INTO dbo.T (C) VALUES (N'wA');\n-- plants: [dbo].[T]\n", "witness.outside-block", 1)]
    [InlineData("-- plants: [a].[b].[c]\nDELETE FROM dbo.T;", "witness.header", 1)]
    [InlineData("-- plants: [dbo].[T]\nDELETE FROM dbo.T;\n-- witness:\nDELETE FROM dbo.T;", "witness.header", 3)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C VALUES (1);", "witness.unparsed", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) VALUES (N'wA');\nGO\nDELETE FROM dbo.T;", "witness.batch", 4)]
    [InlineData("-- plants: [dbo].[T]\n\nCREATE TABLE dbo.U (Id int);", "witness.not-dml", 3)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) EXEC dbo.Rows;", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) SELECT r.BulkColumn FROM OPENROWSET(BULK N'wB', SINGLE_CLOB) AS r;",
        "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nUPDATE dbo.T SET C = N'wA';\nUPDATE dbo.T\n  SET C = N'Bob';", "witness.not-synthetic", 4)]
    public void A_declared_witness_is_refused_by_its_line_for_each_statement_or_literal_it_may_not_carry(string text, string code,
        int line)
    {
        var error = Failed(Witnesses.Parse(text, Repository), code);

        Assert.StartsWith("dbchange/witnesses.sql line " + line + " ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// VALUES.md P1 and law 12′ in its planted form: a declared literal enters a copy when it is empty, carries the planter's mark w, is
    /// a literal the repository's model defines (or one under a LIKE pattern it defines, the wildcards dropped, then the mark), is
    /// written as a number, an ISO date or time, or one of the planter's two uniqueidentifier forms, or is binary that is empty, begins
    /// with 0x77, or the model defines. Any other value can have come from an environment, and is refused without being quoted.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P1")]
    [InlineData("N'wAnything'", true)]
    [InlineData("N''", true)]
    [InlineData("N'Acme Holdings'", true)]
    [InlineData("N'ABCw1'", true)]
    [InlineData("N'42'", true)]
    [InlineData("'2026-10-20'", true)]
    [InlineData("'2026-10-20T10:00:00.1234567'", true)]
    [InlineData("'12:30:00'", true)]
    [InlineData("'77777777-0000-4000-8000-000000000001'", true)]
    [InlineData("'FFFFFFFF-FFFF-4FFF-BFFF-000000000001'", true)]
    [InlineData("0x77AB", true)]
    [InlineData("0x", true)]
    [InlineData("0x0a0b", true)]
    [InlineData("N'Bob'", false)]
    [InlineData("N'Acme'", false)]
    [InlineData("N'acme holdings'", false)]
    [InlineData("N'ABC1'", false)]
    [InlineData("0x0102", false)]
    [InlineData("N'Pa55!planted#7f3a'", false)]
    public void A_declared_literal_enters_the_copy_only_when_it_is_synthetic_or_the_repository_s_own(string literal, bool admitted)
    {
        var parsed = Witnesses.Parse("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) VALUES (" + literal + ");", Repository);

        Assert.Equal(admitted, parsed is Result<IReadOnlyList<Witnesses.Block>>.Ok);
        if (!admitted)
        {
            PlantedValue.Password.AbsentFrom(Failed(parsed, "witness.not-synthetic"));
        }
    }

    /// <summary>
    /// Step 7 (the Facsimile's F1): the copy answers each precondition the environment answered as the environment did, every no
    /// included: a violation the copy lacks, one it adds, a table it fills that the environment holds empty, and classes it holds beyond
    /// those the environment names are each refused, naming the precondition; classes the environment names none of are not compared;
    /// a precondition the copy's own plan lacks, and one it counted no answer to, are refused with what each means.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Acceptance_requires_each_answer_the_environment_gave_every_no_included()
    {
        var (lacks, adds, fills, classes, unnamed, kept, absent, uncounted) = (On("A", new PreconditionKind.NotNull(), "C"),
            On("B", new PreconditionKind.NotNull(), "C"), On("C", new PreconditionKind.TableEmpty()),
            On("D", new PreconditionKind.Unique(null, []), "C"), On("E", new PreconditionKind.Unique(null, []), "C"),
            On("F", new PreconditionKind.NotNull(), "C"), On("G", new PreconditionKind.NotNull(), "C"),
            On("H", new PreconditionKind.NotNull(), "C"));
        var environment = new ExistingData(
        [
            State(lacks, true, true), State(adds, true, false), State(fills, false, false),
            State(classes, true, true) with { Classes = RowClasses.Exact }, State(unnamed, true, true), State(kept, true, false),
            State(absent, true, true), State(uncounted, true, false),
        ]);
        SortedArray<Precondition> asked = [lacks, adds, fills, classes, unnamed, kept, uncounted];
        SortedArray<PreconditionState> copy =
        [
            State(lacks, true, false), State(adds, true, true), State(fills, true, true),
            State(classes, true, true) with { Classes = RowClasses.Exact | RowClasses.Case },
            State(unnamed, true, true) with { Classes = RowClasses.Case }, State(kept, true, false),
        ];

        var refused = SyntheticCopy.Accepted(environment, asked, copy);

        Assert.Equal(["NotNull on [dbo].[A] ([C])", "NotNull on [dbo].[B] ([C])", "TableEmpty on [dbo].[C]", "Unique on [dbo].[D] ([C])",
                "NotNull on [dbo].[G] ([C])", "NotNull on [dbo].[H] ([C])"],
            refused.Select(f => f.Subject));
        Assert.All(refused, f => Assert.Equal(("witness.acceptance", Severity.Error), (f.Code, f.Severity)));
        Assert.Equal("The copy answers NotNull on [dbo].[A] ([C]) populated and clean, where the environment answered it populated and "
            + "violating.", refused[0].Message);
        Assert.StartsWith("Plant a row that breaks it", refused[0].Remedy, StringComparison.Ordinal);
        Assert.StartsWith("Correct the rows dbchange/witnesses.sql declares", refused[1].Remedy, StringComparison.Ordinal);
        Assert.EndsWith("populated and violating (exact).", refused[3].Message, StringComparison.Ordinal);
        Assert.Contains("holds no such precondition", refused[4].Message, StringComparison.Ordinal);
        Assert.Contains("counts no answer to it", refused[5].Message, StringComparison.Ordinal);
    }

    private static PreconditionState State(Precondition precondition, bool populated, bool violating) =>
        new(precondition, populated, violating);

    private static Precondition On(string table, PreconditionKind kind, params string[] columns) =>
        new(Value(Name.Of("dbo", table)), SortedArray.Of(columns.Select(Column)), kind);

    private static Name Column(string name) => Value(Name.Of(name));

    private static ColumnType Type(long length, string? collation) => new(Column("nvarchar"), length, false, 0, 0, collation);
}

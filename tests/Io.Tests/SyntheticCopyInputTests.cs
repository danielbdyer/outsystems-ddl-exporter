using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;
using Elements = DbChange.Kernel.Tests.KernelProperties;
using CaseModel = DbChange.Kernel.Tests.Model;

namespace DbChange.Io.Tests;

/// <summary>
/// What synthetic-copy up reads before it reaches the local server (WP 3.5): the environment's existing data as a file,
/// dbchange.existing-data/1, which holds names and booleans and no value of any row; the change's declared witnesses,
/// dbchange/witnesses.sql, DML in blocks under header lines, each literal synthetic or the repository's own; and step 7's acceptance,
/// which requires the environment's answers one by one, every no included.
/// </summary>
public sealed class SyntheticCopyInputTests
{
    /// <summary>
    /// The repository a declared witness is held to: a copy whose dbo.T holds C nvarchar(40), N int and B varbinary(16), B's default
    /// writing 0x0A0B; a check of dbo.Product.Code LIKE 'ABC%', and one of dbo.[Order].Status in the OR form SQL Server stores for IN
    /// ('O', 'C'); a seed of the name N'Acme Holdings' and the postal code N'98101'; and the synonym dbo.Remote.
    /// </summary>
    private static readonly Witnesses.Repository Repository = Witnesses.Repository.Of(
        [
            .. Copy.Elements,
            Element.PostDeploy("MERGE dbo.Account AS t USING (VALUES (N'Acme Holdings')) AS s (Name) ON t.Name = s.Name\n"
                + "WHEN NOT MATCHED THEN INSERT (Name) VALUES (s.Name);\nINSERT INTO dbo.Address (PostalCode) VALUES (N'98101');"),
            Elements.New(Elements.Key("Synonym", "dbo", "Remote"), []),
        ],
        Value(Schema.Of(Copy.Elements)));

    private static CaseModel Copy => CaseModel.Of("T.C nvarchar(40) NULL", "T.N int NULL", "T.B varbinary(16) NULL",
            "Product.Code nvarchar(20) NULL", "Order.Status char(1) NULL")
        .Default("DF_T_B", "T", "B", "(0x0A0B)").Check("CK_Product_Code", "Product", "([Code] like 'ABC%')", ["Code"])
        .Check("CK_Order_Status", "Order", "([Status]='C' OR [Status]='O')", ["Status"]);

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
        "$.states[0].classes[0] is no class a state of the precondition's kind holds.")]
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
    /// than DML, an INSERT of EXEC's rows, a read through OPENROWSET, a table on a linked server, as the INSERT's target, the source of
    /// an INSERT ... SELECT or MERGE's USING, a table or a function in another database of the local server, the repository's synonym
    /// dbo.Remote as a source, as a target named in one part and as a function named in another case, OPENQUERY as an INSERT's source
    /// and as an INSERT's, an UPDATE's and a DELETE's target, OPENDATASOURCE as an INSERT's target, OPENROWSET with a provider as a
    /// source and as an INSERT's target, OUTPUT INTO a table on a linked server, and a literal neither synthetic nor the repository's.
    /// The block runs as one batch on the copy's connection, so a four-part name, a rowset function, or a synonym standing for a name on
    /// another server, would write to a linked environment or copy its rows into the copy.
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
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO [prod-sql].[Catalog].[dbo].[Customer] (Name) VALUES (N'w1');", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nDELETE FROM dbo.T;\nINSERT INTO dbo.T (Name, Email)\n  SELECT Name, Email FROM [prod-sql].[Catalog]"
        + ".[dbo].[Customer];", "witness.not-dml", 3)]
    [InlineData("-- plants: [dbo].[T]\nMERGE dbo.T AS t USING [prod-sql].[Catalog].[dbo].[Customer] AS s ON t.Id = s.Id\n"
        + "  WHEN NOT MATCHED THEN INSERT (Name) VALUES (s.Name);", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nUPDATE dbo.T SET C = N'wA';\nINSERT INTO dbo.T (Name) SELECT Name FROM OtherDb.dbo.Customer;",
        "witness.not-dml", 3)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) VALUES (OtherDb.dbo.Secret(1));", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C)\n  SELECT r.C FROM dbo.Remote AS r;", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nUPDATE dbo.T SET C = N'wA';\nINSERT INTO Remote (C) VALUES (N'wB');", "witness.not-dml", 3)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (N) VALUES (dbo.REMOTE(1));", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) SELECT q.C FROM OPENQUERY([prod-sql], 'SELECT C FROM Catalog.dbo.Customer') AS q;",
        "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO OPENQUERY([prod-sql], 'SELECT C FROM Catalog.dbo.Customer') VALUES (N'wA');",
        "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nUPDATE OPENQUERY([prod-sql], 'SELECT C FROM Catalog.dbo.Customer') SET C = N'wA';",
        "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nDELETE OPENQUERY([prod-sql], 'SELECT C FROM Catalog.dbo.Customer');", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO OPENDATASOURCE('MSOLEDBSQL', 'Server=prod-sql;Trusted_Connection=yes;').Catalog.dbo.Customer "
        + "(Name) VALUES (N'wA');", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO dbo.T (C) SELECT r.Name FROM OPENROWSET('MSOLEDBSQL', "
        + "'Server=prod-sql;Trusted_Connection=yes;', 'SELECT Name FROM Catalog.dbo.Customer') AS r;", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nINSERT INTO OPENROWSET('MSOLEDBSQL', 'Server=prod-sql;Trusted_Connection=yes;', "
        + "'SELECT Name FROM Catalog.dbo.Customer') VALUES (N'wA');", "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nDELETE FROM dbo.T OUTPUT deleted.C INTO [prod-sql].[Catalog].[dbo].[Customer] (Name);",
        "witness.not-dml", 2)]
    [InlineData("-- plants: [dbo].[T]\nUPDATE dbo.T SET C = N'wA';\nUPDATE dbo.T\n  SET C = N'Bob';", "witness.not-synthetic", 4)]
    public void A_declared_witness_is_refused_by_its_line_for_each_statement_or_literal_it_may_not_carry(string text, string code,
        int line)
    {
        var error = Failed(Witnesses.Parse(text, Repository), code);

        Assert.StartsWith("dbchange/witnesses.sql line " + line + " ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// VALUES.md P1 and law 12′ in its planted form, a literal written into the character column dbo.T.C: it enters a copy when it is
    /// empty, carries the planter's mark w, is a literal the repository's model defines, begins with the text a LIKE pattern of the
    /// model holds before its first wildcard and then the mark (N'ABCw1' under LIKE 'ABC%'; the 'O' the model's check lists is no
    /// pattern, so N'O' enters and N'Owen Jones' does not), is written as an ISO date or time or as one of the planter's two
    /// uniqueidentifier forms, or is binary that is empty, begins with 0x77, or the model defines. A string of digits, a phone or a card
    /// number among them, enters only where the model defines it, written in quotes or without them. Any other value can have come from
    /// an environment, and is refused without being quoted.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P1")]
    [InlineData("N'wAnything'", true)]
    [InlineData("N''", true)]
    [InlineData("N'Acme Holdings'", true)]
    [InlineData("N'ABCw1'", true)]
    [InlineData("N'O'", true)]
    [InlineData("N'Owen Jones'", false)]
    [InlineData("N'Ow1'", false)]
    [InlineData("N'98101'", true)]
    [InlineData("98101", true)]
    [InlineData("42", false)]
    [InlineData("N'42'", false)]
    [InlineData("N'2065550101'", false)]
    [InlineData("N'+12065550101'", false)]
    [InlineData("'4111111111111111'", false)]
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
    /// VALUES.md P1 for a number, which carries no mark (PLT-124): one written into dbo.T's int column N stays a number and enters, but
    /// SQL Server stores one written into its nvarchar column C or its varbinary column B as text or bytes, so there it enters only where
    /// its digits are a literal the model defines, as the postal code 98101 is. So it is for the value an INSERT writes by its column
    /// list or, with none, by the table's columns in order; an UPDATE's SET, under an alias, a sign or parentheses; a MERGE's INSERT and
    /// UPDATE, through the VALUES list it reads; an INSERT ... SELECT, through a derived table or a common table expression; and the
    /// operand of a CONVERT or a CAST to a character type, or an argument of CONCAT, wherever it stands. A length REPLICATE takes and a
    /// number a WHERE compares make no text. A number refused is named by its line and not quoted.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P1")]
    [InlineData("INSERT INTO dbo.T (N) VALUES (2065550101);", true)]
    [InlineData("INSERT INTO dbo.T (C) VALUES (2065550101);", false)]
    [InlineData("INSERT INTO dbo.T (C) VALUES (98101);", true)]
    [InlineData("INSERT INTO dbo.T (B) VALUES (2065550101);", false)]
    [InlineData("INSERT INTO dbo.T VALUES (N'wA', 2065550101, NULL);", true)]
    [InlineData("INSERT INTO dbo.T VALUES (2065550101, 1, NULL);", false)]
    [InlineData("UPDATE dbo.T SET N = 2065550101, C = N'wA' WHERE N = 4111111111111111;", true)]
    [InlineData("UPDATE t SET t.C = (-2065550101) FROM dbo.T AS t;", false)]
    [InlineData("MERGE dbo.T AS t USING (VALUES (7, N'wA')) AS s (Id, Name) ON t.N = s.Id\n"
        + "  WHEN NOT MATCHED THEN INSERT (N, C) VALUES (s.Id, s.Name);", true)]
    [InlineData("MERGE dbo.T AS t USING (VALUES (7, 2065550101)) AS s (Id, Phone) ON t.N = s.Id\n"
        + "  WHEN NOT MATCHED THEN INSERT (N, C) VALUES (s.Id, s.Phone);", false)]
    [InlineData("MERGE dbo.T AS t USING (VALUES (7, 2065550101)) AS s (Id, Phone) ON t.N = s.Id\n"
        + "  WHEN MATCHED THEN UPDATE SET C = s.Phone;", false)]
    [InlineData("INSERT INTO dbo.T (C) SELECT 4111111111111111;", false)]
    [InlineData("INSERT INTO dbo.T (C) SELECT v.p FROM (VALUES (4111111111111111)) AS v (p);", false)]
    [InlineData("WITH v (p) AS (SELECT 4111111111111111) INSERT INTO dbo.T (C) SELECT p FROM v;", false)]
    [InlineData("INSERT INTO dbo.T (C) VALUES (CONVERT(nvarchar(20), 4111111111111111));", false)]
    [InlineData("INSERT INTO dbo.T (N) VALUES (CAST(CAST(4111111111111111 AS varchar(20)) AS bigint));", false)]
    [InlineData("INSERT INTO dbo.T (C) VALUES (CONCAT(N'w', $2065550101));", false)]
    [InlineData("INSERT INTO dbo.T (C) VALUES (REPLICATE(N'w', 41));", true)]
    public void A_number_enters_a_character_or_binary_column_only_where_the_model_defines_its_digits(string statement, bool admitted)
    {
        var parsed = Witnesses.Parse("-- plants: [dbo].[T]\n" + statement, Repository);

        Assert.Equal(admitted, parsed is Result<IReadOnlyList<Witnesses.Block>>.Ok);
        if (!admitted)
        {
            var refused = Failed(parsed, "witness.not-synthetic");
            Assert.StartsWith("dbchange/witnesses.sql line 2 carries a number SQL Server makes text or bytes of", refused.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain("206555", refused.Message + refused.Remedy, StringComparison.Ordinal);
            Assert.DoesNotContain("411111", refused.Message + refused.Remedy, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The repository's rules as io reads them from a model DacFx 170.5.96 builds from T-SQL, the base package's elements read as the
    /// copy's tables as synthetic-copy up reads them: CREATE SYNONYM dbo.Remote FOR a table on the linked server prod-sql is a Synonym
    /// element, so a block reading dbo.Remote is witness.not-dml naming it; CK_Product_Code CHECK (Code LIKE 'ABC%') gives the text
    /// ABC, so N'ABCw1' enters dbo.Product.Code and N'ABC1' does not; and Code is nvarchar, so a number written into it is refused.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_model_s_synonym_LIKE_pattern_and_column_types_hold_a_declared_witness_as_DacFx_reads_the_model()
    {
        using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160,
            new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
        model.AddObjects("CREATE TABLE dbo.Product (Id INT NOT NULL, Code NVARCHAR(20) NOT NULL CONSTRAINT CK_Product_Code CHECK "
            + "(Code LIKE 'ABC%'));");
        model.AddObjects("CREATE SYNONYM dbo.Remote FOR [prod-sql].[Catalog].[dbo].[Customer];");
        var elements = Value(Ssdt.ReadModel(model));
        var repository = Witnesses.Repository.Of(elements, Value(Schema.Of(elements)));
        Result<IReadOnlyList<Witnesses.Block>> Declared(string statement) =>
            Witnesses.Parse("-- plants: [dbo].[Product]\n" + statement, repository);

        var remote = Failed(Declared("INSERT INTO dbo.Product (Id, Code) SELECT r.Id, r.Name FROM dbo.Remote AS r;"), "witness.not-dml");

        Assert.StartsWith("dbchange/witnesses.sql line 2 holds the synonym [dbo].[Remote], which can stand for a table", remote.Message,
            StringComparison.Ordinal);
        Assert.IsType<Result<IReadOnlyList<Witnesses.Block>>.Ok>(Declared("INSERT INTO dbo.Product (Id, Code) VALUES (1, N'ABCw1');"));
        Failed(Declared("INSERT INTO dbo.Product (Id, Code) VALUES (1, N'ABC1');"), "witness.not-synthetic");
        Failed(Declared("INSERT INTO dbo.Product (Id, Code) VALUES (2065550101, 2065550101);"), "witness.not-synthetic");
        Assert.IsType<Result<IReadOnlyList<Witnesses.Block>>.Ok>(Declared("INSERT INTO dbo.Product (Id, Code) VALUES (2065550101, N'w1');"));
    }

    /// <summary>
    /// PAR-033 and VALUES.md P1: a declared block runs as one batch on the copy's connection, with triggers firing, so an object of the
    /// repository that reads another server reaches it for the block. In a model DacFx 170.5.96 builds from T-SQL, the view
    /// dbo.RemoteView reads [prod-sql].[Catalog].[dbo].[Customer] and dbo.OverRemote reads dbo.RemoteView; the function dbo.RemoteRows
    /// returns OPENQUERY's rows and dbo.RemoteCount counts through OPENDATASOURCE; dbo.Ship executes its batch AT [prod-sql], and the
    /// trigger dbo.Audit_Copied runs dbo.Ship on each row inserted into dbo.Audit. A block reading any of the four objects, or writing
    /// dbo.Audit, under its own name or an alias, is witness.not-dml by its line, naming the object; a block reading the view
    /// dbo.LocalView over dbo.Customer, reading dbo.Audit or writing dbo.Customer enters.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P1")]
    public void A_model_s_view_function_or_trigger_that_reaches_another_server_is_refused_to_a_declared_witness_by_its_line()
    {
        using var model = new Microsoft.SqlServer.Dac.Model.TSqlModel(Microsoft.SqlServer.Dac.Model.SqlServerVersion.Sql160,
            new Microsoft.SqlServer.Dac.Model.TSqlModelOptions());
        model.AddObjects("CREATE TABLE dbo.Customer (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(40) NULL);");
        model.AddObjects("CREATE TABLE dbo.Audit (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(40) NULL);");
        model.AddObjects("CREATE VIEW dbo.RemoteView AS SELECT Name FROM [prod-sql].[Catalog].[dbo].[Customer];");
        model.AddObjects("CREATE VIEW dbo.OverRemote AS SELECT Name FROM dbo.RemoteView;");
        model.AddObjects("CREATE VIEW dbo.LocalView AS SELECT Id, Name FROM dbo.Customer;");
        model.AddObjects("CREATE FUNCTION dbo.RemoteRows() RETURNS TABLE AS RETURN SELECT q.Name FROM OPENQUERY([prod-sql], "
            + "'SELECT Name FROM Catalog.dbo.Customer') AS q;");
        model.AddObjects("CREATE FUNCTION dbo.RemoteCount() RETURNS INT AS BEGIN RETURN (SELECT COUNT(*) FROM OPENDATASOURCE("
            + "'MSOLEDBSQL', 'Server=prod-sql;Trusted_Connection=yes;').Catalog.dbo.Customer); END");
        model.AddObjects("CREATE PROCEDURE dbo.Ship AS EXEC ('INSERT INTO Catalog.dbo.Audit (Name) VALUES (1);') AT [prod-sql];");
        model.AddObjects("CREATE TRIGGER dbo.Audit_Copied ON dbo.Audit AFTER INSERT AS EXEC dbo.Ship;");
        var elements = Value(Ssdt.ReadModel(model));
        var repository = Witnesses.Repository.Of(elements, Value(Schema.Of(elements)));
        Result<IReadOnlyList<Witnesses.Block>> Declared(string statement) =>
            Witnesses.Parse("-- plants: [dbo].[Customer]\n" + statement, repository);

        var remote = Failed(Declared("INSERT INTO dbo.Customer (Id, Name) SELECT 1, v.Name FROM dbo.RemoteView AS v;"), "witness.not-dml");

        Assert.StartsWith("dbchange/witnesses.sql line 2 holds the view [dbo].[RemoteView], which reaches another server",
            remote.Message, StringComparison.Ordinal);
        Assert.All(new[]
        {
            ("INSERT INTO dbo.Customer (Id, Name) SELECT 1, v.Name FROM dbo.OverRemote AS v;", "the view [dbo].[OverRemote]"),
            ("INSERT INTO dbo.Customer (Id, Name) SELECT 1, r.Name FROM dbo.RemoteRows() AS r;", "the function [dbo].[RemoteRows]"),
            ("INSERT INTO dbo.Customer (Id) VALUES (dbo.REMOTECOUNT());", "the function [dbo].[RemoteCount]"),
            ("INSERT INTO dbo.Audit (Id, Name) VALUES (1, N'w1');", "a write to [dbo].[Audit], whose trigger [dbo].[Audit_Copied]"),
            ("UPDATE a SET a.Name = N'w2' FROM dbo.Audit AS a;", "a write to [dbo].[Audit], whose trigger [dbo].[Audit_Copied]"),
        }, refused => Assert.StartsWith("dbchange/witnesses.sql line 2 holds " + refused.Item2 + ", which reaches another server",
            Failed(Declared(refused.Item1), "witness.not-dml").Message, StringComparison.Ordinal));
        Assert.All(new[]
        {
            "INSERT INTO dbo.Customer (Id, Name) SELECT v.Id, v.Name FROM dbo.LocalView AS v;",
            "INSERT INTO dbo.Customer (Id, Name) SELECT a.Id, a.Name FROM dbo.Audit AS a;", "UPDATE dbo.Customer SET Name = N'w3';",
        }, admitted => Assert.IsType<Result<IReadOnlyList<Witnesses.Block>>.Ok>(Declared(admitted)));
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

    /// <summary>
    /// Step 8 (the Facsimile's F2): the plan from the base to the copy, operation by operation. The drop and the create of a foreign key
    /// the copy keeps disabled as the environment holds it are one note, synthetic-copy.disabled, since a deploy plans the same against
    /// the environment; the refresh DacFx adds for a view over a table it alters is a consequence, no difference of its own; a check the
    /// repository writes with IN, dropped and created, is one warning that names the OR form SQL Server stores, as an extract reads it,
    /// and as check drift names it; and the alter of the table is synthetic-copy.identity.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Step_8_finds_a_difference_in_each_operation_of_the_plan_from_the_base_but_a_kept_disabled_key_s_and_a_consequence()
    {
        var (key, check) = (Elements.Key("ForeignKeyConstraint", "dbo", "FK_CustomerAddress_Customer"),
            Elements.Key("CheckConstraint", "dbo", "CK_Order_StatusText"));
        var (table, view) = (Elements.Key("Table", "dbo", "Order"), Elements.Key("View", "dbo", "vCustomerOrders"));
        const string Stored = "[StatusText]=N'Cancelled' OR [StatusText]=N'Shipped' OR [StatusText]=N'Pending'";
        SortedArray<Element> Model(string expression) =>
            [Elements.New(table, []), Elements.New(check, [("Expression", new Value.Script(expression))])];
        var plan = new DeployReport(
        [
            new PlanOperation(new PlanOperationKind.Drop(), key, []), new PlanOperation(new PlanOperationKind.Create(), key, []),
            new PlanOperation(new PlanOperationKind.Drop(), check, []), new PlanOperation(new PlanOperationKind.Create(), check, []),
            new PlanOperation(new PlanOperationKind.Refresh(), view, []), new PlanOperation(new PlanOperationKind.Alter(), table, []),
        ], []);
        var notes = new List<Finding>();

        var differences = SyntheticCopy.Differences(plan, Model(Stored), Model("StatusText IN (N'Pending', N'Shipped', N'Cancelled')"),
            Collation.CaseSensitive, new HashSet<ElementKey> { key }, notes);

        Assert.Equal([("synthetic-copy.identity", "Table [dbo].[Order]"),
                ("synthetic-copy.check-written-with-in", "CheckConstraint [dbo].[CK_Order_StatusText]")],
            differences.Select(f => (f.Code, f.Subject)));
        Assert.All(differences, f => Assert.Equal(Severity.Warning, f.Severity));
        Assert.Equal("The plan from the base to the copy would alter Table [dbo].[Order], so the copy's schema differs from the repository "
            + "at the ref.", differences[0].Message);
        Assert.EndsWith(": CHECK (" + Stored + ").", differences[1].Remedy, StringComparison.Ordinal);
        var kept = Assert.Single(notes);
        Assert.Equal(("synthetic-copy.disabled", Severity.Note, "ForeignKeyConstraint [dbo].[FK_CustomerAddress_Customer]"),
            (kept.Code, kept.Severity, kept.Subject));
    }

    private static PreconditionState State(Precondition precondition, bool populated, bool violating) =>
        new(precondition, populated, violating);

    private static Precondition On(string table, PreconditionKind kind, params string[] columns) =>
        new(Value(Name.Of("dbo", table)), SortedArray.Of(columns.Select(Column)), kind);

    private static Name Column(string name) => Value(Name.Of(name));

    private static ColumnType Type(long length, string? collation) => new(Column("nvarchar"), length, false, 0, 0, collation);
}

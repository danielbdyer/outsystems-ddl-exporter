using System;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// The facts ReadSideTests compares, pinned without SQL Server: a catalog as v2's CatalogCodec writes one, by hand, and the Schema that
/// stands for the same table give the same lines; and the Ubuntu SQL lane builds v2's read side, so ReadSideTests runs v2 there.
/// </summary>
public sealed class ReadSideFactsTests
{
    /// <summary>dbo.Line (Id int IDENTITY PK, OrderId int NOT NULL FK to dbo.[Order], Amount decimal(18, 2) NOT NULL DEFAULT 0
    /// CHECK (Amount &gt;= 0), Note nvarchar(max) NULL, Twice AS Id * 2, Flag bit NOT NULL DEFAULT 1, UNIQUE (OrderId, Id DESC)), as v2's
    /// read side writes it: SQL Server's text of each expression, a bit default as a BooleanLit, and a decimal default as written.</summary>
    private const string Catalog = """
        { "modules": [ { "kinds": [ {
          "ssKey": "K1", "physical": { "schema": "dbo", "table": "Line" },
          "attributes": [
            { "ssKey": "A1", "type": "Integer", "column": { "columnName": "Id", "isNullable": false }, "isPrimaryKey": true,
              "isIdentity": true, "length": null, "precision": null, "scale": null, "defaultValue": null, "computed": null, "sqlStorage": null },
            { "ssKey": "A2", "type": "Integer", "column": { "columnName": "OrderId", "isNullable": false }, "isPrimaryKey": false,
              "isIdentity": false, "length": null, "precision": null, "scale": null, "defaultValue": null, "computed": null, "sqlStorage": null },
            { "ssKey": "A3", "type": "Decimal", "column": { "columnName": "Amount", "isNullable": false }, "isPrimaryKey": false,
              "isIdentity": false, "length": null, "precision": 18, "scale": 2, "defaultValue": { "kind": "DecimalLit", "value": "0" },
              "computed": null, "sqlStorage": null },
            { "ssKey": "A4", "type": "Text", "column": { "columnName": "Note", "isNullable": true }, "isPrimaryKey": false,
              "isIdentity": false, "length": null, "precision": null, "scale": null, "defaultValue": null, "computed": null, "sqlStorage": null },
            { "ssKey": "A5", "type": "Integer", "column": { "columnName": "Twice", "isNullable": true }, "isPrimaryKey": false,
              "isIdentity": false, "defaultValue": null, "computed": { "expression": "([Id]*(2))", "isPersisted": false }, "sqlStorage": null },
            { "ssKey": "A6", "type": "Boolean", "column": { "columnName": "Flag", "isNullable": false }, "isPrimaryKey": false,
              "isIdentity": false, "defaultValue": { "kind": "BooleanLit", "value": true }, "computed": null, "sqlStorage": null } ],
          "references": [ { "sourceAttribute": "A2", "targetKind": "K2", "name": "FK_Line_OrderId", "onDelete": "NoAction" } ],
          "indexes": [ { "isUnique": true, "columns": [ { "attribute": "A2", "direction": "Ascending" },
            { "attribute": "A1", "direction": "Descending" } ] }, { "isUnique": false, "columns": [ { "attribute": "A4" } ] } ],
          "columnChecks": [ { "name": "CK__Line__Amount__1A2B", "definition": "([Amount]>=(0))" } ],
          "triggers": [ { "name": "trg_Line", "isDisabled": true } ] },
          { "ssKey": "K2", "physical": { "schema": "dbo", "table": "Order" }, "attributes": [], "references": [], "indexes": [],
            "columnChecks": [], "triggers": [] } ] } ] }
        """;

    [Fact]
    [Trait("Category", "fast")]
    public void The_facts_of_v2_s_catalog_and_of_a_schema_agree_on_a_hand_written_pair_defaults_and_checks_stripped_of_their_outer_parentheses()
    {
        var line = Name("dbo", "Line");
        Column.Stored Stored(int ordinal, string name, bool nullable, ColumnType type, Default? d = null, Identity? identity = null) =>
            new(ordinal, Name(name), nullable, type, null, identity, d, false, false);
        var schema = new Schema([
            new Table(line,
                [
                    Stored(1, "Id", false, Type("int"), identity: new Identity(1, 1)), Stored(2, "OrderId", false, Type("int")),
                    Stored(3, "Amount", false, Type("decimal") with { Precision = 18, Scale = 2 }, new Default(null, new("((0))"), null)),
                    Stored(4, "Note", true, Type("nvarchar") with { IsMax = true }),
                    new Column.Computed(5, Name("Twice"), true, new("([Id]*(2))"), false),
                    Stored(6, "Flag", false, Type("bit"), new Default(Name("dbo", "DF_Line_Flag"), new("((1))"), null)),
                ],
                new PrimaryKey(null, [new KeyColumn(0, Name("Id"), false)], true),
                [new Unique(Key("UniqueConstraint", "UQ_Line"), Name("dbo", "UQ_Line"),
                    [new KeyColumn(0, Name("OrderId"), false), new KeyColumn(1, Name("Id"), true)], false, null, false)],
                [new Reference(Key("ForeignKeyConstraint", "FK_Line_Order"), Name("dbo", "FK_Line_Order"), Name("dbo", "Order"),
                    [new Reference.Leg(0, Name("OrderId"), Name("Id"))], ReferentialAction.Cascade, ReferentialAction.NoAction, false, false)],
                [new Check(Key("CheckConstraint", "CK_Line"), null, new("[Amount]>=(0)"), false)],
                [new Trigger(Name("dbo", "trg_Line"), true, false)], null),
            new Table(Name("dbo", "Order"), [], null, [], [], [], [], null),
        ]);

        var v2 = ReadSideFacts.Of(JsonNode.Parse(Catalog)!);

        Assert.Equal(ReadSideFacts.Of(schema), v2);
        Assert.Equal(
        [
            "check [dbo].[Line] [Amount]>=(0)",
            "column [dbo].[Line].[Amount] 3 Decimal(18,2) not-null",
            "column [dbo].[Line].[Flag] 6 Boolean not-null",
            "column [dbo].[Line].[Id] 1 Integer not-null primary-key identity",
            "column [dbo].[Line].[Note] 4 Text(max) null",
            "column [dbo].[Line].[OrderId] 2 Integer not-null",
            "column [dbo].[Line].[Twice] 5 computed null",
            "computed [dbo].[Line].[Twice] virtual [Id]*(2)",
            "default [dbo].[Line].[Amount] 0",
            "default [dbo].[Line].[Flag] 1",
            "reference [dbo].[Line].[OrderId] -> [dbo].[Order]",
            "table [dbo].[Line]",
            "table [dbo].[Order]",
            "trigger [dbo].[Line] trg_Line disabled",
            "unique [dbo].[Line] (OrderId, Id desc)",
        ], v2);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("((0))", "0")]
    [InlineData("(N'Pending')", "N'Pending'")]
    [InlineData("(NEXT VALUE FOR [dbo].[NoteNumber])", "NEXT VALUE FOR [dbo].[NoteNumber]")]
    [InlineData("([a])+([b])", "([a])+([b])")]
    [InlineData(" ( (len([Code])) ) ", "len([Code])")]
    public void An_expression_loses_only_the_parentheses_that_enclose_all_of_it(string expression, string stripped) =>
        Assert.Equal(stripped, ReadSideFacts.Stripped(expression));

    /// <summary>The Ubuntu SQL lane builds v2's read side and names it in DBCHANGE_READSIDE before the fixture tests, with the .NET 9 SDK v2
    /// pins; without that step ReadSideTests compares v3 with the committed facts alone, and nothing would say so.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_Ubuntu_SQL_lane_builds_v2_s_read_side_before_its_fixture_tests()
    {
        var lane = Repository.Read(".github/workflows/dbchange.yml").Split("\n  sql-windows:")[0].Split("\n  sql-ubuntu:")[1];

        var builds = lane.IndexOf("program=\"$(bash ci/readside.sh)\"\n          echo \"DBCHANGE_READSIDE=$program\" >> \"$GITHUB_ENV\"",
            StringComparison.Ordinal);
        var tests = lane.IndexOf("--filter \"Category=fixture&Spike!=S4\"", StringComparison.Ordinal);

        Assert.True(builds > 0 && builds < tests, "dbchange.yml's sql-ubuntu job does not set DBCHANGE_READSIDE before its fixture tests");
        Assert.Contains("dotnet-version: 9.0.314", lane, StringComparison.Ordinal);
    }

    private static Name Name(string part) => Expect.Value(Kernel.Name.Of(part));

    private static Name Name(string schema, string part) => Expect.Value(Kernel.Name.Of(schema, part));

    private static ColumnType Type(string name) => new(Name(name), 0, false, 0, 0, null);

    private static ElementKey Key(string type, string name) => Expect.Value(ElementKey.Of(type, Name("dbo", name)));
}

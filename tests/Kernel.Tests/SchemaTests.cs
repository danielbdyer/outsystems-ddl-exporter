using System.Collections.Generic;
using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;
using static DbChange.Kernel.Tests.SchemaProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// Schema.Of reads the typed schema the planter reads from a model's elements (V3_MILESTONES.md WP 3.1), in the shapes DacFx 170.5.96
/// gives a published copy's objects; the PLT cases are archive/v2/ssdt-agent/facsimile/cases/20-planter.md's. Io.Tests' ReadSideTests
/// compares it, on a published copy, with what v2's read side writes for the same copy.
/// </summary>
public sealed class SchemaTests
{
    private static readonly Name Dbo = Ok(Name.Of("dbo", "T"));

    [Fact]
    [Trait("Category", "fast")]
    public void Schema_of_the_elements_a_schema_is_written_as_is_that_schema() =>
        Schemas.Sample(schema => Ok(Schema.Of(SchemaElements.Of(schema))) == schema);

    /// <summary>
    /// What no table owns is no part of the schema: a view, its columns and a unique index and an INSTEAD OF trigger on it, a procedure,
    /// a grant, statistics, a role, the database's options, both deploy scripts and a refactorlog entry.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Schema_of_a_model_ignores_every_element_no_table_owns() =>
        Schemas.Sample(schema =>
        {
            var model = SchemaElements.Of(schema);
            return Ok(Schema.Of(SortedArray.Of(model.Concat(Unowned(schema))))) == Ok(Schema.Of(model));
        });

    /// <summary>
    /// PLT-52 and PLT-125: under MATCH SIMPLE one NULL in a foreign key's columns switches its check off for that row, and a disabled key
    /// checks nothing, so either makes the reference optional. The columns' nullability and the keys' Disabled are drawn again in the
    /// elements, after the schema was written.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_reference_is_optional_exactly_when_one_of_its_columns_is_nullable_or_it_is_disabled() =>
        Gen.Select(Schemas, Gen.Bool.Array[64]).Sample((schema, flips) =>
        {
            var redrawn = SortedArray.Of(SchemaElements.Of(schema).Select((e, i) => Flipped(e, flips[i % flips.Length])));
            var read = Ok(Schema.Of(redrawn));
            return read.Tables.All(t => t.References.All(r =>
                r.Optional == (r.Disabled || r.Legs.Any(l => t.Find(l.Column)!.Nullable))));
        });

    /// <summary>PLT-30 and PLT-31: DacFx reads a PERSISTED NOT NULL computed column as Nullable true and PersistedNullable false
    /// (measured), and gives a computed column no DataType.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_persisted_computed_column_takes_its_nullability_from_PersistedNullable_and_a_computed_column_has_no_type()
    {
        var table = Key("Table", "dbo", "T");
        ElementKey Column(string name) => Key(table, "Column", name);
        Element Computed(string name, bool persisted, Value persistedNullable) => New(Column(name),
            [("Expression", new Value.Script("([Id]*(2))")), ("GeneratedAlwaysType", None), ("IsIdentity", Bool(false)),
                ("Nullable", Bool(true)), ("Persisted", Bool(persisted)), ("PersistedNullable", persistedNullable)],
            [("ExpressionDependencies", [Column("Id")])]);
        var model = SortedArray.Of(
            New(table, [], [("Columns", [Column("Id"), Column("Doubled"), Column("Virtual")])]),
            New(Column("Id"), [("Nullable", Bool(false)), ("GeneratedAlwaysType", None), ("Expression", new Value.Null())],
                [("DataType", [Ok(ElementKey.Of("DataType", Ok(Name.Of("int"))))])]),
            Computed("Doubled", true, Bool(false)), Computed("Virtual", false, new Value.Null()));

        var columns = Ok(Schema.Of(model)).Tables.Single().Columns;

        Assert.Equal(new Column.Computed(2, Ok(Name.Of("Doubled")), false, new Value.Script("([Id]*(2))"), true), columns[1]);
        Assert.Equal(new Column.Computed(3, Ok(Name.Of("Virtual")), true, new Value.Script("([Id]*(2))"), false), columns[2]);
        Assert.IsType<Column.Stored>(columns[0]);
    }

    /// <summary>PLT-33: DEFAULT (NEXT VALUE FOR dbo.NoteNumber) reads, on a copy, as the expression SQL Server keeps and an
    /// ExpressionDependencies naming Sequence [dbo].[NoteNumber].</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_default_that_draws_from_a_sequence_names_the_sequence()
    {
        var number = new Column.Stored(2, Ok(Name.Of("Number")), false, SystemType("int"), null, null,
            new Default(Ok(Name.Of("dbo", "DF_T_Number")), new Value.Script("(NEXT VALUE FOR [dbo].[NoteNumber])"), NoteNumber), false,
            false);
        var literal = number with { Ordinal = 3, Name = Ok(Name.Of("Kept")), Default = new Default(null, new Value.Script("((0))"), null) };
        var schema = new Schema([Table(Dbo, Id, number, literal)]);

        var read = Ok(Schema.Of(SchemaElements.Of(schema)));

        Assert.Equal(schema, read);
        Assert.Equal(NoteNumber, ((Column.Stored)read.Tables[0].Columns[1]).Default!.Sequence);
        Assert.Null(((Column.Stored)read.Tables[0].Columns[2]).Default!.Sequence);
    }

    /// <summary>PLT-34 and PLT-35: a system-versioned table's TemporalSystemVersioningHistoryTable names its history table, its period
    /// columns are generated, and the history table is versioned as that table's history.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_temporal_table_names_its_history_table_and_its_period_columns_and_the_history_table_names_it()
    {
        var (price, history) = (Ok(Name.Of("dbo", "Price")), Ok(Name.Of("dbo", "PriceHistory")));
        var (from, to) = (Ok(Name.Of("ValidFrom")), Ok(Name.Of("ValidTo")));
        var schema = new Schema([
            Table(price, Id, new Column.Generated(2, from, false, SystemType("datetime2", scale: 7), GeneratedAs.RowStart),
                new Column.Generated(3, to, false, SystemType("datetime2", scale: 7), GeneratedAs.RowEnd))
                with { Versioning = new Versioning.Current(history, from, to) },
            Table(history, Id, Plain(2, "ValidFrom", SystemType("datetime2", scale: 7)), Plain(3, "ValidTo", SystemType("datetime2", scale: 7)))
                with { Versioning = new Versioning.History(price) },
        ]);

        var read = Ok(Schema.Of(SchemaElements.Of(schema)));

        Assert.Equal(new Versioning.Current(history, from, to), read.Find(price)!.Versioning);
        Assert.Equal(new Versioning.History(price), read.Find(history)!.Versioning);
        Assert.Equal(GeneratedAs.RowEnd, Assert.IsType<Column.Generated>(read.Find(price)!.Find(to)).As);
        Assert.Equal(schema, read);
    }

    /// <summary>PLT-40 and PLT-74: UQ_OrderLine_Order_Line UNIQUE (OrderId, LineNumber DESC) and UIX_Product_LegacyCode ... WHERE
    /// CategoryId IS NOT NULL are both uniques, the filter kept; a non-unique index is none.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_unique_constraint_and_a_unique_index_are_uniques_in_key_order_and_a_filtered_index_keeps_its_filter()
    {
        var model = Model.Of("T.Id int NOT NULL", "T.OrderId int NOT NULL", "T.LineNumber int NOT NULL", "T.Code nvarchar(40) NULL")
            .UniqueConstraint("UQ_T_Order_Line", "T", "OrderId", "LineNumber")
            .FilteredIndex("UIX_T_Code", "T", ["Code"], "([Code] IS NOT NULL)", "Code")
            .Index("IX_T_Code", "T", ["Code"]).Elements;
        var descending = SortedArray.Of(model.Select(e => e.Key.Type == "UniqueConstraint"
            ? Ok(Element.Of(e.Key, e.Properties.Append(new Element.Property("Columns[1].Ascending", Bool(false))), e.Relationships)) : e));

        var uniques = Ok(Schema.Of(descending)).Tables.Single().Uniques;

        Assert.Equal(2, uniques.Count);
        var constraint = uniques.Single(u => !u.IsIndex);
        Assert.Equal([new KeyColumn(0, Ok(Name.Of("OrderId")), false), new KeyColumn(1, Ok(Name.Of("LineNumber")), true)],
            constraint.Columns);
        Assert.Equal(new Value.Script("([Code] IS NOT NULL)"), uniques.Single(u => u.IsIndex).Filter);
        Assert.Equal(Ok(Name.Of("UIX_T_Code")), uniques.Single(u => u.IsIndex).Constraint);
    }

    /// <summary>io keys an unnamed constraint under its column or its table (PrimaryKeyConstraint [dbo].[T].[Host], CheckConstraint
    /// [dbo].[T].[Id].[ExpressionDependencies]) and a named one at the top level in its table's schema.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_unnamed_constraint_has_no_name_and_a_named_one_its_two_part_name()
    {
        var table = Key("Table", "dbo", "T");
        var unnamed = new Check(Key(Key(table, "Column", "Id"), "CheckConstraint", "ExpressionDependencies"), null,
            new Value.Script("([Id]>(0))"), false);
        var named = unnamed with { Key = Key("CheckConstraint", "dbo", "CK_T"), Constraint = Ok(Name.Of("dbo", "CK_T")) };
        var schema = new Schema([Table(Dbo, Id) with
        {
            Key = new PrimaryKey(null, [new KeyColumn(0, Ok(Name.Of("Id")), false)], true), Checks = [unnamed, named],
        }]);

        var read = Ok(Schema.Of(SchemaElements.Of(schema))).Tables.Single();

        Assert.Null(read.Key!.Constraint);
        Assert.Equal(new Name?[] { null, Ok(Name.Of("dbo", "CK_T")) }, read.Checks.Select(c => c.Constraint).OrderBy(n => n.HasValue));
    }

    /// <summary>
    /// io keys an unresolved reference as the type Unresolved, and a model the schema reads must hold every element it follows: a table's
    /// column, an alias type, a foreign key's parent, a history table.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Column")]
    [InlineData("DataType")]
    [InlineData("ForeignTable")]
    [InlineData("Unresolved")]
    public void A_relationship_to_an_element_the_model_does_not_hold_is_the_error_model_unresolved(string missing)
    {
        var parent = Ok(Name.Of("dbo", "P"));
        var alias = new Column.Stored(2, Ok(Name.Of("Code")), false, ShortCodeStores, ShortCode, null, null, false, false);
        var reference = new Reference(Key("ForeignKeyConstraint", "dbo", "FK_T_P"), Ok(Name.Of("dbo", "FK_T_P")), parent,
            [new Reference.Leg(0, Ok(Name.Of("Id")), Ok(Name.Of("Id")))], ReferentialAction.NoAction, ReferentialAction.NoAction, false,
            false);
        var model = SchemaElements.Of(new Schema([Table(Dbo, Id, alias) with { References = [reference] }, Table(parent, Id)]));
        var broken = missing switch
        {
            "Column" => model.Where(e => e.Key != Key(Key("Table", "dbo", "T"), "Column", "Code")),
            "DataType" => model.Where(e => e.Key.Type != "DataType"),
            "ForeignTable" => model.Where(e => e.Key != Key("Table", "dbo", "P") && e.Key.Parent != Key("Table", "dbo", "P")),
            _ => model.Select(e => e.Key.Type != "ForeignKeyConstraint" ? e : Ok(Element.Of(e.Key, e.Properties,
                e.Relationships.Select(r => r.Name != "ForeignTable" ? r
                    : Element.Relationship.Of(r.Name, [Ok(ElementKey.Of("Unresolved", Ok(Name.Of("dbo", "Gone"))))]))))),
        };

        var error = Expect.Failed(Schema.Of(SortedArray.Of(broken)), "model.unresolved");

        Assert.Contains(missing == "Unresolved" ? "[dbo].[Gone]" : missing == "ForeignTable" ? "[dbo].[P]" : "[dbo]", error.Message, System.StringComparison.Ordinal);
    }

    /// <summary>An IDENTITY on a decimal(38, 0) column may seed beyond bigint; the planter writes literals of 64 bits at most.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_identity_seed_beyond_64_bits_is_the_error_model_identity()
    {
        var model = SchemaElements.Of(new Schema([Table(Dbo, Id with { Identity = new Identity(1, 1) })]));
        var seeded = SortedArray.Of(model.Select(e => e.Key.Type != "Column" ? e : Ok(Element.Of(e.Key,
            e.Properties.Select(p => p.Name == "IdentitySeed" ? p with { Value = Text("99999999999999999999") } : p), e.Relationships))));

        var error = Expect.Failed(Schema.Of(seeded), "model.identity");

        Assert.Contains("99999999999999999999", error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_schema_finds_a_table_and_a_table_a_column_by_name() =>
        Schemas.Sample(schema => schema.Tables.All(t => schema.Find(t.Name) == t && t.Columns.All(c => t.Find(c.Name) == c))
            && schema.Find(Ok(Name.Of("dbo", "Absent"))) is null);

    private static readonly Value None = new Value.Enumeration("ColumnGeneratedAlwaysType", "None");

    private static readonly Column.Stored Id = Plain(1, "Id", SystemType("int"));

    private static Column.Stored Plain(int ordinal, string name, ColumnType type) =>
        new(ordinal, Ok(Name.Of(name)), false, type, null, null, null, false, false);

    private static Table Table(Name name, params Column[] columns) =>
        new(name, SortedArray.Of(columns), null, [], [], [], [], null);

    /// <summary>The element with its Nullable, when it is a stored column, or its Disabled, when it is a foreign key, made
    /// <paramref name="value"/>.</summary>
    private static Element Flipped(Element e, bool value) =>
        (e.Key.Type, e["Expression"], e["GeneratedAlwaysType"]) switch
        {
            ("Column", Value.Null, Value.Enumeration { Member: "None" }) => Ok(Element.Of(e.Key,
                e.Properties.Select(p => p.Name == "Nullable" ? p with { Value = Bool(value) } : p), e.Relationships)),
            ("ForeignKeyConstraint", _, _) => Ok(Element.Of(e.Key,
                e.Properties.Select(p => p.Name == "Disabled" ? p with { Value = Bool(value) } : p), e.Relationships)),
            _ => e,
        };

    /// <summary>Objects no table of the schema owns, over its first table.</summary>
    private static IEnumerable<Element> Unowned(Schema schema)
    {
        var view = Key("View", "dbo", "V");
        var table = SchemaElements.TableKey(schema.Tables[0].Name);
        var script = new Value.Script("SELECT 1 AS One");
        return
        [
            New(view, [("Definition", script)], [("Columns", [Key(view, "Column", "One")])]),
            New(Key(view, "Column", "One"), [("Nullable", Bool(false))], [("DataType", [Ok(ElementKey.Of("DataType", Ok(Name.Of("int"))))])]),
            New(Key(view, "Index", "UX_V"), [("Unique", Bool(true))], [("Columns", [Key(view, "Column", "One")]), ("IndexedObject", [view])]),
            New(Key("DmlTrigger", "dbo", "trg_V"), [("TriggerType", new Value.Enumeration("TriggerType", "InsteadOf"))],
                [("TriggerObject", [view])]),
            New(Key("Procedure", "dbo", "P"), [("Definition", script)]),
            New(Ok(ElementKey.Of("Role", Ok(Name.Of("Reader")))), []),
            New(Key(table, "Statistics", "ST"), [], [("OnObject", [table])]),
            New(Ok(ElementKey.Of("Permission", Ok(Name.Of("Grant.Select.Reader")))), [], [("SecuredObject", [table])]),
            New(Ok(ElementKey.Of("DatabaseOptions", Ok(Name.Of("DatabaseOptions")))), [("Collation", Text("SQL_Latin1_General_CP1_CI_AS"))]),
            Element.PreDeploy("PRINT 1;"), Element.PostDeploy("PRINT 2;"),
            Ok(Element.RefactorLogEntry("0f8fad5b-d9cb-469f-a165-70867728950e", [new Element.Property("NewName", Text("[dbo].[T]"))])),
        ];
    }
}

using System.Collections.Generic;
using System.Linq;
using DbChange.Tests;
using Xunit;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// A column's type is what its element holds, as io/Ssdt.ReadModel reads DacFx 170.5.96 (measured): the DataType relationship's one target,
/// a system type keyed in one part or an alias keyed in two, and the Length, IsMax, Precision, Scale and Collation properties. The type
/// SQL Server stores, which WP 2.3 writes as TRY_CONVERT's target, resolves an alias through its DataType element.
/// </summary>
public sealed class ColumnTypeTests
{
    private static readonly ElementKey Table = Key("Table", "dbo", "T");

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("nvarchar", 50L, false, 0L, 0L, null)]
    [InlineData("nvarchar", 0L, true, 0L, 0L, "Latin1_General_CS_AS")]
    [InlineData("decimal", 0L, false, 18L, 2L, null)]
    [InlineData("datetime2", 0L, false, 0L, 7L, null)]
    [InlineData("float", 0L, false, 53L, 0L, null)]
    public void A_column_s_type_is_its_data_type_and_five_facets_as_the_model_holds_them(string type, long length, bool isMax,
        long precision, long scale, string? collation)
    {
        var column = New(Key(Table, "Column", "c"),
            [("Collation", collation is null ? new Value.Null() : Text(collation)), ("IsMax", Bool(isMax)), ("Length", Int(length)),
                ("Nullable", Bool(true)), ("Precision", Int(precision)), ("Scale", Int(scale))],
            [("DataType", [Ok(ElementKey.Of("DataType", Ok(Name.Of(type))))])]);

        var declared = Ok(ColumnType.Of(column));

        Assert.Equal(new ColumnType(Ok(Name.Of(type)), length, isMax, precision, scale, collation), declared);
        Assert.False(declared.IsAlias);
        Assert.Equal(declared, Ok(declared.Stored(new Dictionary<ElementKey, Element>())));
    }

    /// <summary>
    /// PLT-27 (tests/Cases/20-planter.md): a column of TYPE dbo.ShortCode FROM nvarchar(6) declares the alias,
    /// Length 0 as DacFx writes it, and stores as nvarchar(6) under the column's own collation.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_alias_typed_column_stores_as_its_base_type_with_the_alias_s_facets()
    {
        var shortCode = Ok(ElementKey.Of("DataType", Ok(Name.Of("dbo", "ShortCode"))));
        var alias = New(shortCode,
            [("SqlDataType", new Value.Enumeration("SqlDataType", "Unknown")), ("UddtIsMax", Bool(false)), ("UddtLength", Int(6)),
                ("UddtNullable", Bool(false)), ("UddtPrecision", Int(0)), ("UddtScale", Int(0))],
            [("Type", [Ok(ElementKey.Of("DataType", Ok(Name.Of("nvarchar"))))])]);
        var column = Model.Of("T.c dbo.ShortCode NULL Latin1_General_CS_AS").Elements.Single(e => e.Key.Type == "Column");

        var declared = Ok(ColumnType.Of(column));

        Assert.Equal(new ColumnType(Ok(Name.Of("dbo", "ShortCode")), 0, false, 0, 0, "Latin1_General_CS_AS"), declared);
        Assert.True(declared.IsAlias);
        Assert.Equal(new ColumnType(Ok(Name.Of("nvarchar")), 6, false, 0, 0, "Latin1_General_CS_AS"),
            Ok(declared.Stored(new Dictionary<ElementKey, Element> { [shortCode] = alias })));
        Expect.Failed(declared.Stored(new Dictionary<ElementKey, Element>()), "model.unresolved");
        Expect.Failed(declared.Stored(new Dictionary<ElementKey, Element> { [shortCode] = New(shortCode, []) }), "model.unresolved");
    }

    /// <summary>A computed column declares no type: DacFx gives it an Expression and ExpressionDependencies and no DataType.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_column_with_no_one_data_type_is_model_unresolved()
    {
        var computed = New(Key(Table, "Column", "Twice"), [("Expression", new Value.Script("([Amt] * 2)")), ("Persisted", Bool(true))],
            [("ExpressionDependencies", [Key(Table, "Column", "Amt")])]);
        var twice = New(Key(Table, "Column", "c"), [],
            [("DataType", [Ok(ElementKey.Of("DataType", Ok(Name.Of("int")))), Ok(ElementKey.Of("DataType", Ok(Name.Of("bigint"))))])]);

        Expect.Failed(ColumnType.Of(computed), "model.unresolved");
        Expect.Failed(ColumnType.Of(twice), "model.unresolved");
    }
}

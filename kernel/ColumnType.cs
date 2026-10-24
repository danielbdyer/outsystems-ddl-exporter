using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>
/// The type a column declares, as the model holds it: the one target of its DataType relationship, a system type such as <c>[nvarchar]</c>
/// or an alias a CREATE TYPE defines, such as <c>[dbo].[ShortCode]</c>, and the five facets DacFx holds on the column: Length, IsMax,
/// Precision, Scale and Collation. A facet the type does not take holds 0, false or null, and DacFx writes some defaults of its own
/// (measured on DacFx 170.5.96: datetime2's Scale 7, float's Precision 53, an alias-typed column's Length 0); a null Collation is the
/// database's. Two types are equal or not, and no type is wider or narrower than another: the kernel judges no change a narrowing or a
/// widening (decision 2.9), so a Fits precondition holds both types and the aggregate query of WP 2.3 counts what fails to convert, with
/// TRY_CONVERT to the <see cref="Stored"/> type.
/// </summary>
public sealed record ColumnType(Name Type, long Length, bool IsMax, long Precision, long Scale, string? Collation)
{
    /// <summary>Whether the type is an alias a CREATE TYPE defines, which is named in two parts; a system type is named in one.</summary>
    public bool IsAlias => Type.Schema is not null;

    /// <summary>
    /// The type a Column element declares: its DataType relationship's one target, and its Length, IsMax, Precision, Scale and Collation. A
    /// computed column declares none, so it, and any column without exactly one DataType target, is the error model.unresolved.
    /// </summary>
    public static Result<ColumnType> Of(Element column) =>
        column.Relationships.FirstOrDefault(r => r.Name == "DataType")?.Targets is [var type]
            ? new ColumnType(type.Key.Name, Integer(column, "Length"), column["IsMax"] is Value.Boolean { IsTrue: true },
                Integer(column, "Precision"), Integer(column, "Scale"), Text(column["Collation"]))
            : Unresolved(column.Key + " has no one DataType target: a computed column declares no type, and any other column one.");

    /// <summary>
    /// The type SQL Server stores, which TRY_CONVERT takes where it takes no alias: this type, for a system type; for an alias, the type
    /// its DataType element names as Type, with that element's UddtLength, UddtIsMax, UddtPrecision and UddtScale, and this column's
    /// Collation. An alias <paramref name="model"/> does not hold, or one that names no Type, is the error model.unresolved.
    /// </summary>
    public Result<ColumnType> Stored(IReadOnlyDictionary<ElementKey, Element> model) =>
        !IsAlias ? this
        : ElementKey.Of("DataType", Type).Bind<ColumnType>(key =>
            model.GetValueOrDefault(key) is { } alias && alias.Relationships.FirstOrDefault(r => r.Name == "Type")?.Targets is [var stored]
                ? new ColumnType(stored.Key.Name, Integer(alias, "UddtLength"), alias["UddtIsMax"] is Value.Boolean { IsTrue: true },
                    Integer(alias, "UddtPrecision"), Integer(alias, "UddtScale"), Collation)
                : Unresolved(key + ", the alias type a column declares, is no element of the model with one Type target to store as."));

    /// <summary>
    /// The most bytes a value of the stored type takes in an index key, as SQL Server stores it (KeyLength, Msg 1946): a string's or a
    /// binary's declared length, twice it for a Unicode string; a number's, a date's or a time's fixed size by its precision or scale; a
    /// uniqueidentifier's 16, a rowversion's 8, a sql_variant's 8,016 and a hierarchyid's 892; long.MaxValue for a max type; null for an
    /// alias, which <see cref="Stored"/> resolves first, and for a type no key holds.
    /// </summary>
    public long? KeyBytes
    {
        get
        {
            if (IsMax)
            {
                return long.MaxValue;
            }

            long Scaled(long two, long four, long seven) => Scale <= 2 ? two : Scale <= 4 ? four : seven;
            return IsAlias ? null : Type.Base.ToLowerInvariant() switch
            {
                "char" or "varchar" or "binary" or "varbinary" => Length,
                "nchar" or "nvarchar" => 2 * Length,
                "sysname" => 256,
                "bit" or "tinyint" => 1,
                "smallint" => 2,
                "int" or "real" or "smallmoney" or "smalldatetime" => 4,
                "bigint" or "money" or "datetime" or "rowversion" or "timestamp" => 8,
                "float" => Precision is > 0 and <= 24 ? 4 : 8,
                "decimal" or "numeric" => Precision <= 9 ? 5 : Precision <= 19 ? 9 : Precision <= 28 ? 13 : 17,
                "date" => 3,
                "time" => Scaled(3, 4, 5),
                "datetime2" => Scaled(6, 7, 8),
                "datetimeoffset" => Scaled(8, 9, 10),
                "uniqueidentifier" => 16,
                "sql_variant" => 8016,
                "hierarchyid" => 892,
                _ => null,
            };
        }
    }

    /// <summary>An integer facet as the model holds it; 0 where the element does not carry it.</summary>
    private static long Integer(Element element, string property) => element[property] is Value.Integer i ? i.Number : 0;

    private static string? Text(Value? value) => value switch
    {
        Value.Text t => t.Content,
        Value.Script s => s.Content,
        _ => null,
    };

    /// <summary>The remedy of model.unresolved: io/Ssdt.ReadModel reads every relationship DacFx holds.</summary>
    internal const string ReadModelDefect =
        "Report the model's source with this error; io/Ssdt.ReadModel reads every relationship DacFx holds, so a missing one is a defect.";

    private static Error Unresolved(string message) => new("model.unresolved", message, ReadModelDefect);
}

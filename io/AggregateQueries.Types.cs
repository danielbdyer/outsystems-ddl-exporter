using System;
using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnType = DbChange.Kernel.ColumnType;
using ScriptLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DbChange.Io;

/// <summary>
/// The type SQL Server gives an operand of model text, as far as the check's rewriter reads it (Check.cs). Where two operands of different
/// types meet, SQL Server converts the one of lower data type precedence to the other's type, and every string type stands below every
/// number, date and time type and uniqueidentifier: a string compared with, added to or listed beside such a value converts with no
/// CONVERT written, and one that does not convert raises Msg 245 or 248, which quote it. The type it converts to is the other operand's
/// own, time, datetime2 and datetimeoffset at scale 7, as SQL Server 2022 (16.0.4305.5) plans CONVERT_IMPLICIT: tinyint for a tinyint
/// column, decimal(5,2) for a decimal(5,2) one, numeric(2,1) for the literal 1.5, int for 0, time(7) for a time(3) column. A string that
/// meets hierarchyid, xml or a spatial or CLR type is parsed into it, and the parse's error quotes the string (Msg 6522), so the rewriter
/// reads no such meeting.
/// </summary>
public static partial class AggregateQueries
{
    /// <summary>SQL Server's data type precedence, lowest first: numeric ranks with decimal, sysname with nvarchar.</summary>
    private static readonly string[] Precedence = ["binary", "varbinary", "char", "varchar", "nchar", "nvarchar", "uniqueidentifier",
        "timestamp", "image", "text", "ntext", "bit", "tinyint", "smallint", "int", "bigint", "smallmoney", "money", "decimal", "real",
        "float", "time", "date", "smalldatetime", "datetime", "datetime2", "datetimeoffset", "xml", "sql_variant"];

    /// <summary>
    /// An operand's type: its base type's name, its kind, and the type TRY_CONVERT takes for a string SQL Server converts to it, null
    /// where the rewriter does not read its precision, as for a sum of decimals.
    /// </summary>
    private sealed record Typed(string Base, Family Family, string? Target)
    {
        /// <summary>NULL written as a literal, which converts nothing it meets (measured: NULL &gt; a string raises nothing).</summary>
        public static readonly Typed Null = new("null", Family.Other, null);

        public static readonly Typed String = new("varchar", Family.Character, null);

        public static readonly Typed Int = new("int", Family.Integer, "int");

        /// <summary>A string type, char to ntext: one SQL Server converts to a number, a date and time type or uniqueidentifier it
        /// meets.</summary>
        public bool IsString => Family is Family.Character or Family.Text;

        /// <summary>A number, a date and time type or uniqueidentifier: a type a string it meets converts to, which can fail.</summary>
        public bool TakesStrings => Family is Family.Integer or Family.Bit or Family.Decimal or Family.Approximate or Family.Date
            or Family.Time or Family.DateTime or Family.DateTime2 or Family.Guid;

        /// <summary>
        /// A type SQL Server parses a string it meets into with no CONVERT written, where no TRY_ form stands, and whose failure can quote
        /// the string: hierarchyid (Msg 6522, measured on SQL Server 2022), xml, a spatial or other CLR type, and any type of no kind the
        /// rewriter reads. sql_variant takes a string as it is (measured), and NULL converts nothing.
        /// </summary>
        public bool ParsesStrings => Family == Family.Other && Base is not ("null" or "sql_variant");

        /// <summary>The type's place in SQL Server's data type precedence, higher above lower; -1 for NULL and a type not listed.</summary>
        public int Rank => Array.IndexOf(Precedence, Base switch { "numeric" => "decimal", "sysname" => "nvarchar", var b => b });

        /// <summary>A column's type as SQL Server stores it.</summary>
        public static Typed Of(ColumnType type) => Of(type.IsAlias ? "" : type.Type.Base.ToLowerInvariant(), Written(type));

        /// <summary>The type a CONVERT, CAST or TRY_ form names; null for an alias.</summary>
        public static Typed? Of(DataTypeReference type) => type switch
        {
            SqlDataTypeReference sql => Of(sql.SqlDataTypeOption.ToString().ToLowerInvariant(), TSql.Text(sql)),
            UserDataTypeReference { Name.BaseIdentifier.Value: var name }
                when string.Equals(name, "sysname", StringComparison.OrdinalIgnoreCase) => Of("sysname", "sysname"),
            _ => null,
        };

        /// <summary>
        /// A literal's type as SQL Server reads it: an integer within int's range an int, a larger one or a decimal numeric of its digits
        /// (3000000000 is numeric(10,0), 1.5 numeric(2,1)), a real float, money money, and a string a string.
        /// </summary>
        public static Typed? Of(ScriptLiteral literal) => literal switch
        {
            NullLiteral => Null,
            StringLiteral => String,
            BinaryLiteral => new("varbinary", Family.Binary, null),
            IntegerLiteral i => long.TryParse(i.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= int.MaxValue
                ? Int : Numeric(i.Value.TrimStart('0'), ""),
            NumericLiteral d => d.Value.Split('.') switch
            {
                [var whole] => Numeric(whole.TrimStart('0'), ""),
                [var whole, var fraction] => Numeric(whole.TrimStart('0'), fraction),
                _ => null,
            },
            RealLiteral => Of("float", "float"),
            MoneyLiteral => Of("money", "money"),
            _ => null,
        };

        /// <summary>The type of a sum, a difference, a product or a quotient: the operand's of higher precedence, read exactly for the
        /// integer types, float and real, whose arithmetic keeps the type, and without its precision for any other.</summary>
        public static Typed? Arithmetic(Typed? first, Typed? second)
        {
            if (first is null || second is null)
            {
                return null;
            }

            var higher = first.Rank >= second.Rank ? first : second;
            return higher.Family is Family.Integer or Family.Approximate ? higher : higher with { Target = null };
        }

        private static Typed? Numeric(string whole, string fraction) => whole.Length + fraction.Length is var digits and <= 38
            ? Of("numeric", "numeric(" + Math.Max(digits, 1).ToString(CultureInfo.InvariantCulture) + ","
                + fraction.Length.ToString(CultureInfo.InvariantCulture) + ")")
            : null;

        private static Typed Of(string name, string written) => new(name, FamilyOf(name),
            name is "time" or "datetime2" or "datetimeoffset" ? name + "(7)" : written);
    }
}

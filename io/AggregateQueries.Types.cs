using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// reads no such meeting. ISNULL converts its second argument to the type of its first whatever their precedence, and that conversion's
/// error quotes the value too (Msg 220 for an integer into tinyint or smallint, Msg 232 for a float), so the rewriter reads none but a
/// string's into a string type, which never fails, and a literal's, which quotes no row, where the query writes the first argument as
/// itself.
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

        /// <summary>Whether two operands are of one type, length, precision and scale, as each is written for TRY_CONVERT; one whose
        /// precision the rewriter does not read, such as a string literal's length, is of no other's type.</summary>
        public bool Same(Typed other) => Base == other.Base && Target is { } own && other.Target is { } theirs
            && string.Equals(Plain(own), Plain(theirs), StringComparison.Ordinal);

        /// <summary>A type as written for TRY_CONVERT, its spaces dropped and its letters in lower case: DECIMAL (5, 2) as decimal(5,2).
        /// </summary>
        private static string Plain(string written) => string.Concat(written.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();

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

    /// <summary>The check's rewriter (Check.cs), where it reads the type of a call and a string SQL Server parses.</summary>
    private sealed partial class Rewriter
    {
        /// <summary>
        /// The type of a call the allowlist admits: LEN and DATALENGTH an int; UPPER, LOWER, LTRIM and RTRIM a string; ABS its argument's;
        /// ISNULL its first argument's, to which SQL Server converts its second, with no CONVERT written, on the rows whose first is NULL.
        /// That conversion leaves <see cref="Why"/>, as measured on SQL Server 2022: ISNULL(CAST(NULL AS tinyint), [Num]) raises Msg 220
        /// quoting a Num of 98765, and a column the change creates, which the query writes as NULL, converts nothing in the query where
        /// it does in the deployment. A string into a string type is read where the query writes the first argument as itself, since
        /// SQL Server truncates it or writes '?' without an error, as the deployment does, and so is a literal, whose conversion reads no
        /// row's value: ISNULL([Flag], (0)) over a bit column. Null for any other call, and for ABS of a string, which SQL Server
        /// converts to float.
        /// </summary>
        private Typed? Function(FunctionCall f)
        {
            var first = f.Parameters.Count > 0 ? TypeOf(f.Parameters[0]) : null;
            switch (f.FunctionName.Value.ToUpperInvariant())
            {
                case "LEN" or "DATALENGTH":
                    return Typed.Int;
                case "UPPER" or "LOWER" or "LTRIM" or "RTRIM":
                    return Typed.String;
                case "ABS" when first is { IsString: true }:
                    Why ??= "it calls ABS on a string, which SQL Server converts to float with no CONVERT written";
                    return null;
                case "ABS":
                    return first;
                case "ISNULL" when f.Parameters.Count == 2:
                    var second = TypeOf(f.Parameters[1]);
                    var held = !Untyped(f.Parameters[0]);
                    var constant = held && Constant(f.Parameters[1]);
                    if (first is { TakesStrings: true } && second is { IsString: true } && !constant)
                    {
                        f.Parameters[1] = Converted(f.Parameters[1], first, every: false);
                    }

                    if (first is { ParsesStrings: true } && second is { IsString: true })
                    {
                        Parses([first, second]);
                    }

                    // The literal NULL as the first argument takes the second's type, and converts nothing.
                    var converts = first != Typed.Null && second != Typed.Null && (first is null || second is null || !first.Same(second));
                    var kept = constant || (first is { IsString: true } && second is { IsString: true } && held);
                    if (converts && !kept)
                    {
                        Why ??= "SQL Server converts ISNULL's second argument to the type of its first with no CONVERT written, on the"
                            + " rows whose first is NULL, and the conversion's error can quote the row's value (Msg 220 for an integer"
                            + " into tinyint or smallint, Msg 232 for a float), so the rows it fails on are not known read-only";
                    }

                    return first == Typed.Null ? second : first;
                default:
                    return null;
            }
        }

        /// <summary>Whether a string meets, among <paramref name="met"/>, a type SQL Server parses it into with no CONVERT written, whose
        /// error would quote it (<see cref="Typed.ParsesStrings"/>); where one does, it leaves <see cref="Why"/>.</summary>
        private bool Parses(IReadOnlyList<Typed?> met)
        {
            if (met.FirstOrDefault(t => t is { ParsesStrings: true }) is not { } parser || !met.Any(t => t is { IsString: true }))
            {
                return false;
            }

            Why ??= "it sets a string against a value of " + (parser.Base.Length > 0 ? parser.Base : "an alias type") + ", which SQL Server"
                + " parses the string into with no CONVERT written, and the error of a string that does not parse quotes it";
            return true;
        }

        /// <summary>Whether a value is a literal, a number, a string or binary, in parentheses or not and with a sign or not: its
        /// conversion reads no row's value, and the query converts it as the check's validation does.</summary>
        private static bool Constant(ScalarExpression value) => value switch
        {
            IntegerLiteral or NumericLiteral or RealLiteral or MoneyLiteral or StringLiteral or BinaryLiteral => true,
            ParenthesisExpression p => Constant(p.Expression),
            UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } u =>
                Constant(u.Expression),
            _ => false,
        };

        /// <summary>Whether the query writes NULL here, in parentheses or not, as it does for a column the change creates: SQL Server gives
        /// that NULL no type, and the column's type stands in the rewriter's map alone.</summary>
        private static bool Untyped(ScalarExpression value) =>
            value is NullLiteral || (value is ParenthesisExpression p && Untyped(p.Expression));
    }
}

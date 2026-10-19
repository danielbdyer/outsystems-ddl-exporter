using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnType = DbChange.Kernel.ColumnType;

namespace DbChange.Io;

/// <summary>
/// Fits (decision 2.9): the rows of a column whose type the change alters on which ALTER COLUMN fails, the violating rows, and beside
/// them the rows of each value class, each an item of its own (the Facsimile's ARCHITECTURE.md §4). Into a string type with a length:
/// over length, the value converted to the type's max form longer than the new length in bytes once its trailing spaces are gone, which
/// ALTER COLUMN refuses; and over bytes, longer only with them, which ALTER COLUMN trims without an error (measured on SQL Server 2022,
/// 16.0.4305.5). Into a binary type with a length: over length, the value longer than the new length. Into any other type the violating
/// rows are the non-null values TRY_CONVERT gives NULL, and the classes are: not convertible; an empty string, which the integer types,
/// bit, money, float, real and the date and time types read without an error, as 0 or 1900-01-01 00:00, and decimal and uniqueidentifier
/// refuse; overflow, a value of the same kind out of the new type's range; and a lossy round trip where one value's predicate shows it:
/// a number into bit other than 0 or 1, the time of a date and time into date, its date into time, and an offset other than +00:00 that
/// a datetimeoffset loses. The classes share no row; over bytes, an empty string a type reads and a lossy round trip convert without an
/// error, so they are counted as classes and never as violating. What no count shows prints a line: characters substituted with '?'
/// into a character type whose code page can lack them, and values rounded where the fractional digits the type holds fall. A pair of
/// types SQL Server converts neither way, or a type no count reads (xml, sql_variant, hierarchyid, a spatial or CLR type, text or image
/// as the new type), reads not predictable read-only.
/// </summary>
public static partial class AggregateQueries
{
    /// <summary>A type's kind, as SQL Server's table of explicit conversions groups the types: TRY_CONVERT refuses a pair of kinds
    /// <see cref="Converts"/> does not hold with Msg 529 (measured on SQL Server 2022 for every pair of types).</summary>
    private enum Family
    {
        Character,
        Text,
        Binary,
        Image,
        Integer,
        Bit,
        Decimal,
        Approximate,
        Date,
        Time,
        DateTime,
        DateTime2,
        Guid,
        Other,
    }

    private static Asked Fits(Coordinates.Counted counted, PreconditionKind.Fits fits, Context context, List<Finding> notes)
    {
        var precondition = counted.Precondition;
        if (context.Column(counted, precondition.Table, precondition.Columns[0]) is not { Name: { } name })
        {
            return Asked.Not(Unplaced(precondition.Columns[0]));
        }

        if ((context.Stored(fits.Before, inHead: false), context.Stored(fits.After, inHead: true)) is not ({ } before, { } after))
        {
            return Asked.Not("the model holds no type the column's alias names");
        }

        var (from, to) = (FamilyOf(before), FamilyOf(after));
        if (!Converts(from, to) || TSql.DataType(Written(after)) is not { } target)
        {
            return Asked.Not("dbchange counts no conversion from " + Written(before) + " to " + Written(after));
        }

        var subject = precondition.Table + "." + precondition.Columns[0];
        notes.AddRange(Lines(subject, before, after, from, to, context));
        var items = Misfits(name, before, after, from, to, target, context).ToList();
        return new Part(precondition, [.. items.Select(i => ((ScalarExpression)Sum(i.When), i.Class))], [],
            [.. items.Where(i => i.Class is not null).Select(i => i.Class!.Value)],
            (_, values) => values.Count == 0 ? (0L, Array.Empty<long>()) : (values[0], values.Skip(1).ToArray()));
    }

    /// <summary>
    /// The predicates a retype from <paramref name="before"/> to <paramref name="after"/> counts over the column <paramref name="name"/>
    /// of the table aliased t: first, with no class, the rows on which ALTER COLUMN fails, the violating rows; then each value class and
    /// the predicate that holds for its rows alone. Nothing fails into a max type, and no class is counted there.
    /// </summary>
    private static IEnumerable<(Class? Class, BooleanExpression When)> Misfits(string name, ColumnType before, ColumnType after,
        Family from, Family to, DataTypeReference target, Context context)
    {
        ScalarExpression Value() => ColumnRef("t", name);
        var bases = (Before: before.Type.Base.ToLowerInvariant(), After: after.Type.Base.ToLowerInvariant());
        if (to is Family.Character or Family.Binary)
        {
            if (after.IsMax)
            {
                yield break;
            }

            var unicode = bases.After is "nchar" or "nvarchar" or "sysname";
            var length = bases.After == "sysname" && after.Length == 0 ? 128 : after.Length;
            var bytes = Number(to == Family.Character && unicode ? 2 * length : length);
            ScalarExpression Converted() => TryConvert(to == Family.Binary ? "varbinary(max)" : unicode ? "nvarchar(max)" : "varchar(max)",
                from is Family.Character or Family.Text && to == Family.Character && context.Collation(before) != context.Collation(after)
                    ? ColumnRef("t", name, context.Collation(after)) : Value());
            var longer = to == Family.Binary
                ? Compare(Call("DATALENGTH", Converted()), BooleanComparisonType.GreaterThan, bytes)
                : Compare(Call("DATALENGTH", Call("RTRIM", Converted())), BooleanComparisonType.GreaterThan, bytes);
            yield return (null, longer);
            yield return (Class.OverLength, longer);
            if (to == Family.Character)
            {
                yield return (Class.OverBytes, All([Compare(Call("DATALENGTH", Converted()), BooleanComparisonType.GreaterThan, bytes),
                    Compare(Call("DATALENGTH", Call("RTRIM", Converted())), BooleanComparisonType.LessThanOrEqualTo, bytes)]));
            }

            yield break;
        }

        BooleanExpression Fails() =>
            All([Is(Value(), isNull: false), Is(new TryConvertCall { DataType = target, Parameter = Value() }, isNull: true)]);
        yield return (null, Fails());
        var wide = from == Family.Character ? Wider(after) : null;
        if (from is Family.Character or Family.Binary)
        {
            var also = new List<BooleanExpression>();
            if (from == Family.Character)
            {
                also.Add(Compare(Call("LEN", Value()), BooleanComparisonType.NotEqualToBrackets, Number(0)));
            }

            if (wide is not null)
            {
                also.Add(Is(TryConvert(wide, Value()), isNull: true));
            }

            yield return (Class.NotConvertible, All([Fails(), .. also]));
        }

        if (from == Family.Character)
        {
            yield return (Class.EmptyString, Compare(Call("LEN", Value()), BooleanComparisonType.Equals, Number(0)));
        }

        Family[] numbers = [Family.Integer, Family.Bit, Family.Decimal, Family.Approximate, Family.DateTime];
        Family[] dates = [Family.Date, Family.Time, Family.DateTime, Family.DateTime2];
        if ((numbers.Contains(from) && to is not Family.Bit && numbers.Contains(to)) || (dates.Contains(from) && dates.Contains(to)))
        {
            yield return (Class.Overflow, Fails());
        }
        else if (wide is not null)
        {
            yield return (Class.Overflow, All([Fails(), Is(TryConvert(wide, Value()), isNull: false)]));
        }

        var dropped = new List<BooleanExpression>();
        if (to == Family.Bit && from is Family.Integer or Family.Decimal or Family.Approximate)
        {
            var unlike = In(Value(), Number(0), Number(1));
            unlike.NotDefined = true;
            dropped.Add(unlike);
        }

        if (from is Family.DateTime or Family.DateTime2 && to is Family.Date or Family.Time)
        {
            dropped.Add(to == Family.Date
                ? Compare(TryConvert("time", Value()), BooleanComparisonType.NotEqualToBrackets, new StringLiteral { Value = "00:00:00" })
                : Compare(TryConvert("date", Value()), BooleanComparisonType.NotEqualToBrackets, new StringLiteral { Value = "19000101" }));
        }

        if (bases.Before == "datetimeoffset" && bases.After != "datetimeoffset")
        {
            dropped.Add(new LikePredicate { FirstExpression = TryConvert("nvarchar(40)", Value()), SecondExpression = Unicode("%+00:00"),
                NotDefined = true });
        }

        if (dropped.Count > 0)
        {
            yield return (Class.LossyRoundTrip, Any(dropped));
        }
    }

    /// <summary>
    /// The lines a Fits site prints for what no count shows: characters substituted with '?' where a character type that is no Unicode type
    /// takes a Unicode value or one of another collation, whose code page can lack a character; and values rounded where the fractional
    /// digits the new type holds fall, which a string read as a number or a date and time can carry too, but not one read as an integer,
    /// which fails instead.
    /// </summary>
    private static IEnumerable<Finding> Lines(string subject, ColumnType before, ColumnType after, Family from, Family to, Context context)
    {
        var change = subject + " changes from " + Written(before) + " to " + Written(after);
        if (after.Type.Base.ToLowerInvariant() is "char" or "varchar" && from is Family.Character or Family.Text
            && (before.Type.Base.ToLowerInvariant() is "nchar" or "nvarchar" or "ntext" or "sysname"
                || context.Collation(before) != context.Collation(after)))
        {
            yield return Finding.Note("fits.substituted-characters", subject, change + " under " + context.Collation(after)
                + ": TRY_CONVERT and ALTER COLUMN alike write '?' for a character its code page lacks, without an error, so no count shows"
                + " such a value.");
        }

        if (Fraction(after) is { } lesser && Fraction(before) is { } greater && lesser < greater
            && !(from is Family.Character or Family.Text && to is Family.Integer or Family.Bit))
        {
            yield return Finding.Note("fits.rounded-values", subject, change
                + ", which holds fewer decimals or fractional seconds: TRY_CONVERT and ALTER COLUMN alike round such a value, without an"
                + " error, so no count shows it.");
        }
    }

    private static Family FamilyOf(ColumnType type) => type.IsAlias ? Family.Other : type.Type.Base.ToLowerInvariant() switch
    {
        "char" or "varchar" or "nchar" or "nvarchar" or "sysname" => Family.Character,
        "text" or "ntext" => Family.Text,
        "binary" or "varbinary" => Family.Binary,
        "image" => Family.Image,
        "tinyint" or "smallint" or "int" or "bigint" => Family.Integer,
        "bit" => Family.Bit,
        "decimal" or "numeric" or "money" or "smallmoney" => Family.Decimal,
        "float" or "real" => Family.Approximate,
        "date" => Family.Date,
        "time" => Family.Time,
        "datetime" or "smalldatetime" => Family.DateTime,
        "datetime2" or "datetimeoffset" => Family.DateTime2,
        "uniqueidentifier" => Family.Guid,
        _ => Family.Other,
    };

    /// <summary>Whether TRY_CONVERT takes a value of the one kind to the other, as SQL Server 2022 answered for every pair of types; a
    /// retype into text, ntext or image, or from or into a kind no count reads, is not counted.</summary>
    private static bool Converts(Family from, Family to) => (from, to) switch
    {
        (Family.Other, _) or (_, Family.Other) or (_, Family.Text) or (_, Family.Image) => false,
        (Family.Text, _) => to == Family.Character,
        (Family.Image, _) => to == Family.Binary,
        (Family.Character, _) => true,
        (Family.Binary, _) => to != Family.Approximate,
        (Family.Integer or Family.Bit or Family.Decimal or Family.Approximate, _) => to is Family.Character or Family.Binary
            or Family.Integer or Family.Bit or Family.Decimal or Family.Approximate or Family.DateTime,
        (Family.Date, _) => to is Family.Character or Family.Binary or Family.Date or Family.DateTime or Family.DateTime2,
        (Family.Time, _) => to is Family.Character or Family.Binary or Family.Time or Family.DateTime or Family.DateTime2,
        (Family.DateTime, _) => to != Family.Guid,
        (Family.DateTime2, _) => to is Family.Character or Family.Binary or Family.Date or Family.Time or Family.DateTime
            or Family.DateTime2,
        (Family.Guid, _) => to is Family.Character or Family.Binary or Family.Guid,
        _ => false,
    };

    /// <summary>The wider type of a number's or a date's kind a string that overflows <paramref name="type"/> still converts to; none
    /// where no wider type of the kind exists, or for an integer, whose overflowing string reads not convertible as the catalog's QST-23
    /// says.</summary>
    private static string? Wider(ColumnType type) => type.Type.Base.ToLowerInvariant() switch
    {
        "decimal" or "numeric" => "decimal(38," + Digits(type.Scale) + ")",
        "money" or "smallmoney" => "decimal(38,4)",
        "real" => "float",
        "datetime" or "smalldatetime" => "datetime2(7)",
        _ => null,
    };

    /// <summary>The fractional digits a type holds: a string's and a float's without bound, an integer's and a date's none, money's four,
    /// datetime's three, and a decimal's, a time's or a datetime2's its Scale; none for a kind with no number in it.</summary>
    private static int? Fraction(ColumnType type) => FamilyOf(type) switch
    {
        Family.Character or Family.Text or Family.Approximate => int.MaxValue,
        Family.Integer or Family.Bit or Family.Date => 0,
        Family.Decimal => type.Type.Base.ToLowerInvariant() is "money" or "smallmoney" ? 4 : (int)type.Scale,
        Family.DateTime => type.Type.Base.ToLowerInvariant() == "datetime" ? 3 : 0,
        Family.Time or Family.DateTime2 => (int)type.Scale,
        _ => null,
    };

    private static TryConvertCall TryConvert(string type, ScalarExpression value) =>
        new() { DataType = TSql.DataType(type) ?? throw new UnreachableException(type), Parameter = value };
}

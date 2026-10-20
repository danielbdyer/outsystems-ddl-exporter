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
/// them the rows of each value class, each an item of its own (the Facsimile's ARCHITECTURE.md §4). Into a string or binary type with a
/// length, a value too long for it, as measured value by value on SQL Server 2022 (16.0.4305.5): from a string, longer than the new length
/// in bytes once its trailing spaces are gone, which ALTER COLUMN refuses (Msg 2628), and over bytes, longer only with them, which it
/// trims without an error; from a number or uniqueidentifier, its string form longer (Msg 8115, 234, 292, 232 or 8170). Those are over
/// length and violating. From a binary type a value longer than the new length is over length and decides nothing alone: ALTER COLUMN
/// drops its bytes past the new length where they are all 0x00 (0x010200 into varbinary(2), 0x4100 into varchar(1)) and refuses it
/// otherwise (Msg 2628), and no query the allowlist admits tells the two apart, so the site reads not predictable read-only once any such
/// value is counted, and where none is, no row is violating. ALTER COLUMN writes the others without an error, a lossy round trip and never
/// violating: tinyint, smallint or int into char or varchar as '*', a date and time type into a string cut at the new length, an integer
/// into a binary type with its high bytes dropped, which loses a value outside 0 to 256^n - 1, and decimal, money, float or
/// uniqueidentifier into a binary type shorter than its own bytes. Into any other type the violating rows are the non-null values
/// TRY_CONVERT gives NULL, and the classes are: not convertible; an empty string, which the integer types, bit, money, float, real and the
/// date and time types read without an error, as 0 or 1900-01-01 00:00, and decimal and uniqueidentifier refuse; overflow, a value of the
/// same kind out of the new type's range; and a lossy round trip where one value's predicate shows it: a number into bit other than 0 or 1,
/// the time of a date and time into date, its date into time, and an offset other than +00:00 that a datetimeoffset loses. The classes
/// share no row; over bytes, an empty string a type reads and a lossy round trip convert without an error, so they are counted as classes
/// and never as violating. What no count shows prints a line: characters substituted with '?' into a character type whose code page can
/// lack them, and values rounded where the fractional digits the type holds fall, or into float or real, which keep about 15 and 7
/// significant digits, from a type that holds more. A pair of types SQL Server converts neither way, and a type no count reads (xml,
/// sql_variant, hierarchyid, a spatial or CLR type, text or image as the new type), read not predictable read-only.
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
        var violating = items.Any(i => i.Class is null);

        // From a binary type into one with a length, the one item counts the values longer than the new type, and any such value leaves
        // the site not predictable read-only (TooLong).
        string? Longer(IReadOnlyList<long> values) => values[0] == 0 ? null
            : Digits(values[0]) + (values[0] == 1 ? " row holds" : " rows hold") + " a value longer than " + Written(after) + " takes, and"
                + " ALTER COLUMN drops its bytes past the new length where they are all 0x00 and refuses it otherwise (Msg 2628), which no"
                + " query the allowlist admits tells apart";
        return new Part(precondition, [.. items.Select(i => ((ScalarExpression)Sum(i.When), i.Class))], [],
            [.. items.Where(i => i.Class is not null).Select(i => i.Class!.Value)],
            (_, values) => violating ? (values[0], values.Skip(1).ToArray()) : (0L, values.ToArray()),
            from is Family.Binary or Family.Image && items.Count > 0 && !violating ? Longer : null);
    }

    /// <summary>
    /// The predicates a retype from <paramref name="before"/> to <paramref name="after"/> counts over the column <paramref name="name"/>
    /// of the table aliased t: first, with no class, the rows on which ALTER COLUMN fails, the violating rows, where it can fail on any;
    /// then each value class and the predicate that holds for its rows alone. Nothing fails into a max type, and no class is counted there.
    /// </summary>
    private static IEnumerable<(Class? Class, BooleanExpression When)> Misfits(string name, ColumnType before, ColumnType after,
        Family from, Family to, DataTypeReference target, Context context) =>
        to is Family.Character or Family.Binary ? after.IsMax ? [] : TooLong(name, before, after, from, to, context)
        : Unconverted(name, before, after, from, to, target);

    /// <summary>
    /// A retype into a string or binary type with a length: the rows too long for it, violating and over length where ALTER COLUMN
    /// refuses them, a lossy round trip where it writes them without an error; and, from a string, over bytes.
    /// </summary>
    private static IEnumerable<(Class? Class, BooleanExpression When)> TooLong(string name, ColumnType before, ColumnType after,
        Family from, Family to, Context context)
    {
        var bytes = Number(Capacity(after));
        BooleanExpression Over(ScalarExpression length) => Compare(length, BooleanComparisonType.GreaterThan, bytes);
        if (from is Family.Binary or Family.Image)
        {
            // A binary value's own bytes, a trailing 0x20 among them: ALTER COLUMN keeps 0x412020 out of varchar(1) (Msg 2628) and writes
            // 0x4100 as 'A', so a value longer is over length alone, and Fits reads the site not predictable read-only once one is counted.
            yield return (Class.OverLength, Over(Call("DATALENGTH", ColumnRef("t", name))));
            yield break;
        }

        if (to == Family.Binary && from is Family.Integer or Family.Bit)
        {
            // An integer keeps its low bytes, so a value from 0 to 256^n - 1 comes back as it was.
            if (after.Length < before.Type.Base.ToLowerInvariant() switch { "bigint" => 8, "int" => 4, "smallint" => 2, _ => 1 })
            {
                yield return (Class.LossyRoundTrip, Any([Compare(ColumnRef("t", name), BooleanComparisonType.LessThan, Number(0)),
                    Compare(ColumnRef("t", name), BooleanComparisonType.GreaterThan, Number((1L << (int)(8 * after.Length)) - 1))]));
            }

            yield break;
        }

        var strings = from is Family.Character or Family.Text;
        var form = to == Family.Binary ? "varbinary(max)" : Unicode(after) ? "nvarchar(max)" : "varchar(max)";
        ScalarExpression Converted() => TryConvert(form, ColumnRef("t", name,
            strings && to == Family.Character && context.Collation(before) != context.Collation(after) ? context.Collation(after) : null));
        if (strings && to == Family.Character)
        {
            yield return (null, Over(Call("DATALENGTH", Call("RTRIM", Converted()))));
            yield return (Class.OverLength, Over(Call("DATALENGTH", Call("RTRIM", Converted()))));
            yield return (Class.OverBytes, All([Over(Call("DATALENGTH", Converted())),
                Compare(Call("DATALENGTH", Call("RTRIM", Converted())), BooleanComparisonType.LessThanOrEqualTo, bytes)]));
            yield break;
        }

        // A string or a date and time type into a binary type ALTER COLUMN refuses whatever the rows hold (Msg 257, measured on an empty
        // table); such a retype counts its rows too long as violating, and whether DacFx alters or rebuilds the table is not checked.
        var lossy = to == Family.Binary ? from is Family.Decimal or Family.Approximate or Family.Guid
            : from is Family.Date or Family.Time or Family.DateTime or Family.DateTime2
                || (from == Family.Integer && !Unicode(after) && before.Type.Base.ToLowerInvariant() != "bigint");
        yield return lossy ? (Class.LossyRoundTrip, Over(Call("DATALENGTH", Converted()))) : (null, Over(Call("DATALENGTH", Converted())));
        if (!lossy)
        {
            yield return (Class.OverLength, Over(Call("DATALENGTH", Converted())));
        }
    }

    /// <summary>The bytes a string or binary type with a length holds: its length, twice it for a Unicode type, sysname's 256.</summary>
    private static long Capacity(ColumnType type) =>
        (type.Type.Base.ToLowerInvariant() == "sysname" && type.Length == 0 ? 128 : type.Length) * (Unicode(type) ? 2 : 1);

    /// <summary>A retype into any other type: the non-null values TRY_CONVERT gives NULL, violating, and the value classes.</summary>
    private static IEnumerable<(Class? Class, BooleanExpression When)> Unconverted(string name, ColumnType before, ColumnType after,
        Family from, Family to, DataTypeReference target)
    {
        ScalarExpression Value() => ColumnRef("t", name);
        var bases = (Before: before.Type.Base.ToLowerInvariant(), After: after.Type.Base.ToLowerInvariant());
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
    /// which fails instead, and where float or real keeps fewer digits than the old type holds (<see cref="Rounds"/>).
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

        if (to == Family.Approximate ? Rounds(before, after)
            : Fraction(after) is { } lesser && Fraction(before) is { } greater && lesser < greater
                && !(from is Family.Character or Family.Text && to is Family.Integer or Family.Bit))
        {
            var keeps = to != Family.Approximate ? ", which holds fewer decimals or fractional seconds"
                : ", which keeps about " + (Real(after) ? "7" : "15") + " significant digits, a decimal fraction as the nearest binary one";
            yield return Finding.Note("fits.rounded-values", subject, change + keeps
                + ": TRY_CONVERT and ALTER COLUMN alike round such a value, without an error, so no count shows it.");
        }
    }

    /// <summary>
    /// Whether ALTER COLUMN into float or real rounds a value of <paramref name="before"/>, as measured value by value on SQL Server 2022:
    /// real keeps about 7 significant digits and float about 15, each holding a decimal fraction as the nearest binary one, so a decimal or
    /// numeric with a Scale or with more digits than the new type keeps, money, smallmoney, bigint, int into real, float into real and a
    /// string, which can hold any digits, are rounded (1234567.89 as decimal(18,2) into real is 1234567.875, 9007199254740993 as bigint
    /// into float 9007199254740992, 0.1 as float into real 0.100000001490116); real into float, int into float and smallint into real keep
    /// every value. ALTER COLUMN refuses datetime and smalldatetime into either (Msg 257, measured on an empty table), so neither is read.
    /// </summary>
    private static bool Rounds(ColumnType before, ColumnType after) => before.Type.Base.ToLowerInvariant() switch
    {
        "decimal" or "numeric" => before.Scale > 0 || before.Precision > (Real(after) ? 7 : 15),
        "money" or "smallmoney" or "bigint" => true,
        "int" => Real(after),
        "float" => Real(after) && !Real(before),
        _ => FamilyOf(before) == Family.Character,
    };

    /// <summary>Whether a type is real, as float(1) to float(24) are stored; float with no precision is float(53).</summary>
    private static bool Real(ColumnType type) => type.Type.Base.ToLowerInvariant() switch
    {
        "real" => true,
        "float" => type.Precision is > 0 and <= 24,
        _ => false,
    };

    private static Family FamilyOf(ColumnType type) => type.IsAlias ? Family.Other : FamilyOf(type.Type.Base.ToLowerInvariant());

    /// <summary>A system type's kind, by its name in lower case.</summary>
    private static Family FamilyOf(string name) => name switch
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

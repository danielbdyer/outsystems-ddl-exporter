using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace DbChange.Kernel;

public sealed partial record Witness
{
    /// <summary>
    /// The planter's literals, as the Facsimile's PLT cases fix them. A literal is a function of the column's stored type and width, the
    /// row's ordinal n and its class alone (the Facsimile's D3), never of the column's name (PLT-16), its default (PLT-32) or another
    /// column. A number is n, wrapped to its type's range: n mod 2 for bit, n mod 256 for tinyint, the integer part n mod 10^(p−s) of a
    /// decimal(p,s), whose scale digits are zero, or n × 10^−s where p = s (PLT-01 to PLT-06). A date is 2000-01-01 plus n days, at
    /// midnight in the ISO forms no DATEFORMAT reads otherwise, and a time n seconds (PLT-07 to PLT-11). A string is w and n in decimal,
    /// else in base 36, else its first characters, fitted to the column's width (PLT-12 to PLT-15); bytes are 0x77 and n big-endian
    /// (PLT-17, PLT-18); a uniqueidentifier is 77777777-0000-4000-8000- and n in twelve hexadecimal digits (PLT-19); and xml, sql_variant,
    /// hierarchyid and the spatial types take the forms of PLT-20, PLT-23, PLT-24 and PLT-25.
    /// </summary>
    private static class Literals
    {
        public enum Family
        {
            Integer,
            Bit,
            Decimal,
            Money,
            Approximate,
            Date,
            Time,
            DateTime,
            DateTime2,
            DateTimeOffset,
            Character,
            Binary,
            Guid,
            Xml,
            Variant,
            Hierarchy,
            Spatial,
            Version,
            Other,
        }

        public static Family FamilyOf(ColumnType type) => type.IsAlias ? Family.Other : Base(type) switch
        {
            "bit" => Family.Bit,
            "tinyint" or "smallint" or "int" or "bigint" => Family.Integer,
            "decimal" or "numeric" => Family.Decimal,
            "money" or "smallmoney" => Family.Money,
            "float" or "real" => Family.Approximate,
            "date" => Family.Date,
            "time" => Family.Time,
            "datetime" or "smalldatetime" => Family.DateTime,
            "datetime2" => Family.DateTime2,
            "datetimeoffset" => Family.DateTimeOffset,
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "sysname" => Family.Character,
            "binary" or "varbinary" or "image" => Family.Binary,
            "uniqueidentifier" => Family.Guid,
            "xml" => Family.Xml,
            "sql_variant" => Family.Variant,
            "hierarchyid" => Family.Hierarchy,
            "geography" or "geometry" => Family.Spatial,
            "rowversion" or "timestamp" => Family.Version,
            _ => Family.Other,
        };

        public static bool Numeric(Family family) =>
            family is Family.Integer or Family.Bit or Family.Decimal or Family.Money or Family.Approximate;

        /// <summary>The literal at ordinal <paramref name="n"/>, or null for a type the planter writes none for.</summary>
        public static Planted? Of(ColumnType type, long n) => FamilyOf(type) switch
        {
            var f when Numeric(f) => new Planted.Number(Format(type, Ordinal(type, n))),
            Family.Date => new Planted.Text(Day(n), false),
            Family.Time => new Planted.Text(Seconds(n), false),
            Family.DateTime or Family.DateTime2 => new Planted.Text(Day(n) + "T00:00:00", false),
            Family.DateTimeOffset => new Planted.Text(Day(n) + "T00:00:00+00:00", false),
            Family.Character => new Planted.Text(Marker(n, Width(type)), National(type)),
            Family.Binary => new Planted.Bytes(Binary(type, n)),
            Family.Guid => new Planted.Text(Guid(n), false),
            Family.Xml => new Planted.Text("<w n=\"" + Digits(n) + "\"/>", true),
            Family.Variant => new Planted.Text("w" + Digits(n), true),
            Family.Hierarchy => new Planted.Text("/" + Digits(n) + "/", false),
            Family.Spatial => new Planted.Text("POINT(" + Digits(n % 180) + " 0)", false),
            Family.Version => new Planted.Omitted(),
            _ => null,
        };

        /// <summary>
        /// The value at ordinal n of a column the change retypes from <paramref name="before"/> to <paramref name="after"/> that is of no
        /// class (PLT-94, PLT-95): it converts without an error or a loss and fits both types. A string bound for another type holds that
        /// type's literal as text, fitted to the string's width, or the marker for xml and sql_variant; a number holds a value both types
        /// hold; a date and time bound for time holds 1900-01-01, which time drops without a loss. Null where nothing fits: a literal
        /// whose string form is longer than the new string type, a conversion SQL Server refuses (date to time, time to date, a number
        /// to a date and time type but datetime), and any other retype but a string's, or one into sql_variant of a type it holds.
        /// </summary>
        public static Planted? Clean(ColumnType before, ColumnType after, long n)
        {
            var (from, to) = (FamilyOf(before), FamilyOf(after));
            var width = Width(before);
            if (to is Family.Character or Family.Binary && Width(after) is var target)
            {
                var narrower = target is { } t && (width is null || t < width) ? t : width;
                return from switch
                {
                    Family.Character => new Planted.Text(Marker(n, to == Family.Binary && National(before) ? narrower / 2 : narrower),
                        National(before)),
                    Family.Binary => new Planted.Bytes(Binary(before with { Length = narrower ?? 0, IsMax = narrower is null }, n)),
                    _ when to == Family.Character && Shown(before, n) > (target ?? long.MaxValue) => null,
                    _ => Of(before, n),
                };
            }

            if (Numeric(to) && from == Family.Character)
            {
                var value = Ordinal(after, n);
                return Fitted(width, National(before), Format(after, value), Plain(value));
            }

            if (Numeric(to) && Numeric(from))
            {
                var value = Ordinal(after, n);
                value = Holds(before, value) ? value : Holds(after, Ordinal(before, n)) ? Ordinal(before, n) : new Exact(0, 0);
                return new Planted.Number(Format(before, value));
            }

            if (to is Family.Date or Family.Time or Family.DateTime or Family.DateTime2 or Family.DateTimeOffset)
            {
                return from switch
                {
                    Family.Character => Of(after, n) is Planted.Text text
                        ? Fitted(width, National(before), text.Content, to == Family.Time ? Seconds(n) : Day(n).Replace("-", "",
                            StringComparison.Ordinal))
                        : null,
                    Family.DateTime or Family.DateTime2 when to == Family.Time => new Planted.Text("1900-01-01T" + Seconds(n), false),
                    Family.DateTimeOffset when to == Family.Time => new Planted.Text("1900-01-01T" + Seconds(n) + "+00:00", false),
                    Family.Date when to == Family.Time => null,
                    Family.Time when to == Family.Date => null,
                    Family.Date or Family.Time or Family.DateTime or Family.DateTime2 or Family.DateTimeOffset => Of(before, n),
                    Family.Integer or Family.Decimal or Family.Money or Family.Approximate when to == Family.DateTime => Of(before, n),
                    _ => null,
                };
            }

            if (from == Family.Character)
            {
                return to is Family.Xml or Family.Variant ? new Planted.Text(Marker(n, width), National(before))
                    : Of(after, n) is Planted.Text text ? Fitted(width, National(before), text.Content) : null;
            }

            // sql_variant holds a value of every type the planter writes but xml, the CLR types and the max and large-object types.
            return to == Family.Variant && !before.IsMax && Base(before) is not ("text" or "ntext" or "image")
                && from is not (Family.Xml or Family.Hierarchy or Family.Spatial or Family.Version or Family.Other) ? Of(before, n) : null;
        }

        /// <summary>The length of the string SQL Server converts the literal at ordinal n to, where it differs from the literal's own
        /// length: a date and time type's default form, a uniqueidentifier's 36 characters, and a number's digits, money's with two
        /// after the point; null for the other types.</summary>
        private static long? Shown(ColumnType type, long n) => FamilyOf(type) switch
        {
            Family.Date or Family.Time or Family.DateTime or Family.DateTime2 or Family.DateTimeOffset => Converted(type),
            Family.Guid => 36,
            Family.Money => Point(Ordinal(type, n), 2).Length,
            Family.Approximate => Plain(Ordinal(type, n)).Length,
            var f when Numeric(f) => Format(type, Ordinal(type, n)).Length,
            _ => null,
        };

        /// <summary>
        /// The value at ordinal n of a column the change retypes that is of <paramref name="class"/>, as WP 2.3's queries count it
        /// (io/AggregateQueries.Fits.cs; PLT-62 to PLT-68): over length, w and n padded with w one character past the new length; over
        /// bytes, padded with spaces instead; not convertible, w and n; an empty string; overflow, the smallest value past the new type's
        /// range in the old one's form; a lossy round trip, a value ALTER COLUMN writes and loses. Null where the old type holds none.
        /// </summary>
        public static Planted? Class(ColumnType before, ColumnType after, RowClasses @class, long n)
        {
            var (from, to) = (FamilyOf(before), FamilyOf(after));
            var (width, target) = (Width(before), to is Family.Character or Family.Binary ? Width(after) : null);
            var lossy = to == Family.Character && !National(after) && Base(before) is "tinyint" or "smallint" or "int";
            return @class switch
            {
                RowClasses.OverLength when target is { } l => from switch
                {
                    Family.Character when Holds(width, l + 1) => new Planted.Text(Padded("w" + Digits(n), l + 1, 'w'), National(before)),
                    Family.Binary when Holds(width, l + 1) => new Planted.Bytes(string.Concat(Enumerable.Repeat("77", (int)l + 1))),
                    Family.Integer or Family.Decimal or Family.Money when !lossy => Power(before, l),
                    Family.Guid when l < 36 => Of(before, n),
                    _ => null,
                },
                RowClasses.OverBytes when target is { } l && from == Family.Character && !Fixed(before) && Holds(width, l + 1) =>
                    new Planted.Text(Padded(Marker(n, l), l + 1, ' '), National(before)),
                RowClasses.NotConvertible when from == Family.Character => new Planted.Text(Marker(n, width), National(before)),
                RowClasses.EmptyString when from == Family.Character => new Planted.Text("", National(before)),
                RowClasses.Overflow => Overflow(before, after, from, to),
                RowClasses.LossyRoundTrip => Lossy(before, after, from, to, n, lossy),
                _ => null,
            };
        }

        /// <summary>The smallest value past the new type's range, in the old type's form (PLT-68): of a date and time bound for datetime,
        /// 1700-01-01, before its 1753-01-01, and for smalldatetime, 2079-06-07, after its 2079-06-06, which datetime holds too.</summary>
        private static Planted? Overflow(ColumnType before, ColumnType after, Family from, Family to)
        {
            Exact? past = to switch
            {
                Family.Integer => new Exact(Range(after).Max + 1, 0),
                Family.Decimal => new Exact(BigInteger.Pow(10, (int)Math.Max(0, after.Precision - after.Scale)), 0),
                Family.Money => new Exact(Base(after) == "smallmoney" ? 214749 : 922337203685478, 0),
                Family.Approximate when Base(after) == "real" => new Exact(BigInteger.Pow(10, 39), 0),
                _ => null,
            };
            if (past is { } value)
            {
                return Numeric(from) ? Holds(before, value) ? new Planted.Number(Format(before, value)) : null
                    : from == Family.Character ? Fitted(Width(before), National(before), Plain(value)) : null;
            }

            if (to != Family.DateTime)
            {
                return null;
            }

            var day = Base(after) == "smalldatetime" ? "2079-06-07" : "1700-01-01";
            return from switch
            {
                Family.Character => Fitted(Width(before), National(before), day + "T00:00:00"),
                Family.Date => new Planted.Text(day, false),
                Family.DateTime when (Base(before), Base(after)) is ("datetime", "smalldatetime") =>
                    new Planted.Text(day + "T00:00:00", false),
                Family.DateTime2 => new Planted.Text(day + "T00:00:00", false),
                Family.DateTimeOffset => new Planted.Text(day + "T00:00:00+00:00", false),
                _ => null,
            };
        }

        /// <summary>A value ALTER COLUMN writes without an error and loses, as io/AggregateQueries.Fits.cs counts a lossy round
        /// trip.</summary>
        private static Planted? Lossy(ColumnType before, ColumnType after, Family from, Family to, long n, bool cut)
        {
            var target = Width(after);
            if (to == Family.Character && target is { } l)
            {
                return cut ? Power(before, l)
                    : from is Family.Date or Family.Time or Family.DateTime or Family.DateTime2 or Family.DateTimeOffset
                        && Converted(before) > l ? Of(before, n)
                    : null;
            }

            if (to == Family.Binary && target is { } bytes)
            {
                return from == Family.Integer && bytes < Range(before).Bytes && Base(before) != "tinyint" ? new Planted.Number("-1")
                    : from is Family.Approximate or Family.Guid && bytes < (Base(before) == "real" ? 4 : from == Family.Guid ? 16 : 8)
                        ? Of(before, n)
                    : null;
            }

            return (from, to) switch
            {
                (_, Family.Bit) when Numeric(from) && Holds(before, new Exact(2, 0)) => new Planted.Number(Format(before, new Exact(2, 0))),
                (Family.DateTimeOffset, not Family.DateTimeOffset) => new Planted.Text(Day(n) + "T00:00:00+01:00", false),
                (Family.DateTime or Family.DateTime2, Family.Date) => new Planted.Text(Day(n) + "T00:01:00", false),
                (Family.DateTime or Family.DateTime2, Family.Time) => Of(before, n),
                _ => null,
            };
        }

        /// <summary>How many distinct literals a one-column key of the type holds from ordinal 1, for a need's message: 2 for bit, 256
        /// for tinyint, 10^(p−s) for a decimal, and the markers that fit a string of one or two characters.</summary>
        public static long Room(ColumnType type) => FamilyOf(type) switch
        {
            Family.Bit => 2,
            Family.Integer when Base(type) == "tinyint" => 256,
            Family.Decimal => (long)BigInteger.Min(long.MaxValue,
                BigInteger.Pow(10, (int)Math.Min(19, type.Precision > type.Scale ? type.Precision - type.Scale : type.Scale))),
            Family.Character when Width(type) is { } w && w <= 2 => w == 1 ? 1 : 35,
            Family.Binary when Width(type) == 1 => 256,
            _ => long.MaxValue,
        };

        /// <summary>A type as T-SQL writes it, for a message: nvarchar(50), varchar(max), decimal(18,2), int, [dbo].[ShortCode].</summary>
        public static string Written(ColumnType type) => type.IsAlias ? type.Type.ToString() : FamilyOf(type) switch
        {
            Family.Character or Family.Binary when Base(type) is not ("text" or "ntext" or "image" or "sysname") =>
                Base(type) + "(" + (type.IsMax ? "max" : Digits(type.Length)) + ")",
            Family.Decimal => Base(type) + "(" + Digits(type.Precision) + "," + Digits(type.Scale) + ")",
            _ => Base(type),
        };

        public static bool Unicode(ColumnType type) => FamilyOf(type) == Family.Character && National(type);

        public static bool Fixed(ColumnType type) => Base(type) is "char" or "nchar" or "binary";

        /// <summary>A string's or a binary's width in characters or bytes; null for a max type, text, ntext and image.</summary>
        public static long? Width(ColumnType type) => FamilyOf(type) is Family.Character or Family.Binary
            && !type.IsMax && Base(type) is not ("text" or "ntext" or "image")
            ? Base(type) == "sysname" && type.Length == 0 ? 128 : Math.Max(1, type.Length) : null;

        /// <summary>w and n in decimal if that fits <paramref name="width"/>, else in base 36, else its first characters
        /// (PLT-12).</summary>
        public static string Marker(long n, long? width)
        {
            var (decimalForm, base36) = ("w" + Digits(n), "w" + Base36(n));
            return width is not { } w || decimalForm.Length <= w ? decimalForm : base36.Length <= w ? base36 : decimalForm[..(int)w];
        }

        private static bool National(ColumnType type) => Base(type) is "nchar" or "nvarchar" or "ntext" or "sysname";

        private static string Base(ColumnType type) => type.Type.Base.ToLowerInvariant();

        private static bool Holds(long? width, long length) => width is null || width >= length;

        private static Planted.Text? Fitted(long? width, bool national, params string[] texts) =>
            texts.FirstOrDefault(t => Holds(width, t.Length)) is { } fits ? new Planted.Text(fits, national) : null;

        private static string Padded(string text, long length, char with) =>
            text.Length >= length ? text[..(int)length] : text + new string(with, (int)length - text.Length);

        /// <summary>10^length in the type's form, where it holds it: a number whose string form is one digit longer than length.</summary>
        private static Planted.Number? Power(ColumnType type, long length) =>
            new Exact(BigInteger.Pow(10, (int)Math.Min(length, 400)), 0) is var power && Holds(type, power)
                ? new Planted.Number(Format(type, power)) : null;

        /// <summary>The length of the string SQL Server converts a date and time type to by default: style 0 for datetime and
        /// smalldatetime, the ISO form for the others.</summary>
        private static long Converted(ColumnType type) => Base(type) switch
        {
            "date" => 10,
            "time" => 8 + (type.Scale > 0 ? 1 + type.Scale : 0),
            "datetime" or "smalldatetime" => 19,
            "datetime2" => 19 + (type.Scale > 0 ? 1 + type.Scale : 0),
            _ => 26 + (type.Scale > 0 ? 1 + type.Scale : 0),
        };

        private static string Day(long n) =>
            new DateOnly(2000, 1, 1).AddDays((int)(n % 2_900_000)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Seconds(long n) =>
            TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(n % 86_400)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        private static string Guid(long n) =>
            "77777777-0000-4000-8000-" + (n % 0x1_0000_0000_0000L).ToString("X12", CultureInfo.InvariantCulture);

        private static string Digits(long n) => n.ToString(CultureInfo.InvariantCulture);

        private static string Base36(long n)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var text = "";
            do
            {
                text = digits[(int)(n % 36)] + text;
                n /= 36;
            }
            while (n > 0);
            return text;
        }

        /// <summary>0x77 and n big-endian in the bytes left, or n's bytes alone where the width holds no more (PLT-17, PLT-18).</summary>
        private static string Binary(ColumnType type, long n)
        {
            var width = Width(type);
            if (Fixed(type))
            {
                var w = (int)Math.Min(width ?? 1, 64);
                return w >= 2 ? "77" + BigEndian(n, w - 1) : BigEndian(n, 1);
            }

            var minimal = Math.Max(1, (64 - long.LeadingZeroCount(n) + 7) / 8);
            return width is null || width >= 1 + minimal ? "77" + BigEndian(n, (int)minimal) : BigEndian(n, (int)width.Value);
        }

        private static string BigEndian(long n, int bytes) => string.Concat(Enumerable.Range(0, bytes).Reverse()
            .Select(i => (i >= 8 ? 0 : (n >> (8 * i)) & 0xFF).ToString("X2", CultureInfo.InvariantCulture)));

        /// <summary>An exact number: Units × 10^−Scale.</summary>
        private readonly record struct Exact(BigInteger Units, int Scale)
        {
            /// <summary>The same number with no trailing zero digit after the point.</summary>
            public Exact Normal()
            {
                var (units, scale) = (Units, Scale);
                while (scale > 0 && units % 10 == 0)
                {
                    (units, scale) = (units / 10, scale - 1);
                }

                return new Exact(units, scale);
            }

            /// <summary>Units at <paramref name="scale"/> digits after the point, which is at least <see cref="Scale"/>.</summary>
            public BigInteger At(int scale) => Units * BigInteger.Pow(10, scale - Scale);
        }

        /// <summary>The number at ordinal n of a numeric type, wrapped to its range (PLT-01 to PLT-06).</summary>
        private static Exact Ordinal(ColumnType type, long n) => FamilyOf(type) switch
        {
            Family.Bit => new Exact(n % 2, 0),
            Family.Integer => new Exact(n % (Range(type).Max + 1), 0),
            Family.Decimal when type.Precision > type.Scale => new Exact(n % BigInteger.Pow(10, (int)(type.Precision - type.Scale)), 0),
            Family.Decimal => new Exact(n % BigInteger.Pow(10, (int)type.Scale), (int)type.Scale),
            Family.Money when Base(type) == "smallmoney" => new Exact(n % 214_748, 0),
            _ => new Exact(n, 0),
        };

        /// <summary>The range of an integer type, and its bytes.</summary>
        private static (BigInteger Min, BigInteger Max, int Bytes) Range(ColumnType type) => Base(type) switch
        {
            "tinyint" => (0, 255, 1),
            "smallint" => (short.MinValue, short.MaxValue, 2),
            "int" => (int.MinValue, int.MaxValue, 4),
            _ => (long.MinValue, long.MaxValue, 8),
        };

        /// <summary>Whether a numeric type holds the number exactly.</summary>
        private static bool Holds(ColumnType type, Exact value)
        {
            var v = value.Normal();
            var magnitude = BigInteger.Abs(v.Units);
            return FamilyOf(type) switch
            {
                Family.Bit => v.Scale == 0 && (v.Units == 0 || v.Units == 1),
                Family.Integer => v.Scale == 0 && v.Units >= Range(type).Min && v.Units <= Range(type).Max,
                Family.Decimal => v.Scale <= type.Scale && magnitude < BigInteger.Pow(10, (int)(type.Precision - type.Scale) + v.Scale),
                Family.Money => v.Scale <= 4 && v.At(4) <= (Base(type) == "smallmoney" ? int.MaxValue : long.MaxValue),
                Family.Approximate => magnitude < BigInteger.Pow(10, (Base(type) == "real" ? 38 : 308) + v.Scale),
                _ => false,
            };
        }

        /// <summary>A number as T-SQL writes it for the type: digits for an integer or bit, the scale's digits for a decimal, four for
        /// money, and an exponent for float and real.</summary>
        private static string Format(ColumnType type, Exact value) => FamilyOf(type) switch
        {
            Family.Decimal => Point(value, (int)type.Scale),
            Family.Money => Point(value, 4),
            Family.Approximate => Plain(value) + "E0",
            _ => Plain(value),
        };

        /// <summary>The number in the fewest digits: 1, 0.01, 1000.</summary>
        private static string Plain(Exact value) => Point(value.Normal(), value.Normal().Scale);

        private static string Point(Exact value, int scale)
        {
            var units = value.At(Math.Max(scale, value.Scale));
            var digits = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
            return (units < 0 ? "-" : "") + (scale == 0 ? digits : digits[..^scale] + "." + digits[^scale..]);
        }
    }
}

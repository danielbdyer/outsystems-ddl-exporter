using System;
using System.Globalization;
using System.Text;

namespace Osm.Emission.Formatting;

public sealed class SqlLiteralFormatter
{
    public string FormatValue(object? value)
    {
        if (value is null)
        {
            return "NULL";
        }

        return value switch
        {
            string s => $"N'{EscapeUnicodeString(s)}'",
            char c => $"N'{EscapeUnicodeString(c.ToString())}'",
            bool b => b ? "1" : "0",
            byte bt => bt.ToString(CultureInfo.InvariantCulture),
            sbyte sb => sb.ToString(CultureInfo.InvariantCulture),
            short sh => sh.ToString(CultureInfo.InvariantCulture),
            ushort ush => ush.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint ui => ui.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G17", CultureInfo.InvariantCulture),
            float f => f.ToString("G9", CultureInfo.InvariantCulture),
            DateOnly date => $"'{date:yyyy-MM-dd}'",
            TimeOnly time => $"'{time:HH:mm:ss.fffffff}'",
            DateTime dt => $"'{dt:yyyy-MM-ddTHH:mm:ss.fffffff}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-ddTHH:mm:ss.fffffffK}'",
            TimeSpan ts => $"'{ts:c}'",
            Guid g => $"'{g:D}'",
            byte[] bytes => FormatBinary(bytes),
            _ => $"N'{EscapeUnicodeString(value.ToString() ?? string.Empty)}'",
        };
    }

    public string EscapeString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string EscapeUnicodeString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string FormatBinary(byte[] value)
    {
        if (value.Length == 0)
        {
            return "0x";
        }

        var builder = new StringBuilder(value.Length * 2 + 2);
        builder.Append("0x");
        foreach (var b in value)
        {
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

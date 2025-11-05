using System;
using System.Globalization;

namespace Osm.Pipeline.UatUsers;

public readonly record struct UserIdentifier : IComparable<UserIdentifier>
{
    private enum IdentifierKind
    {
        Numeric = 0,
        Guid = 1,
        Text = 2
    }

    private UserIdentifier(string value, long? numericValue, IdentifierKind kind)
    {
        Value = value;
        NumericValue = numericValue;
        Kind = kind;
    }

    private IdentifierKind Kind { get; }

    public string Value { get; }

    public long? NumericValue { get; }

    public bool IsGuid => Kind == IdentifierKind.Guid;

    public bool IsText => Kind == IdentifierKind.Text;

    public bool IsNumeric => NumericValue.HasValue;

    public static UserIdentifier FromString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("User identifier cannot be empty.");
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return new UserIdentifier(numeric.ToString(CultureInfo.InvariantCulture), numeric, IdentifierKind.Numeric);
        }

        if (Guid.TryParse(trimmed, out var guid))
        {
            return new UserIdentifier(guid.ToString("D"), null, IdentifierKind.Guid);
        }

        return new UserIdentifier(trimmed, null, IdentifierKind.Text);
    }

    public static bool TryParse(string? value, out UserIdentifier identifier)
    {
        identifier = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            identifier = FromString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static UserIdentifier FromDatabaseValue(object value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value switch
        {
            long l => new UserIdentifier(l.ToString(CultureInfo.InvariantCulture), l, IdentifierKind.Numeric),
            int i => new UserIdentifier(i.ToString(CultureInfo.InvariantCulture), i, IdentifierKind.Numeric),
            short s => new UserIdentifier(s.ToString(CultureInfo.InvariantCulture), s, IdentifierKind.Numeric),
            byte b => new UserIdentifier(b.ToString(CultureInfo.InvariantCulture), b, IdentifierKind.Numeric),
            decimal d => FromIntegralDecimal(d),
            double dbl => FromFloatingPoint(dbl),
            float fl => FromFloatingPoint(fl),
            Guid guid => new UserIdentifier(guid.ToString("D"), null, IdentifierKind.Guid),
            string s => FromString(s),
            _ => FromString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? throw new FormatException("Unsupported identifier type."))
        };
    }

    private static UserIdentifier FromIntegralDecimal(decimal value)
    {
        if (decimal.Truncate(value) != value)
        {
            throw new FormatException($"Value '{value}' is not an integral identifier.");
        }

        var numeric = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return new UserIdentifier(numeric.ToString(CultureInfo.InvariantCulture), numeric, IdentifierKind.Numeric);
    }

    private static UserIdentifier FromFloatingPoint(double value)
    {
        var rounded = Math.Round(value);
        if (Math.Abs(value - rounded) > double.Epsilon)
        {
            throw new FormatException($"Value '{value}' is not an integral identifier.");
        }

        var numeric = Convert.ToInt64(rounded);
        return new UserIdentifier(numeric.ToString(CultureInfo.InvariantCulture), numeric, IdentifierKind.Numeric);
    }

    public int CompareTo(UserIdentifier other)
    {
        if (NumericValue.HasValue && other.NumericValue.HasValue)
        {
            return NumericValue.Value.CompareTo(other.NumericValue.Value);
        }

        if (NumericValue.HasValue)
        {
            return -1;
        }

        if (other.NumericValue.HasValue)
        {
            return 1;
        }

        var kindComparison = Kind.CompareTo(other.Kind);
        if (kindComparison != 0)
        {
            return kindComparison;
        }

        return string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString()
    {
        return Value;
    }
}

using System;
using System.Globalization;

namespace Osm.Smo;

public enum IdentifierQuoteStrategy
{
    SquareBracket,
    DoubleQuote,
    None,
}

public enum ConstraintNameKind
{
    PrimaryKey,
    UniqueIndex,
    NonUniqueIndex,
    ForeignKey,
}

public sealed record IndexNamingOptions(
    string PrimaryKeyPrefix,
    string UniqueIndexPrefix,
    string NonUniqueIndexPrefix,
    string ForeignKeyPrefix)
{
    public static IndexNamingOptions Default { get; } = new(
        PrimaryKeyPrefix: "PK",
        UniqueIndexPrefix: "UIX",
        NonUniqueIndexPrefix: "IX",
        ForeignKeyPrefix: "FK");

    public string Apply(string normalizedName, ConstraintNameKind kind)
    {
        var prefix = kind switch
        {
            ConstraintNameKind.PrimaryKey => PrimaryKeyPrefix,
            ConstraintNameKind.UniqueIndex => UniqueIndexPrefix,
            ConstraintNameKind.NonUniqueIndex => NonUniqueIndexPrefix,
            ConstraintNameKind.ForeignKey => ForeignKeyPrefix,
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return normalizedName;
        }

        prefix = NormalizePrefix(prefix);
        var trimmed = normalizedName ?? string.Empty;
        var separatorIndex = trimmed.IndexOf('_');
        if (separatorIndex < 0)
        {
            return string.Create(prefix.Length + (trimmed.Length > 0 ? trimmed.Length + 1 : 0), (prefix, trimmed), static (span, state) =>
            {
                var (prefixValue, suffixValue) = state;
                prefixValue.AsSpan().CopyTo(span);
                if (!string.IsNullOrEmpty(suffixValue))
                {
                    span[prefixValue.Length] = '_';
                    suffixValue.AsSpan().CopyTo(span[(prefixValue.Length + 1)..]);
                }
            });
        }

        var suffix = trimmed[(separatorIndex + 1)..];
        return string.IsNullOrEmpty(suffix)
            ? prefix
            : string.Create(prefix.Length + 1 + suffix.Length, (prefix, suffix), static (span, state) =>
            {
                var (prefixValue, suffixValue) = state;
                prefixValue.AsSpan().CopyTo(span);
                span[prefixValue.Length] = '_';
                suffixValue.AsSpan().CopyTo(span[(prefixValue.Length + 1)..]);
            });
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var trimmed = prefix.Trim();
        return trimmed.ToUpper(CultureInfo.InvariantCulture);
    }
}

public sealed record SmoFormatOptions(
    IdentifierQuoteStrategy IdentifierQuoteStrategy,
    bool NormalizeWhitespace,
    IndexNamingOptions IndexNaming)
{
    public static SmoFormatOptions Default { get; } = new(
        IdentifierQuoteStrategy: IdentifierQuoteStrategy.SquareBracket,
        NormalizeWhitespace: true,
        IndexNaming: IndexNamingOptions.Default);

    public SmoFormatOptions WithIndexNaming(IndexNamingOptions naming)
    {
        if (naming is null)
        {
            throw new ArgumentNullException(nameof(naming));
        }

        return this with { IndexNaming = naming };
    }

    public SmoFormatOptions WithIdentifierQuoteStrategy(IdentifierQuoteStrategy strategy)
        => this with { IdentifierQuoteStrategy = strategy };

    public SmoFormatOptions WithWhitespaceNormalization(bool normalize)
        => this with { NormalizeWhitespace = normalize };
}

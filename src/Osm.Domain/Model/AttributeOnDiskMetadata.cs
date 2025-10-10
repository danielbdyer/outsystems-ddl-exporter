namespace Osm.Domain.Model;

public sealed record AttributeOnDiskMetadata(
    bool? IsNullable,
    string? SqlType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    string? Collation,
    bool? IsIdentity,
    bool? IsComputed,
    string? ComputedDefinition,
    string? DefaultDefinition)
{
    public static readonly AttributeOnDiskMetadata Empty = new(null, null, null, null, null, null, null, null, null, null);

    public static AttributeOnDiskMetadata Create(
        bool? isNullable,
        string? sqlType,
        int? maxLength,
        int? precision,
        int? scale,
        string? collation,
        bool? isIdentity,
        bool? isComputed,
        string? computedDefinition,
        string? defaultDefinition)
    {
        var normalizedType = string.IsNullOrWhiteSpace(sqlType) ? null : sqlType!.Trim();
        var normalizedCollation = string.IsNullOrWhiteSpace(collation) ? null : collation!.Trim();
        var normalizedComputed = string.IsNullOrWhiteSpace(computedDefinition) ? null : computedDefinition;
        var normalizedDefault = string.IsNullOrWhiteSpace(defaultDefinition) ? null : defaultDefinition;

        return new AttributeOnDiskMetadata(
            isNullable,
            normalizedType,
            maxLength,
            precision,
            scale,
            normalizedCollation,
            isIdentity,
            isComputed,
            normalizedComputed,
            normalizedDefault);
    }
}

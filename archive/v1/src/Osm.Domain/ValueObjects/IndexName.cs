using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct IndexName(string Value)
{
    public static Result<IndexName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "index.name.invalid", "Index name")
            .Map(static v => new IndexName(v));

    public override string ToString() => Value;
}

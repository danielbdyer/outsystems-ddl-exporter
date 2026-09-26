using System.Collections.Immutable;

using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record ExtendedProperty(string Name, string? Value)
{
    public static Result<ExtendedProperty> Create(string? name, string? value)
    {
        var nameResult = StringValidators.RequiredIdentifier(name, "extendedProperty.name.invalid", "Extended property name", 128);
        if (nameResult.IsFailure)
        {
            return Result<ExtendedProperty>.Failure(nameResult.Errors);
        }

        var normalizedValue = value switch
        {
            null => null,
            { Length: 0 } => null,
            _ => value!
        };

        return Result<ExtendedProperty>.Success(new ExtendedProperty(nameResult.Value, normalizedValue));
    }

    public static ImmutableArray<ExtendedProperty> EmptyArray => ImmutableArray<ExtendedProperty>.Empty;
}

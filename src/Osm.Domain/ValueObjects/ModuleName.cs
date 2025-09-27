using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct ModuleName(string Value)
{
    public static Result<ModuleName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "module.name.invalid", "Module name")
            .Map(static v => new ModuleName(v));

    public override string ToString() => Value;
}

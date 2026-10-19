using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct ModuleName(string Value)
{
    private static readonly char[] ForbiddenCharacters = new[] { ',', '\r', '\n' };

    public static Result<ModuleName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "module.name.invalid", "Module name")
            .Ensure(
                static v => v.IndexOfAny(ForbiddenCharacters) < 0,
                ValidationError.Create(
                    "module.name.invalid",
                    "Module name must not contain commas or line breaks."))
            .Map(static v => new ModuleName(v));

    public override string ToString() => Value;
}

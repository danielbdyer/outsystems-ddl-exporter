using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

public readonly record struct TriggerName(string Value)
{
    public static Result<TriggerName> Create(string? value)
        => StringValidators.RequiredIdentifier(value, "trigger.name.invalid", "Trigger name")
            .Map(static v => new TriggerName(v));

    public override string ToString() => Value;
}

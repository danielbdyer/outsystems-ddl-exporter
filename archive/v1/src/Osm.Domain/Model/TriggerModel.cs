using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record TriggerModel(
    TriggerName Name,
    bool IsDisabled,
    string Definition)
{
    public static Result<TriggerModel> Create(TriggerName name, bool isDisabled, string? definition)
    {
        if (definition is null)
        {
            return Result<TriggerModel>.Failure(ValidationError.Create(
                "trigger.definition.missing",
                "Trigger definition must be provided."));
        }

        return Result<TriggerModel>.Success(new TriggerModel(name, isDisabled, definition));
    }
}

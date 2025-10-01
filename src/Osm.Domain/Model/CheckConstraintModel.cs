using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record CheckConstraintModel(
    ConstraintName Name,
    string Definition,
    bool IsActive)
{
    public static Result<CheckConstraintModel> Create(
        ConstraintName name,
        string? definition,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return Result<CheckConstraintModel>.Failure(ValidationError.Create(
                "checkConstraint.definition.invalid",
                "Check constraint definition must be provided."));
        }

        var trimmedDefinition = definition.Trim();

        return Result<CheckConstraintModel>.Success(new CheckConstraintModel(name, trimmedDefinition, isActive));
    }
}

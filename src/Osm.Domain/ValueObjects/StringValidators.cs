using Osm.Domain.Abstractions;

namespace Osm.Domain.ValueObjects;

internal static class StringValidators
{
    public static Result<string> RequiredIdentifier(string? value, string code, string description, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<string>.Failure(ValidationError.Create(code, $"{description} must be provided."));
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            return Result<string>.Failure(ValidationError.Create(code, $"{description} must be {maxLength} characters or fewer."));
        }

        return Result<string>.Success(trimmed);
    }
}

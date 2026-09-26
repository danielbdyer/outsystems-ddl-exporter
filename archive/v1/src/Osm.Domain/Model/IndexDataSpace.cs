using Osm.Domain.Abstractions;

namespace Osm.Domain.Model;

public sealed record IndexDataSpace(string Name, string Type)
{
    public static Result<IndexDataSpace> Create(string? name, string? type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<IndexDataSpace>.Failure(
                ValidationError.Create("index.dataSpace.name.invalid", "Data space name must be provided."));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return Result<IndexDataSpace>.Failure(
                ValidationError.Create("index.dataSpace.type.invalid", "Data space type must be provided."));
        }

        return Result<IndexDataSpace>.Success(new IndexDataSpace(name.Trim(), type.Trim()));
    }
}

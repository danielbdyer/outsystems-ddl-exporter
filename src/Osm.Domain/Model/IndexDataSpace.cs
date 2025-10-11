using System;

namespace Osm.Domain.Model;

public sealed record IndexDataSpace(string Name, string Type)
{
    public static IndexDataSpace Create(string? name, string? type)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Data space requires both name and type.");
        }

        return new IndexDataSpace(name.Trim(), type.Trim());
    }
}

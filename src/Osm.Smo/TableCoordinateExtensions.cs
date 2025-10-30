using System;
using Osm.Domain.ValueObjects;

namespace Osm.Smo;

public static class TableCoordinateExtensions
{
    public static TableCoordinate ToCoordinate(this SmoTableDefinition table, bool includeModule = false)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        var result = TableCoordinate.Create(includeModule ? table.Module : null, table.Schema, table.Name);
        if (result.IsFailure)
        {
            throw new ArgumentException("Invalid SMO table definition provided for coordinate creation.", nameof(table));
        }

        return result.Value;
    }
}

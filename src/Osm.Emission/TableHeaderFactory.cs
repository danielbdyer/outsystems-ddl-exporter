using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.ValueObjects;
using Osm.Smo;

namespace Osm.Emission;

public sealed class TableHeaderFactory
{
    public IReadOnlyList<PerTableHeaderItem>? Create(
        SmoTableDefinition table,
        SmoBuildOptions options,
        ImmutableDictionary<TableCoordinate, SmoRenameMapping> renameLookup)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!options.Header.Enabled)
        {
            return null;
        }

        var builder = ImmutableArray.CreateBuilder<PerTableHeaderItem>();
        builder.Add(PerTableHeaderItem.Create("LogicalName", table.LogicalName));
        builder.Add(PerTableHeaderItem.Create("Module", table.Module));

        if (!renameLookup.IsEmpty &&
            renameLookup.TryGetValue(table.ToCoordinate(), out var mapping))
        {
            if (!string.Equals(mapping.EffectiveName, table.Name, StringComparison.OrdinalIgnoreCase))
            {
                builder.Add(PerTableHeaderItem.Create("RenamedFrom", $"{table.Schema}.{table.Name}"));
                builder.Add(PerTableHeaderItem.Create("EffectiveName", mapping.EffectiveName));
            }

            if (!string.Equals(mapping.OriginalModule, mapping.Module, StringComparison.Ordinal))
            {
                builder.Add(PerTableHeaderItem.Create("OriginalModule", mapping.OriginalModule));
            }
        }

        return builder.ToImmutable();
    }
}

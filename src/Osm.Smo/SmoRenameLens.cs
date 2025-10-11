using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;

namespace Osm.Smo;

public sealed record SmoRenameLensRequest(SmoModel Model, NamingOverrideOptions NamingOverrides);

public sealed record SmoRenameMapping(
    string Module,
    string OriginalModule,
    string Schema,
    string PhysicalName,
    string LogicalName,
    string EffectiveName);

public sealed class SmoRenameLens
{
    public ImmutableArray<SmoRenameMapping> Project(SmoRenameLensRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Model is null)
        {
            throw new ArgumentNullException(nameof(request.Model));
        }

        if (request.Model.Tables.IsDefaultOrEmpty)
        {
            return ImmutableArray<SmoRenameMapping>.Empty;
        }

        var namingOverrides = request.NamingOverrides ?? NamingOverrideOptions.Empty;
        var builder = ImmutableArray.CreateBuilder<SmoRenameMapping>(request.Model.Tables.Length);

        foreach (var table in request.Model.Tables)
        {
            if (table is null)
            {
                continue;
            }

            var effectiveName = namingOverrides.GetEffectiveTableName(
                table.Schema,
                table.Name,
                table.LogicalName,
                table.OriginalModule);

            builder.Add(new SmoRenameMapping(
                table.Module,
                table.OriginalModule,
                table.Schema,
                table.Name,
                table.LogicalName,
                effectiveName));
        }

        return builder.ToImmutable().Sort(RenameMappingComparer.Instance);
    }

    private sealed class RenameMappingComparer : IComparer<SmoRenameMapping>
    {
        public static readonly RenameMappingComparer Instance = new();

        public int Compare(SmoRenameMapping? x, SmoRenameMapping? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var comparison = StringComparer.Ordinal.Compare(x.Module, y.Module);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(x.Schema, y.Schema);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.OrdinalIgnoreCase.Compare(x.PhysicalName, y.PhysicalName);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(x.LogicalName, y.LogicalName);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x.EffectiveName, y.EffectiveName);
        }
    }
}

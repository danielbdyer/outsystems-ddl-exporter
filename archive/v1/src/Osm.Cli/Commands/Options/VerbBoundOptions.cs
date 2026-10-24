using System;
using System.Collections.Immutable;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Options;

internal sealed record VerbBoundOptions<TOverrides>(
    string? ConfigurationPath,
    ModuleFilterOverrides? ModuleFilter,
    CacheOptionsOverrides? Cache,
    SqlOptionsOverrides? Sql,
    TighteningOverrides? Tightening,
    SchemaApplyOverrides? SchemaApply,
    UatUsersOverrides? UatUsers,
    TOverrides Overrides,
    ImmutableDictionary<Type, object?> Extensions)
{
    public TExtension? GetExtension<TExtension>()
    {
        if (Extensions.TryGetValue(typeof(TExtension), out var value))
        {
            return (TExtension?)value;
        }

        return default;
    }
}

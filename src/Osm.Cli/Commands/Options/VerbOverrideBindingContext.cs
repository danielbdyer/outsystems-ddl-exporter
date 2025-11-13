using System.CommandLine.Parsing;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Options;

internal sealed record VerbOverrideBindingContext(
    ParseResult ParseResult,
    ModuleFilterOverrides? ModuleFilter,
    CacheOptionsOverrides? Cache,
    SqlOptionsOverrides? Sql,
    TighteningOverrides? Tightening,
    SchemaApplyOverrides? SchemaApply,
    UatUsersOverrides? UatUsers);

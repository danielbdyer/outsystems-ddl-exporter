using Osm.Domain.Configuration;
using Osm.Pipeline.Sql;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed record ModelExecutionScope(
    string ModelPath,
    ModuleFilterOptions ModuleFilter,
    SupplementalModelOptions SupplementalModels,
    TighteningOptions TighteningOptions,
    ResolvedSqlOptions SqlOptions,
    SmoBuildOptions SmoOptions,
    TypeMappingPolicy TypeMappingPolicy,
    string? ProfilePath = null,
    string? BaselineProfilePath = null);

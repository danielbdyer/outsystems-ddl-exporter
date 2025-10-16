using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Application;

public interface IModelResolutionService
{
    Task<Result<ModelResolutionResult>> ResolveModelAsync(
        CliConfiguration configuration,
        BuildSsdtOverrides overrides,
        ModuleFilterOptions moduleFilter,
        ResolvedSqlOptions sqlOptions,
        OutputDirectoryResolution outputResolution,
        CancellationToken cancellationToken);
}

public sealed record ModelResolutionResult(
    string ModelPath,
    bool WasExtracted,
    ImmutableArray<string> Warnings);

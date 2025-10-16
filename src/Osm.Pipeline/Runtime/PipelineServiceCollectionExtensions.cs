using System;
using Microsoft.Extensions.DependencyInjection;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Runtime;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddSingleton<ICliConfigurationService, CliConfigurationService>();
        services.AddSingleton<CliConfigurationLoader>();

        services.AddSingleton<IModelJsonDeserializer, ModelJsonDeserializer>();
        services.AddSingleton<IProfileSnapshotDeserializer, ProfileSnapshotDeserializer>();

        services.AddSingleton<Func<string, SqlConnectionOptions, IDbConnectionFactory>>(
            _ => (connectionString, options) => new SqlConnectionFactory(connectionString, options));

        services.AddSingleton<IDataProfilerFactory, DataProfilerFactory>();
        services.AddSingleton<IModelIngestionService, ModelIngestionService>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        services.AddSingleton<TighteningPolicy>();
        services.AddSingleton<SmoModelFactory>();
        services.AddSingleton<SsdtEmitter>();
        services.AddSingleton<PolicyDecisionLogWriter>();
        services.AddSingleton<EmissionFingerprintCalculator>();
        services.AddSingleton<StaticEntitySeedScriptGenerator>();
        services.AddSingleton(static _ => StaticEntitySeedTemplate.Load());
        services.AddSingleton<IEvidenceCacheService, EvidenceCacheService>();
        services.AddSingleton<EvidenceCacheCoordinator>();

        services.AddSingleton<BuildSsdtRequestAssembler>();
        services.AddSingleton<IModelResolutionService, ModelResolutionService>();
        services.AddSingleton<IOutputDirectoryResolver, OutputDirectoryResolver>();
        services.AddSingleton<INamingOverridesBinder, NamingOverridesBinder>();
        services.AddSingleton<IStaticDataProviderFactory, StaticDataProviderFactory>();

        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>, BuildSsdtApplicationService>();
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>, CompareWithDmmApplicationService>();
        services.AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>, ExtractModelApplicationService>();

        services.AddSingleton<ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>, BuildSsdtPipeline>();
        services.AddSingleton<ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>, DmmComparePipeline>();
        services.AddSingleton<ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>, ExtractModelPipeline>();

        services.AddSingleton<IPipelineVerb, BuildSsdtVerb>();
        services.AddSingleton<IPipelineVerb, DmmCompareVerb>();
        services.AddSingleton<IPipelineVerb, ExtractModelVerb>();

        services.AddSingleton<IVerbRegistry, VerbRegistry>();

        return services;
    }
}

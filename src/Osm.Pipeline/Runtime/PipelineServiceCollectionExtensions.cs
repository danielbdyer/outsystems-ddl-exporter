using System;
using Microsoft.Extensions.DependencyInjection;
using Osm.Json;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

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

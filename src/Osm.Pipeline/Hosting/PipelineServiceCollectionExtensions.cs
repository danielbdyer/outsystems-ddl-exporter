using System;
using Microsoft.Extensions.DependencyInjection;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Hosting.Verbs;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Json;
using Osm.Pipeline.Reports;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.StaticData;
using Osm.Pipeline.UatUsers;

namespace Osm.Pipeline.Hosting;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddOsmPipeline(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<ICliConfigurationService, CliConfigurationService>();
        services.AddSingleton<IModelJsonDeserializer, ModelJsonDeserializer>();
        services.AddSingleton<IProfileSnapshotDeserializer, ProfileSnapshotDeserializer>();
        services.AddSingleton<Func<string, SqlConnectionOptions, IDbConnectionFactory>>(
            _ => (connectionString, options) => new SqlConnectionFactory(connectionString, options));
        services.AddSingleton<IDataProfilerFactory, DataProfilerFactory>();
        services.AddSingleton<IModelIngestionService, ModelIngestionService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ICommandDispatcher>(sp => new CommandDispatcher(sp.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>, BuildSsdtPipeline>();
        services.AddSingleton<ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>, DmmComparePipeline>();
        services.AddSingleton<ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>, ExtractModelPipeline>();
        services.AddSingleton<BuildSsdtRequestAssembler>();
        services.AddSingleton<IModelResolutionService, ModelResolutionService>();
        services.AddSingleton<IOutputDirectoryResolver, OutputDirectoryResolver>();
        services.AddSingleton<INamingOverridesBinder, NamingOverridesBinder>();
        services.AddSingleton<IStaticDataProviderFactory, StaticDataProviderFactory>();
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>, BuildSsdtApplicationService>();
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>, CompareWithDmmApplicationService>();
        services.AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>, ExtractModelApplicationService>();
        services.AddSingleton<IPipelineReportLauncher, PipelineReportLauncher>();

        services.AddSingleton<IPipelineVerb<BuildSsdtVerbOptions>, BuildSsdtVerb>();
        services.AddSingleton<IPipelineVerb<CompareWithDmmVerbOptions>, CompareWithDmmVerb>();
        services.AddSingleton<IPipelineVerb<ExtractModelVerbOptions>, ExtractModelVerb>();
        services.AddSingleton<IPipelineVerb<InspectModelVerbOptions>, InspectModelVerb>();
        services.AddSingleton<IPipelineVerb<UatUsersVerbOptions>, UatUsersVerb>();

        services.AddSingleton<IPipelineVerb>(sp => sp.GetRequiredService<IPipelineVerb<BuildSsdtVerbOptions>>());
        services.AddSingleton<IPipelineVerb>(sp => sp.GetRequiredService<IPipelineVerb<CompareWithDmmVerbOptions>>());
        services.AddSingleton<IPipelineVerb>(sp => sp.GetRequiredService<IPipelineVerb<ExtractModelVerbOptions>>());
        services.AddSingleton<IPipelineVerb>(sp => sp.GetRequiredService<IPipelineVerb<InspectModelVerbOptions>>());
        services.AddSingleton<IPipelineVerb>(sp => sp.GetRequiredService<IPipelineVerb<UatUsersVerbOptions>>());
        services.AddSingleton<IVerbRegistry, VerbRegistry>();

        return services;
    }
}

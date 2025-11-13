using System;
using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.UatUsers;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Profiling;

namespace Osm.Pipeline.Runtime;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        return services
            .AddPipelineInfrastructure()
            .AddPipelineConfiguration()
            .AddPipelineSqlInfrastructure()
            .AddPipelineIngestion()
            .AddPipelineEvidence()
            .AddPipelineTightening()
            .AddPipelineEmission()
            .AddPipelineOrchestration()
            .AddPipelineApplications()
            .AddPipelineVerbs();
    }

    internal static IServiceCollection AddPipelineInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IPathCanonicalizer, ForwardSlashPathCanonicalizer>();

        return services;
    }

    internal static IServiceCollection AddPipelineConfiguration(this IServiceCollection services)
    {
        services.AddSingleton<ICliConfigurationService, CliConfigurationService>();
        services.AddSingleton<CliConfigurationLoader>();

        return services;
    }

    internal static IServiceCollection AddPipelineSqlInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<Func<string, SqlConnectionOptions, IDbConnectionFactory>>(
            _ => (connectionString, options) => new SqlConnectionFactory(connectionString, options));
        services.AddSingleton<IMetadataResultSetProcessorFactory, MetadataResultSetProcessorFactory>();

        return services;
    }

    internal static IServiceCollection AddPipelineIngestion(this IServiceCollection services)
    {
        services.AddSingleton<IModelJsonDeserializer, ModelJsonDeserializer>();
        services.AddSingleton<IProfileSnapshotDeserializer, ProfileSnapshotDeserializer>();
        services.AddSingleton<IProfileSnapshotSerializer, ProfileSnapshotSerializer>();
        services.AddSingleton<IDataProfilerFactory, DataProfilerFactory>();
        services.AddSingleton<NullCountQueryBuilder>();
        services.AddSingleton<UniqueCandidateQueryBuilder>();
        services.AddSingleton<ForeignKeyProbeQueryBuilder>();
        services.AddSingleton<IProfilingProbePolicy, ProfilingProbePolicy>();
        services.AddSingleton<IModelIngestionService, ModelIngestionService>();
        services.AddSingleton<ModuleFilter>();
        services.AddSingleton<SupplementalEntityLoader>();
        services.AddSingleton<IProfilingInsightGenerator, ProfilingInsightGenerator>();

        return services;
    }

    internal static IServiceCollection AddPipelineEvidence(this IServiceCollection services)
    {
        services.AddSingleton<IEvidenceCacheService, EvidenceCacheService>();
        services.AddSingleton<EvidenceCacheCoordinator>();

        return services;
    }

    internal static IServiceCollection AddPipelineTightening(this IServiceCollection services)
    {
        services.AddSingleton<TighteningPolicy>();
        services.AddSingleton<ITighteningAnalyzer, TighteningOpportunitiesAnalyzer>();

        return services;
    }

    internal static IServiceCollection AddPipelineEmission(this IServiceCollection services)
    {
        services.AddSingleton<SmoModelFactory>();
        services.AddSingleton<SsdtEmitter>();
        services.AddSingleton<PolicyDecisionLogWriter>();
        services.AddSingleton<EmissionFingerprintCalculator>();
        services.AddSingleton<OpportunityLogWriter>();
        services.AddSingleton<SqlLiteralFormatter>();
        services.AddSingleton<StaticSeedSqlBuilder>();
        services.AddSingleton<StaticEntitySeedTemplateService>();
        services.AddSingleton<StaticEntitySeedScriptGenerator>();
        services.AddSingleton<DynamicEntityInsertGenerator>();
        services.AddSingleton<ISsdtSqlValidator, SsdtSqlValidator>();

        return services;
    }

    internal static IServiceCollection AddPipelineOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<BuildSsdtRequestAssembler>();
        services.AddSingleton<IModelResolutionService, ModelResolutionService>();
        services.AddSingleton<IOutputDirectoryResolver, OutputDirectoryResolver>();
        services.AddSingleton<INamingOverridesBinder, NamingOverridesBinder>();
        services.AddSingleton<IStaticDataProviderFactory, StaticDataProviderFactory>();
        services.AddSingleton<IDynamicEntityDataProvider, SqlDynamicEntityDataProvider>();
        services.AddSingleton<IPipelineBootstrapper, PipelineBootstrapper>();
        services.AddSingleton<BuildSsdtBootstrapStep>();
        services.AddSingleton<BuildSsdtEvidenceCacheStep>();
        services.AddSingleton<BuildSsdtPolicyDecisionStep>();
        services.AddSingleton<BuildSsdtEmissionStep>();
        services.AddSingleton<BuildSsdtSqlProjectStep>();
        services.AddSingleton<BuildSsdtSqlValidationStep>();
        services.AddSingleton<BuildSsdtStaticSeedStep>();
        services.AddSingleton<BuildSsdtDynamicInsertStep>();
        services.AddSingleton<BuildSsdtTelemetryPackagingStep>();
        services.AddSingleton<ISchemaDataApplier, SchemaDataApplier>();
        services.AddSingleton<SchemaApplyOrchestrator>();
        services.AddSingleton<IModelUserSchemaGraphFactory, ModelUserSchemaGraphFactory>();
        services.AddSingleton<FullExportCoordinator>();
        services.AddSingleton<IUatUsersPipelineRunner, UatUsersPipelineRunner>();

        services.AddSingleton<ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>, BuildSsdtPipeline>();
        services.AddSingleton<ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>, DmmComparePipeline>();
        services.AddSingleton<ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>, ExtractModelPipeline>();
        services.AddSingleton<ICommandHandler<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>, CaptureProfilePipeline>();
        services.AddSingleton<ICommandHandler<FullExportPipelineRequest, FullExportPipelineResult>, FullExportPipeline>();
        services.AddSingleton<ICommandHandler<TighteningAnalysisPipelineRequest, TighteningAnalysisPipelineResult>, TighteningAnalysisPipeline>();

        return services;
    }

    internal static IServiceCollection AddPipelineApplications(this IServiceCollection services)
    {
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>, BuildSsdtApplicationService>();
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>, CompareWithDmmApplicationService>();
        services.AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>, ExtractModelApplicationService>();
        services.AddSingleton<IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult>, AnalyzeApplicationService>();
        services.AddSingleton<IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>, CaptureProfileApplicationService>();
        services.AddSingleton<IApplicationService<FullExportApplicationInput, FullExportApplicationResult>, FullExportApplicationService>();

        return services;
    }

    internal static IServiceCollection AddPipelineVerbs(this IServiceCollection services)
    {
        services.AddSingleton<IPipelineVerb, BuildSsdtVerb>();
        services.AddSingleton<IPipelineVerb, ProfileVerb>();
        services.AddSingleton<IPipelineVerb, DmmCompareVerb>();
        services.AddSingleton<IPipelineVerb, ExtractModelVerb>();
        services.AddSingleton<IPipelineVerb, AnalyzeVerb>();
        services.AddSingleton<IPipelineVerb, FullExportVerb>();
        services.AddSingleton<IVerbRegistry, VerbRegistry>();

        return services;
    }
}

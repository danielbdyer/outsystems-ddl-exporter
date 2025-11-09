using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests.Runtime;

public class PipelineServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPipelineInfrastructure_RegistersSystemTimeProvider()
    {
        var services = new ServiceCollection();

        services.AddPipelineInfrastructure();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(TimeProvider.System, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddPipelineConfiguration_RegistersCliConfigurationServices()
    {
        var services = new ServiceCollection();

        services.AddPipelineConfiguration();

        Assert.Contains(services, d => d.ServiceType == typeof(ICliConfigurationService) && d.ImplementationType == typeof(CliConfigurationService));
        Assert.Contains(services, d => d.ServiceType == typeof(CliConfigurationLoader) && d.ImplementationType == typeof(CliConfigurationLoader));
    }

    [Fact]
    public void AddPipelineSqlInfrastructure_RegistersConnectionFactoryDelegate()
    {
        var services = new ServiceCollection();

        services.AddPipelineSqlInfrastructure();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(Func<string, SqlConnectionOptions, IDbConnectionFactory>));
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void AddPipelineIngestion_RegistersIngestionDependencies()
    {
        var services = new ServiceCollection();

        services.AddPipelineIngestion();

        Assert.Contains(services, d => d.ServiceType == typeof(IModelJsonDeserializer) && d.ImplementationType == typeof(ModelJsonDeserializer));
        Assert.Contains(services, d => d.ServiceType == typeof(IProfileSnapshotDeserializer) && d.ImplementationType == typeof(ProfileSnapshotDeserializer));
        Assert.Contains(services, d => d.ServiceType == typeof(IProfileSnapshotSerializer) && d.ImplementationType == typeof(ProfileSnapshotSerializer));
        Assert.Contains(services, d => d.ServiceType == typeof(IDataProfilerFactory) && d.ImplementationType == typeof(DataProfilerFactory));
        Assert.Contains(services, d => d.ServiceType == typeof(IModelIngestionService) && d.ImplementationType == typeof(ModelIngestionService));
        Assert.Contains(services, d => d.ServiceType == typeof(ModuleFilter) && d.ImplementationType == typeof(ModuleFilter));
        Assert.Contains(services, d => d.ServiceType == typeof(SupplementalEntityLoader) && d.ImplementationType == typeof(SupplementalEntityLoader));
        Assert.Contains(services, d => d.ServiceType == typeof(IProfilingInsightGenerator) && d.ImplementationType == typeof(ProfilingInsightGenerator));
    }

    [Fact]
    public void AddPipelineEvidence_RegistersEvidenceCacheServices()
    {
        var services = new ServiceCollection();

        services.AddPipelineEvidence();

        Assert.Contains(services, d => d.ServiceType == typeof(IEvidenceCacheService) && d.ImplementationType == typeof(EvidenceCacheService));
        Assert.Contains(services, d => d.ServiceType == typeof(EvidenceCacheCoordinator) && d.ImplementationType == typeof(EvidenceCacheCoordinator));
    }

    [Fact]
    public void AddPipelineTightening_RegistersTighteningPolicy()
    {
        var services = new ServiceCollection();

        services.AddPipelineTightening();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TighteningPolicy));
        Assert.Equal(typeof(TighteningPolicy), descriptor.ImplementationType);
    }

    [Fact]
    public void AddPipelineEmission_RegistersEmissionComponents()
    {
        var services = new ServiceCollection();

        services.AddPipelineEmission();

        Assert.Contains(services, d => d.ServiceType == typeof(SmoModelFactory) && d.ImplementationType == typeof(SmoModelFactory));
        Assert.Contains(services, d => d.ServiceType == typeof(SsdtEmitter) && d.ImplementationType == typeof(SsdtEmitter));
        Assert.Contains(services, d => d.ServiceType == typeof(PolicyDecisionLogWriter) && d.ImplementationType == typeof(PolicyDecisionLogWriter));
        Assert.Contains(services, d => d.ServiceType == typeof(EmissionFingerprintCalculator) && d.ImplementationType == typeof(EmissionFingerprintCalculator));
        Assert.Contains(services, d => d.ServiceType == typeof(SqlLiteralFormatter) && d.ImplementationType == typeof(SqlLiteralFormatter));
        Assert.Contains(services, d => d.ServiceType == typeof(StaticSeedSqlBuilder) && d.ImplementationType == typeof(StaticSeedSqlBuilder));
        Assert.Contains(services, d => d.ServiceType == typeof(StaticEntitySeedTemplateService) && d.ImplementationType == typeof(StaticEntitySeedTemplateService));
        Assert.Contains(services, d => d.ServiceType == typeof(StaticEntitySeedScriptGenerator) && d.ImplementationType == typeof(StaticEntitySeedScriptGenerator));
    }

    [Fact]
    public void AddPipelineOrchestration_RegistersCommandHandlersAndDependencies()
    {
        var services = new ServiceCollection();

        services.AddPipelineOrchestration();

        Assert.Contains(services, d => d.ServiceType == typeof(ICommandDispatcher) && d.ImplementationType == typeof(CommandDispatcher));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtRequestAssembler) && d.ImplementationType == typeof(BuildSsdtRequestAssembler));
        Assert.Contains(services, d => d.ServiceType == typeof(IModelResolutionService) && d.ImplementationType == typeof(ModelResolutionService));
        Assert.Contains(services, d => d.ServiceType == typeof(IOutputDirectoryResolver) && d.ImplementationType == typeof(OutputDirectoryResolver));
        Assert.Contains(services, d => d.ServiceType == typeof(INamingOverridesBinder) && d.ImplementationType == typeof(NamingOverridesBinder));
        Assert.Contains(services, d => d.ServiceType == typeof(IStaticDataProviderFactory) && d.ImplementationType == typeof(StaticDataProviderFactory));
        Assert.Contains(services, d => d.ServiceType == typeof(IPipelineBootstrapper) && d.ImplementationType == typeof(PipelineBootstrapper));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtBootstrapStep) && d.ImplementationType == typeof(BuildSsdtBootstrapStep));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtEvidenceCacheStep) && d.ImplementationType == typeof(BuildSsdtEvidenceCacheStep));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtPolicyDecisionStep) && d.ImplementationType == typeof(BuildSsdtPolicyDecisionStep));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtEmissionStep) && d.ImplementationType == typeof(BuildSsdtEmissionStep));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtSqlValidationStep) && d.ImplementationType == typeof(BuildSsdtSqlValidationStep));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtStaticSeedStep) && d.ImplementationType == typeof(BuildSsdtStaticSeedStep));
        Assert.Contains(services, d => d.ServiceType == typeof(BuildSsdtTelemetryPackagingStep) && d.ImplementationType == typeof(BuildSsdtTelemetryPackagingStep));

        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>) && d.ImplementationType == typeof(BuildSsdtPipeline));
        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>) && d.ImplementationType == typeof(DmmComparePipeline));
        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>) && d.ImplementationType == typeof(ExtractModelPipeline));
        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>) && d.ImplementationType == typeof(CaptureProfilePipeline));
    }

    [Fact]
    public void AddPipelineApplications_RegistersApplicationServices()
    {
        var services = new ServiceCollection();

        services.AddPipelineApplications();

        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>) && d.ImplementationType == typeof(BuildSsdtApplicationService));
        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>) && d.ImplementationType == typeof(CompareWithDmmApplicationService));
        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>) && d.ImplementationType == typeof(ExtractModelApplicationService));
        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>) && d.ImplementationType == typeof(CaptureProfileApplicationService));
        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<FullExportApplicationInput, FullExportApplicationResult>) && d.ImplementationType == typeof(FullExportApplicationService));
    }

    [Fact]
    public void AddPipelineVerbs_RegistersVerbsAndRegistry()
    {
        var services = new ServiceCollection();

        services.AddPipelineVerbs();

        var verbDescriptors = services.Where(d => d.ServiceType == typeof(IPipelineVerb)).ToArray();
        Assert.Collection(
            verbDescriptors,
            descriptor => Assert.Equal(typeof(BuildSsdtVerb), descriptor.ImplementationType),
            descriptor => Assert.Equal(typeof(ProfileVerb), descriptor.ImplementationType),
            descriptor => Assert.Equal(typeof(DmmCompareVerb), descriptor.ImplementationType),
            descriptor => Assert.Equal(typeof(ExtractModelVerb), descriptor.ImplementationType),
            descriptor => Assert.Equal(typeof(AnalyzeVerb), descriptor.ImplementationType),
            descriptor => Assert.Equal(typeof(FullExportVerb), descriptor.ImplementationType));

        Assert.Contains(services, d => d.ServiceType == typeof(IVerbRegistry) && d.ImplementationType == typeof(VerbRegistry));
    }

    [Fact]
    public void AddPipeline_ComposesAllSlices()
    {
        var services = new ServiceCollection();

        services.AddPipeline();

        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(ICliConfigurationService));
        Assert.Contains(services, d => d.ServiceType == typeof(IModelIngestionService));
        Assert.Contains(services, d => d.ServiceType == typeof(TighteningPolicy));
        Assert.Contains(services, d => d.ServiceType == typeof(SsdtEmitter));
        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>));
        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>));
        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>));
        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>));
        Assert.Contains(services, d => d.ServiceType == typeof(IApplicationService<FullExportApplicationInput, FullExportApplicationResult>));
        Assert.Contains(services, d => d.ServiceType == typeof(IVerbRegistry));
    }
}

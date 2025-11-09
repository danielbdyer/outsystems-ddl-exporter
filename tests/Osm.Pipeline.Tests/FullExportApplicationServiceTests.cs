using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Model.Entities;
using Osm.Domain.Model.Metadata;
using Osm.Domain.Model.Names;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tightening.Validations;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class FullExportApplicationServiceTests
{
    [Fact]
    public async Task RunAsync_SkipExtraction_ReusesProvidedModelPath()
    {
        var configuration = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: "config/appsettings.json");
        var moduleFilter = new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>());
        var sql = new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null);
        var cache = new CacheOptionsOverrides(null, null);

        var buildOverrides = new BuildSsdtOverrides(
            ModelPath: "artifacts/model.json",
            ProfilePath: null,
            OutputDirectory: "out",
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var profileOverrides = new CaptureProfileOverrides(null, "profiles", "sql", null, null);
        var extractOverrides = new ExtractModelOverrides(null, null, null, null, null, null);
        var overrides = new FullExportOverrides(buildOverrides, profileOverrides, extractOverrides, SkipExtraction: true, SkipProfiling: false);

        var profileService = new RecordingProfileService(CreateProfileResult());
        var extractService = new NotInvokedExtractService();
        var buildService = new RecordingBuildService(CreateBuildResult());

        var service = new FullExportApplicationService(profileService, extractService, buildService);

        var input = new FullExportApplicationInput(configuration, overrides, moduleFilter, sql, cache);
        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value;

        Assert.True(value.Extraction.Skipped);
        Assert.Equal("artifacts/model.json", value.Extraction.OutputPath);
        Assert.True(profileService.Invoked);
        Assert.True(buildService.Invoked);
        Assert.Equal("artifacts/model.json", buildService.LastInput?.Overrides.ModelPath);
        Assert.Equal("artifacts/model.json", buildService.LastResult.ModelPath);
    }

    [Fact]
    public async Task RunAsync_SkipProfiling_ReusesProvidedProfilePath()
    {
        var configuration = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
        var moduleFilter = new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>());
        var sql = new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null);
        var cache = new CacheOptionsOverrides(null, null);

        var buildOverrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: "out",
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var profileOverrides = new CaptureProfileOverrides(null, null, null, "profiles/existing/profile.json", null);
        var extractOverrides = new ExtractModelOverrides(null, null, null, null, null, null);
        var overrides = new FullExportOverrides(buildOverrides, profileOverrides, extractOverrides, SkipExtraction: false, SkipProfiling: true);

        var extractionResult = new ExtractModelApplicationResult(CreateExtractionResult(), "model.extracted.json");
        var extractService = new RecordingExtractService(extractionResult);
        var profileService = new NotInvokedProfileService();
        var buildService = new RecordingBuildService(CreateBuildResult());

        var service = new FullExportApplicationService(profileService, extractService, buildService);

        var input = new FullExportApplicationInput(configuration, overrides, moduleFilter, sql, cache);
        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value;

        Assert.False(value.Extraction.Skipped);
        Assert.True(extractService.Invoked);
        Assert.True(buildService.Invoked);
        Assert.True(value.Profile.Skipped);
        Assert.Equal("profiles/existing/profile.json", value.Profile.ProfilePath);
        Assert.Equal("profiles/existing/profile.json", buildService.LastInput?.Overrides.ProfilePath);
        Assert.Equal("model.extracted.json", buildService.LastInput?.Overrides.ModelPath);
    }

    private static CaptureProfileApplicationResult CreateProfileResult()
    {
        var snapshot = ProfileSnapshot.Create(Array.Empty<ColumnProfile>(), Array.Empty<UniqueCandidateProfile>(), Array.Empty<CompositeUniqueCandidateProfile>(), Array.Empty<ForeignKeyReality>());
        if (snapshot.IsFailure)
        {
            throw new InvalidOperationException("Failed to create profile snapshot for test.");
        }

        var moduleSummary = new CaptureProfileModuleSummary(false, Array.Empty<string>(), true, true);
        var supplementalSummary = new CaptureProfileSupplementalSummary(true, Array.Empty<string>());
        var snapshotSummary = new CaptureProfileSnapshotSummary(0, 0, 0, 0, 0);
        var manifest = new CaptureProfileManifest(
            "model.json",
            "profiles/profile.json",
            "sql",
            moduleSummary,
            supplementalSummary,
            snapshotSummary,
            Array.Empty<CaptureProfileInsight>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);

        var pipelineResult = new CaptureProfilePipelineResult(
            snapshot.Value,
            manifest,
            "profiles/profile.json",
            "profiles/manifest.json",
            ImmutableArray<ProfilingInsight>.Empty,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty,
            MultiEnvironmentProfileReport.Empty);

        return new CaptureProfileApplicationResult(
            pipelineResult,
            "profiles",
            "model.json",
            "sql",
            pipelineResult.ProfilePath,
            null);
    }

    private static BuildSsdtApplicationResult CreateBuildResult()
    {
        var profileResult = ProfileSnapshot.Create(Array.Empty<ColumnProfile>(), Array.Empty<UniqueCandidateProfile>(), Array.Empty<CompositeUniqueCandidateProfile>(), Array.Empty<ForeignKeyReality>());
        if (profileResult.IsFailure)
        {
            throw new InvalidOperationException("Failed to create profile snapshot for test.");
        }

        var toggles = TighteningToggleSnapshot.Create(TighteningOptions.Default);
        var decisionReport = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            ImmutableDictionary<string, ToggleExportValue>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            toggles);

        var manifest = new SsdtManifest(
            Array.Empty<TableManifestEntry>(),
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(0, 0, 0),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty,
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        var pipelineResult = new BuildSsdtPipelineResult(
            profileResult.Value,
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            "decision.log",
            "opportunities.json",
            "validations.json",
            "suggestions/safe-to-apply.sql",
            "-- safe script\nGO\n",
            "suggestions/needs-remediation.sql",
            "-- remediation script\nGO\n",
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty,
            MultiEnvironmentProfileReport.Empty);

        return new BuildSsdtApplicationResult(
            pipelineResult,
            "sql",
            "profiles/profile.json",
            "out",
            "artifacts/model.json",
            ModelWasExtracted: false,
            ImmutableArray<string>.Empty);
    }

    private static ModelExtractionResult CreateExtractionResult()
    {
        var moduleName = ModuleName.Create("SampleModule").Value;
        var entityName = EntityName.Create("SampleEntity").Value;
        var tableName = TableName.Create("OSUSR_SAMPLE").Value;
        var schemaName = SchemaName.Create("dbo").Value;
        var attribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("Id").Value,
            dataType: "int",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true).Value;
        var entity = EntityModel.Create(
            moduleName,
            entityName,
            tableName,
            schemaName,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { attribute },
            metadata: EntityMetadata.Empty,
            allowMissingPrimaryKey: false).Value;
        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { entity }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
        var metadata = new OutsystemsMetadataSnapshot(
            Array.Empty<OutsystemsModuleRow>(),
            Array.Empty<OutsystemsEntityRow>(),
            Array.Empty<OutsystemsAttributeRow>(),
            Array.Empty<OutsystemsReferenceRow>(),
            Array.Empty<OutsystemsPhysicalTableRow>(),
            Array.Empty<OutsystemsColumnRealityRow>(),
            Array.Empty<OutsystemsColumnCheckRow>(),
            Array.Empty<OutsystemsColumnCheckJsonRow>(),
            Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Array.Empty<OutsystemsIndexRow>(),
            Array.Empty<OutsystemsIndexColumnRow>(),
            Array.Empty<OutsystemsForeignKeyRow>(),
            Array.Empty<OutsystemsForeignKeyColumnRow>(),
            Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            Array.Empty<OutsystemsAttributeHasFkRow>(),
            Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Array.Empty<OutsystemsTriggerRow>(),
            Array.Empty<OutsystemsAttributeJsonRow>(),
            Array.Empty<OutsystemsRelationshipJsonRow>(),
            Array.Empty<OutsystemsIndexJsonRow>(),
            Array.Empty<OutsystemsTriggerJsonRow>(),
            Array.Empty<OutsystemsModuleJsonRow>(),
            "database");

        var buffer = new MemoryStream();
        using (var writer = new StreamWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("{\"model\":true}");
            writer.Flush();
        }

        buffer.Position = 0;
        var payload = ModelJsonPayload.FromStream(buffer);
        return new ModelExtractionResult(model, payload, DateTimeOffset.UtcNow, Array.Empty<string>(), metadata);
    }

    private sealed class RecordingProfileService : IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>
    {
        private readonly CaptureProfileApplicationResult _result;

        public RecordingProfileService(CaptureProfileApplicationResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public bool Invoked { get; private set; }

        public Task<Result<CaptureProfileApplicationResult>> RunAsync(CaptureProfileApplicationInput input, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return Task.FromResult(Result<CaptureProfileApplicationResult>.Success(_result));
        }
    }

    private sealed class NotInvokedExtractService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
    {
        public Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Extract service should not be invoked when skipping extraction.");
    }

    private sealed class NotInvokedProfileService : IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>
    {
        public Task<Result<CaptureProfileApplicationResult>> RunAsync(CaptureProfileApplicationInput input, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Profile service should not be invoked when skipping profiling.");
    }

    private sealed class RecordingExtractService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
    {
        private readonly ExtractModelApplicationResult _result;

        public RecordingExtractService(ExtractModelApplicationResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public bool Invoked { get; private set; }

        public Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return Task.FromResult(Result<ExtractModelApplicationResult>.Success(_result));
        }
    }

    private sealed class RecordingBuildService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        private readonly BuildSsdtApplicationResult _result;

        public RecordingBuildService(BuildSsdtApplicationResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public bool Invoked { get; private set; }

        public BuildSsdtApplicationInput? LastInput { get; private set; }

        public BuildSsdtApplicationResult LastResult => _result;

        public Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            LastInput = input;
            return Task.FromResult(Result<BuildSsdtApplicationResult>.Success(_result));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Validation.Tightening;
using Opportunities = Osm.Validation.Tightening.Opportunities;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class FullExportApplicationServiceTests
{
    [Fact]
    public async Task RunAsync_ReusesExistingModel_WhenReuseSignalProvided()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(modelPath, "{}");

        try
        {
            var model = CreateModel();
            var modelDeserializer = new StubModelJsonDeserializer(model);

            var profileResult = new CaptureProfileApplicationResult(
                CreateCaptureProfilePipelineResult(),
                OutputDirectory: "profiles",
                ModelPath: modelPath,
                ProfilerProvider: "fixture",
                FixtureProfilePath: null);
            var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));

            var extractService = new RecordingExtractService();

            var buildResult = CreateBuildResult(modelPath);
            var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));

            var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());

            var service = new FullExportApplicationService(
                profileService,
                extractService,
                buildService,
                schemaApplyOrchestrator,
                modelDeserializer);

            var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
            var overrides = new FullExportOverrides(
                new BuildSsdtOverrides(modelPath, null, null, null, null, null, null, null),
                new CaptureProfileOverrides(modelPath, null, null, null, null),
                new ExtractModelOverrides(null, null, null, null, null, null),
                Apply: null,
                ReuseModelPath: true);
            var input = new FullExportApplicationInput(
                configurationContext,
                overrides,
                new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
                new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null),
                new CacheOptionsOverrides(null, null));

            var result = await service.RunAsync(input, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(0, extractService.CallCount);
            Assert.NotNull(buildService.LastInput);
            Assert.Equal(modelPath, buildService.LastInput!.Overrides.ModelPath);
            Assert.NotNull(buildService.LastInput.DynamicDataset);
            Assert.True(buildService.LastInput.DynamicDataset!.IsEmpty);
            Assert.True(result.Value.Extraction.ModelWasReused);
            Assert.Equal(Path.GetFullPath(modelPath), Path.GetFullPath(result.Value.Extraction.OutputPath));
        }
        finally
        {
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }
        }
    }

    private static OsmModel CreateModel()
    {
        var moduleName = ModuleName.Create("AppCore").Value;
        var entityName = EntityName.Create("Customer").Value;
        var tableName = TableName.Create("OSUSR_CUSTOMER").Value;
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
        return OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
    }

    private static CaptureProfilePipelineResult CreateCaptureProfilePipelineResult()
    {
        var snapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;
        var manifest = new CaptureProfileManifest(
            ModelPath: "model.json",
            ProfilePath: "profile.json",
            ProfilerProvider: "fixture",
            ModuleFilter: new CaptureProfileModuleSummary(false, Array.Empty<string>(), IncludeSystemModules: true, IncludeInactiveModules: true),
            SupplementalModels: new CaptureProfileSupplementalSummary(false, Array.Empty<string>()),
            Snapshot: new CaptureProfileSnapshotSummary(0, 0, 0, 0, 0),
            Insights: Array.Empty<CaptureProfileInsight>(),
            Warnings: Array.Empty<string>(),
            CapturedAtUtc: DateTimeOffset.UtcNow);
        return new CaptureProfilePipelineResult(
            snapshot,
            manifest,
            ProfilePath: "profile.json",
            ManifestPath: "manifest.json",
            Insights: ImmutableArray<ProfilingInsight>.Empty,
            ExecutionLog: PipelineExecutionLog.Empty,
            Warnings: ImmutableArray<string>.Empty,
            MultiEnvironmentReport: null);
    }

    private static BuildSsdtApplicationResult CreateBuildResult(string modelPath)
    {
        var profileSnapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;
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
            TighteningToggleSnapshot.Create(TighteningOptions.Default));
        var manifest = new SsdtManifest(
            Array.Empty<TableManifestEntry>(),
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(0, 0, 0),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());
        var opportunities = new Opportunities.OpportunitiesReport(
            ImmutableArray<Opportunities.Opportunity>.Empty,
            ImmutableDictionary<Opportunities.OpportunityDisposition, int>.Empty,
            ImmutableDictionary<Opportunities.OpportunityCategory, int>.Empty,
            ImmutableDictionary<Opportunities.OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);
        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        var pipelineResult = new BuildSsdtPipelineResult(
            profileSnapshot,
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            DecisionLogPath: "decision.json",
            OpportunitiesPath: "opportunities.json",
            ValidationsPath: "validations.json",
            SafeScriptPath: "safe.sql",
            SafeScript: string.Empty,
            RemediationScriptPath: "remediation.sql",
            RemediationScript: string.Empty,
            StaticSeedScriptPaths: ImmutableArray<string>.Empty,
            DynamicInsertScriptPaths: ImmutableArray<string>.Empty,
            TelemetryPackagePaths: ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            EvidenceCache: null,
            ExecutionLog: PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            Warnings: ImmutableArray<string>.Empty,
            MultiEnvironmentReport: null);

        return new BuildSsdtApplicationResult(
            pipelineResult,
            ProfilerProvider: "fixture",
            ProfilePath: "profile.json",
            OutputDirectory: "out",
            ModelPath: modelPath,
            ModelWasExtracted: false,
            ModelExtractionWarnings: ImmutableArray<string>.Empty);
    }

    private sealed class StubProfileService : IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>
    {
        private readonly Result<CaptureProfileApplicationResult> _result;

        public StubProfileService(Result<CaptureProfileApplicationResult> result)
        {
            _result = result;
        }

        public Task<Result<CaptureProfileApplicationResult>> RunAsync(CaptureProfileApplicationInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class RecordingExtractService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
    {
        public int CallCount { get; private set; }

        public Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Extract service should not be invoked when model reuse is enabled.");
        }
    }

    private sealed class RecordingBuildService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        private readonly Result<BuildSsdtApplicationResult> _result;

        public RecordingBuildService(Result<BuildSsdtApplicationResult> result)
        {
            _result = result;
        }

        public BuildSsdtApplicationInput? LastInput { get; private set; }

        public Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubSchemaDataApplier : ISchemaDataApplier
    {
        public Task<Result<SchemaDataApplyOutcome>> ApplyAsync(
            SchemaDataApplyRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<SchemaDataApplyOutcome>.Success(new SchemaDataApplyOutcome(
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ExecutedBatchCount: 0,
                TimeSpan.Zero)));
    }

    private sealed class StubModelJsonDeserializer : IModelJsonDeserializer
    {
        private readonly OsmModel _model;

        public StubModelJsonDeserializer(OsmModel model)
        {
            _model = model;
        }

        public Result<OsmModel> Deserialize(Stream jsonStream, ICollection<string>? warnings = null, ModelJsonDeserializerOptions? options = null)
        {
            warnings?.Clear();
            return Result<OsmModel>.Success(_model);
        }
    }
}

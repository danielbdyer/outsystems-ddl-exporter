using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class BuildSsdtEmissionStepTests
{
    [Fact]
    public async Task ExecuteAsync_logs_smo_model_creation_metadata()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var bootstrap = CreateBootstrapContext();
        var state = CreateDecisionState(request, bootstrap);

        var smoModel = CreateSampleSmoModel();
        var manifest = CreateManifest();
        var artifacts = CreateOpportunityArtifacts(output.Path);

        var factory = new FakeSmoModelFactory(smoModel);
        var emitter = new FakeSsdtEmitter(manifest);
        var opportunityWriter = new FakeOpportunityLogWriter(artifacts);
        var step = new BuildSsdtEmissionStep(
            factory,
            emitter,
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            opportunityWriter);

        var result = await step.ExecuteAsync(state);

        Assert.True(result.IsSuccess);
        Assert.True(factory.Invoked);
        var log = result.Value.Log.Build();
        var entry = Assert.Single(log.Entries.Where(e => e.Step == "smo.model.created"));
        Assert.Equal("1", entry.Metadata["counts.tables"]);
        Assert.Equal("2", entry.Metadata["counts.columns"]);
        Assert.Equal("1", entry.Metadata["counts.indexes"]);
        Assert.Equal("1", entry.Metadata["counts.foreignKeys"]);
    }

    [Fact]
    public async Task ExecuteAsync_records_emission_and_persistence_metadata()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var bootstrap = CreateBootstrapContext();
        var state = CreateDecisionState(request, bootstrap);

        var smoModel = CreateSampleSmoModel();
        var manifest = CreateManifest();
        var artifacts = CreateOpportunityArtifacts(output.Path);

        var factory = new FakeSmoModelFactory(smoModel);
        var emitter = new FakeSsdtEmitter(manifest);
        var opportunityWriter = new FakeOpportunityLogWriter(artifacts);
        var step = new BuildSsdtEmissionStep(
            factory,
            emitter,
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            opportunityWriter);

        var result = await step.ExecuteAsync(state);

        Assert.True(result.IsSuccess);
        Assert.True(emitter.Invoked);
        Assert.True(opportunityWriter.Invoked);

        var emissionReady = result.Value;
        Assert.Equal(artifacts.ReportPath, emissionReady.OpportunityArtifacts.ReportPath);
        Assert.Equal(artifacts.SafeScriptPath, emissionReady.OpportunityArtifacts.SafeScriptPath);
        Assert.Equal(artifacts.RemediationScriptPath, emissionReady.OpportunityArtifacts.RemediationScriptPath);

        var log = emissionReady.Log.Build();
        var emissionEntry = Assert.Single(log.Entries.Where(e => e.Step == "ssdt.emission.completed"));
        Assert.Equal(output.Path, emissionEntry.Metadata["paths.output"]);
        Assert.Equal(manifest.Tables.Count.ToString(), emissionEntry.Metadata["counts.tableArtifacts"]);
        Assert.Equal("false", emissionEntry.Metadata["flags.policySummary.included"]);

        var decisionEntry = Assert.Single(log.Entries.Where(e => e.Step == "policy.log.persisted"));
        Assert.Equal(emissionReady.DecisionLogPath, decisionEntry.Metadata["paths.decision"]);
        Assert.Equal("0", decisionEntry.Metadata["counts.diagnostics"]);

        var opportunityEntry = Assert.Single(log.Entries.Where(e => e.Step == "opportunities.persisted"));
        Assert.Equal(artifacts.ReportPath, opportunityEntry.Metadata["paths.report"]);
        Assert.Equal(artifacts.SafeScriptPath, opportunityEntry.Metadata["paths.safeScript"]);
        Assert.Equal(artifacts.RemediationScriptPath, opportunityEntry.Metadata["paths.remediationScript"]);
        Assert.Equal(state.Opportunities.TotalCount.ToString(), opportunityEntry.Metadata["counts.total"]);
    }

    private static BuildSsdtPipelineRequest CreateRequest(string outputDirectory)
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        return new BuildSsdtPipelineRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            outputDirectory,
            TighteningOptions.Default,
            SupplementalModelOptions.Default,
            "fixture",
            ProfilePath: null,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicyLoader.LoadDefault(),
            EvidenceCache: null,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null);
    }

    private static PipelineBootstrapContext CreateBootstrapContext()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var profile = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        return new PipelineBootstrapContext(
            model,
            ImmutableArray<EntityModel>.Empty,
            profile,
            ImmutableArray<ProfilingInsight>.Empty,
            ImmutableArray<string>.Empty);
    }

    private static DecisionsSynthesized CreateDecisionState(
        BuildSsdtPipelineRequest request,
        PipelineBootstrapContext bootstrap)
    {
        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, string>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var report = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            TighteningToggleSnapshot.Create(TighteningOptions.Default));

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        return new DecisionsSynthesized(
            request,
            new PipelineExecutionLogBuilder(TimeProvider.System),
            bootstrap,
            EvidenceCache: null,
            decisions,
            report,
            opportunities,
            ImmutableArray<PipelineInsight>.Empty);
    }

    private static SmoModel CreateSampleSmoModel()
    {
        var columns = ImmutableArray.Create(
            new SmoColumnDefinition(
                PhysicalName: "Id",
                Name: "Id",
                LogicalName: "Id",
                DataType: DataType.Int,
                Nullable: false,
                IsIdentity: true,
                IdentitySeed: 1,
                IdentityIncrement: 1,
                IsComputed: false,
                ComputedExpression: null,
                DefaultExpression: null,
                Collation: null,
                Description: null,
                DefaultConstraint: null,
                CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty),
            new SmoColumnDefinition(
                PhysicalName: "Name",
                Name: "Name",
                LogicalName: "Name",
                DataType: new DataType(SqlDataType.NVarChar, 50),
                Nullable: true,
                IsIdentity: false,
                IdentitySeed: 0,
                IdentityIncrement: 0,
                IsComputed: false,
                ComputedExpression: null,
                DefaultExpression: null,
                Collation: null,
                Description: null,
                DefaultConstraint: null,
                CheckConstraints: ImmutableArray<SmoCheckConstraintDefinition>.Empty));

        var indexes = ImmutableArray.Create(
            new SmoIndexDefinition(
                Name: "PK_Sample",
                IsUnique: true,
                IsPrimaryKey: true,
                IsPlatformAuto: false,
                Description: null,
                Columns: ImmutableArray.Create(new SmoIndexColumnDefinition("Id", 0, false, false)),
                Metadata: SmoIndexMetadata.Empty));

        var foreignKeys = ImmutableArray.Create(
            new SmoForeignKeyDefinition(
                Name: "FK_Sample",
                Columns: ImmutableArray.Create("Id"),
                ReferencedModule: "Core",
                ReferencedTable: "Parent",
                ReferencedSchema: "dbo",
                ReferencedColumns: ImmutableArray.Create("Id"),
                ReferencedLogicalTable: "Parent",
                DeleteAction: ForeignKeyAction.NoAction,
                IsNoCheck: false));

        var table = new SmoTableDefinition(
            Module: "Core",
            OriginalModule: "Core",
            Name: "Sample",
            Schema: "dbo",
            Catalog: "OutSystems",
            LogicalName: "Sample",
            Description: null,
            Columns: columns,
            Indexes: indexes,
            ForeignKeys: foreignKeys,
            Triggers: ImmutableArray<SmoTriggerDefinition>.Empty);

        return SmoModel.Create(ImmutableArray.Create(table));
    }

    private static SsdtManifest CreateManifest()
    {
        var tables = new List<TableManifestEntry>
        {
            new(
                Module: "Core",
                Schema: "dbo",
                Table: "Sample",
                TableFile: "Modules/Core.Sample.sql",
                Indexes: Array.Empty<string>(),
                ForeignKeys: Array.Empty<string>(),
                IncludesExtendedProperties: false)
        };

        return new SsdtManifest(
            tables,
            new SsdtManifestOptions(
                IncludePlatformAutoIndexes: false,
                EmitBareTableOnly: false,
                SanitizeModuleNames: true,
                ModuleParallelism: 1),
            PolicySummary: null,
            Emission: new SsdtEmissionMetadata("sha256", "abc123"),
            PreRemediation: Array.Empty<PreRemediationManifestEntry>(),
            Coverage: SsdtCoverageSummary.CreateComplete(1, 2, 3),
            PredicateCoverage: SsdtPredicateCoverage.Empty,
            Unsupported: Array.Empty<string>());
    }

    private static OpportunityArtifacts CreateOpportunityArtifacts(string outputDirectory)
    {
        var reportPath = Path.Combine(outputDirectory, "opportunities.json");
        var safePath = Path.Combine(outputDirectory, "safe.sql");
        var remediationPath = Path.Combine(outputDirectory, "remediation.sql");
        return new OpportunityArtifacts(reportPath, safePath, "SELECT 1;", remediationPath, "SELECT 2;");
    }

    private sealed class FakeSmoModelFactory : ISmoModelFactory
    {
        private readonly SmoModel _model;

        public FakeSmoModelFactory(SmoModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public bool Invoked { get; private set; }

        public SmoModel Create(
            OsmModel model,
            PolicyDecisionSet decisions,
            ProfileSnapshot? profile = null,
            SmoBuildOptions? options = null,
            IEnumerable<EntityModel>? supplementalEntities = null,
            TypeMappingPolicy? typeMappingPolicy = null)
        {
            Invoked = true;
            return _model;
        }
    }

    private sealed class FakeSsdtEmitter : ISsdtEmitter
    {
        private readonly SsdtManifest _manifest;

        public FakeSsdtEmitter(SsdtManifest manifest)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        public bool Invoked { get; private set; }

        public Task<SsdtManifest> EmitAsync(
            SmoModel model,
            string outputDirectory,
            SmoBuildOptions options,
            SsdtEmissionMetadata emission,
            PolicyDecisionReport? decisionReport = null,
            IReadOnlyList<PreRemediationManifestEntry>? preRemediation = null,
            SsdtCoverageSummary? coverage = null,
            SsdtPredicateCoverage? predicateCoverage = null,
            IReadOnlyList<string>? unsupported = null,
            CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return Task.FromResult(_manifest);
        }
    }

    private sealed class FakeOpportunityLogWriter : IOpportunityLogWriter
    {
        private readonly OpportunityArtifacts _artifacts;

        public FakeOpportunityLogWriter(OpportunityArtifacts artifacts)
        {
            _artifacts = artifacts;
        }

        public bool Invoked { get; private set; }

        public Task<OpportunityArtifacts> WriteAsync(
            string outputDirectory,
            OpportunitiesReport report,
            CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return Task.FromResult(_artifacts);
        }
    }
}

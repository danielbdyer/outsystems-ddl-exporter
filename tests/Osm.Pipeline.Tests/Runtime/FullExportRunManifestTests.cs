using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Pipeline.UatUsers;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tightening.Validations;
using Tests.Support;
using Xunit;

using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;

namespace Osm.Pipeline.Tests.Runtime;

public sealed class FullExportRunManifestTests
{
    [Fact]
    public void ComputeTiming_ReturnsExpectedDuration()
    {
        var entries = new List<PipelineLogEntry>
        {
            new(DateTimeOffset.Parse("2024-03-01T12:00:00Z"), "start", "started", ImmutableDictionary<string, string?>.Empty),
            new(DateTimeOffset.Parse("2024-03-01T12:00:05Z"), "mid", "running", ImmutableDictionary<string, string?>.Empty),
            new(DateTimeOffset.Parse("2024-03-01T12:00:09Z"), "end", "completed", ImmutableDictionary<string, string?>.Empty)
        };

        var log = new PipelineExecutionLog(entries);

        var timing = FullExportRunManifest.ComputeTiming(log);

        Assert.Equal(entries[0].TimestampUtc, timing.StartedAtUtc);
        Assert.Equal(entries[^1].TimestampUtc, timing.CompletedAtUtc);
        Assert.Equal(TimeSpan.FromSeconds(9), timing.Duration);
    }

    [Fact]
    public void SerializeManifest_ProducesExpectedShape()
    {
        var generatedAt = DateTimeOffset.Parse("2024-03-05T10:30:00Z");
        var stages = ImmutableArray.Create(
            new FullExportStageManifest(
                "extract-model",
                generatedAt,
                generatedAt + TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                ImmutableArray.Create("extraction-warning"),
                ImmutableDictionary<string, string?>.Empty),
            new FullExportStageManifest(
                "build-ssdt",
                generatedAt + TimeSpan.FromMinutes(1),
                generatedAt + TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(1),
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string?>.Empty));

        var dynamicArtifacts = ImmutableArray.Create(
            new FullExportManifestArtifact("model-json", "/tmp/model.json", "application/json"),
            new FullExportManifestArtifact("full-export-manifest", "/tmp/full-export.manifest.json", "application/json"),
            new FullExportManifestArtifact("dynamic-insert", "/tmp/DynamicData/App/Entity.dynamic.sql", "application/sql"));
        var staticArtifacts = ImmutableArray.Create(
            new FullExportManifestArtifact("static-seed", "/tmp/Seeds/StaticEntities.seed.sql", "application/sql"));

        var manifest = new FullExportRunManifest(
            generatedAt,
            "config/full-export.json",
            stages,
            dynamicArtifacts,
            staticArtifacts,
            true,
            ImmutableArray.Create("extraction-warning"));

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("config/full-export.json", root.GetProperty("ConfigurationPath").GetString());

        var stagesElement = root.GetProperty("Stages");
        Assert.Equal(2, stagesElement.GetArrayLength());

        var firstStage = stagesElement[0];
        Assert.Equal("extract-model", firstStage.GetProperty("Name").GetString());
        Assert.Equal("00:00:05", firstStage.GetProperty("Duration").GetString());

        var warnings = firstStage.GetProperty("Warnings");
        Assert.Contains("extraction-warning", warnings.EnumerateArray().Select(static element => element.GetString()));

        Assert.True(root.GetProperty("StaticSeedArtifactsIncludedInDynamic").GetBoolean());

        var dynamicElement = root.GetProperty("DynamicArtifacts");
        Assert.Equal(3, dynamicElement.GetArrayLength());
        Assert.Equal("model-json", dynamicElement[0].GetProperty("Name").GetString());
        Assert.Equal("/tmp/model.json", dynamicElement[0].GetProperty("Path").GetString());
        Assert.Equal("full-export-manifest", dynamicElement[1].GetProperty("Name").GetString());
        Assert.Equal("dynamic-insert", dynamicElement[2].GetProperty("Name").GetString());

        var staticElement = root.GetProperty("StaticSeedArtifacts");
        Assert.Single(staticElement.EnumerateArray());
        Assert.Equal("static-seed", staticElement[0].GetProperty("Name").GetString());
    }

    [Fact]
    public void Create_PartitionsStaticSeedArtifacts()
    {
        using var tempDir = new TempDirectory();
        var dynamicRoot = Path.Combine(tempDir.Path, "Dynamic");
        Directory.CreateDirectory(dynamicRoot);
        var seedRoot = Path.Combine(tempDir.Path, "Seeds");
        Directory.CreateDirectory(seedRoot);

        var safeScriptPath = Path.Combine(dynamicRoot, "safe.sql");
        File.WriteAllText(safeScriptPath, "-- safe");
        var remediationScriptPath = Path.Combine(dynamicRoot, "remediation.sql");
        File.WriteAllText(remediationScriptPath, "-- remediation");

        var moduleSeedPath = Path.Combine(seedRoot, "ModuleA", "StaticEntities.seed.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(moduleSeedPath)!);
        File.WriteAllText(moduleSeedPath, "-- module seed");

        var masterSeedPath = Path.Combine(seedRoot, "StaticEntities.seed.sql");
        File.WriteAllText(masterSeedPath, "-- master seed");

        var staticSeedPaths = ImmutableArray.Create(moduleSeedPath, masterSeedPath);

        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        var extraction = CreateExtractionApplicationResult(modelPath);
        var capture = CreateCaptureApplicationResult(profilePath, modelPath, Path.Combine(tempDir.Path, "Profiles"));
        var build = CreateBuildApplicationResult(dynamicRoot, modelPath, profilePath, safeScriptPath, remediationScriptPath, staticSeedPaths);
        var schemaApply = new SchemaApplyResult(
            Attempted: false,
            SafeScriptApplied: false,
            StaticSeedsApplied: false,
            AppliedScripts: ImmutableArray<string>.Empty,
            AppliedSeedScripts: ImmutableArray<string>.Empty,
            SkippedScripts: ImmutableArray<string>.Empty,
            Warnings: ImmutableArray<string>.Empty,
            PendingRemediationCount: 0,
            SafeScriptPath: safeScriptPath,
            RemediationScriptPath: remediationScriptPath,
            StaticSeedScriptPaths: staticSeedPaths,
            Duration: TimeSpan.Zero,
            StaticSeedSynchronizationMode: StaticSeedSynchronizationMode.NonDestructive,
            StaticSeedValidation: StaticSeedValidationSummary.NotAttempted);

        var applicationResult = new FullExportApplicationResult(
            build,
            capture,
            extraction,
            schemaApply,
            SchemaApplyOptions.Disabled,
            UatUsersApplicationResult.Disabled);
        var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, "config/full-export.json");
        var verbResult = new FullExportVerbResult(configurationContext, applicationResult);

        var manifestPath = Path.Combine(dynamicRoot, FullExportVerb.RunManifestFileName);
        File.WriteAllText(manifestPath, "{}");

        var artifacts = new List<PipelineArtifact>
        {
            new("model-json", extraction.OutputPath, "application/json"),
            new("profile", capture.PipelineResult.ProfilePath, "application/json"),
            new("profile-manifest", capture.PipelineResult.ManifestPath, "application/json"),
            new("decision-log", build.PipelineResult.DecisionLogPath, "application/json"),
            new("opportunities", build.PipelineResult.OpportunitiesPath, "application/json"),
            new("validations", build.PipelineResult.ValidationsPath, "application/json"),
            new("opportunity-safe", safeScriptPath, "application/sql"),
            new("opportunity-remediation", remediationScriptPath, "application/sql"),
            new("static-seed", moduleSeedPath, "application/sql"),
            new("static-seed", masterSeedPath, "application/sql"),
            new("manifest", Path.Combine(dynamicRoot, "manifest.json"), "application/json"),
            new("full-export-manifest", manifestPath, "application/json"),
            new("ssdt-project", build.PipelineResult.SqlProjectPath, "application/xml")
        };

        foreach (var insertPath in build.PipelineResult.DynamicInsertScriptPaths)
        {
            artifacts.Add(new PipelineArtifact("dynamic-insert", insertPath, "application/sql"));
        }

        var manifest = FullExportRunManifest.Create(verbResult, artifacts, TimeProvider.System);

        Assert.True(manifest.StaticSeedArtifactsIncludedInDynamic);
        Assert.Equal(artifacts.Count, manifest.DynamicArtifacts.Length);
        Assert.Equal(staticSeedPaths.Length, manifest.StaticSeedArtifacts.Length);
        Assert.Equal(staticSeedPaths.Length, manifest.DynamicArtifacts.Count(artifact => artifact.Name == "static-seed"));

        var seedRootFullPath = Path.GetFullPath(seedRoot);
        Assert.All(manifest.StaticSeedArtifacts, artifact =>
            Assert.StartsWith(seedRootFullPath, Path.GetFullPath(artifact.Path), StringComparison.OrdinalIgnoreCase));

        var buildStage = Assert.Single(manifest.Stages, stage => stage.Name == "build-ssdt");
        Assert.True(buildStage.Artifacts.TryGetValue("staticSeedRoot", out var stageSeedRoot));
        Assert.Equal(seedRootFullPath, Path.GetFullPath(stageSeedRoot!));
        Assert.Equal(seedRootFullPath, Path.GetFullPath(FullExportRunManifest.ResolveStaticSeedRoot(build.PipelineResult)!));

        Assert.True(buildStage.Artifacts.TryGetValue("dynamicInsertRoot", out var stageDynamicRoot));
        var expectedDynamicRoot = Path.GetFullPath(Path.Combine(dynamicRoot, "DynamicData", "ModuleA"));
        Assert.Equal(expectedDynamicRoot, Path.GetFullPath(stageDynamicRoot!));
        Assert.Equal(expectedDynamicRoot, Path.GetFullPath(FullExportRunManifest.ResolveDynamicInsertRoot(build.PipelineResult)!));

        Assert.True(buildStage.Artifacts.TryGetValue("sqlProject", out var stageSqlProject));
        Assert.Equal(
            Path.GetFullPath(build.PipelineResult.SqlProjectPath),
            Path.GetFullPath(stageSqlProject!));

        Assert.True(buildStage.Artifacts.TryGetValue("staticSeedOrdering", out var seedOrdering));
        Assert.Equal("alphabetical", seedOrdering);
        Assert.True(buildStage.Artifacts.TryGetValue("dynamicInsertOrdering", out var insertOrdering));
        Assert.Equal("alphabetical", insertOrdering);

        var dynamicFiles = Directory.GetFiles(dynamicRoot, "*", SearchOption.AllDirectories);
        Assert.DoesNotContain(dynamicFiles, path => Path.GetFullPath(path).StartsWith(seedRootFullPath, StringComparison.OrdinalIgnoreCase));

        var staticFiles = Directory.GetFiles(seedRoot, "*", SearchOption.AllDirectories)
            .Select(static path => Path.GetFullPath(path))
            .ToArray();
        Assert.All(staticSeedPaths.Select(Path.GetFullPath), path => Assert.Contains(path, staticFiles));
    }

    private static ExtractModelApplicationResult CreateExtractionApplicationResult(string modelPath)
    {
        var extractionResult = new ModelExtractionResult(
            ModelFixtures.LoadModel("model.edge-case.json"),
            ModelJsonPayload.FromFile(modelPath),
            DateTimeOffset.UtcNow,
            Array.Empty<string>(),
            CreateMetadataSnapshot("TestDatabase"));
        return new ExtractModelApplicationResult(extractionResult, modelPath);
    }

    private static CaptureProfileApplicationResult CreateCaptureApplicationResult(string profilePath, string modelPath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestPath, "{}");

        var profile = ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json"));
        var manifest = new CaptureProfileManifest(
            modelPath,
            profilePath,
            "fixture",
            new CaptureProfileModuleSummary(false, Array.Empty<string>(), true, true),
            new CaptureProfileSupplementalSummary(false, Array.Empty<string>()),
            new CaptureProfileSnapshotSummary(0, 0, 0, 0, 0),
            Array.Empty<CaptureProfileInsight>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);

        var pipelineResult = new CaptureProfilePipelineResult(
            profile,
            manifest,
            profilePath,
            manifestPath,
            ImmutableArray<ProfilingInsight>.Empty,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty,
            null);

        return new CaptureProfileApplicationResult(
            pipelineResult,
            outputDirectory,
            modelPath,
            "fixture",
            profilePath);
    }

    private static BuildSsdtApplicationResult CreateBuildApplicationResult(
        string outputDirectory,
        string modelPath,
        string profilePath,
        string safeScriptPath,
        string remediationScriptPath,
        ImmutableArray<string> staticSeedPaths)
    {
        Directory.CreateDirectory(outputDirectory);
        var manifestFilePath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestFilePath, "{}");

        var manifest = new SsdtManifest(
            new[]
            {
                new TableManifestEntry("Core", "dbo", "Sample", "Modules/Core.Sample.sql", Array.Empty<string>(), Array.Empty<string>(), false)
            },
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "abc123"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(1, 1, 1),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());

        var toggleSnapshot = TighteningToggleSnapshot.Create(TighteningOptions.Default);
        var togglePrecedence = toggleSnapshot
            .ToExportDictionary()
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var decisionReport = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            togglePrecedence,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            toggleSnapshot);

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty,
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        var decisionLogPath = Path.Combine(outputDirectory, "decision-log.json");
        File.WriteAllText(decisionLogPath, "{}");
        var opportunitiesPath = Path.Combine(outputDirectory, "opportunities.json");
        File.WriteAllText(opportunitiesPath, "{}");
        var validationsPath = Path.Combine(outputDirectory, "validations.json");
        File.WriteAllText(validationsPath, "{}");
        var dynamicInsertDirectory = Path.Combine(outputDirectory, "DynamicData", "ModuleA");
        Directory.CreateDirectory(dynamicInsertDirectory);
        var dynamicInsertPath = Path.Combine(dynamicInsertDirectory, "Entity.dynamic.sql");
        File.WriteAllText(dynamicInsertPath, "-- dynamic insert");

        var pipelineResult = new BuildSsdtPipelineResult(
            ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json")),
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            decisionLogPath,
            opportunitiesPath,
            validationsPath,
            safeScriptPath,
            "PRINT 'safe';",
            remediationScriptPath,
            "PRINT 'remediation';",
            Path.Combine(outputDirectory, "OutSystemsModel.sqlproj"),
            staticSeedPaths,
            ImmutableArray.Create(dynamicInsertPath),
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            ImmutableArray<string>.Empty,
            null);

        return new BuildSsdtApplicationResult(
            pipelineResult,
            "fixture",
            profilePath,
            outputDirectory,
            modelPath,
            true,
            ImmutableArray<string>.Empty);
    }

    private static OutsystemsMetadataSnapshot CreateMetadataSnapshot(string databaseName)
    {
        return new OutsystemsMetadataSnapshot(
            Modules: Array.Empty<OutsystemsModuleRow>(),
            Entities: Array.Empty<OutsystemsEntityRow>(),
            Attributes: Array.Empty<OutsystemsAttributeRow>(),
            References: Array.Empty<OutsystemsReferenceRow>(),
            PhysicalTables: Array.Empty<OutsystemsPhysicalTableRow>(),
            ColumnReality: Array.Empty<OutsystemsColumnRealityRow>(),
            ColumnChecks: Array.Empty<OutsystemsColumnCheckRow>(),
            ColumnCheckJson: Array.Empty<OutsystemsColumnCheckJsonRow>(),
            PhysicalColumnsPresent: Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Indexes: Array.Empty<OutsystemsIndexRow>(),
            IndexColumns: Array.Empty<OutsystemsIndexColumnRow>(),
            ForeignKeys: Array.Empty<OutsystemsForeignKeyRow>(),
            ForeignKeyColumns: Array.Empty<OutsystemsForeignKeyColumnRow>(),
            ForeignKeyAttributeMap: Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            AttributeForeignKeys: Array.Empty<OutsystemsAttributeHasFkRow>(),
            ForeignKeyColumnsJson: Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            ForeignKeyAttributeJson: Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Triggers: Array.Empty<OutsystemsTriggerRow>(),
            AttributeJson: Array.Empty<OutsystemsAttributeJsonRow>(),
            RelationshipJson: Array.Empty<OutsystemsRelationshipJsonRow>(),
            IndexJson: Array.Empty<OutsystemsIndexJsonRow>(),
            TriggerJson: Array.Empty<OutsystemsTriggerJsonRow>(),
            ModuleJson: Array.Empty<OutsystemsModuleJsonRow>(),
            DatabaseName: databaseName);
    }
}

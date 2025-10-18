using Osm.Domain.Configuration;
using Osm.Emission.Seeds;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.StaticData;
using Osm.Pipeline.SqlExtraction;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class StaticDataProviderFactoryTests
{
    private static readonly ResolvedSqlOptions DefaultSqlOptions = new(
        ConnectionString: null,
        CommandTimeoutSeconds: null,
        Sampling: new SqlSamplingSettings(null, null),
        Authentication: new SqlAuthenticationSettings(null, null, null, null),
        MetadataContract: MetadataContractOverrides.Strict);

    [Fact]
    public void Create_FailsWhenStaticDataRequiredButUnavailable()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);
        var seeds = StaticSeedOptions.Create(groupByModule: true, StaticSeedSynchronizationMode.Authoritative).Value;
        var emission = EmissionOptions.Create(
            TighteningOptions.Default.Emission.PerTableFiles,
            TighteningOptions.Default.Emission.IncludePlatformAutoIndexes,
            TighteningOptions.Default.Emission.SanitizeModuleNames,
            TighteningOptions.Default.Emission.EmitBareTableOnly,
            TighteningOptions.Default.Emission.EmitTableHeaders,
            TighteningOptions.Default.Emission.ModuleParallelism,
            TighteningOptions.Default.Emission.NamingOverrides,
            seeds).Value;
        var options = TighteningOptions.Create(
            TighteningOptions.Default.Policy,
            TighteningOptions.Default.ForeignKeys,
            TighteningOptions.Default.Uniqueness,
            TighteningOptions.Default.Remediation,
            emission,
            TighteningOptions.Default.Mocking).Value;
        var factory = new StaticDataProviderFactory();

        var result = factory.Create(overrides, DefaultSqlOptions, options);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, static error => error.Code == "pipeline.buildSsdt.staticData.missingSource");
    }

    [Fact]
    public void Create_UsesFixtureProviderWhenPathProvided()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: "fixtures",
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);
        var factory = new StaticDataProviderFactory();

        var result = factory.Create(overrides, DefaultSqlOptions, TighteningOptions.Default);

        Assert.True(result.IsSuccess);
        Assert.IsType<FixtureStaticEntityDataProvider>(result.Value);
    }

    [Fact]
    public void Create_UsesSqlProviderWhenConnectionStringAvailable()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: null,
            ProfilePath: null,
            OutputDirectory: null,
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);
        var sqlOptions = DefaultSqlOptions with { ConnectionString = "Server=.;Database=Osm;" };
        var factory = new StaticDataProviderFactory();

        var result = factory.Create(overrides, sqlOptions, TighteningOptions.Default);

        Assert.True(result.IsSuccess);
        Assert.IsType<SqlStaticEntityDataProvider>(result.Value);
    }
}

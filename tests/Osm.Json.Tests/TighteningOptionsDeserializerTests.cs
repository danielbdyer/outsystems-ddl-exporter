using System.IO;
using Osm.Domain.Configuration;
using Osm.Json.Configuration;
using Tests.Support;

namespace Osm.Json.Tests;

public class TighteningOptionsDeserializerTests
{
    private readonly TighteningOptionsDeserializer _deserializer = new();

    [Fact]
    public void Deserialize_Should_Load_Default_Configuration()
    {
        var path = Path.Combine(FixtureFile.RepositoryRoot, "config", "default-tightening.json");
        using var stream = File.OpenRead(path);

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess);
        Assert.Equal(TighteningOptions.Default, result.Value);
    }

    [Fact]
    public void Deserialize_Should_Fail_For_Invalid_Mode()
    {
        const string json = "{ \"policy\": { \"mode\": \"Invalid\", \"nullBudget\": 0.0 }, \"foreignKeys\": { \"enableCreation\": true, \"allowCrossSchema\": false, \"allowCrossCatalog\": false }, \"uniqueness\": { \"enforceSingleColumnUnique\": true, \"enforceMultiColumnUnique\": true }, \"remediation\": { \"generatePreScripts\": true, \"sentinels\": { \"numeric\": \"0\", \"text\": \"\", \"date\": \"1900-01-01\" }, \"maxRowsDefaultBackfill\": 10 }, \"emission\": { \"perTableFiles\": true, \"includePlatformAutoIndexes\": false, \"sanitizeModuleNames\": true, \"emitBareTableOnly\": false }, \"mocking\": { \"useProfileMockFolder\": false, \"profileMockFolder\": null } }"; 
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "config.policy.mode.invalid");
    }

    [Fact]
    public void Deserialize_Should_Fail_For_Invalid_NullBudget()
    {
        const string json = "{ \"policy\": { \"mode\": \"Cautious\", \"nullBudget\": 1.5 }, \"foreignKeys\": { \"enableCreation\": true, \"allowCrossSchema\": false, \"allowCrossCatalog\": false }, \"uniqueness\": { \"enforceSingleColumnUnique\": true, \"enforceMultiColumnUnique\": true }, \"remediation\": { \"generatePreScripts\": true, \"sentinels\": { \"numeric\": \"0\", \"text\": \"\", \"date\": \"1900-01-01\" }, \"maxRowsDefaultBackfill\": 10 }, \"emission\": { \"perTableFiles\": true, \"includePlatformAutoIndexes\": false, \"sanitizeModuleNames\": true, \"emitBareTableOnly\": false }, \"mocking\": { \"useProfileMockFolder\": false, \"profileMockFolder\": null } }"; 
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "options.policy.nullBudget.outOfRange");
    }

    [Fact]
    public void Deserialize_Should_Parse_Entity_Naming_Overrides()
    {
        const string json = "{ \"policy\": { \"mode\": \"EvidenceGated\", \"nullBudget\": 0.0 }, \"foreignKeys\": { \"enableCreation\": true, \"allowCrossSchema\": false, \"allowCrossCatalog\": false }, \"uniqueness\": { \"enforceSingleColumnUnique\": true, \"enforceMultiColumnUnique\": true }, \"remediation\": { \"generatePreScripts\": true, \"sentinels\": { \"numeric\": \"0\", \"text\": \"\", \"date\": \"1900-01-01\" }, \"maxRowsDefaultBackfill\": 10 }, \"emission\": { \"perTableFiles\": true, \"includePlatformAutoIndexes\": false, \"sanitizeModuleNames\": true, \"emitBareTableOnly\": false, \"namingOverrides\": { \"rules\": [ { \"module\": null, \"entity\": \"Customer\", \"override\": \"CUSTOMER_EXTERNAL\" } ] } }, \"mocking\": { \"useProfileMockFolder\": false, \"profileMockFolder\": null } }"; 
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess);
        var namingOverrides = result.Value.Emission.NamingOverrides;
        Assert.True(namingOverrides.TryGetEntityOverride(null, "Customer", out var tableName));
        Assert.Equal("CUSTOMER_EXTERNAL", tableName.Value);
    }

    [Fact]
    public void Deserialize_Should_Honor_Legacy_Table_And_Entity_Collections()
    {
        const string json = "{ \"policy\": { \"mode\": \"EvidenceGated\", \"nullBudget\": 0.0 }, \"foreignKeys\": { \"enableCreation\": true, \"allowCrossSchema\": false, \"allowCrossCatalog\": false }, \"uniqueness\": { \"enforceSingleColumnUnique\": true, \"enforceMultiColumnUnique\": true }, \"remediation\": { \"generatePreScripts\": true, \"sentinels\": { \"numeric\": \"0\", \"text\": \"\", \"date\": \"1900-01-01\" }, \"maxRowsDefaultBackfill\": 10 }, \"emission\": { \"perTableFiles\": true, \"includePlatformAutoIndexes\": false, \"sanitizeModuleNames\": true, \"emitBareTableOnly\": false, \"namingOverrides\": { \"tables\": [ { \"schema\": \"dbo\", \"table\": \"OSUSR_ABC_CUSTOMER\", \"override\": \"CUSTOMER_PORTAL\" } ], \"entities\": [ { \"module\": \"Sales\", \"entity\": \"Customer\", \"override\": \"CUSTOMER_EXTERNAL\" } ] } }, \"mocking\": { \"useProfileMockFolder\": false, \"profileMockFolder\": null } }"; 
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess);
        var namingOverrides = result.Value.Emission.NamingOverrides;
        Assert.True(namingOverrides.TryGetTableOverride("dbo", "OSUSR_ABC_CUSTOMER", out var physical));
        Assert.Equal("CUSTOMER_PORTAL", physical);
        Assert.True(namingOverrides.TryGetEntityOverride("Sales", "Customer", out var logical));
        Assert.Equal("CUSTOMER_EXTERNAL", logical.Value);
    }
}

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
        const string json = "{ \"policy\": { \"mode\": \"Invalid\", \"nullBudget\": 0.0 }, \"foreignKeys\": { \"enableCreation\": true, \"allowCrossSchema\": false, \"allowCrossCatalog\": false }, \"uniqueness\": { \"enforceSingleColumnUnique\": true, \"enforceMultiColumnUnique\": true }, \"remediation\": { \"generatePreScripts\": true, \"sentinels\": { \"numeric\": \"0\", \"text\": \"\", \"date\": \"1900-01-01\" }, \"maxRowsDefaultBackfill\": 10 }, \"emission\": { \"perTableFiles\": true, \"includePlatformAutoIndexes\": false, \"sanitizeModuleNames\": true, \"emitConcatenatedConstraints\": false }, \"mocking\": { \"useProfileMockFolder\": false, \"profileMockFolder\": null } }";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "config.policy.mode.invalid");
    }

    [Fact]
    public void Deserialize_Should_Fail_For_Invalid_NullBudget()
    {
        const string json = "{ \"policy\": { \"mode\": \"Cautious\", \"nullBudget\": 1.5 }, \"foreignKeys\": { \"enableCreation\": true, \"allowCrossSchema\": false, \"allowCrossCatalog\": false }, \"uniqueness\": { \"enforceSingleColumnUnique\": true, \"enforceMultiColumnUnique\": true }, \"remediation\": { \"generatePreScripts\": true, \"sentinels\": { \"numeric\": \"0\", \"text\": \"\", \"date\": \"1900-01-01\" }, \"maxRowsDefaultBackfill\": 10 }, \"emission\": { \"perTableFiles\": true, \"includePlatformAutoIndexes\": false, \"sanitizeModuleNames\": true, \"emitConcatenatedConstraints\": false }, \"mocking\": { \"useProfileMockFolder\": false, \"profileMockFolder\": null } }";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "options.policy.nullBudget.outOfRange");
    }
}

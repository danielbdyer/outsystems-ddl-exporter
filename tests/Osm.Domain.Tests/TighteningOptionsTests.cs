using Osm.Domain.Configuration;

namespace Osm.Domain.Tests;

public class TighteningOptionsTests
{
    [Fact]
    public void PolicyOptions_Should_Reject_OutOfRange_NullBudget()
    {
        var result = PolicyOptions.Create(TighteningMode.Cautious, -0.1);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "options.policy.nullBudget.outOfRange");
    }

    [Fact]
    public void RemediationOptions_Should_Require_Sentinels()
    {
        var result = RemediationOptions.Create(generatePreScripts: true, sentinels: null!, maxRowsDefaultBackfill: 1);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "options.remediation.sentinels.null");
    }

    [Fact]
    public void MockingOptions_Should_Require_Folder_When_Enabled()
    {
        var result = MockingOptions.Create(useProfileMockFolder: true, profileMockFolder: " ");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "options.mocking.folder.required");
    }

    [Fact]
    public void TighteningOptions_Create_Should_Succeed_With_Valid_Children()
    {
        var options = TighteningOptions.Create(
            PolicyOptions.Create(TighteningMode.Aggressive, 0.5).Value,
            ForeignKeyOptions.Create(true, true, false).Value,
            UniquenessOptions.Create(true, false).Value,
            RemediationOptions.Create(true, RemediationSentinelOptions.Create("0", "", "1900-01-01").Value, 10).Value,
            EmissionOptions.Create(true, true, false, true).Value,
            MockingOptions.Create(false, null).Value);

        Assert.True(options.IsSuccess);
        Assert.Equal(TighteningMode.Aggressive, options.Value.Policy.Mode);
        Assert.Equal("0", options.Value.Remediation.Sentinels.Numeric);
    }

    [Fact]
    public void TableNamingOverride_Should_Default_Schema_When_Omitted()
    {
        var result = TableNamingOverride.Create(null, "OSUSR_ABC_CUSTOMER", "CUSTOMER_PORTAL");

        Assert.True(result.IsSuccess);
        Assert.Equal("dbo", result.Value.Schema.Value);
        Assert.Equal("OSUSR_ABC_CUSTOMER", result.Value.Source.Value);
        Assert.Equal("CUSTOMER_PORTAL", result.Value.Target.Value);
    }

    [Fact]
    public void NamingOverrideOptions_Should_Fail_On_Conflicting_Duplicate()
    {
        var first = TableNamingOverride.Create("dbo", "OSUSR_ABC_CUSTOMER", "CUSTOMER_PORTAL").Value;
        var second = TableNamingOverride.Create("dbo", "OSUSR_ABC_CUSTOMER", "CUSTOMER_BACKOFFICE").Value;

        var result = NamingOverrideOptions.Create(new[] { first, second });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "namingOverride.duplicate");
    }
}

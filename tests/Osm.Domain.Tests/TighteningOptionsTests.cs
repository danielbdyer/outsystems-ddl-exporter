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
            EmissionOptions.Create(true, true, false, true, false, 1).Value,
            MockingOptions.Create(false, null).Value);

        Assert.True(options.IsSuccess);
        Assert.Equal(TighteningMode.Aggressive, options.Value.Policy.Mode);
        Assert.Equal("0", options.Value.Remediation.Sentinels.Numeric);
        Assert.False(options.Value.ForeignKeys.TreatMissingDeleteRuleAsIgnore);
    }

    [Fact]
    public void NamingOverrideRule_Should_Default_Schema_When_Only_Table()
    {
        var result = NamingOverrideRule.Create(null, "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_PORTAL");

        Assert.True(result.IsSuccess);
        Assert.Equal("dbo", result.Value.Schema?.Value);
        Assert.Equal("OSUSR_ABC_CUSTOMER", result.Value.PhysicalName?.Value);
        Assert.Equal("CUSTOMER_PORTAL", result.Value.Target.Value);
    }

    [Fact]
    public void NamingOverrideOptions_Should_Fail_On_Conflicting_Physical_Override()
    {
        var first = NamingOverrideRule.Create("dbo", "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_PORTAL").Value;
        var second = NamingOverrideRule.Create("dbo", "OSUSR_ABC_CUSTOMER", null, null, "CUSTOMER_BACKOFFICE").Value;

        var result = NamingOverrideOptions.Create(new[] { first, second });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "namingOverride.duplicate");
    }

    [Fact]
    public void NamingOverrideRule_Should_Create_Module_Scoped_Logical_Override()
    {
        var result = NamingOverrideRule.Create(null, null, "Sales", "Customer", "CUSTOMER_EXTERNAL");

        Assert.True(result.IsSuccess);
        Assert.Equal("Sales", result.Value.Module?.Value);
        Assert.Equal("Customer", result.Value.LogicalName?.Value);
        Assert.Equal("CUSTOMER_EXTERNAL", result.Value.Target.Value);
    }

    [Fact]
    public void NamingOverrideOptions_Should_Apply_Entity_Override_By_Logical_Name()
    {
        var overrideResult = NamingOverrideRule.Create(null, null, null, "Customer", "CUSTOMER_EXTERNAL");
        Assert.True(overrideResult.IsSuccess);

        var optionsResult = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(optionsResult.IsSuccess);

        var effective = optionsResult.Value.GetEffectiveTableName("dbo", "OSUSR_ABC_CUSTOMER", "Customer");
        Assert.Equal("CUSTOMER_EXTERNAL", effective);
    }

    [Fact]
    public void NamingOverrideOptions_Should_Prefer_Module_Specific_Entity_Override()
    {
        var globalOverride = NamingOverrideRule.Create(null, null, null, "Customer", "CUSTOMER_GLOBAL");
        var moduleOverride = NamingOverrideRule.Create(null, null, "Sales", "Customer", "CUSTOMER_SALES");
        Assert.True(globalOverride.IsSuccess);
        Assert.True(moduleOverride.IsSuccess);

        var optionsResult = NamingOverrideOptions.Create(new[] { globalOverride.Value, moduleOverride.Value });
        Assert.True(optionsResult.IsSuccess);

        var moduleEffective = optionsResult.Value.GetEffectiveTableName("dbo", "OSUSR_SAL_CUSTOMER", "Customer", "Sales");
        Assert.Equal("CUSTOMER_SALES", moduleEffective);

        var fallbackEffective = optionsResult.Value.GetEffectiveTableName("dbo", "OSUSR_FIN_CUSTOMER", "Customer", "Finance");
        Assert.Equal("CUSTOMER_GLOBAL", fallbackEffective);
    }

    [Fact]
    public void NamingOverrideOptions_Should_Fail_On_Conflicting_Entity_Overrides()
    {
        var first = NamingOverrideRule.Create(null, null, null, "Customer", "CUSTOMER_EXTERNAL");
        var second = NamingOverrideRule.Create(null, null, null, "Customer", "CUSTOMER_ALT");
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        var result = NamingOverrideOptions.Create(new[] { first.Value, second.Value });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "namingOverride.entity.duplicate");
    }

    [Fact]
    public void NamingOverrideOptions_Should_Apply_Physical_And_Logical_From_Single_Rule()
    {
        var rule = NamingOverrideRule.Create("dbo", "OSUSR_RTJ_CATEGORY", "Inventory", "Category", "CATEGORY_STATIC");
        Assert.True(rule.IsSuccess);

        var optionsResult = NamingOverrideOptions.Create(new[] { rule.Value });
        Assert.True(optionsResult.IsSuccess);

        var effective = optionsResult.Value.GetEffectiveTableName("dbo", "OSUSR_RTJ_CATEGORY", "Category", "Inventory");
        Assert.Equal("CATEGORY_STATIC", effective);

        var fallback = optionsResult.Value.GetEffectiveTableName("dbo", "OSUSR_RTJ_CATEGORY", null);
        Assert.Equal("CATEGORY_STATIC", fallback);
    }

    [Fact]
    public void NullabilityOverrideRule_Should_Require_Module_Entity_And_Attribute()
    {
        var result = NullabilityOverrideRule.Create(null, "Customer", "Email");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "module.name.invalid");
    }

    [Fact]
    public void NullabilityOverrideOptions_Should_Relax_Configured_Column()
    {
        var rule = NullabilityOverrideRule.Create("Sales", "Customer", "Email").Value;

        var options = NullabilityOverrideOptions.Create(new[] { rule });

        Assert.True(options.IsSuccess);
        Assert.True(options.Value.ShouldRelax("Sales", "Customer", "Email"));
        Assert.False(options.Value.ShouldRelax("Sales", "Customer", "Phone"));
    }
}

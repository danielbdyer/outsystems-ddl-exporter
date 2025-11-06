using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class EntityLookupResolverTests
{
    [Fact]
    public void Should_Report_Conflicting_Module_Overrides_When_Not_All_Duplicates_Are_Covered()
    {
        var customerA = TighteningEvaluatorTestHelper.CreateEntity(
            "Sales",
            "Customer",
            "OSUSR_SALES_CUSTOMER",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var customerB = TighteningEvaluatorTestHelper.CreateEntity(
            "Support",
            "Customer",
            "OSUSR_SUPPORT_CUSTOMER",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var customerC = TighteningEvaluatorTestHelper.CreateEntity(
            "Billing",
            "Customer",
            "OSUSR_BILLING_CUSTOMER",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var salesModule = TighteningEvaluatorTestHelper.CreateModule("Sales", customerA);
        var supportModule = TighteningEvaluatorTestHelper.CreateModule("Support", customerB);
        var billingModule = TighteningEvaluatorTestHelper.CreateModule("Billing", customerC);
        var model = TighteningEvaluatorTestHelper.CreateModel(salesModule, supportModule, billingModule);

        var overrides = Result.Collect(new[]
        {
            NamingOverrideRule.Create(null, null, "Sales", "Customer", "CUSTOMER_SALES"),
            NamingOverrideRule.Create(null, null, "Support", "Customer", "CUSTOMER_SUPPORT")
        }).Value;

        var namingOverrides = NamingOverrideOptions.Create(overrides).Value;

        var resolution = EntityLookupResolver.Resolve(model, namingOverrides);

        Assert.Single(resolution.Diagnostics);
        var diagnostic = resolution.Diagnostics[0];
        Assert.Equal("tightening.entity.duplicate.conflict", diagnostic.Code);
        Assert.Equal(TighteningDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.False(diagnostic.ResolvedByOverride);
        Assert.Equal("Customer", diagnostic.LogicalName);
        Assert.Equal("Billing", diagnostic.CanonicalModule);
    }

    [Fact]
    public void Should_Select_Override_When_Module_Provides_Single_Canonical()
    {
        var customerA = TighteningEvaluatorTestHelper.CreateEntity(
            "Sales",
            "Customer",
            "OSUSR_SALES_CUSTOMER",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var customerB = TighteningEvaluatorTestHelper.CreateEntity(
            "Support",
            "Customer",
            "OSUSR_SUPPORT_CUSTOMER",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var salesModule = TighteningEvaluatorTestHelper.CreateModule("Sales", customerA);
        var supportModule = TighteningEvaluatorTestHelper.CreateModule("Support", customerB);
        var model = TighteningEvaluatorTestHelper.CreateModel(salesModule, supportModule);

        var overrides = Result.Collect(new[]
        {
            NamingOverrideRule.Create(null, null, "Sales", "Customer", "CUSTOMER_SALES")
        }).Value;

        var namingOverrides = NamingOverrideOptions.Create(overrides).Value;

        var resolution = EntityLookupResolver.Resolve(model, namingOverrides);

        Assert.Empty(resolution.Diagnostics.Where(d => d.Code == "tightening.entity.duplicate.conflict"));
        var diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal("tightening.entity.duplicate.resolved", diagnostic.Code);
        Assert.True(diagnostic.ResolvedByOverride);
        Assert.Equal("Sales", diagnostic.CanonicalModule);
        Assert.True(resolution.Lookup.TryGetValue(new EntityName("Customer"), out var canonical));
        Assert.Equal("Sales", canonical.Module.Value);
    }

    [Fact]
    public void Should_Treat_All_Module_Overrides_As_Resolved()
    {
        var customerA = TighteningEvaluatorTestHelper.CreateEntity(
            "Sales",
            "Customer",
            "OSUSR_SALES_CUSTOMER",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var customerB = TighteningEvaluatorTestHelper.CreateEntity(
            "Support",
            "Customer",
            "OSUSR_SUPPORT_CUSTOMER",
            new[]
            {
                TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true)
            });

        var salesModule = TighteningEvaluatorTestHelper.CreateModule("Sales", customerA);
        var supportModule = TighteningEvaluatorTestHelper.CreateModule("Support", customerB);
        var model = TighteningEvaluatorTestHelper.CreateModel(salesModule, supportModule);

        var overrides = Result.Collect(new[]
        {
            NamingOverrideRule.Create(null, null, "Sales", "Customer", "CUSTOMER_SALES"),
            NamingOverrideRule.Create(null, null, "Support", "Customer", "CUSTOMER_SUPPORT")
        }).Value;

        var namingOverrides = NamingOverrideOptions.Create(overrides).Value;

        var resolution = EntityLookupResolver.Resolve(model, namingOverrides);

        var diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal("tightening.entity.duplicate.resolved", diagnostic.Code);
        Assert.Equal(TighteningDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.True(diagnostic.ResolvedByOverride);
        Assert.Equal("Customer", diagnostic.LogicalName);
        Assert.True(resolution.Lookup.TryGetValue(new EntityName("Customer"), out var canonical));
        Assert.Equal("Sales", canonical.Module.Value);
    }
}

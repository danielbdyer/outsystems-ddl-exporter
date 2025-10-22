using System;
using System.IO;
using System.Text.RegularExpressions;
using Tests.Support;
using Xunit;

namespace Osm.Json.Tests;

public static class AdvancedSqlScriptTests
{
    private static readonly Regex CaseWrappedJsonQuery = new(
        @"CASE\s+WHEN[\s\S]+?THEN\s+JSON_QUERY",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("src/AdvancedSql/outsystems_model_export.sql")]
    [InlineData("src/AdvancedSql/outsystems_metadata_rowsets.sql")]
    public static void ConditionalJsonFragments_ShouldUseOuterJsonQuery(string relativePath)
    {
        var script = ReadScript(relativePath);
        Assert.False(CaseWrappedJsonQuery.IsMatch(script),
            "Expected script to embed JSON_QUERY outside conditional branches to prevent string-escaped JSON fragments.");
    }

    [Theory]
    [InlineData("src/AdvancedSql/outsystems_model_export.sql")]
    [InlineData("src/AdvancedSql/outsystems_metadata_rowsets.sql")]
    public static void JsonAggregates_ShouldEmbedNestedFragmentsWithoutEscaping(string relativePath)
    {
        var script = ReadScript(relativePath);

        Assert.Contains("JSON_QUERY(CASE WHEN cr.AttrId IS NOT NULL OR chk.AttrId IS NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(CASE WHEN NULLIF(LTRIM(RTRIM(a.AttrDescription)), '') IS NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(CASE WHEN ai.DataSpaceName IS NOT NULL OR ai.DataSpaceType IS NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(CASE WHEN NULLIF(LTRIM(RTRIM(en.EntityDescription)), '') IS NOT NULL", script, StringComparison.Ordinal);

        Assert.Contains("JSON_QUERY(fkc.ColumnsJson)", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(faj.ConstraintJson)", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(COALESCE(aj.AttributesJson, N'[]'))", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(COALESCE(rj.RelationshipsJson, N'[]'))", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(COALESCE(ij.IndexesJson, N'[]'))", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(COALESCE(ai.DataCompressionJson, N'[]'))", script, StringComparison.Ordinal);
        Assert.Contains("JSON_QUERY(icj.ColumnsJson)", script, StringComparison.Ordinal);

        if (relativePath.EndsWith("outsystems_model_export.sql", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains("JSON_QUERY(mj.[module.entities])", script, StringComparison.Ordinal);
        }
    }

    private static string ReadScript(string relativePath)
    {
        var root = FixtureFile.RepositoryRoot;
        var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(absolute);
    }
}

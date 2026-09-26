using System;
using System.IO;
using System.Text;

public static class DocumentationFile
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        ".."));

    public static string Read(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path must be provided.", nameof(relativePath));
        }

        var path = Path.GetFullPath(Path.Combine(RepoRoot, relativePath));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Documentation file not found: {path}");
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    public static string ReadSection(string content, string heading, string? nextHeading = null)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (string.IsNullOrWhiteSpace(heading))
        {
            throw new ArgumentException("Heading must be provided.", nameof(heading));
        }

        var start = content.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(nextHeading))
        {
            return content[start..];
        }

        var end = content.IndexOf(nextHeading, start, StringComparison.OrdinalIgnoreCase);
        return end > start ? content[start..end] : content[start..];
    }
}

public class FullExportDocumentationTests
{
    [Fact]
    public void FullExportContract_spells_out_ssdt_playbook()
    {
        var doc = DocumentationFile.Read(Path.Combine("docs", "full-export-artifact-contract.md"));

        Assert.Contains("build.staticSeedRoot", doc, StringComparison.Ordinal);
        Assert.Contains("build.sqlProjectPath", doc, StringComparison.Ordinal);
        Assert.Contains("Script.PostDeployment.sql", doc, StringComparison.Ordinal);
        Assert.Contains("DynamicData (deprecated)", doc, StringComparison.Ordinal);
        Assert.Contains("tests/Fixtures/emission/edge-case", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_links_contract_from_cli_summary_and_ssdt_section()
    {
        var readme = DocumentationFile.Read("readme.md");

        var cliSegment = DocumentationFile.ReadSection(readme, "SSDT Emission Summary", "---");
        Assert.Contains("Full Export Artifact Contract", cliSegment, StringComparison.Ordinal);

        var ssdtSection = DocumentationFile.ReadSection(
            readme,
            "## 9. Per-Table DDL Emitter (SSDT-Ready Output)",
            "## 10.");
        Assert.Contains("Full Export Artifact Contract", ssdtSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_quickstart_documents_full_export_uat_users_bundle()
    {
        var readme = DocumentationFile.Read("readme.md");
        var quickstart = DocumentationFile.ReadSection(readme, "## 0. TL;DR Quickstart", "## 1.");

        Assert.Contains("--enable-uat-users", quickstart, StringComparison.Ordinal);
        Assert.Contains("00_user_map.template.csv", quickstart, StringComparison.Ordinal);
        Assert.Contains("01_preview.csv", quickstart, StringComparison.Ordinal);
        Assert.Contains("02_apply_user_remap.sql", quickstart, StringComparison.Ordinal);
        Assert.Contains("03_catalog.txt", quickstart, StringComparison.Ordinal);
    }

    [Fact]
    public void UatUsersVerbDocs_call_out_full_export_manifest_expectations()
    {
        var doc = DocumentationFile.Read(Path.Combine("docs", "verbs", "uat-users.md"));

        Assert.Contains("--enable-uat-users", doc, StringComparison.Ordinal);
        Assert.Contains("Stages[].Name == \"uat-users\"", doc, StringComparison.Ordinal);
        Assert.Contains("uat-users-preview", doc, StringComparison.Ordinal);
        Assert.Contains("uat-users-catalog", doc, StringComparison.Ordinal);
    }
}

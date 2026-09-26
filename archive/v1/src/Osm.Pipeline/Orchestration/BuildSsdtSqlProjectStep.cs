using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtSqlProjectStep : IBuildSsdtStep<EmissionReady, SqlProjectSynthesized>
{
    private const string ProjectNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
    private const string DefaultProjectName = "OutSystemsModel";

    public Task<Result<SqlProjectSynthesized>> ExecuteAsync(
        EmissionReady state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var manifest = state.Manifest ?? throw new InvalidOperationException("Manifest must be available before synthesizing the SSDT project.");
        var outputDirectory = state.Request.OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Output directory must be provided before synthesizing the SSDT project.");
        }

        var projectPath = ResolveProjectPath(state);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = DefaultProjectName;
        }

        var modules = EnumerateProjectItems(outputDirectory, Path.Combine(outputDirectory, "Modules"));
        var seeds = EnumerateProjectItems(outputDirectory, Path.Combine(outputDirectory, "Seeds"));

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            Directory.CreateDirectory(projectDirectory);
        }

        WriteProject(projectPath, projectName, modules.Relative, seeds.Relative, cancellationToken);

        state.Log.Record(
            "ssdt.sqlproj.generated",
            "Synthesized SSDT project file for emitted modules and seed scripts.",
            new PipelineLogMetadataBuilder()
                .WithPath("sqlproj", projectPath)
                .WithCount("moduleScripts", modules.Count)
                .WithCount("postDeployScripts", seeds.Count)
                .Build());

        return Task.FromResult(Result<SqlProjectSynthesized>.Success(new SqlProjectSynthesized(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Validations,
            state.Insights,
            manifest,
            state.DecisionLogPath,
            state.OpportunityArtifacts,
            projectPath)));
    }

    private static (ImmutableArray<string> Relative, int Count) EnumerateProjectItems(string root, string directory)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Root directory must be provided.", nameof(root));
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return (ImmutableArray<string>.Empty, 0);
        }

        var rootFullPath = Path.GetFullPath(root);
        var relativePaths = Directory
            .EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
            .Select(path => ConvertToProjectPath(Path.GetRelativePath(rootFullPath, Path.GetFullPath(path))))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        return (relativePaths, relativePaths.Length);
    }

    private static void WriteProject(
        string projectPath,
        string projectName,
        ImmutableArray<string> moduleItems,
        ImmutableArray<string> seedItems,
        CancellationToken cancellationToken)
    {
        var ns = XNamespace.Get(ProjectNamespace);
        var projectElement = new XElement(ns + "Project", new XAttribute("DefaultTargets", "Build"));

        projectElement.Add(CreatePropertyGroup(ns, projectName));

        projectElement.Add(CreateModuleItemGroup(ns, moduleItems));
        projectElement.Add(CreateSeedItemGroup(ns, seedItems));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), projectElement);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineHandling = NewLineHandling.Entitize
        };

        using var writer = XmlWriter.Create(projectPath, settings);
        cancellationToken.ThrowIfCancellationRequested();
        document.Save(writer);
    }

    private static XElement CreatePropertyGroup(XNamespace ns, string projectName)
    {
        return new XElement(
            ns + "PropertyGroup",
            new XElement(ns + "DSP", "Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider"),
            new XElement(ns + "ModelCollation", "1033,CI"),
            new XElement(ns + "ModelVersion", "2.0"),
            new XElement(ns + "Name", projectName),
            new XElement(ns + "RootPath", "."),
            new XElement(ns + "SqlTargetName", projectName),
            new XElement(ns + "DefaultCollation", "SQL_Latin1_General_CP1_CI_AS"),
            new XElement(ns + "TargetFrameworkVersion", "v4.7.2"),
            new XElement(ns + "ProjectVersion", "4.2"));
    }

    private static XElement CreateModuleItemGroup(XNamespace ns, ImmutableArray<string> moduleItems)
    {
        var group = new XElement(ns + "ItemGroup");

        if (!moduleItems.IsDefaultOrEmpty && moduleItems.Length > 0)
        {
            foreach (var module in moduleItems)
            {
                group.Add(new XElement(ns + "Build", new XAttribute("Include", module)));
            }
        }
        else
        {
            group.Add(new XElement(ns + "Build", new XAttribute("Include", @"Modules\**\*.sql")));
        }

        return group;
    }

    private static XElement CreateSeedItemGroup(XNamespace ns, ImmutableArray<string> seedItems)
    {
        var group = new XElement(ns + "ItemGroup");

        if (!seedItems.IsDefaultOrEmpty && seedItems.Length > 0)
        {
            foreach (var seed in seedItems)
            {
                group.Add(new XElement(ns + "PostDeploy", new XAttribute("Include", seed)));
            }
        }
        else
        {
            group.Add(new XElement(ns + "PostDeploy", new XAttribute("Include", @"Seeds\**\*.sql")));
        }

        return group;
    }

    private static string ResolveProjectPath(EmissionReady state)
    {
        var hint = state.Request.SqlProjectPathHint;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            return Path.GetFullPath(hint);
        }

        return Path.Combine(state.Request.OutputDirectory, DefaultProjectName + ".sqlproj");
    }

    private static string ConvertToProjectPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return relativePath;
        }

        var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '\\').Replace(Path.AltDirectorySeparatorChar, '\\');
        return normalized;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Tests.Support;

public sealed class EmissionOutput
{
    private EmissionOutput(
        string root,
        EmissionManifest manifest,
        IReadOnlyList<StaticSeedModule> staticSeedModules,
        IReadOnlyList<string> staticSeedMasterScripts,
        IReadOnlyList<string> tableScripts)
    {
        Root = root;
        Manifest = manifest;
        StaticSeedModules = staticSeedModules;
        StaticSeedMasterScripts = staticSeedMasterScripts;
        TableScripts = tableScripts;
    }

    public string Root { get; }

    public EmissionManifest Manifest { get; }

    public IReadOnlyList<StaticSeedModule> StaticSeedModules { get; }

    public IReadOnlyList<string> StaticSeedMasterScripts { get; }

    public IReadOnlyList<string> TableScripts { get; }

    public static EmissionOutput Load(string root)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Manifest file not found at '{manifestPath}'.", manifestPath);
        }

        using var manifestStream = File.OpenRead(manifestPath);
        var manifest = JsonSerializer.Deserialize<EmissionManifest>(manifestStream, SerializerOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Failed to deserialize manifest at '{manifestPath}'.");
        }

        var staticSeedModules = ReadStaticSeedModules(root);
        var staticSeedMasterScripts = ReadStaticSeedMasterScripts(root);
        var tableScripts = ReadTableScripts(root);

        return new EmissionOutput(root, manifest, staticSeedModules, staticSeedMasterScripts, tableScripts);
    }

    public EmissionSnapshotWorkspace CreateSnapshot(params string[] relativePaths)
        => CreateSnapshot((IEnumerable<string>)relativePaths);

    public EmissionSnapshotWorkspace CreateSnapshot(IEnumerable<string> relativePaths)
    {
        if (relativePaths is null)
        {
            throw new ArgumentNullException(nameof(relativePaths));
        }

        var temp = new TempDirectory();

        try
        {
            foreach (var relativePath in relativePaths)
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var normalized = NormalizePath(relativePath);
                var source = Path.Combine(Root, normalized);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException($"Could not locate '{normalized}' in emission output.", source);
                }

                var destination = Path.Combine(temp.Path, normalized);
                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(source, destination, overwrite: true);
            }

            return new EmissionSnapshotWorkspace(temp);
        }
        catch
        {
            temp.Dispose();
            throw;
        }
    }

    public string GetAbsolutePath(string relativePath)
    {
        if (relativePath is null)
        {
            throw new ArgumentNullException(nameof(relativePath));
        }

        return Path.Combine(Root, NormalizePath(relativePath));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    private static IReadOnlyList<StaticSeedModule> ReadStaticSeedModules(string root)
    {
        var seedsRoot = Path.Combine(root, "Seeds");
        if (!Directory.Exists(seedsRoot))
        {
            return Array.Empty<StaticSeedModule>();
        }

        var modules = new List<StaticSeedModule>();
        foreach (var moduleDirectory in Directory.EnumerateDirectories(seedsRoot))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            var files = Directory.EnumerateFiles(moduleDirectory, "*.sql", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            modules.Add(new StaticSeedModule(moduleName, files));
        }

        modules.Sort((left, right) => string.Compare(left.Module, right.Module, StringComparison.OrdinalIgnoreCase));
        return modules.ToImmutableArray();
    }

    private static IReadOnlyList<string> ReadStaticSeedMasterScripts(string root)
    {
        var seedsRoot = Path.Combine(root, "Seeds");
        if (!Directory.Exists(seedsRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(seedsRoot, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static IReadOnlyList<string> ReadTableScripts(string root)
    {
        var modulesRoot = Path.Combine(root, "Modules");
        if (!Directory.Exists(modulesRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(modulesRoot, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record EmissionSnapshotWorkspace : IDisposable
{
    private readonly TempDirectory _directory;

    public EmissionSnapshotWorkspace(TempDirectory directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public string Path => _directory.Path;

    public void Dispose() => _directory.Dispose();
}

public sealed record EmissionManifest(
    EmissionManifestOptions Options,
    EmissionManifestPolicySummary PolicySummary,
    EmissionManifestCoverage Coverage,
    EmissionManifestPredicateCoverage PredicateCoverage,
    IReadOnlyList<EmissionManifestTable> Tables,
    IReadOnlyList<string> Unsupported,
    IReadOnlyList<EmissionManifestPreRemediation> PreRemediation);

public sealed record EmissionManifestOptions(
    bool IncludePlatformAutoIndexes,
    bool EmitBareTableOnly,
    bool SanitizeModuleNames,
    int ModuleParallelism);

public sealed record EmissionManifestPolicySummary(
    int ColumnCount,
    int TightenedColumnCount,
    int RemediationColumnCount,
    int UniqueIndexCount,
    int UniqueIndexesEnforcedCount,
    int UniqueIndexesRequireRemediationCount,
    int ForeignKeyCount,
    int ForeignKeysCreatedCount,
    IReadOnlyDictionary<string, int> ColumnRationales,
    IReadOnlyDictionary<string, int> UniqueIndexRationales,
    IReadOnlyDictionary<string, int> ForeignKeyRationales);

public sealed record EmissionManifestCoverage(
    EmissionManifestCoverageSection Tables,
    EmissionManifestCoverageSection Columns,
    EmissionManifestCoverageSection Constraints);

public sealed record EmissionManifestCoverageSection(int Emitted, int Total, double Percentage);

public sealed record EmissionManifestPredicateCoverage(
    IReadOnlyList<EmissionPredicateCoverageEntry> Tables,
    IReadOnlyDictionary<string, int> PredicateCounts);

public sealed record EmissionPredicateCoverageEntry(
    string Module,
    string Schema,
    string Table,
    IReadOnlyList<string> Predicates);

public sealed record EmissionManifestTable(
    string Module,
    string Schema,
    string Table,
    string TableFile,
    IReadOnlyList<string> Indexes,
    IReadOnlyList<string> ForeignKeys,
    bool IncludesExtendedProperties);

public sealed record EmissionManifestPreRemediation(
    string Schema,
    string Table,
    string Column,
    string Issue,
    string Description);

public sealed record StaticSeedModule(string Module, IReadOnlyList<string> SeedFiles);


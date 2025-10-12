using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Osm.Smo;

namespace Osm.Dmm;

public sealed class SsdtTableLayoutComparator
{
    public SsdtTableLayoutComparisonResult Compare(SmoModel model, SmoBuildOptions options, string ssdtRoot)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(ssdtRoot))
        {
            throw new ArgumentException("SSDT project root must be provided.", nameof(ssdtRoot));
        }

        var expectedFiles = BuildExpectedFileMap(model, options);
        var actualFiles = BuildActualFileMap(ssdtRoot, out var unrecognizedFiles);

        var modelDifferences = new List<DmmDifference>();
        var ssdtDifferences = new List<DmmDifference>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in expectedFiles)
        {
            if (!actualFiles.TryGetValue(entry.Key, out var candidates) || candidates.Count == 0)
            {
                var coordinates = SplitKey(entry.Key);
                modelDifferences.Add(Difference.Table(coordinates.Schema, coordinates.Table, "FilePresence", entry.Value, null, entry.Value));
                continue;
            }

            matched.Add(entry.Key);

            if (!candidates.Any(path => string.Equals(path, entry.Value, StringComparison.OrdinalIgnoreCase)))
            {
                var coordinates = SplitKey(entry.Key);
                ssdtDifferences.Add(Difference.Table(
                    coordinates.Schema,
                    coordinates.Table,
                    "FileLocation",
                    entry.Value,
                    string.Join(", ", candidates),
                    candidates.FirstOrDefault()));
            }

            if (candidates.Count > 1)
            {
                var coordinates = SplitKey(entry.Key);
                ssdtDifferences.Add(Difference.Table(
                    coordinates.Schema,
                    coordinates.Table,
                    "DuplicateFiles",
                    entry.Value,
                    string.Join(", ", candidates),
                    candidates.FirstOrDefault()));
            }
        }

        foreach (var entry in actualFiles)
        {
            if (matched.Contains(entry.Key))
            {
                continue;
            }

            foreach (var path in entry.Value)
            {
                var coordinates = SplitKey(entry.Key);
                ssdtDifferences.Add(Difference.Table(coordinates.Schema, coordinates.Table, "FilePresence", null, path, path));
            }
        }

        foreach (var path in unrecognizedFiles)
        {
            ssdtDifferences.Add(Difference.Table(string.Empty, string.Empty, "UnrecognizedFile", null, path, path));
        }

        var isMatch = modelDifferences.Count == 0 && ssdtDifferences.Count == 0;
        return new SsdtTableLayoutComparisonResult(isMatch, modelDifferences, ssdtDifferences);
    }

    private static Dictionary<string, string> BuildExpectedFileMap(SmoModel model, SmoBuildOptions options)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (model.Tables.IsDefaultOrEmpty)
        {
            return map;
        }

        foreach (var table in model.Tables)
        {
            var effectiveName = options.NamingOverrides.GetEffectiveTableName(
                table.Schema,
                table.Name,
                table.LogicalName,
                table.OriginalModule);
            var key = Key(table.Schema, effectiveName);
            var module = table.Module ?? string.Empty;
            var fileName = $"{table.Schema}.{effectiveName}.sql";
            var relativePath = NormalizeRelativePath(Path.Combine("Modules", module, "Tables", fileName));
            map[key] = relativePath;
        }

        return map;
    }

    private static Dictionary<string, List<string>> BuildActualFileMap(string ssdtRoot, out List<string> unrecognizedFiles)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        unrecognizedFiles = new List<string>();

        var modulesRoot = Path.Combine(ssdtRoot, "Modules");
        if (!Directory.Exists(modulesRoot))
        {
            return map;
        }

        foreach (var file in Directory.EnumerateFiles(modulesRoot, "*.sql", SearchOption.AllDirectories))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(ssdtRoot, file));
            if (!IsTableFile(relativePath))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(file);
            var separator = fileName.IndexOf('.');
            if (separator <= 0 || separator >= fileName.Length - 1)
            {
                unrecognizedFiles.Add(relativePath);
                continue;
            }

            var schema = fileName[..separator];
            var table = fileName[(separator + 1)..];
            var key = Key(schema, table);

            if (!map.TryGetValue(key, out var list))
            {
                list = new List<string>();
                map[key] = list;
            }

            list.Add(relativePath);
        }

        return map;
    }

    private static bool IsTableFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
        {
            return false;
        }

        if (!string.Equals(segments[0], "Modules", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(segments[^2], "Tables", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static (string Schema, string Table) SplitKey(string key)
    {
        var parts = key.Split('.', 2);
        return (parts.Length > 0 ? parts[0] : string.Empty, parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static string Key(string schema, string table) => $"{schema}.{table}";

    private static class Difference
    {
        public static DmmDifference Table(string schema, string table, string property, string? expected, string? actual, string? artifact)
            => new(schema, table, property, Expected: string.IsNullOrWhiteSpace(expected) ? null : expected, Actual: string.IsNullOrWhiteSpace(actual) ? null : actual, ArtifactPath: artifact);
    }
}

public sealed record SsdtTableLayoutComparisonResult(
    bool IsMatch,
    IReadOnlyList<DmmDifference> ModelDifferences,
    IReadOnlyList<DmmDifference> SsdtDifferences);

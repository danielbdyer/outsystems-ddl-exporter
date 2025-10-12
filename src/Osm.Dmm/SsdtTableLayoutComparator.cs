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

        var modelDifferences = new List<string>();
        var ssdtDifferences = new List<string>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in expectedFiles)
        {
            if (!actualFiles.TryGetValue(entry.Key, out var candidates) || candidates.Count == 0)
            {
                modelDifferences.Add($"missing table file {entry.Value}");
                continue;
            }

            matched.Add(entry.Key);

            if (!candidates.Any(path => string.Equals(path, entry.Value, StringComparison.OrdinalIgnoreCase)))
            {
                ssdtDifferences.Add($"table file mismatch for {entry.Key}: expected {entry.Value}, actual {string.Join(", ", candidates)}");
            }

            if (candidates.Count > 1)
            {
                ssdtDifferences.Add($"duplicate table files for {entry.Key}: {string.Join(", ", candidates)}");
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
                ssdtDifferences.Add($"unexpected table file {path}");
            }
        }

        foreach (var path in unrecognizedFiles)
        {
            ssdtDifferences.Add($"unrecognized table file {path}");
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

    private static string Key(string schema, string table) => $"{schema}.{table}";
}

public sealed record SsdtTableLayoutComparisonResult(
    bool IsMatch,
    IReadOnlyList<string> ModelDifferences,
    IReadOnlyList<string> SsdtDifferences);

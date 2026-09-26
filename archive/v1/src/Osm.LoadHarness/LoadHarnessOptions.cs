using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.LoadHarness;

public sealed record LoadHarnessOptions(
    string ConnectionString,
    string? SafeScriptPath,
    string? RemediationScriptPath,
    ImmutableArray<string> StaticSeedScriptPaths,
    ImmutableArray<string> DynamicInsertScriptPaths,
    string ReportOutputPath,
    int? CommandTimeoutSeconds)
{
    public static LoadHarnessOptions Create(
        string connectionString,
        string? safeScriptPath,
        string? remediationScriptPath,
        IEnumerable<string>? staticSeedScriptPaths,
        IEnumerable<string>? dynamicInsertScriptPaths,
        string? reportOutputPath,
        int? commandTimeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required to run the load harness.", nameof(connectionString));
        }

        var seeds = staticSeedScriptPaths is null
            ? ImmutableArray<string>.Empty
            : ImmutableArray.CreateRange(staticSeedScriptPaths);

        var dynamic = dynamicInsertScriptPaths is null
            ? ImmutableArray<string>.Empty
            : ImmutableArray.CreateRange(dynamicInsertScriptPaths);

        var reportPath = string.IsNullOrWhiteSpace(reportOutputPath)
            ? "load-harness.report.json"
            : reportOutputPath;

        return new LoadHarnessOptions(
            connectionString,
            NormalizePath(safeScriptPath),
            NormalizePath(remediationScriptPath),
            NormalizePaths(seeds),
            NormalizePaths(dynamic),
            reportPath,
            commandTimeoutSeconds);
    }

    private static string? NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path;

    private static ImmutableArray<string> NormalizePaths(ImmutableArray<string> paths)
    {
        if (paths.IsDefaultOrEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(paths.Length);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            builder.Add(path);
        }

        return builder.ToImmutable();
    }
}

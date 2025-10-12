using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Smo;

namespace Osm.Pipeline.Application;

internal static class CacheMetadataBuilder
{
    public static IReadOnlyDictionary<string, string?> Build(
        TighteningOptions options,
        ModuleFilterOptions moduleFilter,
        CliConfiguration configuration,
        string? resolvedModelPath,
        string? resolvedProfilePath,
        string? resolvedDmmPath)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["policy.mode"] = options.Policy.Mode.ToString(),
            ["policy.nullBudget"] = options.Policy.NullBudget.ToString(CultureInfo.InvariantCulture),
            ["foreignKeys.enableCreation"] = options.ForeignKeys.EnableCreation.ToString(),
            ["foreignKeys.allowCrossSchema"] = options.ForeignKeys.AllowCrossSchema.ToString(),
            ["foreignKeys.allowCrossCatalog"] = options.ForeignKeys.AllowCrossCatalog.ToString(),
            ["uniqueness.singleColumn"] = options.Uniqueness.EnforceSingleColumnUnique.ToString(),
            ["uniqueness.multiColumn"] = options.Uniqueness.EnforceMultiColumnUnique.ToString(),
            ["remediation.generatePreScripts"] = options.Remediation.GeneratePreScripts.ToString(),
            ["remediation.maxRowsDefaultBackfill"] = options.Remediation.MaxRowsDefaultBackfill.ToString(CultureInfo.InvariantCulture),
            ["emission.perTableFiles"] = options.Emission.PerTableFiles.ToString(),
            ["emission.includePlatformAutoIndexes"] = options.Emission.IncludePlatformAutoIndexes.ToString(),
            ["emission.sanitizeModuleNames"] = options.Emission.SanitizeModuleNames.ToString(),
            ["emission.bareTableOnly"] = options.Emission.EmitBareTableOnly.ToString(),
            ["mocking.useProfileMockFolder"] = options.Mocking.UseProfileMockFolder.ToString(),
        };

        metadata["moduleFilter.includeSystemModules"] = moduleFilter.IncludeSystemModules.ToString();
        metadata["moduleFilter.includeInactiveModules"] = moduleFilter.IncludeInactiveModules.ToString();

        if (!moduleFilter.Modules.IsDefaultOrEmpty)
        {
            metadata["moduleFilter.modules"] = string.Join(",", moduleFilter.Modules.Select(static module => module.Value));
        }

        if (!string.IsNullOrWhiteSpace(options.Mocking.ProfileMockFolder))
        {
            metadata["mocking.profileMockFolder"] = Path.GetFullPath(options.Mocking.ProfileMockFolder);
        }

        if (!string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            metadata["inputs.model"] = Path.GetFullPath(resolvedModelPath);
        }
        else if (!string.IsNullOrWhiteSpace(configuration.ModelPath))
        {
            metadata["inputs.model"] = Path.GetFullPath(configuration.ModelPath);
        }

        if (!string.IsNullOrWhiteSpace(resolvedProfilePath))
        {
            metadata["inputs.profile"] = Path.GetFullPath(resolvedProfilePath);
        }
        else if (!string.IsNullOrWhiteSpace(configuration.ProfilePath))
        {
            metadata["inputs.profile"] = Path.GetFullPath(configuration.ProfilePath);
        }

        if (!string.IsNullOrWhiteSpace(resolvedDmmPath))
        {
            metadata["inputs.dmm"] = Path.GetFullPath(resolvedDmmPath);
        }
        else if (!string.IsNullOrWhiteSpace(configuration.DmmPath))
        {
            metadata["inputs.dmm"] = Path.GetFullPath(configuration.DmmPath);
        }

        if (!string.IsNullOrWhiteSpace(configuration.Cache.Root))
        {
            metadata["cache.root"] = Path.GetFullPath(configuration.Cache.Root);
        }

        if (configuration.Cache.Refresh.HasValue)
        {
            metadata["cache.refreshRequested"] = configuration.Cache.Refresh.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(configuration.Profiler.Provider))
        {
            metadata["profiler.provider"] = configuration.Profiler.Provider;
        }

        if (!string.IsNullOrWhiteSpace(configuration.Profiler.MockFolder))
        {
            metadata["profiler.mockFolder"] = Path.GetFullPath(configuration.Profiler.MockFolder);
        }

        if (!string.IsNullOrWhiteSpace(configuration.Sql.ConnectionString))
        {
            metadata["sql.connectionHash"] = ComputeSha256(configuration.Sql.ConnectionString);
        }

        if (configuration.Sql.CommandTimeoutSeconds.HasValue)
        {
            metadata["sql.commandTimeoutSeconds"] = configuration.Sql.CommandTimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (configuration.Sql.Sampling.RowSamplingThreshold.HasValue)
        {
            metadata["sql.sampling.rowThreshold"] = configuration.Sql.Sampling.RowSamplingThreshold.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (configuration.Sql.Sampling.SampleSize.HasValue)
        {
            metadata["sql.sampling.sampleSize"] = configuration.Sql.Sampling.SampleSize.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (configuration.Sql.Authentication.Method.HasValue)
        {
            metadata["sql.authentication.method"] = configuration.Sql.Authentication.Method.Value.ToString();
        }

        if (configuration.Sql.Authentication.TrustServerCertificate.HasValue)
        {
            metadata["sql.authentication.trustServerCertificate"] = configuration.Sql.Authentication.TrustServerCertificate.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(configuration.Sql.Authentication.ApplicationName))
        {
            metadata["sql.authentication.applicationName"] = configuration.Sql.Authentication.ApplicationName;
        }

        if (!string.IsNullOrWhiteSpace(configuration.Sql.Authentication.AccessToken))
        {
            metadata["sql.authentication.accessTokenHash"] = ComputeSha256(configuration.Sql.Authentication.AccessToken);
        }

        if (!string.IsNullOrWhiteSpace(configuration.TypeMapping.Path))
        {
            var resolvedPath = Path.GetFullPath(configuration.TypeMapping.Path);
            metadata["typeMapping.path"] = resolvedPath;
            metadata["typeMapping.hash"] = ComputeFileHash(resolvedPath);
        }

        if (configuration.TypeMapping.Default is { } defaultMapping)
        {
            metadata["typeMapping.default"] = defaultMapping.ToMetadataString();
        }

        if (configuration.TypeMapping.Overrides is { Count: > 0 } overrides)
        {
            metadata["typeMapping.overridesHash"] = ComputeSha256(BuildOverrideSignature(overrides));
        }

        return metadata;
    }

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string BuildOverrideSignature(IReadOnlyDictionary<string, TypeMappingRuleDefinition> overrides)
    {
        if (overrides.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var entry in overrides.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value.ToMetadataString());
            builder.Append(';');
        }

        return builder.ToString();
    }
}

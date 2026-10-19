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

internal sealed class CacheMetadataBuilder
{
    private readonly IPathCanonicalizer _pathCanonicalizer;

    public CacheMetadataBuilder(IPathCanonicalizer pathCanonicalizer)
    {
        _pathCanonicalizer = pathCanonicalizer ?? throw new ArgumentNullException(nameof(pathCanonicalizer));
    }

    public IReadOnlyDictionary<string, string?> Build(
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
            ["foreignKeys.treatMissingDeleteRuleAsIgnore"] = options.ForeignKeys.TreatMissingDeleteRuleAsIgnore.ToString(),
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
            var normalizedModules = moduleFilter.Modules
                .Select(static module => module.Value)
                .OrderBy(static module => module, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            metadata["moduleFilter.modules"] = string.Join(",", normalizedModules);
            metadata["moduleFilter.moduleCount"] = normalizedModules.Length.ToString(CultureInfo.InvariantCulture);
            metadata["moduleFilter.modulesHash"] = ComputeSha256(string.Join(";", normalizedModules));
            metadata["moduleFilter.selectionScope"] = "filtered";
        }
        else
        {
            metadata["moduleFilter.moduleCount"] = "0";
            metadata["moduleFilter.modulesHash"] = ComputeSha256("::all-modules::");
            metadata["moduleFilter.selectionScope"] = "all";
        }

        if (!string.IsNullOrWhiteSpace(options.Mocking.ProfileMockFolder))
        {
            metadata["mocking.profileMockFolder"] = CanonicalizeFullPath(options.Mocking.ProfileMockFolder);
        }

        if (!string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            metadata["inputs.model"] = CanonicalizeFullPath(resolvedModelPath);
        }
        else if (!string.IsNullOrWhiteSpace(configuration.ModelPath))
        {
            metadata["inputs.model"] = CanonicalizeFullPath(configuration.ModelPath);
        }

        if (!string.IsNullOrWhiteSpace(resolvedProfilePath))
        {
            metadata["inputs.profile"] = CanonicalizeFullPath(resolvedProfilePath);
        }
        else if (!string.IsNullOrWhiteSpace(configuration.ProfilePath))
        {
            metadata["inputs.profile"] = CanonicalizeFullPath(configuration.ProfilePath);
        }

        if (!string.IsNullOrWhiteSpace(resolvedDmmPath))
        {
            metadata["inputs.dmm"] = CanonicalizeFullPath(resolvedDmmPath);
        }
        else if (!string.IsNullOrWhiteSpace(configuration.DmmPath))
        {
            metadata["inputs.dmm"] = CanonicalizeFullPath(configuration.DmmPath);
        }

        if (!string.IsNullOrWhiteSpace(configuration.Cache.Root))
        {
            metadata["cache.root"] = CanonicalizeFullPath(configuration.Cache.Root);
        }

        if (configuration.Cache.Refresh.HasValue)
        {
            metadata["cache.refreshRequested"] = configuration.Cache.Refresh.Value.ToString();
        }

        if (configuration.Cache.TimeToLiveSeconds.HasValue)
        {
            metadata["cache.ttlSeconds"] = configuration.Cache.TimeToLiveSeconds.Value
                .ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(configuration.Profiler.Provider))
        {
            metadata["profiler.provider"] = configuration.Profiler.Provider;
        }

        if (!string.IsNullOrWhiteSpace(configuration.Profiler.MockFolder))
        {
            metadata["profiler.mockFolder"] = CanonicalizeFullPath(configuration.Profiler.MockFolder);
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
            metadata["typeMapping.path"] = _pathCanonicalizer.Canonicalize(resolvedPath);
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

    private string CanonicalizeFullPath(string path)
        => _pathCanonicalizer.Canonicalize(Path.GetFullPath(path));

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

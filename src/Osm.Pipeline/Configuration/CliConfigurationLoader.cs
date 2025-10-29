using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Json.Configuration;
using Osm.Smo;

namespace Osm.Pipeline.Configuration;

public sealed class CliConfigurationLoader
{
    private readonly TighteningSectionReader _tighteningReader;
    private readonly SqlSectionReader _sqlReader;
    private readonly ModuleFilterSectionReader _moduleFilterReader;

    public CliConfigurationLoader()
        : this(
            new TighteningSectionReader(new TighteningOptionsDeserializer()),
            new SqlSectionReader(),
            new ModuleFilterSectionReader())
    {
    }

    public CliConfigurationLoader(ITighteningOptionsDeserializer tighteningDeserializer)
        : this(
            new TighteningSectionReader(tighteningDeserializer ?? throw new ArgumentNullException(nameof(tighteningDeserializer))),
            new SqlSectionReader(),
            new ModuleFilterSectionReader())
    {
    }

    internal CliConfigurationLoader(
        TighteningSectionReader tighteningReader,
        SqlSectionReader sqlReader,
        ModuleFilterSectionReader moduleFilterReader)
    {
        _tighteningReader = tighteningReader ?? throw new ArgumentNullException(nameof(tighteningReader));
        _sqlReader = sqlReader ?? throw new ArgumentNullException(nameof(sqlReader));
        _moduleFilterReader = moduleFilterReader ?? throw new ArgumentNullException(nameof(moduleFilterReader));
    }

    public async Task<Result<CliConfiguration>> LoadAsync(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return CliConfiguration.Empty;
        }

        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
        {
            return ValidationError.Create("cli.config.missing", $"Configuration file '{fullPath}' was not found.");
        }

        await using var stream = File.OpenRead(fullPath);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = document.RootElement;
        var baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

        var tighteningResult = await _tighteningReader.ReadAsync(root, baseDirectory, fullPath).ConfigureAwait(false);
        if (tighteningResult.IsFailure)
        {
            return Result<CliConfiguration>.Failure(tighteningResult.Errors);
        }

        var tighteningSection = tighteningResult.Value;
        if (tighteningSection.IsLegacyDocument)
        {
            return CliConfiguration.Empty with { Tightening = tighteningSection.Options ?? TighteningOptions.Default };
        }

        var configuration = CliConfiguration.Empty;
        if (tighteningSection.Options is not null)
        {
            configuration = configuration with { Tightening = tighteningSection.Options };
        }

        var moduleSection = _moduleFilterReader.Read(root, baseDirectory);
        if (moduleSection.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(moduleSection.ModelPath))
            {
                configuration = configuration with { ModelPath = moduleSection.ModelPath };
            }

            if (moduleSection.ModuleFilter != ModuleFilterConfiguration.Empty)
            {
                configuration = configuration with { ModuleFilter = moduleSection.ModuleFilter };
            }
        }

        if (ConfigurationJsonHelpers.TryReadPathProperty(root, "model", baseDirectory, out var legacyModelPath)
            && moduleSection == ModuleFilterSectionReadResult.NotPresent)
        {
            configuration = configuration with { ModelPath = legacyModelPath };
        }

        if (ConfigurationJsonHelpers.TryReadPathProperty(root, "profile", baseDirectory, out var profilePath))
        {
            configuration = configuration with { ProfilePath = profilePath };
        }

        if (ConfigurationJsonHelpers.TryReadPathProperty(root, "dmm", baseDirectory, out var dmmPath))
        {
            configuration = configuration with { DmmPath = dmmPath };
        }

        var cache = configuration.Cache;
        if (TryReadCache(root, baseDirectory, out var cacheConfig))
        {
            cache = cacheConfig;
        }

        configuration = configuration with { Cache = cache };

        var profiler = configuration.Profiler;
        if (TryReadProfiler(root, baseDirectory, out var profilerConfig))
        {
            profiler = profilerConfig;
            if (string.IsNullOrWhiteSpace(configuration.ProfilePath) && !string.IsNullOrWhiteSpace(profiler.ProfilePath))
            {
                configuration = configuration with { ProfilePath = profiler.ProfilePath };
            }
        }

        configuration = configuration with { Profiler = profiler };

        var sql = configuration.Sql;
        if (_sqlReader.TryRead(root, out var sqlConfig))
        {
            sql = sqlConfig;
        }

        configuration = configuration with { Sql = sql };

        if (TryReadTypeMapping(root, baseDirectory, out var typeMappingConfig, out var typeMappingError))
        {
            configuration = configuration with { TypeMapping = typeMappingConfig };
        }
        else if (typeMappingError is not null)
        {
            return Result<CliConfiguration>.Failure(typeMappingError.Value);
        }

        if (TryReadSupplementalModels(root, baseDirectory, out var supplementalModels))
        {
            configuration = configuration with { SupplementalModels = supplementalModels };
        }

        return configuration;
    }

    private static bool TryReadCache(JsonElement root, string baseDirectory, out CacheConfiguration cache)
    {
        cache = CacheConfiguration.Empty;
        if (!root.TryGetProperty("cache", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var rootPath = ConfigurationJsonHelpers.TryReadPathProperty(element, "root", baseDirectory, out var resolvedRoot)
            ? resolvedRoot
            : null;

        bool? refresh = null;
        if (element.TryGetProperty("refresh", out var refreshElement)
            && ConfigurationJsonHelpers.TryParseBoolean(refreshElement, out var parsedRefresh))
        {
            refresh = parsedRefresh;
        }

        cache = new CacheConfiguration(rootPath, refresh);
        return true;
    }

    private static bool TryReadProfiler(JsonElement root, string baseDirectory, out ProfilerConfiguration profiler)
    {
        profiler = ProfilerConfiguration.Empty;
        if (!root.TryGetProperty("profiler", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? provider = null;
        if (element.TryGetProperty("provider", out var providerElement) && providerElement.ValueKind == JsonValueKind.String)
        {
            provider = providerElement.GetString();
        }

        string? profilePath = null;
        if (ConfigurationJsonHelpers.TryReadPathProperty(element, "profilePath", baseDirectory, out var resolvedProfile))
        {
            profilePath = resolvedProfile;
        }

        string? mockFolder = null;
        if (ConfigurationJsonHelpers.TryReadPathProperty(element, "mockFolder", baseDirectory, out var resolvedMock))
        {
            mockFolder = resolvedMock;
        }

        profiler = new ProfilerConfiguration(provider, profilePath, mockFolder);
        return true;
    }

    private static bool TryReadTypeMapping(
        JsonElement root,
        string baseDirectory,
        out TypeMappingConfiguration configuration,
        out ValidationError? error)
    {
        configuration = TypeMappingConfiguration.Empty;
        error = null;

        if (!root.TryGetProperty("typeMapping", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? path = null;
        if (element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String)
        {
            var raw = pathElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var resolved = ConfigurationJsonHelpers.ResolveRelativePath(baseDirectory, raw!);
                if (!File.Exists(resolved))
                {
                    error = ValidationError.Create(
                        "cli.config.typeMapping.missing",
                        $"Type mapping file '{resolved}' was not found.");
                    return false;
                }

                path = resolved;
            }
        }

        TypeMappingRuleDefinition? defaultRule = null;
        if (element.TryGetProperty("default", out var defaultElement) && defaultElement.ValueKind != JsonValueKind.Null)
        {
            if (!TypeMappingRuleDefinition.TryParse(defaultElement, out var parsedDefault, out var defaultError))
            {
                error = ValidationError.Create(
                    "cli.config.typeMapping.default.invalid",
                    defaultError ?? "Invalid default type mapping rule.");
                return false;
            }

            defaultRule = parsedDefault;
        }

        var overrides = new Dictionary<string, TypeMappingRuleDefinition>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("overrides", out var overridesElement) && overridesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in overridesElement.EnumerateObject())
            {
                if (!TypeMappingRuleDefinition.TryParse(property.Value, out var overrideDefinition, out var overrideError))
                {
                    error = ValidationError.Create(
                        "cli.config.typeMapping.override.invalid",
                        $"Failed to parse override for '{property.Name}': {overrideError}");
                    return false;
                }

                overrides[property.Name] = overrideDefinition;
            }
        }

        configuration = new TypeMappingConfiguration(path, defaultRule, overrides);
        return true;
    }

    private static bool TryReadSupplementalModels(JsonElement root, string baseDirectory, out SupplementalModelConfiguration configuration)
    {
        configuration = SupplementalModelConfiguration.Empty;
        if (!root.TryGetProperty("supplementalModels", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        bool? includeUsers = null;
        if (element.TryGetProperty("includeUsers", out var includeElement)
            && ConfigurationJsonHelpers.TryParseBoolean(includeElement, out var parsedInclude))
        {
            includeUsers = parsedInclude;
        }

        var paths = new List<string>();
        if (element.TryGetProperty("paths", out var pathsElement) && pathsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in pathsElement.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.String)
                {
                    var resolved = ConfigurationJsonHelpers.ResolveRelativePath(baseDirectory, child.GetString() ?? string.Empty);
                    paths.Add(resolved);
                }
            }
        }

        configuration = new SupplementalModelConfiguration(includeUsers, paths);
        return true;
    }
}

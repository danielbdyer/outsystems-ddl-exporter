using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Json.Configuration;
using Osm.Smo;

namespace Osm.Pipeline.Configuration;

public sealed class CliConfigurationLoader
{
    private readonly ITighteningOptionsDeserializer _tighteningDeserializer;

    public CliConfigurationLoader()
        : this(new TighteningOptionsDeserializer())
    {
    }

    public CliConfigurationLoader(ITighteningOptionsDeserializer tighteningDeserializer)
    {
        _tighteningDeserializer = tighteningDeserializer ?? throw new ArgumentNullException(nameof(tighteningDeserializer));
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
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        if (IsLegacyTighteningDocument(root))
        {
            await using var tightStream = File.OpenRead(fullPath);
            var legacyResult = _tighteningDeserializer.Deserialize(tightStream);
            if (legacyResult.IsFailure)
            {
                return Result<CliConfiguration>.Failure(legacyResult.Errors);
            }

            return CliConfiguration.Empty with { Tightening = legacyResult.Value };
        }

        var configuration = CliConfiguration.Empty;
        var baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

        if (TryResolveTighteningPath(root, baseDirectory, out var tightPath, out var pathError))
        {
            await using var tightStream = File.OpenRead(tightPath);
            var tightResult = _tighteningDeserializer.Deserialize(tightStream);
            if (tightResult.IsFailure)
            {
                return Result<CliConfiguration>.Failure(tightResult.Errors);
            }

            configuration = configuration with { Tightening = tightResult.Value };
        }
        else if (pathError is not null)
        {
            return Result<CliConfiguration>.Failure(pathError.Value);
        }

        if (TryReadTighteningInline(root, out var inlineElement))
        {
            using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(inlineElement.GetRawText()));
            var inlineResult = _tighteningDeserializer.Deserialize(buffer);
            if (inlineResult.IsFailure)
            {
                return Result<CliConfiguration>.Failure(inlineResult.Errors);
            }

            configuration = configuration with { Tightening = inlineResult.Value };
        }

        if (TryReadModel(root, baseDirectory, out var modelPath, out var moduleFilter))
        {
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                configuration = configuration with { ModelPath = modelPath };
            }

            configuration = configuration with { ModuleFilter = moduleFilter };
        }
        else if (TryReadPath(root, "model", baseDirectory, out var legacyModelPath))
        {
            configuration = configuration with { ModelPath = legacyModelPath };
        }

        if (TryReadPath(root, "profile", baseDirectory, out var profilePath))
        {
            configuration = configuration with { ProfilePath = profilePath };
        }

        if (TryReadPath(root, "dmm", baseDirectory, out var dmmPath))
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
        if (TryReadSql(root, out var sqlConfig))
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

    private static bool IsLegacyTighteningDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return root.TryGetProperty("policy", out _);
    }

    private static bool TryResolveTighteningPath(JsonElement root, string baseDirectory, out string path, out ValidationError? error)
    {
        path = string.Empty;
        error = null;

        if (!root.TryGetProperty("tighteningPath", out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var resolved = ResolveRelativePath(baseDirectory, raw);
        if (!File.Exists(resolved))
        {
            error = ValidationError.Create("cli.config.tighteningPath.missing", $"Tightening configuration '{resolved}' was not found.");
            return false;
        }

        path = resolved;
        return true;
    }

    private static bool TryReadTighteningInline(JsonElement root, out JsonElement inlineElement)
    {
        inlineElement = default;
        if (!root.TryGetProperty("tightening", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        inlineElement = element;
        return true;
    }

    private static bool TryReadCache(JsonElement root, string baseDirectory, out CacheConfiguration cache)
    {
        cache = CacheConfiguration.Empty;
        if (!root.TryGetProperty("cache", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var rootPath = TryReadPath(element, "root", baseDirectory, out var resolvedRoot)
            ? resolvedRoot
            : null;

        bool? refresh = null;
        if (element.TryGetProperty("refresh", out var refreshElement))
        {
            if (refreshElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(refreshElement.GetString(), out var parsedRefresh))
                {
                    refresh = parsedRefresh;
                }
            }
            else if (refreshElement.ValueKind == JsonValueKind.True || refreshElement.ValueKind == JsonValueKind.False)
            {
                refresh = refreshElement.GetBoolean();
            }
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
        if (TryReadPath(element, "profilePath", baseDirectory, out var resolvedProfile))
        {
            profilePath = resolvedProfile;
        }

        string? mockFolder = null;
        if (TryReadPath(element, "mockFolder", baseDirectory, out var resolvedMock))
        {
            mockFolder = resolvedMock;
        }

        profiler = new ProfilerConfiguration(provider, profilePath, mockFolder);
        return true;
    }

    private static bool TryReadSql(JsonElement root, out SqlConfiguration configuration)
    {
        configuration = SqlConfiguration.Empty;
        if (!root.TryGetProperty("sql", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? connectionString = null;
        if (element.TryGetProperty("connectionString", out var connectionElement) && connectionElement.ValueKind == JsonValueKind.String)
        {
            connectionString = connectionElement.GetString();
        }

        int? commandTimeout = null;
        if (element.TryGetProperty("commandTimeoutSeconds", out var timeoutElement) && timeoutElement.ValueKind == JsonValueKind.Number)
        {
            if (timeoutElement.TryGetInt32(out var parsedTimeout))
            {
                commandTimeout = parsedTimeout;
            }
        }

        var sampling = SqlSamplingConfiguration.Empty;
        if (element.TryGetProperty("sampling", out var samplingElement) && samplingElement.ValueKind == JsonValueKind.Object)
        {
            long? threshold = null;
            if (samplingElement.TryGetProperty("rowSamplingThreshold", out var thresholdElement) && thresholdElement.ValueKind == JsonValueKind.Number)
            {
                if (thresholdElement.TryGetInt64(out var parsedThreshold))
                {
                    threshold = parsedThreshold;
                }
            }

            int? size = null;
            if (samplingElement.TryGetProperty("sampleSize", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number)
            {
                if (sizeElement.TryGetInt32(out var parsedSize))
                {
                    size = parsedSize;
                }
            }

            sampling = new SqlSamplingConfiguration(threshold, size);
        }

        var authentication = SqlAuthenticationConfiguration.Empty;
        if (element.TryGetProperty("authentication", out var authElement) && authElement.ValueKind == JsonValueKind.Object)
        {
            SqlAuthenticationMethod? method = null;
            if (authElement.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String)
            {
                var raw = methodElement.GetString();
                if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse(raw, ignoreCase: true, out SqlAuthenticationMethod parsedMethod))
                {
                    method = parsedMethod;
                }
            }

            bool? trustServerCertificate = null;
        if (authElement.TryGetProperty("trustServerCertificate", out var trustElement))
        {
            if (trustElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(trustElement.GetString(), out var parsedTrust))
                {
                    trustServerCertificate = parsedTrust;
                }
            }
            else if (trustElement.ValueKind == JsonValueKind.True || trustElement.ValueKind == JsonValueKind.False)
            {
                trustServerCertificate = trustElement.GetBoolean();
            }
        }

            string? applicationName = null;
            if (authElement.TryGetProperty("applicationName", out var appElement) && appElement.ValueKind == JsonValueKind.String)
            {
                applicationName = appElement.GetString();
            }

            string? accessToken = null;
            if (authElement.TryGetProperty("accessToken", out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
            {
                accessToken = tokenElement.GetString();
            }

            authentication = new SqlAuthenticationConfiguration(method, trustServerCertificate, applicationName, accessToken);
        }

        configuration = new SqlConfiguration(connectionString, commandTimeout, sampling, authentication);
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
                var resolved = ResolveRelativePath(baseDirectory, raw!);
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
        if (element.TryGetProperty("includeUsers", out var includeElement))
        {
            if (includeElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(includeElement.GetString(), out var parsedInclude))
                {
                    includeUsers = parsedInclude;
                }
            }
            else if (includeElement.ValueKind == JsonValueKind.True || includeElement.ValueKind == JsonValueKind.False)
            {
                includeUsers = includeElement.GetBoolean();
            }
        }

        var paths = new List<string>();
        if (element.TryGetProperty("paths", out var pathsElement) && pathsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in pathsElement.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.String)
                {
                    var resolved = ResolveRelativePath(baseDirectory, child.GetString() ?? string.Empty);
                    paths.Add(resolved);
                }
            }
        }

        configuration = new SupplementalModelConfiguration(includeUsers, paths);
        return true;
    }

    private static bool TryReadModel(JsonElement root, string baseDirectory, out string? path, out ModuleFilterConfiguration moduleFilter)
    {
        path = null;
        moduleFilter = ModuleFilterConfiguration.Empty;

        if (!root.TryGetProperty("model", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadPath(element, "path", baseDirectory, out var resolved))
        {
            path = resolved;
        }

        var moduleNames = new List<string>();
        var moduleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entityFilters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var validationOverrides = new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase);
        bool? includeSystem = null;
        bool? includeInactive = null;

        if (element.TryGetProperty("modules", out var modulesElement))
        {
            if (modulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var moduleElement in modulesElement.EnumerateArray())
                {
                    if (moduleElement.ValueKind == JsonValueKind.String)
                    {
                        AddModuleName(moduleElement.GetString());
                    }
                    else if (moduleElement.ValueKind == JsonValueKind.Object)
                    {
                        if (!moduleElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var moduleName = AddModuleName(nameElement.GetString());
                        if (moduleName is null)
                        {
                            continue;
                        }

                        if (moduleElement.TryGetProperty("entities", out var entitiesElement))
                        {
                            var parsedEntities = ParseEntityNames(entitiesElement);
                            if (parsedEntities is null)
                            {
                                entityFilters.Remove(moduleName);
                            }
                            else
                            {
                                entityFilters[moduleName] = parsedEntities.Count > 0
                                    ? parsedEntities.ToArray()
                                    : Array.Empty<string>();
                            }
                        }

                        var moduleOverride = ModuleValidationOverrideConfiguration.Empty;
                        var hasOverride = false;

                        if (moduleElement.TryGetProperty("allowMissingPrimaryKey", out var pkElement))
                        {
                            var primaryKeyEntities = ParseOverrideEntities(pkElement, out var pkAll);
                            moduleOverride = moduleOverride.Merge(new ModuleValidationOverrideConfiguration(
                                primaryKeyEntities.ToArray(),
                                pkAll,
                                Array.Empty<string>(),
                                AllowMissingSchemaForAll: false));
                            hasOverride |= pkAll || primaryKeyEntities.Count > 0;
                        }

                        if (moduleElement.TryGetProperty("allowMissingSchema", out var schemaElement))
                        {
                            var schemaEntities = ParseOverrideEntities(schemaElement, out var schemaAll);
                            moduleOverride = moduleOverride.Merge(new ModuleValidationOverrideConfiguration(
                                Array.Empty<string>(),
                                AllowMissingPrimaryKeyForAll: false,
                                schemaEntities.ToArray(),
                                schemaAll));
                            hasOverride |= schemaAll || schemaEntities.Count > 0;
                        }

                        if (hasOverride)
                        {
                            validationOverrides[moduleName] = moduleOverride;
                        }
                    }
                }
            }
            else if (modulesElement.ValueKind == JsonValueKind.String)
            {
                var raw = modulesElement.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var separators = new[] { ';', ',', '|' };
                    foreach (var token in raw.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = token.Trim();
                        AddModuleName(trimmed);
                    }
                }
            }
        }

        if (element.TryGetProperty("includeSystemModules", out var includeElement))
        {
            if (includeElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(includeElement.GetString(), out var parsedInclude))
                {
                    includeSystem = parsedInclude;
                }
            }
            else if (includeElement.ValueKind == JsonValueKind.True || includeElement.ValueKind == JsonValueKind.False)
            {
                includeSystem = includeElement.GetBoolean();
            }
        }

        if (element.TryGetProperty("includeInactiveModules", out var inactiveElement))
        {
            if (inactiveElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(inactiveElement.GetString(), out var parsedInactive))
                {
                    includeInactive = parsedInactive;
                }
            }
            else if (inactiveElement.ValueKind == JsonValueKind.True || inactiveElement.ValueKind == JsonValueKind.False)
            {
                includeInactive = inactiveElement.GetBoolean();
            }
        }

        var modules = moduleNames.Count > 0
            ? moduleNames.ToArray()
            : Array.Empty<string>();

        moduleFilter = new ModuleFilterConfiguration(modules, includeSystem, includeInactive, entityFilters, validationOverrides);
        return true;

        string? AddModuleName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return null;
            }

            var trimmed = rawName.Trim();
            if (moduleSet.Add(trimmed))
            {
                moduleNames.Add(trimmed);
            }

            return trimmed;
        }

        static List<string>? ParseEntityNames(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True or JsonValueKind.Null => null,
                JsonValueKind.False => new List<string>(),
                JsonValueKind.String => ParseEntityString(element.GetString()),
                JsonValueKind.Array => ParseEntityArray(element),
                _ => new List<string>()
            };

            static List<string>? ParseEntityArray(JsonElement arrayElement)
            {
                var list = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var child in arrayElement.EnumerateArray())
                {
                    if (child.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var parsed = ParseEntityString(child.GetString());
                    if (parsed is null)
                    {
                        return null;
                    }

                    foreach (var value in parsed)
                    {
                        if (seen.Add(value))
                        {
                            list.Add(value);
                        }
                    }
                }

                return list;
            }

            static List<string>? ParseEntityString(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return new List<string>();
                }

                var separators = new[] { ';', ',', '|' };
                var tokens = raw.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                var list = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var token in tokens)
                {
                    var trimmed = token.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }

                    if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    if (seen.Add(trimmed))
                    {
                        list.Add(trimmed);
                    }
                }

                return list;
            }
        }

        static List<string> ParseOverrideEntities(JsonElement element, out bool appliesToAll)
        {
            appliesToAll = false;
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var separators = new[] { ';', ',', '|' };

            switch (element.ValueKind)
            {
                case JsonValueKind.True:
                    appliesToAll = true;
                    break;
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    break;
                case JsonValueKind.String:
                    ParseOverrideTokens(element.GetString(), seen, list, ref appliesToAll, separators);
                    break;
                case JsonValueKind.Array:
                    foreach (var child in element.EnumerateArray())
                    {
                        if (child.ValueKind == JsonValueKind.String)
                        {
                            ParseOverrideTokens(child.GetString(), seen, list, ref appliesToAll, separators);
                        }
                        else if (child.ValueKind == JsonValueKind.True)
                        {
                            appliesToAll = true;
                        }
                    }

                    break;
                default:
                    break;
            }

            if (appliesToAll)
            {
                list.Clear();
            }

            return list;
        }

        static void ParseOverrideTokens(
            string? raw,
            HashSet<string> seen,
            List<string> values,
            ref bool appliesToAll,
            char[] separators)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var tokens = raw.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var trimmed = token.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase))
                {
                    appliesToAll = true;
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    values.Add(trimmed);
                }
            }
        }
    }

    private static bool TryReadPath(JsonElement root, string property, string baseDirectory, out string path)
    {
        path = string.Empty;
        if (!root.TryGetProperty(property, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            path = ResolveRelativePath(baseDirectory, value);
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String)
        {
            var value = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            path = ResolveRelativePath(baseDirectory, value);
            return true;
        }

        return false;
    }

    private static string ResolveRelativePath(string baseDirectory, string value)
    {
        if (Path.IsPathRooted(value))
        {
            return Path.GetFullPath(value);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, value));
    }
}

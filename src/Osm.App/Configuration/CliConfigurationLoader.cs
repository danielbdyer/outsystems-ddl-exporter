using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Json.Configuration;

namespace Osm.App.Configuration;

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

        if (TryReadTypeMap(root, baseDirectory, out var typeMap))
        {
            configuration = configuration with { TypeMapping = typeMap };
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

    private static bool TryReadTypeMap(JsonElement root, string baseDirectory, out TypeMappingConfiguration typeMap)
    {
        typeMap = TypeMappingConfiguration.Empty;
        if (!root.TryGetProperty("typeMap", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? path = null;
        if (element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String)
        {
            var raw = pathElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                path = ResolveRelativePath(baseDirectory, raw!);
            }
        }

        typeMap = new TypeMappingConfiguration(path);
        return true;
    }

    private static bool TryReadCache(JsonElement root, string baseDirectory, out CacheConfiguration cache)
    {
        cache = CacheConfiguration.Empty;
        if (!root.TryGetProperty("cache", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? rootPath = null;
        bool? refresh = null;

        if (element.TryGetProperty("root", out var rootElement) && rootElement.ValueKind == JsonValueKind.String)
        {
            var raw = rootElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                rootPath = ResolveRelativePath(baseDirectory, raw!);
            }
        }

        if (element.TryGetProperty("refresh", out var refreshElement))
        {
            if (refreshElement.ValueKind == JsonValueKind.True || refreshElement.ValueKind == JsonValueKind.False)
            {
                refresh = refreshElement.GetBoolean();
            }
            else if (refreshElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(refreshElement.GetString(), out var parsed))
                {
                    refresh = parsed;
                }
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
        string? profilePath = null;
        string? mockFolder = null;

        if (element.TryGetProperty("provider", out var providerElement) && providerElement.ValueKind == JsonValueKind.String)
        {
            provider = providerElement.GetString();
        }

        if (element.TryGetProperty("profilePath", out var profileElement) && profileElement.ValueKind == JsonValueKind.String)
        {
            var raw = profileElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                profilePath = ResolveRelativePath(baseDirectory, raw!);
            }
        }

        if (element.TryGetProperty("mockFolder", out var mockElement) && mockElement.ValueKind == JsonValueKind.String)
        {
            var raw = mockElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                mockFolder = ResolveRelativePath(baseDirectory, raw!);
            }
        }

        profiler = new ProfilerConfiguration(provider, profilePath, mockFolder);
        return true;
    }

    private static bool TryReadSupplementalModels(
        JsonElement root,
        string baseDirectory,
        out SupplementalModelConfiguration configuration)
    {
        configuration = SupplementalModelConfiguration.Empty;
        if (!root.TryGetProperty("supplementalModels", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        bool? includeUsers = null;
        var paths = new List<string>();

        if (element.TryGetProperty("includeUsers", out var includeUsersElement))
        {
            includeUsers = includeUsersElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(includeUsersElement.GetString(), out var parsed) => parsed,
                _ => includeUsers
            };
        }

        if (element.TryGetProperty("paths", out var pathsElement) && pathsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var pathElement in pathsElement.EnumerateArray())
            {
                if (pathElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var raw = pathElement.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                paths.Add(ResolveRelativePath(baseDirectory, raw!));
            }
        }

        configuration = new SupplementalModelConfiguration(includeUsers, paths);
        return true;
    }

    private static bool TryReadSql(JsonElement root, out SqlConfiguration sql)
    {
        sql = SqlConfiguration.Empty;
        if (!root.TryGetProperty("sql", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? connection = null;
        int? timeout = null;
        var sampling = SqlSamplingConfiguration.Empty;
        var authentication = SqlAuthenticationConfiguration.Empty;

        if (element.TryGetProperty("connectionString", out var connectionElement) && connectionElement.ValueKind == JsonValueKind.String)
        {
            var raw = connectionElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                connection = raw!.Trim();
            }
        }

        if (element.TryGetProperty("commandTimeoutSeconds", out var timeoutElement))
        {
            switch (timeoutElement.ValueKind)
            {
                case JsonValueKind.Number:
                    if (timeoutElement.TryGetInt32(out var number))
                    {
                        timeout = number;
                    }
                    break;
                case JsonValueKind.String:
                    var raw = timeoutElement.GetString();
                    if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        timeout = parsed;
                    }
                    break;
            }
        }

        if (element.TryGetProperty("sampling", out var samplingElement) && samplingElement.ValueKind == JsonValueKind.Object)
        {
            long? threshold = null;
            int? sampleSize = null;

            if (samplingElement.TryGetProperty("rowThreshold", out var thresholdElement))
            {
                switch (thresholdElement.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (thresholdElement.TryGetInt64(out var number))
                        {
                            threshold = number;
                        }
                        break;
                    case JsonValueKind.String:
                        var rawThreshold = thresholdElement.GetString();
                        if (!string.IsNullOrWhiteSpace(rawThreshold) && long.TryParse(rawThreshold, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedThreshold))
                        {
                            threshold = parsedThreshold;
                        }
                        break;
                }
            }

            if (samplingElement.TryGetProperty("sampleSize", out var sampleElement))
            {
                switch (sampleElement.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (sampleElement.TryGetInt32(out var number))
                        {
                            sampleSize = number;
                        }
                        break;
                    case JsonValueKind.String:
                        var rawSample = sampleElement.GetString();
                        if (!string.IsNullOrWhiteSpace(rawSample) && int.TryParse(rawSample, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSample))
                        {
                            sampleSize = parsedSample;
                        }
                        break;
                }
            }

            sampling = new SqlSamplingConfiguration(threshold, sampleSize);
        }

        if (element.TryGetProperty("authentication", out var authElement) && authElement.ValueKind == JsonValueKind.Object)
        {
            SqlAuthenticationMethod? method = null;
            if (authElement.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String)
            {
                var raw = methodElement.GetString();
                if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse(raw, true, out SqlAuthenticationMethod parsed))
                {
                    method = parsed;
                }
            }

            var trust = ReadOptionalBoolean(authElement, "trustServerCertificate");

            string? applicationName = null;
            if (authElement.TryGetProperty("applicationName", out var appElement) && appElement.ValueKind == JsonValueKind.String)
            {
                var raw = appElement.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    applicationName = raw.Trim();
                }
            }

            string? accessToken = null;
            if (authElement.TryGetProperty("accessToken", out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
            {
                var raw = tokenElement.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    accessToken = raw;
                }
            }

            authentication = new SqlAuthenticationConfiguration(method, trust, applicationName, accessToken);
        }

        sql = new SqlConfiguration(connection, timeout, sampling, authentication);
        return true;
    }

    private static bool TryReadModel(
        JsonElement root,
        string baseDirectory,
        out string? path,
        out ModuleFilterConfiguration filter)
    {
        path = null;
        filter = ModuleFilterConfiguration.Empty;

        if (!root.TryGetProperty("model", out var element))
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return false;
                }

                path = ResolveRelativePath(baseDirectory, raw!);
                return true;
            case JsonValueKind.Object:
                if (element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String)
                {
                    var nested = pathElement.GetString();
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        path = ResolveRelativePath(baseDirectory, nested!);
                    }
                }

                var modules = ReadModuleList(element);
                var includeSystem = ReadOptionalBoolean(element, "includeSystemModules");
                var includeInactive = ReadOptionalBoolean(element, "includeInactiveModules");
                filter = new ModuleFilterConfiguration(modules, includeSystem, includeInactive);
                return true;
        }

        return false;
    }

    private static bool TryReadPath(JsonElement root, string propertyName, string baseDirectory, out string? path)
    {
        path = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return false;
                }

                path = ResolveRelativePath(baseDirectory, raw!);
                return true;
            case JsonValueKind.Object:
                if (element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String)
                {
                    var nested = pathElement.GetString();
                    if (string.IsNullOrWhiteSpace(nested))
                    {
                        return false;
                    }

                    path = ResolveRelativePath(baseDirectory, nested!);
                    return true;
                }
                break;
        }

        return false;
    }

    private static string ResolveRelativePath(string baseDirectory, string rawPath)
    {
        var candidate = rawPath.Trim();
        if (Path.IsPathRooted(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, candidate));
    }

    private static IReadOnlyList<string> ReadModuleList(JsonElement element)
    {
        if (!element.TryGetProperty("modules", out var modulesElement))
        {
            return Array.Empty<string>();
        }

        return modulesElement.ValueKind switch
        {
            JsonValueKind.Array => ReadModuleArray(modulesElement),
            JsonValueKind.String => SplitModules(modulesElement.GetString()),
            _ => Array.Empty<string>(),
        };
    }

    private static IReadOnlyList<string> ReadModuleArray(JsonElement modulesElement)
    {
        var modules = new List<string>();
        foreach (var item in modulesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            modules.Add(value.Trim());
        }

        return modules;
    }

    private static IReadOnlyList<string> SplitModules(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var separators = new[] { ',', ';' };
        var modules = value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (modules.Length == 0)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>(modules.Length);
        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                continue;
            }

            list.Add(module.Trim());
        }

        return list;
    }

    private static bool? ReadOptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) ? parsed : null,
            _ => null,
        };
    }
}

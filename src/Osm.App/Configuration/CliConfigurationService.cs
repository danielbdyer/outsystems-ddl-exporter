using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;

namespace Osm.App.Configuration;

public interface ICliConfigurationService
{
    Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default);
}

public sealed class CliConfigurationService : ICliConfigurationService
{
    private readonly CliConfigurationLoader _loader;

    private const string EnvConfigPath = "OSM_CLI_CONFIG_PATH";
    private const string EnvModelPath = "OSM_CLI_MODEL_PATH";
    private const string EnvProfilePath = "OSM_CLI_PROFILE_PATH";
    private const string EnvDmmPath = "OSM_CLI_DMM_PATH";
    private const string EnvCacheRoot = "OSM_CLI_CACHE_ROOT";
    private const string EnvRefreshCache = "OSM_CLI_REFRESH_CACHE";
    private const string EnvProfilerProvider = "OSM_CLI_PROFILER_PROVIDER";
    private const string EnvProfilerMockFolder = "OSM_CLI_PROFILER_MOCK_FOLDER";
    private const string EnvTypeMapPath = "OSM_CLI_TYPEMAP_PATH";
    private const string EnvSqlConnectionString = "OSM_CLI_CONNECTION_STRING";
    private const string EnvSqlCommandTimeout = "OSM_CLI_SQL_COMMAND_TIMEOUT";
    private const string EnvSqlAuthentication = "OSM_CLI_SQL_AUTHENTICATION";
    private const string EnvSqlAccessToken = "OSM_CLI_SQL_ACCESS_TOKEN";
    private const string EnvSqlTrustServerCertificate = "OSM_CLI_SQL_TRUST_SERVER_CERTIFICATE";
    private const string EnvSqlApplicationName = "OSM_CLI_SQL_APPLICATION_NAME";

    public CliConfigurationService()
        : this(new CliConfigurationLoader())
    {
    }

    public CliConfigurationService(CliConfigurationLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public async Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configPath = ResolveConfigPath(overrideConfigPath);
        var loadResult = await _loader.LoadAsync(configPath).ConfigureAwait(false);
        if (loadResult.IsFailure)
        {
            return Result<CliConfigurationContext>.Failure(loadResult.Errors);
        }

        var configuration = ApplyEnvironmentOverrides(loadResult.Value);
        return new CliConfigurationContext(configuration, configPath);
    }

    private static string? ResolveConfigPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var envPath = Environment.GetEnvironmentVariable(EnvConfigPath);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath;
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            "pipeline.yaml",
            "pipeline.yml",
            "pipeline.json",
            "config.json",
            "appsettings.json"
        };

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(currentDirectory, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static CliConfiguration ApplyEnvironmentOverrides(CliConfiguration configuration)
    {
        var result = configuration;

        if (TryGetEnvironmentPath(EnvModelPath, out var modelPath))
        {
            result = result with { ModelPath = modelPath };
        }

        if (TryGetEnvironmentPath(EnvProfilePath, out var profilePath))
        {
            result = result with { ProfilePath = profilePath };
            result = result with { Profiler = result.Profiler with { ProfilePath = profilePath } };
        }
        else if (string.IsNullOrWhiteSpace(result.ProfilePath) && !string.IsNullOrWhiteSpace(result.Profiler.ProfilePath))
        {
            result = result with { ProfilePath = result.Profiler.ProfilePath };
        }

        if (TryGetEnvironmentPath(EnvDmmPath, out var dmmPath))
        {
            result = result with { DmmPath = dmmPath };
        }

        if (TryGetEnvironmentPath(EnvTypeMapPath, out var typeMapPath))
        {
            result = result with { TypeMapping = new TypeMappingConfiguration(Path.GetFullPath(typeMapPath!)) };
        }

        var cache = result.Cache;
        if (TryGetEnvironmentPath(EnvCacheRoot, out var cacheRoot))
        {
            cache = cache with { Root = cacheRoot };
        }

        if (TryParseBoolean(Environment.GetEnvironmentVariable(EnvRefreshCache), out var refreshCache))
        {
            cache = cache with { Refresh = refreshCache };
        }

        result = result with { Cache = cache };

        var profiler = result.Profiler;
        var provider = Environment.GetEnvironmentVariable(EnvProfilerProvider);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            profiler = profiler with { Provider = provider };
        }

        if (TryGetEnvironmentPath(EnvProfilerMockFolder, out var mockFolder))
        {
            profiler = profiler with { MockFolder = mockFolder };
        }

        result = result with { Profiler = profiler };

        var sql = result.Sql;
        var connectionString = Environment.GetEnvironmentVariable(EnvSqlConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            sql = sql with { ConnectionString = connectionString.Trim() };
        }

        if (TryParseInt(Environment.GetEnvironmentVariable(EnvSqlCommandTimeout), out var timeout))
        {
            sql = sql with { CommandTimeoutSeconds = timeout };
        }

        var authentication = sql.Authentication;
        var authenticationRaw = Environment.GetEnvironmentVariable(EnvSqlAuthentication);
        if (!string.IsNullOrWhiteSpace(authenticationRaw) && Enum.TryParse(authenticationRaw, true, out SqlAuthenticationMethod method))
        {
            authentication = authentication with { Method = method };
        }

        if (TryParseBoolean(Environment.GetEnvironmentVariable(EnvSqlTrustServerCertificate), out var trustServerCertificate))
        {
            authentication = authentication with { TrustServerCertificate = trustServerCertificate };
        }

        var applicationName = Environment.GetEnvironmentVariable(EnvSqlApplicationName);
        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            authentication = authentication with { ApplicationName = applicationName!.Trim() };
        }

        var accessToken = Environment.GetEnvironmentVariable(EnvSqlAccessToken);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            authentication = authentication with { AccessToken = accessToken };
        }

        sql = sql with { Authentication = authentication };
        result = result with { Sql = sql };

        return result;
    }

    private static bool TryGetEnvironmentPath(string variable, out string? path)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            path = null;
            return false;
        }

        path = raw;
        return true;
    }

    private static bool TryParseBoolean(string? value, out bool result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out result);
    }

    private static bool TryParseInt(string? value, out int result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return int.TryParse(value, out result);
    }
}

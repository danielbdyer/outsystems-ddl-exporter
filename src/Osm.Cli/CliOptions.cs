using System;
using System.CommandLine;
using Microsoft.Data.SqlClient;

namespace Osm.Cli;

internal static class CliOptions
{
    public static Option<string?> CreateConfigOption()
        => new("--config", "Path to CLI configuration file.");

    public static Option<string?> CreateModulesOption()
    {
        var option = new Option<string?>("--modules", "Comma or semicolon separated list of modules.");
        option.AddAlias("--module");
        return option;
    }

    public static Option<bool> CreateIncludeSystemModulesOption(string description)
        => new("--include-system-modules", description);

    public static Option<bool> CreateExcludeSystemModulesOption(string description)
        => new("--exclude-system-modules", description);

    public static Option<bool> CreateIncludeInactiveModulesOption(string description)
        => new("--include-inactive-modules", description);

    public static Option<bool> CreateOnlyActiveModulesOption(string description)
        => new("--only-active-modules", description);

    public static Option<string?> CreateCacheRootOption()
        => new("--cache-root", "Root directory for evidence caching.");

    public static Option<bool> CreateRefreshCacheOption()
        => new("--refresh-cache", "Force cache refresh for this execution.");

    public static Option<int?> CreateMaxDegreeOfParallelismOption()
        => new("--max-degree-of-parallelism", "Maximum number of modules processed in parallel.");

    public static Option<string[]?> CreateOverrideOption(string name, string description)
    {
        var option = new Option<string[]?>(name, description)
        {
            AllowMultipleArgumentsPerToken = true
        };

        return option;
    }

    public static SqlOptionSet CreateSqlOptionSet()
    {
        var connectionString = new Option<string?>("--connection-string", "SQL connection string override.");
        var commandTimeout = new Option<int?>("--command-timeout", "Command timeout in seconds.");
        var samplingThreshold = new Option<long?>("--sampling-threshold", "Row sampling threshold for SQL profiler.");
        var samplingSize = new Option<int?>("--sampling-size", "Sampling size for SQL profiler.");
        var authenticationMethod = new Option<SqlAuthenticationMethod?>("--sql-authentication", "SQL authentication method.");
        var trustServerCertificate = new Option<bool?>("--sql-trust-server-certificate", result =>
        {
            if (result.Tokens.Count == 0)
            {
                return true;
            }

            if (bool.TryParse(result.Tokens[0].Value, out var parsed))
            {
                return parsed;
            }

            result.ErrorMessage = "Invalid value for --sql-trust-server-certificate. Expected 'true' or 'false'.";
            return null;
        })
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Trust server certificate when connecting to SQL Server."
        };
        var applicationName = new Option<string?>("--sql-application-name", "Application name for SQL connections.");
        var accessToken = new Option<string?>("--sql-access-token", "Access token for SQL authentication.");

        return new SqlOptionSet(
            connectionString,
            commandTimeout,
            samplingThreshold,
            samplingSize,
            authenticationMethod,
            trustServerCertificate,
            applicationName,
            accessToken);
    }
}

internal sealed record SqlOptionSet(
    Option<string?> ConnectionString,
    Option<int?> CommandTimeout,
    Option<long?> SamplingThreshold,
    Option<int?> SamplingSize,
    Option<SqlAuthenticationMethod?> AuthenticationMethod,
    Option<bool?> TrustServerCertificate,
    Option<string?> ApplicationName,
    Option<string?> AccessToken);

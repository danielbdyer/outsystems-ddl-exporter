using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using System.Linq;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Binders;

internal sealed class SqlOptionBinder : BinderBase<SqlOptionsOverrides>, ICommandOptionSource
{
    public SqlOptionBinder()
    {
        ConnectionStringOption = new Option<string?>("--connection-string", "SQL connection string override.");
        CommandTimeoutOption = new Option<int?>("--command-timeout", "Command timeout in seconds.");
        SamplingThresholdOption = new Option<long?>("--sampling-threshold", "Row sampling threshold for SQL profiler.");
        SamplingSizeOption = new Option<int?>("--sampling-size", "Sampling size for SQL profiler.");
        AuthenticationMethodOption = new Option<SqlAuthenticationMethod?>("--sql-authentication", "SQL authentication method.");
        TrustServerCertificateOption = new Option<bool?>("--sql-trust-server-certificate", result =>
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
        ApplicationNameOption = new Option<string?>("--sql-application-name", "Application name for SQL connections.");
        AccessTokenOption = new Option<string?>("--sql-access-token", "Access token for SQL authentication.");
        ProfilingConnectionStringsOption = new Option<string[]>("--profiling-connection-string", "Additional SQL connection strings for profiling secondary environments (prefix with 'Name::' to label each environment).")
        {
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };
    }

    public Option<string?> ConnectionStringOption { get; }

    public Option<int?> CommandTimeoutOption { get; }

    public Option<long?> SamplingThresholdOption { get; }

    public Option<int?> SamplingSizeOption { get; }

    public Option<SqlAuthenticationMethod?> AuthenticationMethodOption { get; }

    public Option<bool?> TrustServerCertificateOption { get; }

    public Option<string?> ApplicationNameOption { get; }

    public Option<string?> AccessTokenOption { get; }

    public Option<string[]> ProfilingConnectionStringsOption { get; }

    public IEnumerable<Option> Options
    {
        get
        {
            yield return ConnectionStringOption;
            yield return CommandTimeoutOption;
            yield return SamplingThresholdOption;
            yield return SamplingSizeOption;
            yield return AuthenticationMethodOption;
            yield return TrustServerCertificateOption;
            yield return ApplicationNameOption;
            yield return AccessTokenOption;
            yield return ProfilingConnectionStringsOption;
        }
    }

    protected override SqlOptionsOverrides GetBoundValue(BindingContext bindingContext)
        => Bind(bindingContext?.ParseResult ?? throw new ArgumentNullException(nameof(bindingContext)));

    public SqlOptionsOverrides Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        var profilingConnections = parseResult.GetValueForOption(ProfilingConnectionStringsOption);
        var normalizedProfilingConnections = profilingConnections is null
            ? Array.Empty<string>()
            : profilingConnections
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .ToArray();

        return new SqlOptionsOverrides(
            parseResult.GetValueForOption(ConnectionStringOption),
            parseResult.GetValueForOption(CommandTimeoutOption),
            parseResult.GetValueForOption(SamplingThresholdOption),
            parseResult.GetValueForOption(SamplingSizeOption),
            parseResult.GetValueForOption(AuthenticationMethodOption),
            parseResult.GetValueForOption(TrustServerCertificateOption),
            parseResult.GetValueForOption(ApplicationNameOption),
            parseResult.GetValueForOption(AccessTokenOption),
            normalizedProfilingConnections.Length == 0 ? null : normalizedProfilingConnections);
    }
}

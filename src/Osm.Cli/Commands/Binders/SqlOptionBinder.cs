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
        OptionalColumnOption = new Option<string[]>(
            "--sql-optional-column",
            description: "Mark a result-set column as optional using the ResultSet:Column syntax. May be specified multiple times.")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        OptionalColumnOption.AddValidator(result =>
        {
            foreach (var token in result.Tokens)
            {
                var separatorIndex = token.Value.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex == token.Value.Length - 1)
                {
                    result.ErrorMessage = "Invalid --sql-optional-column value. Expected ResultSet:Column.";
                    return;
                }

                var resultSet = token.Value.Substring(0, separatorIndex).Trim();
                var column = token.Value.Substring(separatorIndex + 1).Trim();
                if (string.IsNullOrWhiteSpace(resultSet) || string.IsNullOrWhiteSpace(column))
                {
                    result.ErrorMessage = "Invalid --sql-optional-column value. ResultSet and Column names must be non-empty.";
                    return;
                }
            }
        });
    }

    public Option<string?> ConnectionStringOption { get; }

    public Option<int?> CommandTimeoutOption { get; }

    public Option<long?> SamplingThresholdOption { get; }

    public Option<int?> SamplingSizeOption { get; }

    public Option<SqlAuthenticationMethod?> AuthenticationMethodOption { get; }

    public Option<bool?> TrustServerCertificateOption { get; }

    public Option<string?> ApplicationNameOption { get; }

    public Option<string?> AccessTokenOption { get; }

    public Option<string[]> OptionalColumnOption { get; }

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
            yield return OptionalColumnOption;
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

        return new SqlOptionsOverrides(
            parseResult.GetValueForOption(ConnectionStringOption),
            parseResult.GetValueForOption(CommandTimeoutOption),
            parseResult.GetValueForOption(SamplingThresholdOption),
            parseResult.GetValueForOption(SamplingSizeOption),
            parseResult.GetValueForOption(AuthenticationMethodOption),
            parseResult.GetValueForOption(TrustServerCertificateOption),
            parseResult.GetValueForOption(ApplicationNameOption),
            parseResult.GetValueForOption(AccessTokenOption),
            ParseOptionalColumns(parseResult.GetValueForOption(OptionalColumnOption)));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? ParseOptionalColumns(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        var buffer = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var separatorIndex = value.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
            {
                continue;
            }

            var resultSet = value.Substring(0, separatorIndex).Trim();
            var column = value.Substring(separatorIndex + 1).Trim();
            if (string.IsNullOrWhiteSpace(resultSet) || string.IsNullOrWhiteSpace(column))
            {
                continue;
            }

            if (!buffer.TryGetValue(resultSet, out var columns))
            {
                columns = new List<string>();
                buffer[resultSet] = columns;
            }

            columns.Add(column);
        }

        if (buffer.Count == 0)
        {
            return null;
        }

        return buffer.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(),
            StringComparer.OrdinalIgnoreCase);
    }
}

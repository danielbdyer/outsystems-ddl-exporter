using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Binders;

internal sealed class SchemaApplyOptionBinder : BinderBase<SchemaApplyOverrides>, ICommandOptionSource
{
    public SchemaApplyOptionBinder()
    {
        EnabledOption = CreateTriStateOption("--apply", "Enable schema apply after SSDT emission completes.");
        ConnectionStringOption = new Option<string?>("--apply-connection-string", "Connection string to use for schema apply.");
        CommandTimeoutOption = new Option<int?>("--apply-command-timeout", "Schema apply command timeout in seconds.");
        AuthenticationMethodOption = new Option<SqlAuthenticationMethod?>("--apply-sql-authentication", "Authentication method for schema apply connections.");
        TrustServerCertificateOption = CreateTriStateOption("--apply-trust-server-certificate", "Trust the SQL Server certificate when applying schema changes.");
        ApplicationNameOption = new Option<string?>("--apply-application-name", "Application name for schema apply connections.");
        AccessTokenOption = new Option<string?>("--apply-access-token", "Access token for schema apply connections.");
        ApplySafeScriptOption = CreateTriStateOption("--apply-safe-script", "Apply the generated safe opportunity script.");
        ApplyStaticSeedsOption = CreateTriStateOption("--apply-static-seeds", "Apply generated static seed scripts.");
    }

    public Option<bool?> EnabledOption { get; }

    public Option<string?> ConnectionStringOption { get; }

    public Option<int?> CommandTimeoutOption { get; }

    public Option<SqlAuthenticationMethod?> AuthenticationMethodOption { get; }

    public Option<bool?> TrustServerCertificateOption { get; }

    public Option<string?> ApplicationNameOption { get; }

    public Option<string?> AccessTokenOption { get; }

    public Option<bool?> ApplySafeScriptOption { get; }

    public Option<bool?> ApplyStaticSeedsOption { get; }

    public IEnumerable<Option> Options
    {
        get
        {
            yield return EnabledOption;
            yield return ConnectionStringOption;
            yield return CommandTimeoutOption;
            yield return AuthenticationMethodOption;
            yield return TrustServerCertificateOption;
            yield return ApplicationNameOption;
            yield return AccessTokenOption;
            yield return ApplySafeScriptOption;
            yield return ApplyStaticSeedsOption;
        }
    }

    protected override SchemaApplyOverrides GetBoundValue(BindingContext bindingContext)
        => Bind(bindingContext?.ParseResult ?? throw new ArgumentNullException(nameof(bindingContext)));

    public SchemaApplyOverrides Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        return new SchemaApplyOverrides(
            parseResult.GetValueForOption(EnabledOption),
            parseResult.GetValueForOption(ConnectionStringOption),
            parseResult.GetValueForOption(CommandTimeoutOption),
            parseResult.GetValueForOption(AuthenticationMethodOption),
            parseResult.GetValueForOption(TrustServerCertificateOption),
            parseResult.GetValueForOption(ApplicationNameOption),
            parseResult.GetValueForOption(AccessTokenOption),
            parseResult.GetValueForOption(ApplySafeScriptOption),
            parseResult.GetValueForOption(ApplyStaticSeedsOption));
    }

    private static Option<bool?> CreateTriStateOption(string name, string description)
    {
        return new Option<bool?>(name, result =>
        {
            if (result.Tokens.Count == 0)
            {
                return true;
            }

            if (bool.TryParse(result.Tokens[0].Value, out var parsed))
            {
                return parsed;
            }

            result.ErrorMessage = $"Invalid value for {name}. Expected 'true' or 'false'.";
            return null;
        })
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = description
        };
    }
}

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using System.Linq;
using System.Text;
using Osm.Pipeline.Application;

namespace Osm.Cli.Commands.Binders;

internal sealed class UatUsersOptionBinder : BinderBase<UatUsersOverrides>, ICommandOptionSource
{
    public UatUsersOptionBinder()
    {
        EnableOption = new Option<bool>("--enable-uat-users", "Enable generation of UAT user remapping artifacts.");
        UatConnectionOption = new Option<string?>("--uat-conn", "ADO.NET connection string for the UAT database (required when UAT user remapping is enabled).");
        UserSchemaOption = new Option<string?>("--user-schema", () => "dbo", "Schema that owns the user table.");
        UserTableOption = new Option<string?>("--user-table", () => "User", "User table name (schema-qualified values are supported).");
        UserIdOption = new Option<string?>("--user-id-column", () => "Id", "Primary key column for the user table.");
        IncludeColumnsOption = new Option<string[]>(
            name: "--include-columns",
            description: "Restrict catalog to specific column names.",
            parseArgument: static result => result.Tokens
                .Select(token => token.Value)
                .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray())
        {
            AllowMultipleArgumentsPerToken = true
        };
        UserMapOption = new Option<string?>("--user-map", "Path to a CSV containing SourceUserId,TargetUserId mappings.");
        UserDdlOption = new Option<string?>(
            "--user-ddl",
            "SQL seed script or CSV export of dbo.User containing allowed user identifiers (auto-detected).");
        UserIdsOption = new Option<string?>("--user-ids", "Optional CSV or text file containing one allowed user identifier per row.");
        SnapshotOption = new Option<string?>("--snapshot", "Optional path to cache foreign key scans as a snapshot.");
        UserEntityIdOption = new Option<string?>("--user-entity-id", "Optional override identifier for the user entity (accepts btGUID*GUID, physical name, or numeric id).");
    }

    public Option<bool> EnableOption { get; }

    public Option<string?> UatConnectionOption { get; }

    public Option<string?> UserSchemaOption { get; }

    public Option<string?> UserTableOption { get; }

    public Option<string?> UserIdOption { get; }

    public Option<string[]> IncludeColumnsOption { get; }

    public Option<string?> UserMapOption { get; }

    public Option<string?> UserDdlOption { get; }

    public Option<string?> UserIdsOption { get; }

    public Option<string?> SnapshotOption { get; }

    public Option<string?> UserEntityIdOption { get; }

    public IEnumerable<Option> Options
    {
        get
        {
            yield return EnableOption;
            yield return UatConnectionOption;
            yield return UserSchemaOption;
            yield return UserTableOption;
            yield return UserIdOption;
            yield return IncludeColumnsOption;
            yield return UserMapOption;
            yield return UserDdlOption;
            yield return UserIdsOption;
            yield return SnapshotOption;
            yield return UserEntityIdOption;
        }
    }

    protected override UatUsersOverrides GetBoundValue(BindingContext bindingContext)
        => Bind(bindingContext?.ParseResult ?? throw new ArgumentNullException(nameof(bindingContext)));

    public UatUsersOverrides Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        var enabled = parseResult.GetValueForOption(EnableOption);
        if (!enabled)
        {
            return UatUsersOverrides.Disabled;
        }

        var userSchema = parseResult.GetValueForOption(UserSchemaOption) ?? "dbo";
        var userTable = parseResult.GetValueForOption(UserTableOption) ?? "User";

        if (userTable.Contains('.', StringComparison.Ordinal))
        {
            var parts = SplitTableIdentifier(userTable);
            userSchema = parts.Schema;
            userTable = parts.Table;
        }

        var includeColumns = parseResult.GetValueForOption(IncludeColumnsOption);
        var normalizedColumns = includeColumns is null || includeColumns.Length == 0
            ? Array.Empty<string>()
            : includeColumns.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

        return new UatUsersOverrides(
            Enabled: true,
            ConnectionString: parseResult.GetValueForOption(UatConnectionOption),
            UserSchema: userSchema,
            UserTable: userTable,
            UserIdColumn: parseResult.GetValueForOption(UserIdOption) ?? "Id",
            IncludeColumns: normalizedColumns,
            UserMapPath: parseResult.GetValueForOption(UserMapOption),
            AllowedUsersSqlPath: parseResult.GetValueForOption(UserDdlOption),
            AllowedUserIdsPath: parseResult.GetValueForOption(UserIdsOption),
            SnapshotPath: parseResult.GetValueForOption(SnapshotOption),
            UserEntityIdentifier: parseResult.GetValueForOption(UserEntityIdOption));
    }

    internal static (string Schema, string Table) SplitTableIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return ("dbo", "User");
        }

        var parts = ParseIdentifierParts(identifier.Trim());
        if (parts.Count == 0)
        {
            return ("dbo", "User");
        }

        if (parts.Count == 1)
        {
            var tableOnly = parts[0];
            var singleTable = string.IsNullOrWhiteSpace(tableOnly) ? "User" : tableOnly;
            return ("dbo", singleTable);
        }

        var schemaPart = parts[^2];
        var tablePart = parts[^1];
        var resolvedSchema = string.IsNullOrWhiteSpace(schemaPart) ? "dbo" : schemaPart;
        var resolvedTable = string.IsNullOrWhiteSpace(tablePart) ? "User" : tablePart;
        return (resolvedSchema, resolvedTable);
    }

    private static List<string> ParseIdentifierParts(string identifier)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return parts;
        }

        var span = identifier.AsSpan();
        var builder = new StringBuilder();
        var bracketDepth = 0;
        var inQuotes = false;

        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            switch (ch)
            {
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    continue;
                case '"':
                    if (bracketDepth == 0)
                    {
                        inQuotes = !inQuotes;
                        continue;
                    }
                    break;
                case '.':
                    if (bracketDepth == 0 && !inQuotes)
                    {
                        parts.Add(builder.ToString().Trim());
                        builder.Clear();
                        continue;
                    }
                    break;
            }

            builder.Append(ch);
        }

        parts.Add(builder.ToString().Trim());
        return parts;
    }
}

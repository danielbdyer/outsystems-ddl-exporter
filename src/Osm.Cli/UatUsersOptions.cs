using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Osm.Cli;

public sealed class UatUsersOptions
{
    public UatUsersOptions(
        string? modelPath,
        string? uatConnectionString,
        bool fromLiveMetadata,
        string userSchema,
        string userTable,
        string userIdColumn,
        IEnumerable<string>? includeColumns,
        string outputDirectory,
        string? userMapPath,
        string? allowedUsersSqlPath,
        string? allowedUserIdsPath,
        string? snapshotPath,
        string? userEntityIdentifier)
    {
        ModelPath = string.IsNullOrWhiteSpace(modelPath) ? null : Path.GetFullPath(modelPath.Trim());
        UatConnectionString = string.IsNullOrWhiteSpace(uatConnectionString) ? null : uatConnectionString.Trim();
        FromLiveMetadata = fromLiveMetadata;
        UserSchema = string.IsNullOrWhiteSpace(userSchema) ? "dbo" : userSchema.Trim();
        UserTable = string.IsNullOrWhiteSpace(userTable) ? "User" : userTable.Trim();
        UserIdColumn = string.IsNullOrWhiteSpace(userIdColumn) ? "Id" : userIdColumn.Trim();
        IncludeColumns = NormalizeIncludeColumns(includeColumns);
        OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetFullPath("./_artifacts")
            : Path.GetFullPath(outputDirectory);
        UserMapPath = string.IsNullOrWhiteSpace(userMapPath) ? null : Path.GetFullPath(userMapPath.Trim());
        AllowedUsersSqlPath = string.IsNullOrWhiteSpace(allowedUsersSqlPath)
            ? null
            : Path.GetFullPath(allowedUsersSqlPath.Trim());
        AllowedUserIdsPath = string.IsNullOrWhiteSpace(allowedUserIdsPath)
            ? null
            : Path.GetFullPath(allowedUserIdsPath.Trim());

        if (AllowedUsersSqlPath is null && AllowedUserIdsPath is null)
        {
            throw new ArgumentException("Either --user-ddl or --user-ids must be provided.");
        }

        SnapshotPath = string.IsNullOrWhiteSpace(snapshotPath) ? null : Path.GetFullPath(snapshotPath.Trim());
        UserEntityIdentifier = string.IsNullOrWhiteSpace(userEntityIdentifier) ? null : userEntityIdentifier.Trim();
    }

    public string? ModelPath { get; }

    public string? UatConnectionString { get; }

    public bool FromLiveMetadata { get; }

    public string UserSchema { get; }

    public string UserTable { get; }

    public string UserIdColumn { get; }

    public ImmutableArray<string> IncludeColumns { get; }

    public string OutputDirectory { get; }

    public string? UserMapPath { get; }

    public string? AllowedUsersSqlPath { get; }

    public string? AllowedUserIdsPath { get; }

    public string? SnapshotPath { get; }

    public string? UserEntityIdentifier { get; }

    private static ImmutableArray<string> NormalizeIncludeColumns(IEnumerable<string>? includeColumns)
    {
        if (includeColumns is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var value in includeColumns)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                ordered.Add(trimmed);
            }
        }

        return ordered.ToImmutableArray();
    }
}

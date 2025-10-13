using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

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
        string? userMapPath)
    {
        ModelPath = string.IsNullOrWhiteSpace(modelPath) ? null : Path.GetFullPath(modelPath.Trim());
        UatConnectionString = string.IsNullOrWhiteSpace(uatConnectionString) ? null : uatConnectionString.Trim();
        FromLiveMetadata = fromLiveMetadata;
        UserSchema = string.IsNullOrWhiteSpace(userSchema) ? "dbo" : userSchema.Trim();
        UserTable = string.IsNullOrWhiteSpace(userTable) ? "User" : userTable.Trim();
        UserIdColumn = string.IsNullOrWhiteSpace(userIdColumn) ? "Id" : userIdColumn.Trim();
        IncludeColumns = includeColumns?.Select(static value => value.Trim()).Where(static value => value.Length > 0).ToImmutableArray() ?? ImmutableArray<string>.Empty;
        OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetFullPath("./_artifacts")
            : Path.GetFullPath(outputDirectory);
        UserMapPath = string.IsNullOrWhiteSpace(userMapPath) ? null : Path.GetFullPath(userMapPath.Trim());
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
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Osm.Pipeline.UatUsers;

public sealed class UatUsersContext
{
    private IReadOnlyList<UserFkColumn> _userFkCatalog = Array.Empty<UserFkColumn>();
    private IReadOnlyList<UserMappingEntry> _userMappings = Array.Empty<UserMappingEntry>();

    public UatUsersContext(
        IUserSchemaGraph schemaGraph,
        UatUsersArtifacts artifacts,
        string userSchema,
        string userTable,
        string userIdColumn,
        IReadOnlyCollection<string>? includeColumns,
        string userMapPath,
        bool fromLiveMetadata)
    {
        SchemaGraph = schemaGraph ?? throw new ArgumentNullException(nameof(schemaGraph));
        Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));

        if (string.IsNullOrWhiteSpace(userSchema))
        {
            throw new ArgumentException("User schema must be provided.", nameof(userSchema));
        }

        if (string.IsNullOrWhiteSpace(userTable))
        {
            throw new ArgumentException("User table must be provided.", nameof(userTable));
        }

        if (string.IsNullOrWhiteSpace(userIdColumn))
        {
            throw new ArgumentException("User identifier column must be provided.", nameof(userIdColumn));
        }

        if (string.IsNullOrWhiteSpace(userMapPath))
        {
            throw new ArgumentException("User map path must be provided.", nameof(userMapPath));
        }

        UserSchema = userSchema.Trim();
        UserTable = userTable.Trim();
        UserIdColumn = userIdColumn.Trim();
        IncludeColumns = includeColumns is null || includeColumns.Count == 0
            ? null
            : includeColumns.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        UserMapPath = Path.GetFullPath(userMapPath);
        FromLiveMetadata = fromLiveMetadata;
    }

    public IUserSchemaGraph SchemaGraph { get; }

    public UatUsersArtifacts Artifacts { get; }

    public string UserSchema { get; }

    public string UserTable { get; }

    public string UserIdColumn { get; }

    public ISet<string>? IncludeColumns { get; }

    public string UserMapPath { get; }

    public bool FromLiveMetadata { get; }

    public IReadOnlyList<UserFkColumn> UserFkCatalog => _userFkCatalog;

    public IReadOnlyList<UserMappingEntry> UserMap => _userMappings;

    public void SetUserFkCatalog(IReadOnlyList<UserFkColumn> catalog)
    {
        _userFkCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void SetUserMap(IReadOnlyList<UserMappingEntry> mappings)
    {
        _userMappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
    }
}

namespace Osm.Cli;

public sealed record UatUsersOptionOrigins(
    bool ModelPathFromConfiguration,
    bool ConnectionStringFromConfiguration,
    bool FromLiveMetadataFromConfiguration,
    bool UserSchemaFromConfiguration,
    bool UserTableFromConfiguration,
    bool UserIdColumnFromConfiguration,
    bool IncludeColumnsFromConfiguration,
    bool OutputDirectoryFromConfiguration,
    bool UserMapPathFromConfiguration,
    bool UatUserInventoryPathFromConfiguration,
    bool QaUserInventoryPathFromConfiguration,
    bool SnapshotPathFromConfiguration,
    bool UserEntityIdentifierFromConfiguration)
{
    public static UatUsersOptionOrigins None { get; } = new(
        ModelPathFromConfiguration: false,
        ConnectionStringFromConfiguration: false,
        FromLiveMetadataFromConfiguration: false,
        UserSchemaFromConfiguration: false,
        UserTableFromConfiguration: false,
        UserIdColumnFromConfiguration: false,
        IncludeColumnsFromConfiguration: false,
        OutputDirectoryFromConfiguration: false,
        UserMapPathFromConfiguration: false,
        UatUserInventoryPathFromConfiguration: false,
        QaUserInventoryPathFromConfiguration: false,
        SnapshotPathFromConfiguration: false,
        UserEntityIdentifierFromConfiguration: false);
}

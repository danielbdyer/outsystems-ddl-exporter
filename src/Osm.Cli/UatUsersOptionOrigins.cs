namespace Osm.Cli;

public sealed record UatUsersOptionOrigins(
    bool ModelPathFromConfiguration,
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
    bool UserEntityIdentifierFromConfiguration,
    bool MatchingStrategyFromConfiguration,
    bool MatchingAttributeFromConfiguration,
    bool MatchingRegexFromConfiguration,
    bool FallbackModeFromConfiguration,
    bool FallbackTargetsFromConfiguration)
{
    public static UatUsersOptionOrigins None { get; } = new(
        ModelPathFromConfiguration: false,
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
        UserEntityIdentifierFromConfiguration: false,
        MatchingStrategyFromConfiguration: false,
        MatchingAttributeFromConfiguration: false,
        MatchingRegexFromConfiguration: false,
        FallbackModeFromConfiguration: false,
        FallbackTargetsFromConfiguration: false);
}

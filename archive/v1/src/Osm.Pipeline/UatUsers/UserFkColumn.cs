namespace Osm.Pipeline.UatUsers;

public readonly record struct UserFkColumn(
    string SchemaName,
    string TableName,
    string ColumnName,
    string ForeignKeyName);

public readonly record struct UserMappingEntry(UserIdentifier SourceUserId, UserIdentifier? TargetUserId, string? Rationale);

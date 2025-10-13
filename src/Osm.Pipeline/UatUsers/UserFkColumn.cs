namespace Osm.Pipeline.UatUsers;

public readonly record struct UserFkColumn(
    string SchemaName,
    string TableName,
    string ColumnName,
    string ForeignKeyName);

public readonly record struct UserMappingEntry(long SourceUserId, long TargetUserId, string? Note);

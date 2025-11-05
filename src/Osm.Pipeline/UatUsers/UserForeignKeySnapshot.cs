using System;
using System.Collections.Generic;

namespace Osm.Pipeline.UatUsers;

public sealed class UserForeignKeySnapshot
{
    public DateTimeOffset CapturedAt { get; init; }

    public string SourceFingerprint { get; init; } = string.Empty;

    public IReadOnlyList<UserIdentifier> AllowedUserIds { get; init; } = Array.Empty<UserIdentifier>();

    public IReadOnlyList<UserIdentifier> OrphanUserIds { get; init; } = Array.Empty<UserIdentifier>();

    public IReadOnlyList<UserForeignKeySnapshotColumn> Columns { get; init; } = Array.Empty<UserForeignKeySnapshotColumn>();
}

public sealed class UserForeignKeySnapshotColumn
{
    public string Schema { get; init; } = string.Empty;

    public string Table { get; init; } = string.Empty;

    public string Column { get; init; } = string.Empty;

    public IReadOnlyList<UserForeignKeySnapshotValue> Values { get; init; } = Array.Empty<UserForeignKeySnapshotValue>();
}

public sealed class UserForeignKeySnapshotValue
{
    public UserIdentifier UserId { get; init; }

    public long RowCount { get; init; }
}

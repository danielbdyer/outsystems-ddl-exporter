using System;
using System.Collections.Generic;

namespace Osm.Pipeline.UatUsers;

public sealed class UserForeignKeySnapshot
{
    public DateTimeOffset CapturedAt { get; init; }

    public string SourceFingerprint { get; init; } = string.Empty;

    public IReadOnlyList<long> AllowedUserIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> OrphanUserIds { get; init; } = Array.Empty<long>();

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
    public long UserId { get; init; }

    public long RowCount { get; init; }
}

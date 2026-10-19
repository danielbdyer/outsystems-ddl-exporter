using System;

namespace Osm.Dmm;

[Flags]
public enum DmmComparisonFeatures
{
    None = 0,
    Columns = 1 << 0,
    PrimaryKeys = 1 << 1,
    Indexes = 1 << 2,
    ForeignKeys = 1 << 3,
    ExtendedProperties = 1 << 4,
}

namespace Osm.Domain.Model;

public enum IndexKind
{
    Unknown = 0,
    PrimaryKey,
    UniqueConstraint,
    UniqueIndex,
    NonUniqueIndex,
    ClusteredIndex,
    NonClusteredIndex,
}

using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

public sealed record EntityContext(
    EntityModel Entity,
    AttributeModel Attribute,
    ColumnIdentity Identity,
    ColumnProfile? ColumnProfile,
    UniqueCandidateProfile? UniqueProfile,
    ForeignKeyReality? ForeignKeyReality,
    EntityModel? ForeignKeyTarget,
    bool SingleColumnUniqueClean,
    bool SingleColumnUniqueHasDuplicates,
    bool CompositeUniqueClean,
    bool CompositeUniqueHasDuplicates)
{
    public ColumnCoordinate Column => Identity.Coordinate;
}

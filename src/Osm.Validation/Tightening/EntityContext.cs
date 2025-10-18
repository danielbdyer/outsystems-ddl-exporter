using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

public sealed record EntityContext(
    EntityModel Entity,
    AttributeModel Attribute,
    ColumnCoordinate Column,
    ColumnProfile? ColumnProfile,
    UniqueCandidateProfile? UniqueProfile,
    ForeignKeyReality? ForeignKeyReality,
    EntityModel? ForeignKeyTarget,
    bool SingleColumnUniqueClean,
    bool SingleColumnUniqueHasDuplicates,
    bool CompositeUniqueClean,
    bool CompositeUniqueHasDuplicates);

using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening.Signals;

internal readonly record struct NullabilitySignalContext(
    TighteningOptions Options,
    EntityModel Entity,
    AttributeModel Attribute,
    ColumnCoordinate Coordinate,
    ColumnProfile? ColumnProfile,
    UniqueCandidateProfile? UniqueProfile,
    ForeignKeyReality? ForeignKeyReality,
    EntityModel? ForeignKeyTarget,
    bool IsSingleUniqueClean,
    bool HasSingleUniqueDuplicates,
    bool IsCompositeUniqueClean,
    bool HasCompositeUniqueDuplicates)
{
    public bool HasProfile => ColumnProfile is not null;
}

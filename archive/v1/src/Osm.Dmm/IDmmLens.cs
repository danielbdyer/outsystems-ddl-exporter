using System.Collections.Generic;
using Osm.Domain.Abstractions;

namespace Osm.Dmm;

public interface IDmmLens<in TSource>
{
    Result<IReadOnlyList<DmmTable>> Project(TSource source);
}

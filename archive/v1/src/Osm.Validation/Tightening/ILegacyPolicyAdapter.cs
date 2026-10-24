using Osm.Domain.Configuration;

namespace Osm.Validation.Tightening;

public interface ILegacyPolicyAdapter
{
    TighteningOptions Adapt(TighteningMode mode);
}

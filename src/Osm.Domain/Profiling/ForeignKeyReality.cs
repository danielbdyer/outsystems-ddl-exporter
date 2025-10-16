using Osm.Domain.Abstractions;

namespace Osm.Domain.Profiling;

public sealed record ForeignKeyReality(
    ForeignKeyReference Reference,
    bool HasOrphan,
    bool IsNoCheck,
    ProfilingProbeStatus ProbeStatus)
{
    public static Result<ForeignKeyReality> Create(
        ForeignKeyReference reference,
        bool hasOrphan,
        bool isNoCheck,
        ProfilingProbeStatus probeStatus)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (probeStatus is null)
        {
            throw new ArgumentNullException(nameof(probeStatus));
        }

        return Result<ForeignKeyReality>.Success(new ForeignKeyReality(reference, hasOrphan, isNoCheck, probeStatus));
    }
}

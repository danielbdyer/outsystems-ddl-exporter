using System;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Profiling;

public sealed record ForeignKeyReality(
    ForeignKeyReference Reference,
    bool HasOrphan,
    long OrphanCount,
    bool IsNoCheck,
    ProfilingProbeStatus ProbeStatus,
    ForeignKeyOrphanSample? OrphanSample)
{
    public static Result<ForeignKeyReality> Create(
        ForeignKeyReference reference,
        bool hasOrphan,
        long orphanCount,
        bool isNoCheck,
        ProfilingProbeStatus probeStatus,
        ForeignKeyOrphanSample? orphanSample = null)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (probeStatus is null)
        {
            throw new ArgumentNullException(nameof(probeStatus));
        }

        if (orphanCount < 0)
        {
            return Result<ForeignKeyReality>.Failure(
                ValidationError.Create("profile.foreignKey.orphanCount.invalid", "Orphan count cannot be negative."));
        }

        if (!hasOrphan && orphanCount > 0)
        {
            hasOrphan = true;
        }

        if (hasOrphan && orphanCount == 0)
        {
            orphanCount = 1;
        }

        return Result<ForeignKeyReality>.Success(new ForeignKeyReality(reference, hasOrphan, orphanCount, isNoCheck, probeStatus, orphanSample));
    }
}

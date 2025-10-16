using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Profiling;

public sealed record UniqueCandidateProfile(
    SchemaName Schema,
    TableName Table,
    ColumnName Column,
    bool HasDuplicate,
    ProfilingProbeStatus ProbeStatus)
{
    public static Result<UniqueCandidateProfile> Create(
        SchemaName schema,
        TableName table,
        ColumnName column,
        bool hasDuplicate,
        ProfilingProbeStatus probeStatus)
    {
        if (probeStatus is null)
        {
            throw new ArgumentNullException(nameof(probeStatus));
        }

        return Result<UniqueCandidateProfile>.Success(new UniqueCandidateProfile(schema, table, column, hasDuplicate, probeStatus));
    }
}

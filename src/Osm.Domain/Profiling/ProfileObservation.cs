using System;

namespace Osm.Domain.Profiling;

public enum ProfileObservationKind
{
    Column,
    UniqueCandidate,
    CompositeUniqueCandidate,
    ForeignKey
}

public sealed record ProfileObservation
{
    private ProfileObservation(
        ProfileObservationKind kind,
        ColumnProfile? column,
        UniqueCandidateProfile? uniqueCandidate,
        CompositeUniqueCandidateProfile? compositeUniqueCandidate,
        ForeignKeyReality? foreignKey)
    {
        Kind = kind;
        Column = column;
        UniqueCandidate = uniqueCandidate;
        CompositeUniqueCandidate = compositeUniqueCandidate;
        ForeignKey = foreignKey;
    }

    public ProfileObservationKind Kind { get; }

    public ColumnProfile? Column { get; }

    public UniqueCandidateProfile? UniqueCandidate { get; }

    public CompositeUniqueCandidateProfile? CompositeUniqueCandidate { get; }

    public ForeignKeyReality? ForeignKey { get; }

    public static ProfileObservation ForColumn(ColumnProfile column)
    {
        if (column is null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        return new ProfileObservation(ProfileObservationKind.Column, column, null, null, null);
    }

    public static ProfileObservation ForUniqueCandidate(UniqueCandidateProfile candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        return new ProfileObservation(ProfileObservationKind.UniqueCandidate, null, candidate, null, null);
    }

    public static ProfileObservation ForCompositeUniqueCandidate(CompositeUniqueCandidateProfile candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        return new ProfileObservation(ProfileObservationKind.CompositeUniqueCandidate, null, null, candidate, null);
    }

    public static ProfileObservation ForForeignKey(ForeignKeyReality foreignKey)
    {
        if (foreignKey is null)
        {
            throw new ArgumentNullException(nameof(foreignKey));
        }

        return new ProfileObservation(ProfileObservationKind.ForeignKey, null, null, null, foreignKey);
    }
}

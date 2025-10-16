namespace Osm.Domain.Profiling;

public static class ProfilingInsightCodes
{
    public const string HighNullDensity = "PROFILE_HIGH_NULL_DENSITY";
    public const string NullFreeButNullable = "PROFILE_NULL_FREE_NULLABLE";
    public const string PhysicalNullViolations = "PROFILE_PHYSICAL_NULL_VIOLATION";
    public const string UniqueCandidateDuplicates = "PROFILE_UNIQUE_DUPLICATES";
    public const string UniqueCandidateOpportunity = "PROFILE_UNIQUE_OPPORTUNITY";
    public const string CompositeUniqueDuplicates = "PROFILE_COMPOSITE_DUPLICATES";
    public const string CompositeUniqueOpportunity = "PROFILE_COMPOSITE_OPPORTUNITY";
    public const string ForeignKeyOrphans = "PROFILE_FOREIGN_KEY_ORPHANS";
    public const string ForeignKeyUntrusted = "PROFILE_FOREIGN_KEY_UNTRUSTED";
    public const string ForeignKeyOpportunity = "PROFILE_FOREIGN_KEY_OPPORTUNITY";
}

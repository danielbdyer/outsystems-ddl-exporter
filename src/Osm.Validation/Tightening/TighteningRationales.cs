namespace Osm.Validation.Tightening;

public static class TighteningRationales
{
    public const string PrimaryKey = "PK";
    public const string PhysicalNotNull = "PHYSICAL_NOT_NULL";
    public const string UniqueNoNulls = "UNIQUE_NO_NULLS";
    public const string CompositeUniqueNoNulls = "COMPOSITE_UNIQUE_NO_NULLS";
    public const string DataNoNulls = "DATA_NO_NULLS";
    public const string DataHasNulls = "DATA_HAS_NULLS";
    public const string NullBudgetEpsilon = "NULL_BUDGET_EPSILON";
    public const string Mandatory = "MANDATORY";
    public const string DefaultPresent = "DEFAULT_PRESENT";
    public const string ForeignKeyEnforced = "FK_ENFORCED";
    public const string DeleteRuleIgnore = "DELETE_RULE_IGNORE";
    public const string DataHasOrphans = "DATA_HAS_ORPHANS";
    public const string DatabaseConstraintPresent = "DB_CONSTRAINT_PRESENT";
    public const string PolicyEnableCreation = "POLICY_ENABLE_CREATION";
    public const string CrossSchema = "CROSS_SCHEMA";
    public const string CrossCatalog = "CROSS_CATALOG";
    public const string ForeignKeyCreationDisabled = "FK_CREATION_DISABLED";
    public const string ProfileMissing = "PROFILE_MISSING";
    public const string RemediateBeforeTighten = "REMEDIATE_BEFORE_TIGHTEN";
    public const string UniqueDuplicatesPresent = "UNIQUE_DUPLICATES_PRESENT";
    public const string CompositeUniqueDuplicatesPresent = "COMPOSITE_UNIQUE_DUPLICATES_PRESENT";
    public const string PhysicalUniqueKey = "PHYSICAL_UNIQUE_KEY";
    public const string UniquePolicyDisabled = "UNIQUE_POLICY_DISABLED";
}

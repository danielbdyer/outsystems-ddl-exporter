namespace Osm.Pipeline.UatUsers;

/// <summary>
/// Canonical file and directory names for the UAT user-remapping artifact
/// bundle. Both the emit side (pipeline steps, manifest) and the read side
/// (verifier, CLI) must agree on these names; centralizing them here removes
/// the drift hazard of the literals being duplicated across ~7 files.
/// </summary>
public static class UatUsersArtifactNames
{
    /// <summary>Subdirectory (relative to the artifact root) holding the bundle.</summary>
    public const string Directory = "uat-users";

    /// <summary>Operator-editable user-map template emitted for review.</summary>
    public const string UserMapTemplate = "00_user_map.template.csv";

    /// <summary>Default resolved user-map file consumed when no override is supplied.</summary>
    public const string UserMap = "00_user_map.csv";

    /// <summary>Preview of the planned remapping.</summary>
    public const string Preview = "01_preview.csv";

    /// <summary>Idempotent SQL apply script.</summary>
    public const string ApplyScript = "02_apply_user_remap.sql";

    /// <summary>Foreign-key catalog discovered for the user table.</summary>
    public const string Catalog = "03_catalog.txt";

    /// <summary>Matching report describing how source users mapped to targets.</summary>
    public const string MatchingReport = "04_matching_report.csv";

    /// <summary>Verification report produced by the verifier.</summary>
    public const string VerificationReport = "verification-report.json";
}

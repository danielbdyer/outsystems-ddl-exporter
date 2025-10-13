namespace Osm.Pipeline.RemapUsers;

/// <summary>
/// Determines how the pipeline should handle snapshot rows whose foreign keys
/// cannot be matched to a UAT user.
/// </summary>
public enum RemapUsersPolicy
{
    Reassign,
    Prune
}

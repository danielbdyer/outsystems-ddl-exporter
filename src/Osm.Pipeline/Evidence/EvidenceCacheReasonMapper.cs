namespace Osm.Pipeline.Evidence;

internal static class EvidenceCacheReasonMapper
{
    public static string Map(EvidenceCacheInvalidationReason reason)
    {
        return reason switch
        {
            EvidenceCacheInvalidationReason.None => "cache.reused",
            EvidenceCacheInvalidationReason.ManifestMissing => "manifest.missing",
            EvidenceCacheInvalidationReason.ManifestInvalid => "manifest.invalid",
            EvidenceCacheInvalidationReason.ManifestVersionMismatch => "manifest.version.mismatch",
            EvidenceCacheInvalidationReason.KeyMismatch => "cache.key.mismatch",
            EvidenceCacheInvalidationReason.CommandMismatch => "cache.command.mismatch",
            EvidenceCacheInvalidationReason.ManifestExpired => "manifest.expired",
            EvidenceCacheInvalidationReason.ModuleSelectionChanged => "module.selection.changed",
            EvidenceCacheInvalidationReason.MetadataMismatch => "metadata.mismatch",
            EvidenceCacheInvalidationReason.ArtifactsMismatch => "artifacts.mismatch",
            EvidenceCacheInvalidationReason.RefreshRequested => "refresh.requested",
            _ => reason.ToString(),
        };
    }
}

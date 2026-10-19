namespace Projection.Tests

/// Espace-key parameterization of an OSSYS seed script (Tier 1 of the
/// mock-environment fixture design, 2026-07-06). Real OutSystems espacing
/// gives the SAME logical model DIFFERENT physical `OSUSR_<espace>_<name>`
/// prefixes per environment; the transform shifts every `OSUSR_*` physical
/// name — table names in DDL, `ossys_Entity.Physical_Table_Name` metamodel
/// rows, and the constraint/default/check names OutSystems derives from the
/// physical table — while GUID SS_Keys, logical names, and structure stay
/// fixed. Anchored regex, not a blind `String.Replace`: the transform is a
/// named, deliberate operation.
[<RequireQualifiedAccess>]
module OssysSeedBuilder =

    /// Shift every `OSUSR_<KEY>_` physical prefix by prepending `newKey` to
    /// the espace key (`withEspaceKey "X"` turns `OSUSR_ABC_CUSTOMER` into
    /// `OSUSR_XABC_CUSTOMER` — byte-compatible with the espace-invariance
    /// canary's historical transform).
    let withEspaceKey (newKey: string) (seed: string) : string =
        System.Text.RegularExpressions.Regex.Replace(
            seed,
            @"OSUSR_([A-Za-z0-9]+)_",
            fun m -> "OSUSR_" + newKey + m.Groups.[1].Value + "_")

    /// The identity transform — the COMMON QA→UAT case: LifeTime promotion
    /// preserves the espace key, so both cells carry the SAME physical
    /// names. (Deliberately named so a test declares which case it proves.)
    let sameEspace (seed: string) : string = seed

    /// The CLONED-module transform (2026-07-09). A cloned espace is a DISTINCT
    /// OutSystems module — same logical entity/attribute names and structure,
    /// but every native GUID `SS_Key` re-minted (the clone is a different set of
    /// OSSYS rows). Contrast `withEspaceKey`, which KEEPS GUIDs and only shifts
    /// the physical prefix (renditions of ONE model); this re-mints so the two
    /// cells share NO identity and cannot align by SsKey — the case
    /// `NameAlignment.align` (by name) exists for.
    ///
    /// Every distinct `uniqueidentifier` literal is mapped through ONE
    /// deterministic old→new function (`MD5(oldGuid + cloneKey)`), applied
    /// UNIFORMLY — so an entity's `PrimaryKey_SS_Key` still equals its PK
    /// attribute's `SS_Key`, and a reference's `Referenced_*_SS_Key` still
    /// resolves (internal graph consistency preserved), while nothing collides
    /// with the source. The physical prefix is then shifted via `withEspaceKey`,
    /// so the clone's `OSUSR_*` names match the sibling espace cell's — the
    /// physical layout is a sibling; only the identities are new.
    let asClonedModule (cloneKey: string) (seed: string) : string =
        let guidRx =
            System.Text.RegularExpressions.Regex(
                @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")
        let remint (oldGuid: string) : string =
            use md5 = System.Security.Cryptography.MD5.Create()
            let bytes =
                md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(oldGuid.ToLowerInvariant() + "|clone|" + cloneKey))
            (System.Guid bytes).ToString()
        let reminted = guidRx.Replace(seed, fun m -> remint m.Value)
        withEspaceKey cloneKey reminted

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

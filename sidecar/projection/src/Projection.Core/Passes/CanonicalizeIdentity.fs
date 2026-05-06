namespace Projection.Core.Passes

open Projection.Core

/// The first enrichment pass. Brings the catalog into a canonical
/// representation by deterministically ordering every collection by SsKey:
///
///   * modules within a catalog,
///   * kinds within a module,
///   * attributes within a kind,
///   * references within a kind,
///   * static-row populations (when present).
///
/// This is the minimum normalization that makes T1 (determinism) hold
/// across runs whose input order varies (e.g., reader iteration order, set
/// vs. list source). It does not rewrite SsKey strings — a real Catalog
/// Reader may eventually surface the need (whitespace, Unicode form), at
/// which point this pass's `version` is bumped and the rule is added.
///
/// Identity-preserving: the pass never invents, drops, or re-keys an
/// identity (A3, A4). It emits a `Touched` lineage event per kind so that
/// downstream consumers can prove the pass ran (A25).
[<RequireQualifiedAccess>]
module CanonicalizeIdentity =

    /// Pass version. Per A23, lineage events carry this so functionally
    /// different versions of this pass produce distinguishable provenance.
    /// Bump in the same commit that changes the canonicalization rules.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "canonicalizeIdentity"

    let private canonicalizeStaticRows (rows: StaticRow list) : StaticRow list =
        rows |> List.sortBy (fun r -> r.Identifier)

    let private canonicalizeModality (m: ModalityMark) : ModalityMark =
        match m with
        | Static rows   -> Static (canonicalizeStaticRows rows)
        | TenantScoped  -> TenantScoped
        | SoftDeletable -> SoftDeletable

    let private canonicalizeKind (k: Kind) : Kind =
        { k with
            Attributes = k.Attributes |> List.sortBy (fun a -> a.SsKey)
            References = k.References |> List.sortBy (fun r -> r.SsKey)
            Modality   = k.Modality   |> List.map canonicalizeModality }

    let private canonicalizeModule (m: Module) : Module =
        { m with
            Kinds = m.Kinds |> List.map canonicalizeKind |> List.sortBy (fun k -> k.SsKey) }

    /// Build the lineage event recording that the pass observed a kind.
    let private touchedEvent (key: SsKey) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = key
          TransformKind = Touched }

    /// Run the pass over a catalog. Returns the canonicalized catalog
    /// wrapped in a lineage with one `Touched` event per kind.
    let run (c: Catalog) : Lineage<Catalog> =
        let canon =
            { Modules =
                c.Modules
                |> List.map canonicalizeModule
                |> List.sortBy (fun m -> m.SsKey) }
        let events =
            canon
            |> Catalog.allKinds
            |> List.map (fun k -> touchedEvent k.SsKey)
        Lineage.tellMany events (Lineage.ofValue canon)

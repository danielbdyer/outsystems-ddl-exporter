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
        | SystemOwned   -> SystemOwned
        // Chapter A.0' slice η — `Temporal` carries no order-sensitive
        // payload (period column names and history-table coordinates
        // are fixed identifiers, not orderable collections), so
        // canonicalization is identity.
        | Temporal _    -> m

    let private canonicalizeKind (k: Kind) : Kind =
        { k with
            Attributes = k.Attributes |> List.sortBy (fun a -> a.SsKey)
            References = k.References |> List.sortBy (fun r -> r.SsKey)
            Modality   = k.Modality   |> List.map canonicalizeModality }

    let private canonicalizeModule (m: Module) : Module =
        { m with
            Kinds = m.Kinds |> List.map canonicalizeKind |> List.sortBy (fun k -> k.SsKey) }

    /// Pillar 9 (chapter A.4.7 slice α): canonicalization-of-identity
    /// preserves data intention — sorting + normalization is reachable
    /// from `Project(catalog, Policy.empty, profile)` without operator
    /// opinion. Lands in the skeleton.
    let private classification : Classification = DataIntent

    /// Build the lineage event recording that the pass observed a kind.
    let private touchedEvent (key: SsKey) : LineageEvent =
        { PassName       = passName
          PassVersion    = version
          SsKey          = key
          TransformKind  = Touched
          Classification = classification }

    /// Run the pass over a catalog. Returns the canonicalized catalog
    /// wrapped in a lineage with one `Touched` event per kind.
    let run (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.canonicalizeIdentity"
        let canon =
            { Modules =
                c.Modules
                |> List.map canonicalizeModule
                |> List.sortBy (fun m -> m.SsKey)
              Sequences = c.Sequences }
        let events =
            canon
            |> Catalog.allKinds
            |> List.map (fun k -> touchedEvent k.SsKey)
        Lineage.ofValueAndEvents events canon

    /// Chapter A.4.7 slice γ. The pass's canonical registry surface
    /// per `DECISIONS 2026-05-15 (late) — Pillar 9`. Single
    /// `DataIntent` site (deterministic re-sort + modality
    /// normalization; reachable from `Project(catalog, Policy.empty,
    /// profile)`). The `Run` closure wraps the pass's existing
    /// `Lineage<Catalog>` output via `Lineage.map Diagnostics.ofValue`
    /// to match the registry's canonical `Lineage<Diagnostics<'Out>>`
    /// shape. Slice γ.2 (future) makes `let run` private; slice γ
    /// keeps it public during the transition.
    let registered : RegisteredTransform<Catalog, Catalog> =
        { Name = passName
          Domain = Identity
          StageBinding = Pass
          Sites =
            [ { SiteName = "canonicalize"
                Classification = classification
                Rationale = "Catalog-wide deterministic re-sort by SsKey at every level (modules / kinds / attributes / references) plus modality-mark normalization. No operator opinion enters; reachable from Project(catalog, Policy.empty, profile)." } ]
          Run = fun c -> run c |> Lineage.map Diagnostics.ofValue
          Status = Active }

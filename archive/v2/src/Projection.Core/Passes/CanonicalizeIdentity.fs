namespace Projection.Core.Passes

open Projection.Core

/// The first enrichment pass. Brings the catalog into a canonical
/// representation by deterministically ordering every collection:
///
///   * modules within a catalog — by SsKey,
///   * kinds within a module — by SsKey,
///   * attributes within a kind — by `(authored Service-Studio order
///     ascending, then SsKey)` (WP8 / NM-72; PK-first reorder retired
///     2026-07-21 for V1 authored-order parity),
///   * references within a kind — by SsKey,
///   * static-row populations (when present) — by identifier.
///
/// This is the minimum normalization that makes T1 (determinism) hold
/// across runs whose input order varies (e.g., reader iteration order, set
/// vs. list source). It does not rewrite SsKey strings — a real Catalog
/// Reader may eventually surface the need (whitespace, Unicode form), at
/// which point this pass's `version` is bumped and the rule is added.
///
/// WP8 / NM-72 — the attribute ordering honors the operator's authored
/// Service-Studio order (`Attribute.Order`, sourced from the real
/// `ossys_Entity_Attr.Order_Num`). Authored order ascending governs
/// directly — the PK-first reorder was retired 2026-07-21 for V1
/// authored-order parity: V1 emits columns in `Order_Num, AttrId` order,
/// never PK-first, and the emitted PK constraint names its key columns
/// regardless of their position in the CREATE TABLE body, so nothing
/// structural depends on PK columns leading. `Order = None` (hand-built
/// catalogs, the ReadSide reflection path) sorts last and falls back to
/// the SsKey tiebreak, so determinism holds for every source. Emission is
/// inherited uniformly — the SSDT, dacpac, and data-lane emitters all
/// iterate `Kind.Attributes` in list order, so ordering here is the
/// single ordering site.
///
/// Identity-preserving: the pass never invents, drops, or re-keys an
/// identity (A3, A4). It emits a `Touched` lineage event per kind so that
/// downstream consumers can prove the pass ran (A25).
[<RequireQualifiedAccess>]
module CanonicalizeIdentity =

    /// Pass version. Per A23, lineage events carry this so functionally
    /// different versions of this pass produce distinguishable provenance.
    /// Bump in the same commit that changes the canonicalization rules.
    /// v2 (WP8 / NM-72): attribute ordering is `(PK first, then authored
    /// `Attribute.Order` ascending, then SsKey)`, replacing the v1
    /// SsKey-only sort. v3 (2026-07-21): the PK-first rank is retired —
    /// ordering is `(authored `Attribute.Order` ascending, then SsKey)`
    /// for V1 authored-order parity (V1 emits columns in Order_Num, AttrId
    /// order, never PK-first).
    [<Literal>]
    let version : int = 3

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

    /// WP8 / NM-72 — the total order on attributes within a kind.
    /// `(authored-order rank, SsKey)` (the PK-first rank retired
    /// 2026-07-21 for V1 authored-order parity):
    ///   * authored-order rank `(0, n)` for `Order = Some n`, `(1, 0)`
    ///     for `Order = None` — authored attributes precede unauthored,
    ///     ascending within the authored band.
    ///   * SsKey is the final stable tiebreak — when two attributes share
    ///     the authored rank (e.g. both `Order = None`), the order is the
    ///     prior SsKey order, so a hand-built catalog (every `Order =
    ///     None`) is byte-identical to the v1 SsKey sort, and determinism
    ///     (T1) holds for every source.
    let private attributeSortKey (a: Attribute) : ((int * int) * SsKey) =
        let orderRank =
            match a.Order with
            | Some n -> (0, n)
            | None   -> (1, 0)
        (orderRank, a.SsKey)

    let private canonicalizeKind (k: Kind) : Kind =
        { k with
            Attributes = k.Attributes |> List.sortBy attributeSortKey
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
        LineageEvent.forPass passName version classification key Touched

    /// Run the pass over a catalog. Returns the canonicalized catalog
    /// wrapped in a lineage with one `Touched` event per kind.
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `CanonicalizeIdentity.registered.Run`
    let private run (c: Catalog) : Lineage<Catalog> =
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
                Rationale = "Catalog-wide deterministic re-sort at every level (modules / kinds / references by SsKey; attributes by authored Service-Studio order, then SsKey per WP8 / NM-72 — PK-first retired 2026-07-21 for V1 parity) plus modality-mark normalization. No operator opinion enters; the authored order is source evidence, not policy; reachable from Project(catalog, Policy.empty, profile)." } ]
          Run = fun c -> run c |> Lineage.map Diagnostics.ofValue
          Status = Active }

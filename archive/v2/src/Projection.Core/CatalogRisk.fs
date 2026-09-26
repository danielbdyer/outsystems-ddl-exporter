namespace Projection.Core

/// The DATA-RISK classification of a catalog diff — which schema transitions can
/// rewrite or LOSE existing row data on apply (recon #17). This is a DOMAIN
/// concern, not a render one: the `compare` / `diff` faces use it for the
/// "review these first" callout, and the migrate path needs the same predicates
/// to gate a destructive apply — so it lives in Core, pure and property-testable,
/// rather than stranded as private helpers inside the CLI's `Comparison` render
/// module (where it was untestable and un-reusable).
///
/// The predicates answer "does this facet transition touch data?"; the category
/// is the TYPED bucket the at-scale callout groups by (so 300 concerns read as
/// their shape — how many drops / type-changes / tightenings — not a flat wall).
/// The operator-facing *text* of each category is a render concern and stays at
/// the CLI boundary; Core owns only the typed value.

/// The kind of data risk a change carries — the closed bucket vocabulary the
/// danger callout groups by. `Reshape` is the catch-all for a data-touching
/// facet without a more specific bucket.
[<RequireQualifiedAccess>]
type RiskCategory =
    | Dropped
    | TypeChange
    | Tightening
    | PrimaryKeyChange
    | IdentityChange
    | CascadeDelete
    | UniquenessGained
    | Reshape

[<RequireQualifiedAccess>]
module CatalogRisk =

    /// An attribute facet transition that rewrites or risks existing row data: a
    /// type conversion (truncation / cast failure), `null → not null` (rows with
    /// null fail or need backfill), a primary-key change, an identity change. (A
    /// length / scale NARROWING is also a truncation risk — deferred: it needs an
    /// option-aware comparison; named, not silently dropped.)
    let attributeRewritesData (s: Attribute) (t: Attribute) (f: AttributeFacet) : bool =
        match f with
        | AttributeFacet.DataType    -> true
        | AttributeFacet.Nullability -> s.Column.IsNullable && not t.Column.IsNullable
        | AttributeFacet.PrimaryKey  -> true
        | AttributeFacet.Identity    -> true
        | _ -> false

    /// A reference facet transition that risks data: gaining `ON DELETE CASCADE` (a
    /// future delete now cascades to child rows).
    let referenceRewritesData (s: Reference) (t: Reference) (f: ReferenceFacet) : bool =
        match f with
        | ReferenceFacet.OnDelete -> s.OnDelete <> Cascade && t.OnDelete = Cascade
        | _ -> false

    /// An index facet transition that fails on existing data: gaining uniqueness (a
    /// `unique` / `primary key` index errors on apply if duplicates already exist).
    let indexRewritesData (s: Index) (t: Index) (f: IndexFacet) : bool =
        match f with
        | IndexFacet.Uniqueness -> not (IndexUniqueness.isUnique s.Uniqueness) && IndexUniqueness.isUnique t.Uniqueness
        | _ -> false

    /// The risk CATEGORY of a data-touching attribute facet — the typed bucket the
    /// callout groups by at scale. Total over the facet DU; a non-data facet falls
    /// to the generic `Reshape` (it is only ever asked for facets that already
    /// passed `attributeRewritesData`, so the catch-all is the safe floor).
    let attributeRiskCategory (f: AttributeFacet) : RiskCategory =
        match f with
        | AttributeFacet.DataType    -> RiskCategory.TypeChange
        | AttributeFacet.Nullability -> RiskCategory.Tightening
        | AttributeFacet.PrimaryKey  -> RiskCategory.PrimaryKeyChange
        | AttributeFacet.Identity    -> RiskCategory.IdentityChange
        | _                          -> RiskCategory.Reshape

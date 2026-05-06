namespace Projection.Core

/// Selection axis of `Policy` (A12 amended). Determines which kinds
/// participate in a projection. The closed three-way discriminant covers
/// "all" (the default), "include only this set", and "exclude this set."
/// Wider selectors (predicate-driven, profile-driven) appear when admire
/// passes surface them.
type SelectionPolicy =
    /// Every kind in the catalog participates. Default.
    | IncludeAll
    /// Only kinds whose SsKey is in this set participate.
    | IncludeOnly of SsKey Set
    /// Every kind participates except those whose SsKey is in this set.
    | ExcludeOnly of SsKey Set


/// Emission axis. Which artifact families a projection emits. The booleans
/// are deliberate; orthogonality of schema / data / diagnostics is the
/// algebra's commitment (decomposition Vector 2). When emission shapes
/// multiply, this record grows fields rather than packing flags into a DU.
type EmissionPolicy = {
    EmitSchema      : bool
    EmitData        : bool
    EmitDiagnostics : bool
}


/// Insertion axis. How data artifacts are applied to the target. For
/// schema-only configurations this is `SchemaOnly`. The four variants
/// match the masterwork's `InsertionStrategy` (lines 580–666).
type InsertionPolicy =
    | SchemaOnly
    | InsertNew
    | Merge
    | TruncateAndInsert


/// The three-axis policy aggregate (A12 amended). Each axis is its own
/// structured value; the three are composed in a single record. Changing
/// one axis does not constrain the others. `Policy.empty` is the
/// no-policy default — schema-only emission, no selection filter, no
/// insertion semantics — and is a first-class input for use cases that
/// need none of the axes.
type Policy = {
    Selection : SelectionPolicy
    Emission  : EmissionPolicy
    Insertion : InsertionPolicy
}


/// The three substantive inputs to `Project = Π ∘ E` per A6 amended.
/// Bundling them into a single record lets passes name their triple
/// explicitly when they consume more than one.
///
/// Use cases that consume only Catalog (e.g., `canonicalizeIdentity`)
/// continue to take `Catalog` directly; passes that need Policy or
/// Profile evidence accept `ProjectionInput` (or destructure as needed).
type ProjectionInput = {
    Catalog : Catalog
    Policy  : Policy
    Profile : Profile
}


[<RequireQualifiedAccess>]
module SelectionPolicy =

    /// The default — every kind participates.
    let empty : SelectionPolicy = IncludeAll

    /// True iff the kind is selected under this policy.
    let isSelected (key: SsKey) (policy: SelectionPolicy) : bool =
        match policy with
        | IncludeAll        -> true
        | IncludeOnly keys  -> Set.contains key keys
        | ExcludeOnly keys  -> not (Set.contains key keys)

    /// Project a catalog to only its selected kinds. Useful for emitters
    /// that want to operate on the selected subset; structural passes
    /// continue to operate on the full catalog (per A33: sort/order
    /// passes see all kinds, emission filters afterwards).
    let filterCatalog (policy: SelectionPolicy) (c: Catalog) : Catalog =
        { Modules =
            c.Modules
            |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.filter (fun k -> isSelected k.SsKey policy) }) }


[<RequireQualifiedAccess>]
module EmissionPolicy =

    /// Default emission: schema only. The most common configuration and
    /// the one where the algebra's structural claims are sharpest.
    let empty : EmissionPolicy =
        { EmitSchema = true; EmitData = false; EmitDiagnostics = false }

    /// Schema artifacts only.
    let schemaOnly : EmissionPolicy = empty

    /// Data artifacts only — for full-export pipelines that keep schema
    /// emission elsewhere.
    let dataOnly : EmissionPolicy =
        { EmitSchema = false; EmitData = true; EmitDiagnostics = false }

    /// All three artifact families together.
    let combined : EmissionPolicy =
        { EmitSchema = true; EmitData = true; EmitDiagnostics = true }


[<RequireQualifiedAccess>]
module InsertionPolicy =

    let empty : InsertionPolicy = SchemaOnly


[<RequireQualifiedAccess>]
module Policy =

    /// The empty policy: schema-only emission, every kind selected, no
    /// insertion semantics. A valid input for any pass; passes that
    /// consume Policy must produce sensible behavior on `Policy.empty`.
    let empty : Policy =
        { Selection = SelectionPolicy.empty
          Emission  = EmissionPolicy.empty
          Insertion = InsertionPolicy.empty }


[<RequireQualifiedAccess>]
module ProjectionInput =

    /// Build a `ProjectionInput` whose Policy and Profile are the
    /// neutral defaults. Convenience for passes that consume only the
    /// catalog but need to flow through a triple-shaped pipeline.
    let ofCatalog (c: Catalog) : ProjectionInput =
        { Catalog = c; Policy = Policy.empty; Profile = Profile.empty }

    /// True iff the input is in the "no policy, no profile" minimal form.
    let isMinimal (input: ProjectionInput) : bool =
        input.Policy = Policy.empty && Profile.isEmpty input.Profile

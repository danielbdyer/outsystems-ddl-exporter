namespace Projection.Core

/// **H-015: Lens (Cluster B follow-on; 2026-05-22).** A total
/// bidirectional accessor: `Get` always succeeds; `Set` always
/// succeeds — the **total** bidirectional view of a substructure 'a
/// within a supercatalog 's. Used for deep-nested record updates where
/// the existing F# `{ x with Foo = { x.Foo with Bar = ... } }` shape
/// gets verbose at 2+ levels of nesting.
///
/// (The partial dual — a `Prism` over a may-fail `ReverseGet` — was
/// retired 2026-06-04 as unused speculative algebra; rebuild it if a
/// real partial-accessor consumer ever lands.)
///
/// **Lens laws (property-tested in `DiagnosticsTests.fs`):**
///   - **Get-Set:** `set (get s) s = s` — setting back the gotten
///     value yields the original.
///   - **Set-Get:** `get (set a s) = a` — getting back the set value
///     yields what was set.
///   - **Set-Set:** `set a' (set a s) = set a' s` — the second set
///     overwrites the first.
///
/// **Totality.** A Lens is total — both `Get` and `Set` always succeed
/// — so it fits fields that always exist (`Catalog.Modules`,
/// `Module.Kinds`, `Kind.Attributes`). A partial accessor (over a
/// may-not-exist target) would need the retired `Prism`; reintroduce
/// one only under a concrete consumer.
///
/// **Composition.** Lenses compose: `compose (outer : Lens<'s, 'a>)
/// (inner : Lens<'a, 'b>) : Lens<'s, 'b>`. The composed Get/Set thread
/// through both layers — `outerGet >> innerGet` and `innerSet >>
/// outerSet`. The Lens laws hold under composition when they hold for
/// each factor.
///
/// **Compile-order rationale (2026-06-02, lens-adoption sweep).** The
/// `Lens` type + `module Lens` + `module CatalogLenses` are extracted
/// to `Optics.fs` (compiled after `Catalog.fs`, before every consumer
/// in `Passes/`, `Policy.fs`, `CatalogDiff.fs`, `ModuleFilter.fs`,
/// `LineageBuffer.fs`) so the lens vocabulary is visible to every
/// catalog-manipulating site. Previously housed in `Diagnostics.fs`
/// (compile-order 73), which made `Lens` unavailable to most Core
/// consumers; the audit named this as a description-vs-naming smell.
type Lens<'s, 'a> = {
    Get : 's -> 'a
    Set : 'a -> 's -> 's
}

[<RequireQualifiedAccess>]
module Lens =

    /// Apply the getter.
    let get (lens: Lens<'s, 'a>) (s: 's) : 'a = lens.Get s

    /// Apply the setter.
    let set (lens: Lens<'s, 'a>) (a: 'a) (s: 's) : 's = lens.Set a s

    /// Modify the focused substructure by applying a function — the
    /// canonical "lens.over" operation. Equivalent to
    /// `set lens (f (get lens s)) s` but with the get-modify-set
    /// cycle named explicitly.
    let over (lens: Lens<'s, 'a>) (f: 'a -> 'a) (s: 's) : 's =
        lens.Set (f (lens.Get s)) s

    /// Identity lens — `Get = id`, `Set = fun a _ -> a`. The unit of
    /// lens composition; useful as a fixture and as the start of
    /// `compose` chains.
    let identity<'a> : Lens<'a, 'a> = {
        Get = id
        Set = fun a _ -> a
    }

    /// Compose two lenses to focus through both layers in sequence.
    /// `compose outer inner` views a `Lens<'s, 'b>` by traversing
    /// through `outer : Lens<'s, 'a>` then `inner : Lens<'a, 'b>`.
    /// Read as "outer of inner."
    let compose (outer: Lens<'s, 'a>) (inner: Lens<'a, 'b>) : Lens<'s, 'b> = {
        Get = fun s -> inner.Get (outer.Get s)
        Set = fun b s -> outer.Set (inner.Set b (outer.Get s)) s
    }


/// **Canonical lenses for the Catalog IR (chapter-Cluster-B follow-on;
/// 2026-05-22; consumers landed 2026-06-02 lens-adoption sweep).**
/// Sites that compose deep updates over Catalog → Module → Kind /
/// Attribute / Reference / Index land here as reusable optics;
/// consumers compose them via `Lens.compose` to reach arbitrary depth
/// without re-deriving the boilerplate.
///
/// **Production consumers (post-2026-06-02 sweep):**
///   - `modules`: SymmetricClosure, Catalog.mapKinds, CatalogTraversal.mapKinds,
///     ModuleFilter, Policy.filterBySelection
///   - `kindsOf`: Catalog.mapKinds, CatalogTraversal.mapKinds, ModuleFilter,
///     Policy.filterBySelection, CatalogDiff.addKind
///   - `attributesOf`: LogicalColumnEmission.substituteKind
///   - `referencesOf`: SymmetricClosure
///   - `columnOf`: LogicalColumnEmission.substituteAttribute, CatalogDiff.applyFacet
[<RequireQualifiedAccess>]
module CatalogLenses =

    /// `Catalog.Modules`. The outer layer; every deeper catalog lens
    /// composes through this.
    let modules : Lens<Catalog, Module list> = {
        Get = fun c -> c.Modules
        Set = fun ms c -> { c with Modules = ms }
    }


    /// `Module.Kinds`. Composes through `modules` to reach kind-level
    /// updates in a single module; for "every kind across all modules"
    /// patterns, use `Catalog.mapKinds` / `Catalog.foldKinds`
    /// (the imperative-style traversal primitives).
    let kindsOf : Lens<Module, Kind list> = {
        Get = fun m -> m.Kinds
        Set = fun ks m -> { m with Kinds = ks }
    }

    /// `Kind.Attributes`.
    let attributesOf : Lens<Kind, Attribute list> = {
        Get = fun k -> k.Attributes
        Set = fun attrs k -> { k with Attributes = attrs }
    }

    /// `Kind.References`.
    let referencesOf : Lens<Kind, Reference list> = {
        Get = fun k -> k.References
        Set = fun refs k -> { k with References = refs }
    }


    /// `Attribute.Column`. Two production consumers
    /// (`Passes.LogicalColumnEmission.substituteAttribute` +
    /// `CatalogDiff.applyFacet`) share the access path through
    /// `Attribute.Column` to update an inner `ColumnRealization` field;
    /// the lens factors the navigation while the per-field record-update
    /// stays at the call site (one-field inner lenses would each have a
    /// single consumer — over-extraction).
    let columnOf : Lens<Attribute, ColumnRealization> = {
        Get = fun a -> a.Column
        Set = fun c a -> { a with Column = c }
    }

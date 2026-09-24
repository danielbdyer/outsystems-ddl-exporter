namespace Projection.Core

/// **The slice definition** — the declarative "use case" the data-portability
/// capability extracts (the Fully-Qualified Ask, FR1). A `SliceSpec` names a
/// set of ROOTS (a logical entity + a WHERE-style predicate selecting the
/// curated rows) and per-relationship TRAVERSAL DIRECTIVES that shape the
/// closure (follow-to-parent / bounded follow-to-children / stop-at-frontier).
///
/// Everything here is LOGICAL and environment-indifferent: entities are named
/// by COORDINATE (module/entity), columns by logical attribute `Name`. The
/// adapter resolves a coordinate to whichever catalog (source or target) it is
/// bridging, and renders a `Predicate` to that side's physical SQL — the eSpace
/// plane separation (same logical entity, different physical names per env).
[<RequireQualifiedAccess>]
type Predicate =
    /// No restriction — every row.
    | All
    /// `<column> = <value>` (value compared as its raw string form).
    | Equals of column: Name * value: string
    /// `<column> IN (<values>)`.
    | In of column: Name * values: string list
    /// `<column> IS NULL`. Tri-state: matches a genuine SQL NULL (or an
    /// absent cell), NOT the empty string — the `value`/`valueOrEmpty` split
    /// the write path keeps (`NULL ≠ ''`). Grown here (not a `Raw` escape)
    /// because the second consumer — approved data corrections — gates every
    /// derivation on a NULL test that MUST distinguish NULL from `''`.
    | IsNull of column: Name
    /// `<column> IS NOT NULL`. The tri-state complement of `IsNull`: matches
    /// any present value, including the empty string.
    | IsNotNull of column: Name
    /// Conjunction of sub-predicates (empty `And` ≡ `All`).
    | And of Predicate list
    /// A terminal raw-SQL escape hatch for predicates the typed arms cannot yet
    /// express. Rendered verbatim at the oracle (LINT-ALLOW boundary); the
    /// pure in-memory evaluator cannot interpret it, so it is treated as
    /// already-applied (the SQL did the filtering). Grow typed arms at the
    /// second consumer; the propose verb never emits this.
    | Raw of sql: string

[<RequireQualifiedAccess>]
module Predicate =

    /// Evaluate a predicate against a row's logical values — the pure,
    /// in-memory form (directive filters, tests). `Raw` evaluates to `true`
    /// (it was, or will be, applied at the SQL boundary).
    let rec eval (row: StaticRow) (p: Predicate) : bool =
        match p with
        | Predicate.All            -> true
        | Predicate.Equals (c, v)  -> StaticRow.valueOrEmpty c row = v
        | Predicate.In (c, vs)     -> List.contains (StaticRow.valueOrEmpty c row) vs
        | Predicate.IsNull c       -> Option.isNone (StaticRow.value c row)
        | Predicate.IsNotNull c    -> Option.isSome (StaticRow.value c row)
        | Predicate.And ps         -> List.forall (eval row) ps
        | Predicate.Raw _          -> true

/// A logical entity coordinate — the cross-environment identity bridge (the
/// raw `SsKey` GUID is per-environment and will not match across installs;
/// the (module, entity) name pair is stable for the same app across
/// environments). `Module` may be blank when a bare entity name is
/// unambiguous (the live-readback / single-module case).
type EntityCoordinate =
    { Module : string
      Entity : string }

[<RequireQualifiedAccess>]
module EntityCoordinate =

    let create (moduleName: string) (entity: string) : EntityCoordinate =
        { Module = moduleName; Entity = entity }

    /// A module-agnostic coordinate (match by entity name alone).
    let ofEntity (entity: string) : EntityCoordinate =
        { Module = ""; Entity = entity }

    let render (c: EntityCoordinate) : string =
        if c.Module = "" then c.Entity
        else System.String.Concat(c.Module, "/", c.Entity)  // LINT-ALLOW: terminal display projection of a coordinate; not SQL

/// One root of the slice: a logical entity and the predicate selecting the
/// curated rows to seed the closure from.
type RootSpec =
    { Entity    : EntityCoordinate
      Predicate : Predicate }

/// How the closure treats one relationship (FK) edge.
///   * `Up` — follow to the parent (the referential-completeness default; an
///     edge with no directive is implicitly `Up`).
///   * `Down depth` — ALSO follow to children (rows that reference this kind),
///     bounded to `depth` hops (the "one-node-extra-blast-radius" control).
///   * `Stop` — a frontier: do not follow this edge at all.
[<RequireQualifiedAccess>]
type TraversalDirection =
    | Up
    | Down of depth: int
    | Stop

/// A per-relationship traversal directive, addressing the edge by its owning
/// entity and the relationship (reference) `Name`.
type RelationshipDirective =
    { From         : EntityCoordinate
      Relationship : string
      Direction    : TraversalDirection }

/// A versioned, declarative slice definition: the roots plus the traversal
/// directives that shape the closure. Smart-constructed.
type SliceSpec =
    { Version : int
      Roots   : RootSpec list
      Directives : RelationshipDirective list }

[<RequireQualifiedAccess>]
module SliceSpec =

    /// The current slice-definition schema version (the codec re-validates
    /// through `create` on decode).
    [<Literal>]
    let CurrentVersion = 1

    let private noRoots =
        ValidationError.create "slice.roots.empty"
            "A slice must declare at least one root entity + predicate."

    let private dupDirective =
        ValidationError.create "slice.directive.duplicate"
            "A slice declares more than one traversal directive for the same (entity, relationship) edge."

    let private negativeDepth =
        ValidationError.create "slice.directive.negativeDepth"
            "A Down traversal directive has a negative depth."

    /// Build a validated `SliceSpec`: at least one root; no duplicate directive
    /// for the same edge; no negative Down depth. (A39 — the invariants live in
    /// the smart constructor; the codec re-runs this on decode.)
    let create (version: int) (roots: RootSpec list) (directives: RelationshipDirective list) : Result<SliceSpec> =
        if List.isEmpty roots then Error [ noRoots ]
        else
            let edgeKeys =
                directives |> List.map (fun d -> EntityCoordinate.render d.From, d.Relationship)
            if List.length edgeKeys <> List.length (List.distinct edgeKeys) then Error [ dupDirective ]
            else
                let badDepth =
                    directives
                    |> List.exists (fun d ->
                        match d.Direction with
                        | TraversalDirection.Down depth when depth < 0 -> true
                        | _ -> false)
                if badDepth then Error [ negativeDepth ]
                else Ok { Version = version; Roots = roots; Directives = directives }

    /// The directive for an edge `(fromEntity, relationship)`, or the implicit
    /// `Up` default. Coordinates match by entity name (module ignored when
    /// either side is blank — the live-readback case).
    let directionFor (fromEntityName: string) (relationship: string) (spec: SliceSpec) : TraversalDirection =
        spec.Directives
        |> List.tryFind (fun d -> d.From.Entity = fromEntityName && d.Relationship = relationship)
        |> Option.map (fun d -> d.Direction)
        |> Option.defaultValue TraversalDirection.Up

    /// A spec that closes every mandatory/populated parent (the Slice-1
    /// behaviour): the given roots, no directives (every edge implicitly `Up`).
    let closeAllParents (roots: RootSpec list) : Result<SliceSpec> =
        create CurrentVersion roots []

namespace Projection.Core

/// Typed payload for `TransformKind.Removed` — chapter-3.6 slice-α
/// (`CHAPTER_3_6_OPEN.md`). The convention any filtering pass follows:
/// when a node is masked, the removal reason structurally names the
/// rule that fired. Each variant carries the typed payload that the
/// predicate was parameterized on, so audit readers, future dashboards,
/// and tests pattern-match exhaustively rather than substring-parse a
/// pre-rendered name string.
///
/// Variants:
///   - `OriginPredicate of Origin` — `VisibilityMask.hideOrigin`
///     removed the kind because its `Origin` matched.
///   - `ExplicitKeyList` — `VisibilityMask.hideKeys` removed the kind
///     because its SsKey was in the predicate's key list. **Marker
///     variant**; the full key set is intentionally NOT carried —
///     per-event payload would otherwise be O(N), making the trail
///     O(N²) (Big-O Tier-1 discipline; chapter-3.1 audit).
///   - `ModalityPredicate of ModalityMark` — `VisibilityMask.hideModality`
///     removed the kind because its modality contained the mark.
///
/// Adding a new filtering pass that introduces a new predicate shape
/// extends this DU; the closed-DU expansion empirical-test discipline
/// (`DECISIONS 2026-05-13`) applies — exhaustiveness errors should
/// light up only at consumer match sites within Core.
type RemovalReason =
    | OriginPredicate of origin: Origin
    | ExplicitKeyList
    | ModalityPredicate of mark: ModalityMark

/// Companion module for `RemovalReason`. Provides the rendering-
/// boundary projection: typed payload → diagnostic string. Strings
/// emerge ONLY here (and at any future writer-boundary consumer), per
/// the supreme operating discipline at the top of `DECISIONS.md`
/// (chapter 3.5; pillar 1: data-structure-oriented over string-parsing).
[<RequireQualifiedAccess>]
module RemovalReason =

    /// Render the typed reason as a stable diagnostic string. Used by
    /// boundary consumers (RawTextEmitter comments, future audit
    /// readers) that need a flat presentation form. Returns the same
    /// strings the prior `Removed of string` payload carried, so this
    /// is a structural seam, not a behavior change:
    ///   - `OriginPredicate Origin.OsNative` → `"origin=OsNative"`
    ///   - `ExplicitKeyList`                  → `"explicit-key-list"`
    ///   - `ModalityPredicate (Static [...])` → `"modality=[Static(N)]"`
    let toDiagnosticString (reason: RemovalReason) : string =
        match reason with
        | OriginPredicate origin ->
            // Origin's typed renderer already exists; the "origin="
            // prefix is the convention named at VisibilityMask.fs's
            // module docstring (filtering passes name the predicate
            // that fired). Two-element typed list joined at the
            // terminal-text-emission boundary.
            String.concat "" [ "origin="; Origin.toDiagnosticString origin ]  // LINT-ALLOW: terminal diagnostic projection; typed `RemovalReason` DU IS the structure
        | ExplicitKeyList ->
            "explicit-key-list"
        | ModalityPredicate mark ->
            String.concat "" [ "modality="; ModalityMark.toDiagnosticString mark ]  // LINT-ALLOW: terminal diagnostic projection; typed `RemovalReason` DU IS the structure


/// Reason a `SymmetricClosure` pass step skipped attaching an inverse
/// reference. Closed DU; matches the two skip cases the pass classifies
/// in `classifyStep`. Pattern-matched structurally by audit consumers
/// rather than parsed out of a "skipped: …" string. Chapter-3.6 slice-γ
/// (`CHAPTER_3_6_OPEN.md`).
type SymmetricClosureSkipReason =
    /// Target kind referenced by the directional reference is not
    /// present in the catalog (orphan reference). The closure pass
    /// cannot synthesize an inverse without a target.
    | TargetKindAbsent
    /// Target kind is present but exposes no primary key. The closure
    /// pass requires a PK attribute to anchor the inverse reference's
    /// `SourceAttribute`.
    | TargetHasNoPrimaryKey

/// Companion module for `SymmetricClosureSkipReason`. Provides the
/// rendering-boundary projection from the typed reason to the
/// preserved-historical-prose diagnostic string. Strings emerge ONLY
/// here.
[<RequireQualifiedAccess>]
module SymmetricClosureSkipReason =

    /// Render the typed skip-reason as the diagnostic string the prior
    /// `string`-payload `Annotated` event carried. Audit-readable;
    /// stable for diff against historical trails.
    let toDiagnosticString (reason: SymmetricClosureSkipReason) : string =
        match reason with
        | TargetKindAbsent       -> "skipped: target kind absent"
        | TargetHasNoPrimaryKey  -> "skipped: target has no primary key"


/// Typed payload for `TransformKind.Annotated` — chapter-3.6 slice-β/γ
/// (`CHAPTER_3_6_OPEN.md`). Each variant carries the typed pass-
/// decision payload structurally; consumers pattern-match exhaustively
/// rather than substring-parse a built name string. The intervention
/// passes (Nullability / UniqueIndex / ForeignKey / CategoricalUniqueness)
/// share a common shape (`interventionId × outcome`); the closure pass
/// has its own `ClosureSkipped` shape; the `Label` variant is a
/// documented free-form fallback for tests + future passes whose typed
/// shape hasn't yet been earned (production passes use the typed
/// variants).
///
/// **Big-O note:** each variant carries a small typed payload (the
/// outcome DU is bounded per-decision; the skip-reason is a tag). Trail
/// size O(N) events × O(1) per-event payload — same asymptotic shape
/// as the prior string-detail form, with no per-event string
/// allocation.
type AnnotationDetail =
    /// One Nullability pass decision: `interventionId` names the
    /// registered intervention; `outcome` is the typed decision.
    | NullabilityDecision of interventionId: string * outcome: NullabilityOutcome
    /// One UniqueIndex pass decision.
    | UniqueIndexDecision of interventionId: string * outcome: UniqueIndexOutcome
    /// One ForeignKey pass decision.
    | ForeignKeyDecision of interventionId: string * outcome: ForeignKeyOutcome
    /// One CategoricalUniqueness pass decision.
    | CategoricalUniquenessDecision of interventionId: string * outcome: CategoricalUniquenessOutcome
    /// SymmetricClosure pass skipped attaching an inverse for the
    /// reason given. Pairs with `Created` events (one per inverse
    /// successfully attached).
    | ClosureSkipped of reason: SymmetricClosureSkipReason
    /// **Free-form label.** Used by writer-monad-laws tests, the
    /// `Composition.fanOut` synthetic-decision test, and any future
    /// pass whose typed annotation shape hasn't yet been earned.
    /// Production pass drivers MUST use one of the typed variants
    /// above; the typed-payload discipline (chapter 3.6) holds for
    /// production code. Tests and migration-bridges read this variant
    /// as opaque.
    | Label of label: string

/// Companion module for `AnnotationDetail`. Provides the rendering-
/// boundary projection from the typed payload to a stable diagnostic
/// string — preserves the historical `<interventionId> -> <outcome>`
/// shape the prior `Annotated of string` payload carried, so audit
/// consumers and downstream tools that read the trail diff cleanly.
[<RequireQualifiedAccess>]
module AnnotationDetail =

    /// Render the typed annotation payload as the diagnostic string
    /// the prior `string`-payload form carried. Strings emerge ONLY
    /// here (and at any future writer-boundary consumer), per the
    /// supreme operating discipline at the top of `DECISIONS.md`.
    let toDiagnosticString (detail: AnnotationDetail) : string =
        match detail with
        | NullabilityDecision (id, outcome) ->
            String.concat "" [ id; " -> "; NullabilityOutcome.toDiagnosticString outcome ]  // LINT-ALLOW: terminal diagnostic projection; typed `AnnotationDetail` DU IS the structure
        | UniqueIndexDecision (id, outcome) ->
            String.concat "" [ id; " -> "; UniqueIndexOutcome.toDiagnosticString outcome ]  // LINT-ALLOW: terminal diagnostic projection
        | ForeignKeyDecision (id, outcome) ->
            String.concat "" [ id; " -> "; ForeignKeyOutcome.toDiagnosticString outcome ]  // LINT-ALLOW: terminal diagnostic projection
        | CategoricalUniquenessDecision (id, outcome) ->
            String.concat "" [ id; " -> "; CategoricalUniquenessOutcome.toDiagnosticString outcome ]  // LINT-ALLOW: terminal diagnostic projection
        | ClosureSkipped reason ->
            SymmetricClosureSkipReason.toDiagnosticString reason
        | Label label ->
            label


/// Typed payload for `TransformKind.PhysicallyRenamed` — the
/// physical-realization rewrite as a structural pair. Mirrors the
/// `RemovalReason` / `AnnotationDetail` precedent: the typed pair IS
/// the data, downstream consumers pattern-match on `Before` / `After`
/// rather than re-parsing a pre-rendered string. Identity (SsKey)
/// stays on the carrying `LineageEvent`; this payload carries the
/// physical-coordinate axis only.
///
/// Invariant: `Before <> After` (a no-op rename emits no event). Not
/// enforced by smart constructor because consumers (`TableRename`)
/// short-circuit at the visitor before constructing the payload.
type PhysicalRename = {
    Before : TableId
    After  : TableId
}

/// Companion module for `PhysicalRename`. Diagnostic-string projection
/// matches the existing `<schema>.<table>` rendering convention used
/// across other typed payloads (`SymmetricClosureSkipReason`, etc.).
[<RequireQualifiedAccess>]
module PhysicalRename =

    /// Render as `"<beforeSchema>.<beforeTable> -> <afterSchema>.<afterTable>"`.
    /// Strings emerge ONLY here (and at any future writer-boundary
    /// consumer), per the supreme operating discipline at the top of
    /// `DECISIONS.md`.
    let toDiagnosticString (rename: PhysicalRename) : string =
        String.concat "" [  // LINT-ALLOW: terminal diagnostic projection; typed `PhysicalRename` record IS the structure
            rename.Before.Schema; "."; rename.Before.Table
            " -> "
            rename.After.Schema; "."; rename.After.Table
        ]

/// Typed payload for `TransformKind.ColumnPhysicallyRenamed` — the
/// per-attribute physical column-name rewrite as a structural triple.
/// Mirrors `PhysicalRename` (kind-level) one axis down: `Kind` carries
/// the owning kind's physical coordinate (so audit readers can locate
/// the column without re-resolving SsKeys); `Before` / `After` carry
/// the prior and new `ColumnRealization.ColumnName` strings. Slice D.1
/// introduces this variant for `ColumnRenameToLogicalPass`.
///
/// Invariant: `Before <> After` (a no-op rename emits no event). Not
/// enforced by smart constructor because emitting passes short-circuit
/// at the visitor before constructing the payload.
type ColumnRename = {
    Kind   : TableId
    Before : string
    After  : string
}

/// Companion module for `ColumnRename`. Diagnostic-string projection
/// matches the existing `<schema>.<table>[<col>]` rendering convention
/// readers expect.
[<RequireQualifiedAccess>]
module ColumnRename =

    /// Render as `"<schema>.<table>[<before> -> <after>]"`. Strings
    /// emerge ONLY here (and at any future writer-boundary consumer),
    /// per the supreme operating discipline at the top of `DECISIONS.md`.
    let toDiagnosticString (rename: ColumnRename) : string =
        String.concat "" [  // LINT-ALLOW: terminal diagnostic projection; typed `ColumnRename` record IS the structure
            rename.Kind.Schema; "."; rename.Kind.Table; "["
            rename.Before; " -> "; rename.After; "]"
        ]

/// The kind of transformation a lineage event records. The set is small
/// and additive — extend rather than reshape when new pass categories
/// appear, so historical lineage trails stay readable.
type TransformKind =
    /// The pass observed the node but introduced no change. Useful as a
    /// witness that a pass ran ("we looked at this and decided nothing").
    | Touched
    /// The pass changed the node's presentation name. Identity is
    /// untouched (A3, A4, A15). Distinct from `PhysicallyRenamed`, which
    /// records physical-realization rewrites; `Renamed` is the
    /// presentation-name axis.
    | Renamed
    /// The pass introduced a new node with a Derived SsKey (A5). The
    /// derivation reason lives in the SsKey itself; this tag merely
    /// flags the transform's category.
    | Created
    /// The pass masked (withheld) a node from the surface. The typed
    /// `RemovalReason` payload names the predicate (or rule) that
    /// fired. Chapter-3.6 slice-α widened this from `string` to
    /// `RemovalReason` — closed-DU exhaustiveness replaces ad-hoc
    /// string parsing.
    | Removed of reason: RemovalReason
    /// The pass attached metadata (intervention decision, closure
    /// skip, free-form label). The typed `AnnotationDetail` payload
    /// names the annotation structurally. Chapter-3.6 slice-β/γ
    /// widened this from `string` to `AnnotationDetail` — production
    /// passes carry typed pass-decision payloads; tests + migration
    /// use the `Label of string` variant. Strings emerge only at the
    /// rendering boundary via `AnnotationDetail.toDiagnosticString`.
    | Annotated of detail: AnnotationDetail
    /// The pass rewrote the node's physical realization (schema.table).
    /// Identity (SsKey) is untouched (A1); only `Kind.Physical` changes.
    /// The typed `PhysicalRename` payload carries the before/after
    /// `TableId` pair structurally — audit readers and diff tools
    /// pattern-match on the typed value rather than parsing a rendered
    /// string.
    | PhysicallyRenamed of detail: PhysicalRename
    /// The pass rewrote a column's physical name
    /// (`Attribute.Column.ColumnName`). Identity (`Attribute.SsKey`) is
    /// untouched (A1); only the per-column physical realization
    /// changes. Slice D.1 introduces this variant for
    /// `ColumnRenameToLogicalPass`; the typed `ColumnRename` payload
    /// carries the kind coordinate plus before/after column-name
    /// strings.
    | ColumnPhysicallyRenamed of detail: ColumnRename


/// One step in the provenance chain. Per A23, every event carries a
/// `PassVersion` so functionally different versions of the same pass
/// produce distinguishable lineage and replay determinism is preserved
/// across pipeline evolution.
///
/// **Chapter A.4.7 slice α — `Classification` field.** Per pillar 9
/// (harvest-dichotomy classification; `DECISIONS 2026-05-15 (late)`),
/// every event carries its classification (`DataIntent` or
/// `OperatorIntent of OverlayAxis`). Pass drivers self-classify by
/// passing the per-pass / per-site classification to the event-helper.
/// Slice α (this commit) establishes the type-system shape; slice β
/// onward (`TransformRegistry.fs`) makes the classification canonical
/// at the registry. The skeleton-purity property test (slice θ) asserts
/// `Compose.runWithSkeleton` emits zero `OperatorIntent` events;
/// misclassification leaks and the property fails.
type LineageEvent = {
    PassName       : string
    PassVersion    : int
    SsKey          : SsKey
    TransformKind  : TransformKind
    Classification : Classification
}


/// Writer-monadic carrier for any pass output. Per A25, every IR
/// transformation in the pipeline runs inside `Lineage<_>`; lineage is
/// constitutive, not opt-in. Per A26, lineage is metadata travelling
/// alongside structure — it does not participate in structural equality.
///
/// **A26 cash-out (chapter-3.7 slice α; audit Tier-2 #12).** `Lineage<'a>`
/// uses `[<CustomEquality; NoComparison>]` to project equality through
/// `Value` only; the `Trail` is provenance metadata and does not
/// distinguish two carriers semantically. Two passes producing the same
/// `Value` with re-ordered or re-shaped `Trail` events are equal as
/// `Lineage<'a>` carriers; the catalog-level identity claim of A26
/// (`Kind.byIdentity` ignores trails) extends symmetrically to the
/// writer carrier itself. Consumers that need to compare trails use
/// `Lineage.byValueAndTrail` (full structural) or project to `.Trail`
/// directly.
[<CustomEquality; NoComparison>]
type Lineage<'a when 'a : equality> =
    {
        Value : 'a
        Trail : LineageEvent list
    }
    override this.Equals(other: obj) : bool =
        match other with
        | :? Lineage<'a> as o -> this.Value = o.Value
        | _                   -> false
    override this.GetHashCode() : int = hash this.Value
    interface System.IEquatable<Lineage<'a>> with
        member this.Equals(other: Lineage<'a> | null) : bool =
            match other with
            | null -> false
            | o    -> this.Value = o.Value


/// Construction and composition for `Lineage<_>`. The `bind` operator
/// concatenates trails chronologically per A24: under `f >>= g` the
/// resulting trail is `f.Trail ++ g.Trail` — earliest-first. All passes
/// and all readers rely on this order; reversed-trail bugs are subtle and
/// expensive, so the convention is encoded in code, in `AXIOMS.md`, and
/// in the test suite.
[<RequireQualifiedAccess>]
module Lineage =

    /// Equality on `Lineage<'a>` projects through `Value` only (A26;
    /// chapter-3.7 slice α). This helper names the projection at call
    /// sites so the intent reads structurally — `Lineage.byValue m1 m2`
    /// is the same as `m1 = m2` but advertises that trails are
    /// deliberately ignored.
    let byValue (m1: Lineage<'a>) (m2: Lineage<'a>) : bool =
        m1.Value = m2.Value

    /// Full structural equality on `Lineage<'a>` — both `Value` and
    /// `Trail` must match. Used by writer-monad-laws property tests
    /// to assert that `bind` / `map` / `tell` compose trails as
    /// specified (A24 / A25). Production consumers should not need
    /// this; if they do, they are reaching for provenance equality
    /// which is a different question from structural equality.
    let byValueAndTrail (m1: Lineage<'a>) (m2: Lineage<'a>) : bool =
        m1.Value = m2.Value && m1.Trail = m2.Trail

    /// Wrap a value with an empty trail. The unit of the writer monad.
    let ofValue (value: 'a) : Lineage<'a> = { Value = value; Trail = [] }

    /// Wrap a value with a single event. Convenience for passes whose
    /// transformation is described by exactly one event.
    let ofValueWith (event: LineageEvent) (value: 'a) : Lineage<'a> =
        { Value = value; Trail = [event] }

    /// Wrap a value with a list of events in one shot. Per session-36
    /// audit (Agent 4 #4) — the `tellMany events (ofValue x)` shape
    /// recurred at 7 sites across the terminal-event passes
    /// (CanonicalizeIdentity, NamingMorphism, NormalizeStaticPopulations,
    /// SymmetricClosure, TopologicalOrderPass, VisibilityMask). Two-
    /// consumer threshold long crossed; the primitive names the
    /// canonical "build a value plus its trail" pattern.
    let ofValueAndEvents (events: LineageEvent list) (value: 'a) : Lineage<'a> =
        { Value = value; Trail = events }

    /// Append a single event without changing the value. Useful when a
    /// pass needs to record an observation about a node it returns
    /// unchanged (e.g., `Touched`).
    let tell (event: LineageEvent) (m: Lineage<'a>) : Lineage<'a> =
        { m with Trail = m.Trail @ [event] }  // LINT-ALLOW: writer-monad `tell` algebraic primitive; pass drivers use `LineageBuffer` for high-rate accumulation, `tell` is terminal annotation only

    /// Append several events without changing the value.
    let tellMany (events: LineageEvent list) (m: Lineage<'a>) : Lineage<'a> =
        { m with Trail = m.Trail @ events }

    /// Functor map — preserves the trail untouched, transforms the value.
    let map (f: 'a -> 'b) (m: Lineage<'a>) : Lineage<'b> =
        { Value = f m.Value; Trail = m.Trail }

    /// Monadic bind. A24: `bind f m` produces a trail `m.Trail ++ (f
    /// m.Value).Trail` — earliest-first, chronological. The `@` operator
    /// (list concat) is associative, so the writer monad's laws hold.
    let bind (f: 'a -> Lineage<'b>) (m: Lineage<'a>) : Lineage<'b> =
        // A24: chronological — m.Trail first, then the new pass's trail.
        let next = f m.Value
        { Value = next.Value; Trail = m.Trail @ next.Trail }

    /// Write a single event under the unit value. The "writer-primitive"
    /// form — distinct from `tell` (the operational form which augments
    /// an existing carrier) — `write event` is a `Lineage<unit>` whose
    /// trail is `[event]`. This is the primitive that the `lineage` CE
    /// builder uses to desugar `do! Lineage.write event` (H-001).
    let write (event: LineageEvent) : Lineage<unit> =
        { Value = (); Trail = [event] }

    /// Write several events under the unit value. Pairs with `write`;
    /// algebraically equivalent to `events |> List.map write` folded
    /// through `bind`, but emits a single carrier (matches the
    /// `ofValueAndEvents` shape).
    let writeMany (events: LineageEvent list) : Lineage<unit> =
        { Value = (); Trail = events }


/// `lineage { ... }` computation expression builder. Encodes the writer
/// monad's algebra in F# syntax. `let!` is `bind`; `return` is `ofValue`;
/// `do! Lineage.write event` appends an event to the implicit accumulator.
/// The CE is H-001: the same algebra the pass drivers already use, in a
/// surface form that reads as the pipeline it is.
///
/// **Worked equivalence (CE ≡ bind chain):**
/// ```
/// lineage {                            m >>= fun x ->
///     let! x = m                       Lineage.write e >>= fun () ->
///     do! Lineage.write e          ≡   Lineage.ofValue x
///     return x
/// }
/// ```
///
/// **Writer-fidelity discipline holds:** the CE produces values via the
/// canonical primitives (`bind` / `ofValue` / `write`); manual record-
/// building is impossible inside `lineage { ... }`. Every pass that
/// adopts the CE inherits A24 chronological ordering by construction.
type LineageBuilder() =
    member _.Return(x: 'a) : Lineage<'a> = Lineage.ofValue x
    member _.ReturnFrom(m: Lineage<'a>) : Lineage<'a> = m
    member _.Bind(m: Lineage<'a>, f: 'a -> Lineage<'b>) : Lineage<'b> =
        Lineage.bind f m
    member _.Zero() : Lineage<unit> = Lineage.ofValue ()
    /// Sequence two writer carriers: `m1 >> m2` discards the unit value
    /// from `m1` and threads `m2`. Trails compose chronologically (A24).
    member _.Combine(m1: Lineage<unit>, m2: Lineage<'a>) : Lineage<'a> =
        Lineage.bind (fun () -> m2) m1
    /// Eager `Delay`: writer monad has no laziness obligation (Core is
    /// synchronous-by-design per CLAUDE.md "purity-first"). Evaluating
    /// the thunk immediately preserves the algebraic identities.
    member _.Delay(f: unit -> Lineage<'a>) : Lineage<'a> = f ()
    member _.Run(m: Lineage<'a>) : Lineage<'a> = m

[<AutoOpen>]
module LineageBuilders =
    /// The `lineage { ... }` CE entry point (H-001). Open `Projection.Core`
    /// to bring into scope; pass drivers and tests use the CE form when
    /// the bind chain has structural complexity (`let!` + `do!` + `return`).
    let lineage = LineageBuilder()


/// Infix operators for `Lineage<_>`. Open this module at call sites that
/// benefit from `>>=` (the algebra reads more like the formal system).
module LineageOperators =

    /// Bind: `m >>= f` is `Lineage.bind f m`.
    let inline (>>=) (m: Lineage<'a>) (f: 'a -> Lineage<'b>) : Lineage<'b> =
        Lineage.bind f m

    /// Map: `f <!> m` is `Lineage.map f m`.
    let inline (<!>) (f: 'a -> 'b) (m: Lineage<'a>) : Lineage<'b> =
        Lineage.map f m


/// **H-005: LineageTree (branching writer monad; 2026-05-22).** The
/// speculative-execution sibling of `Lineage<'a>`. Where `Lineage<'a>`
/// is the **linear** writer monad (append-only trail) and
/// `Certificate<'a>` is the **terminal** projection of a pipeline,
/// `LineageTree<'a>` is the **branching** form — a tree of writer
/// carriers where the same `Catalog` can fork through multiple policies
/// (or any speculative computation) and all branches' lineages are
/// retained for later comparison.
///
/// **Algebra (Cluster B writer-monad trinity completion).**
/// `LineageTree<'a>` is the **free monad over the branching-list
/// functor** applied to `Lineage<'a>`:
///
///   - `Leaf m` — a single linear branch carrying a `Lineage<'a>`.
///     Algebraically the `Pure` constructor of the free monad.
///   - `Fork branches` — a labeled list of subtrees. Each branch
///     carries a name (for diff / select semantics) and a subtree.
///     Algebraically the `Free F` constructor where F is the
///     labeled-list functor.
///
/// **Inheritance from `Lineage`.** Each leaf is a full `Lineage<'a>`;
/// A24-amended (chronological-bind) holds within each leaf. The tree's
/// `bind` substitutes at every leaf — the new branch's prefix
/// (existing leaf trail) is preserved chronologically; the
/// continuation appends. The monad laws hold for `LineageTree<'a>`
/// because they hold for each leaf's `Lineage<'a>` AND the
/// branching-list functor preserves them by free-monad construction.
///
/// **Two foundational operations** (per H-005 HORIZON sketch):
///   - `branch : Lineage<'a> -> LineageTree<'a>` — lift a linear
///     carrier into a single-leaf tree. The right adjoint of `commit`.
///   - `commit : LineageTree<'a> -> Lineage<'a>` — collapse a tree to
///     one branch via a caller-supplied selector. The left adjoint of
///     `branch`. Round-trip: `commit selector (branch m) = m` for any
///     selector that picks the only leaf.
///
/// **Unlocks (downstream).**
///   - H-033 (policy diff verb): run two policies against the same
///     `Catalog` as a `Fork [policyA; policyB]`; diff the resulting
///     leaves on `SsKey × Classification` to produce the policy delta.
///   - H-035 (policy regression testing): same shape, comparing one
///     policy's output across time-evolution of fixtures.
///   - H-063 (free monad for pass scheduling) inherits the tree
///     structure as its program shape.
///   - Cluster C (policy intelligence) opens once this primitive
///     lands.
///
/// **Why a labeled tree, not a bare list?** Labels make branch diff
/// structural — `paths` returns `(string list, Lineage<'a>) list`
/// where the string list is the label path from root to leaf. Two
/// trees with the same label structure can be diffed leaf-by-leaf
/// without alignment heuristics.
[<CustomEquality; NoComparison>]
type LineageTree<'a when 'a : equality> =
    | Leaf of Lineage<'a>
    | Fork of LineageBranch<'a> list

    override this.Equals(other: obj) : bool =
        match other with
        | :? LineageTree<'a> as o ->
            match this, o with
            | Leaf a, Leaf b -> a = b  // Lineage equality (A26: value-only)
            | Fork a, Fork b -> a = b  // structural equality on branches
            | _ -> false
        | _ -> false
    override this.GetHashCode() : int =
        match this with
        | Leaf m -> hash m
        | Fork bs -> hash bs

/// A single branch within a `Fork`. Carries an operator-readable label
/// and the continuation subtree. The label is the diff/select key.
and [<CustomEquality; NoComparison>] LineageBranch<'a when 'a : equality> =
    {
        Label : string
        Tree : LineageTree<'a>
    }
    override this.Equals(other: obj) : bool =
        match other with
        | :? LineageBranch<'a> as o ->
            this.Label = o.Label && this.Tree = o.Tree
        | _ -> false
    override this.GetHashCode() : int = hash (this.Label, this.Tree)


/// Construction, projection, and composition for `LineageTree<'a>`.
[<RequireQualifiedAccess>]
module LineageTree =

    /// Lift a linear `Lineage<'a>` into a single-leaf tree. The
    /// H-005-named "branch" operation; named here as `ofLineage`
    /// because the HORIZON-sketch "branch" verb conflicts with
    /// `bifurcate` / `Fork` semantics. The `branch` alias below
    /// preserves the sketch's vocabulary at the call site.
    let ofLineage (m: Lineage<'a>) : LineageTree<'a> = Leaf m

    /// Lift a plain value into a single-leaf tree with an empty trail.
    /// The unit of the LineageTree monad (Pure of the free monad).
    let ofValue (value: 'a) : LineageTree<'a> = Leaf (Lineage.ofValue value)

    /// HORIZON-sketch alias: `branch m` reads as "promote a linear
    /// carrier into a branchable tree." Identical to `ofLineage`.
    let branch (m: Lineage<'a>) : LineageTree<'a> = ofLineage m

    /// Build a Fork from a list of (label, subtree) pairs. The labels
    /// must be unique within the Fork; the smart constructor doesn't
    /// enforce this (consumer responsibility) because uniqueness is a
    /// USE-SITE invariant — some consumers (e.g., property tests) want
    /// to model arbitrary branchings including duplicates.
    let fork (branches: (string * LineageTree<'a>) list) : LineageTree<'a> =
        Fork (branches |> List.map (fun (l, t) -> { Label = l; Tree = t }))

    /// Two-branch convenience: `bifurcate (label1, t1) (label2, t2)`.
    /// The canonical shape for policy-diff (`bifurcate ("A", treeA)
    /// ("B", treeB)`).
    let bifurcate
        (left: string * LineageTree<'a>)
        (right: string * LineageTree<'a>)
        : LineageTree<'a> =
        fork [left; right]

    /// All leaves in left-to-right traversal order. The terminal carriers
    /// of every path in the tree.
    let rec leaves (tree: LineageTree<'a>) : Lineage<'a> list =
        match tree with
        | Leaf m -> [m]
        | Fork branches ->
            branches |> List.collect (fun b -> leaves b.Tree)

    /// All leaves paired with their label-path from root. For a single-
    /// leaf tree, the path is `[]`; for a Fork containing labeled
    /// subtrees, each label prepends to its subtree's paths.
    /// Useful for policy-diff which wants `(["policyA"], lineageA)`
    /// and `(["policyB"], lineageB)` from a top-level bifurcation.
    let rec paths (tree: LineageTree<'a>) : (string list * Lineage<'a>) list =
        match tree with
        | Leaf m -> [([], m)]
        | Fork branches ->
            branches
            |> List.collect (fun b ->
                paths b.Tree
                |> List.map (fun (path, leaf) -> (b.Label :: path, leaf)))

    /// Functor map over every leaf's value. Trail structure preserved.
    let rec map (f: 'a -> 'b) (tree: LineageTree<'a>) : LineageTree<'b> =
        match tree with
        | Leaf m -> Leaf (Lineage.map f m)
        | Fork branches ->
            Fork (branches |> List.map (fun b ->
                { Label = b.Label; Tree = map f b.Tree }))

    /// Prepend events to every leaf's trail. The internal primitive
    /// that `bind` uses to thread the existing-leaf's trail into the
    /// f-produced subtree.
    let rec private prepend (events: LineageEvent list) (tree: LineageTree<'a>) : LineageTree<'a> =
        match tree with
        | Leaf m -> Leaf { Value = m.Value; Trail = events @ m.Trail }
        | Fork branches ->
            Fork (branches |> List.map (fun b ->
                { Label = b.Label; Tree = prepend events b.Tree }))

    /// Monadic bind. For each leaf, apply f to produce a new subtree;
    /// the existing leaf's trail prepends to every continuation leaf
    /// (A24 chronological).
    ///
    /// **Algebra.** This is the free-monad bind. The substitution
    /// preserves both the branching structure (Forks of f-results) AND
    /// the linear-writer trail chronology (existing trail prefixes
    /// every continuation).
    let rec bind (f: 'a -> LineageTree<'b>) (tree: LineageTree<'a>) : LineageTree<'b> =
        match tree with
        | Leaf m ->
            // Substitute f m.Value; prepend m.Trail to every continuation.
            prepend m.Trail (f m.Value)
        | Fork branches ->
            Fork (branches |> List.map (fun b ->
                { Label = b.Label; Tree = bind f b.Tree }))

    /// Collapse a tree to a single `Lineage<'a>` via a caller-supplied
    /// selector over all leaves. The H-005-named "commit" operation:
    /// choose one branch's lineage as the canonical output.
    ///
    /// **Pre-condition.** The tree must contain at least one leaf.
    /// `Fork []` is degenerate (no leaves); `commit` on a leaf-less
    /// tree fails. The smart constructor `fork` doesn't enforce
    /// non-emptiness because algebraically Fork [] is still a valid
    /// (terminal) tree — it's `commit` that has the precondition.
    let commit
        (selector: Lineage<'a> list -> Lineage<'a>)
        (tree: LineageTree<'a>)
        : Lineage<'a> =
        match leaves tree with
        | [] ->
            invalidArg "tree" "LineageTree.commit: tree contains no leaves"
        | xs -> selector xs

    /// Commit by first leaf in left-to-right traversal. The simplest
    /// selector; useful when the consumer doesn't care which branch
    /// (or knows the tree is single-leaf).
    let commitFirst (tree: LineageTree<'a>) : Lineage<'a> =
        commit List.head tree

    /// Commit by label path. Walks the tree following the label
    /// sequence; returns `None` if the path doesn't terminate at a
    /// leaf (e.g., the label isn't present or the subtree is a Fork
    /// at the path's end). Caller-error semantics: the consumer
    /// supplies a path it knows exists.
    let rec tryCommitByPath
        (path: string list)
        (tree: LineageTree<'a>)
        : Lineage<'a> option =
        match path, tree with
        | [], Leaf m -> Some m
        | [], Fork _ -> None  // Path terminated at a Fork (not a leaf)
        | label :: rest, Fork branches ->
            branches
            |> List.tryFind (fun b -> b.Label = label)
            |> Option.bind (fun b -> tryCommitByPath rest b.Tree)
        | _ :: _, Leaf _ -> None  // Path has more labels but tree is a Leaf

    /// True if the tree contains exactly one leaf. The single-branch
    /// case where `LineageTree<'a>` is structurally equivalent to
    /// `Lineage<'a>`; round-trip `commitFirst (ofLineage m) = m` holds.
    let rec isLinear (tree: LineageTree<'a>) : bool =
        match tree with
        | Leaf _ -> true
        | Fork [b] -> isLinear b.Tree  // Single-branch Fork still linear
        | Fork _ -> false  // Multi-branch Fork OR empty Fork is not linear

    /// True if the tree has no leaves (degenerate). Useful for
    /// asserting `commit` preconditions before calling.
    let rec isEmpty (tree: LineageTree<'a>) : bool =
        match tree with
        | Leaf _ -> false
        | Fork [] -> true
        | Fork branches -> branches |> List.forall (fun b -> isEmpty b.Tree)

    /// Number of leaves in the tree.
    let leafCount (tree: LineageTree<'a>) : int =
        leaves tree |> List.length

    /// Functor identity equivalence helper for property tests.
    /// `byValueAndStructure t1 t2` returns true iff both trees have the
    /// same branching structure AND every corresponding leaf carries
    /// the same `(Value, Trail)` pair. This is the full structural
    /// equality used by property tests — distinct from F#-default
    /// equality which projects through `Value` only at the leaf level
    /// (via the inner `Lineage<'a>`'s `[<CustomEquality>]`).
    let rec byValueAndStructure (t1: LineageTree<'a>) (t2: LineageTree<'a>) : bool =
        match t1, t2 with
        | Leaf m1, Leaf m2 -> Lineage.byValueAndTrail m1 m2
        | Fork bs1, Fork bs2 when bs1.Length = bs2.Length ->
            List.zip bs1 bs2
            |> List.forall (fun (b1, b2) ->
                b1.Label = b2.Label && byValueAndStructure b1.Tree b2.Tree)
        | _ -> false


/// `lineageTree { ... }` computation expression builder. The branching-
/// writer-monad analog of `lineage { ... }`. Uses the LineageTree's
/// `Bind` / `Return` over leaf substitution.
type LineageTreeBuilder() =
    member _.Return(x: 'a) : LineageTree<'a> = LineageTree.ofValue x
    member _.ReturnFrom(t: LineageTree<'a>) : LineageTree<'a> = t
    member _.Bind(t: LineageTree<'a>, f: 'a -> LineageTree<'b>) : LineageTree<'b> =
        LineageTree.bind f t
    member _.Zero() : LineageTree<unit> = LineageTree.ofValue ()
    member _.Combine(t1: LineageTree<unit>, t2: LineageTree<'a>) : LineageTree<'a> =
        LineageTree.bind (fun () -> t2) t1
    member _.Delay(f: unit -> LineageTree<'a>) : LineageTree<'a> = f ()
    member _.Run(t: LineageTree<'a>) : LineageTree<'a> = t

[<AutoOpen>]
module LineageTreeBuilders =
    /// The `lineageTree { ... }` CE entry point. Used at speculative-
    /// execution sites where the consumer wants the natural F# syntax
    /// for branching writer computations.
    let lineageTree = LineageTreeBuilder()

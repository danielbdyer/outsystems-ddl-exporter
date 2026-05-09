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


/// The kind of transformation a lineage event records. The set is small
/// and additive — extend rather than reshape when new pass categories
/// appear, so historical lineage trails stay readable.
type TransformKind =
    /// The pass observed the node but introduced no change. Useful as a
    /// witness that a pass ran ("we looked at this and decided nothing").
    | Touched
    /// The pass changed the node's presentation name. Identity is
    /// untouched (A3, A4, A15).
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


/// One step in the provenance chain. Per A23, every event carries a
/// `PassVersion` so functionally different versions of the same pass
/// produce distinguishable lineage and replay determinism is preserved
/// across pipeline evolution.
type LineageEvent = {
    PassName      : string
    PassVersion   : int
    SsKey         : SsKey
    TransformKind : TransformKind
}


/// Writer-monadic carrier for any pass output. Per A25, every IR
/// transformation in the pipeline runs inside `Lineage<_>`; lineage is
/// constitutive, not opt-in. Per A26, lineage is metadata travelling
/// alongside structure — it does not participate in structural equality.
type Lineage<'a> = {
    Value : 'a
    Trail : LineageEvent list
}


/// Construction and composition for `Lineage<_>`. The `bind` operator
/// concatenates trails chronologically per A24: under `f >>= g` the
/// resulting trail is `f.Trail ++ g.Trail` — earliest-first. All passes
/// and all readers rely on this order; reversed-trail bugs are subtle and
/// expensive, so the convention is encoded in code, in `AXIOMS.md`, and
/// in the test suite.
[<RequireQualifiedAccess>]
module Lineage =

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


/// Infix operators for `Lineage<_>`. Open this module at call sites that
/// benefit from `>>=` (the algebra reads more like the formal system).
module LineageOperators =

    /// Bind: `m >>= f` is `Lineage.bind f m`.
    let inline (>>=) (m: Lineage<'a>) (f: 'a -> Lineage<'b>) : Lineage<'b> =
        Lineage.bind f m

    /// Map: `f <!> m` is `Lineage.map f m`.
    let inline (<!>) (f: 'a -> 'b) (m: Lineage<'a>) : Lineage<'b> =
        Lineage.map f m

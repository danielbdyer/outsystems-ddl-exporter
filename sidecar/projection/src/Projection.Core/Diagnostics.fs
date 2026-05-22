namespace Projection.Core

/// Severity of a diagnostic entry. Closed three-way DU; extend rather
/// than reshape when new severity bands are forced.
///
/// `Info` — an observation downstream readers might care about (e.g.,
/// "probe ran but vocabulary truncated"). Not actionable on its own.
///
/// `Warning` — a finding the operator should review (e.g., "this
/// attribute is marked Mandatory but had observed nulls"). Does not
/// block the pipeline; the value the writer carries is still
/// well-formed.
///
/// `Error` — a finding that breaks an invariant or names a structurally
/// unsafe outcome. The writer itself does not fail (Diagnostics is a
/// writer, not a Result); call sites that gate on integrity inspect the
/// Entries and decide whether to short-circuit.
///
/// V1's `TighteningDiagnosticSeverity` is `Info | Warning`; V2 adds
/// `Error` because invariant-violation entries (e.g., a probe's
/// declared structure contradicting its observed values) deserve a
/// structural place in the severity DU rather than living as a
/// Warning that consumers must re-classify.
[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    | Info
    | Warning
    | Error


/// One entry in a diagnostic stream. The shape mirrors V1's
/// `TighteningDiagnostic` and `Opportunity` records but generalizes
/// across producers (passes, adapters, emitters).
///
/// `Source` names the producer. Convention:
///   - `<PassName>` for V2 pass-produced entries
///   - `adapter:<adapter-name>` for boundary-produced entries (e.g.,
///     `adapter:OSSYS`, `adapter:ProfileSnapshot`)
///   - `emitter:<emitter-name>` for Π-produced entries
/// The Source is the audit-trail equivalent of `LineageEvent.PassName`;
/// the same passes that emit lineage events also emit diagnostics, so
/// downstream consumers can join the two streams by Source.
///
/// `Code` is machine-greppable; convention is dot-separated like V1's
/// `tightening.nullability.mandatory.nulls`. Top-prefix routes to a
/// consumer category (`tightening.*`, `profiling.*`, `adapter.*`).
///
/// `Message` is human-readable narration; never parsed structurally.
///
/// `SsKey` is optional. Pass-produced entries identify a specific IR
/// node; adapter-produced entries (e.g., "JSON catalog document was
/// unparseable") have nothing structural to point at and leave SsKey
/// `None`.
///
/// `Metadata` carries structural payload. `Map<string, string>` for
/// the first commit; promote to a typed DU when a consumer demands
/// numeric or compound payload (the structural-commitment pattern,
/// AXIOMS.md operational principle: variants at meaningful inflection
/// points beats parametric values at coarser variants).
///
/// **`SuggestedConfig` field** (chapter B.4 slice 6 actionable-
/// diagnostics): operationalizes logging-format contract §12. Entries
/// whose finding has an addressable config knob carry the structured
/// payload naming the JSON path + value the operator could apply to
/// fix the finding. `None` for entries whose remediation is not
/// config-editable (e.g., source-data integrity violations, V1
/// adapter parse failures, schema-evolution warnings). Pillar 9
/// classification: pure data-intent enrichment — the suggestion
/// derives from the finding's evidence, not from operator opinion.
type DiagnosticEntry = {
    Source          : string
    Severity        : DiagnosticSeverity
    Code            : string
    Message         : string
    SsKey           : SsKey option
    Metadata        : Map<string, string>
    SuggestedConfig : SuggestedConfig option
}


/// Operator-actionable config-edit suggestion attached to a
/// `DiagnosticEntry`. Per logging-format contract §12: when V2
/// detects a condition the operator could fix by editing config,
/// the entry carries this payload naming the exact JSON path +
/// value to apply. The downstream `v2 suggest-config <runId>`
/// CLI consumer (chapter post-B.4) merges these into a single
/// operator-facing config patch.
///
/// **Field shape mirrors contract §12 verbatim:**
///   - `Path` — JSONPath-style selector (e.g.,
///     `$.profiling.perTable["OSUSR_FOO.dbo.OrderHeader"].samplingCap`).
///   - `Value` — serialized JSON value as a string (`"100000"` for
///     a numeric; `"\"merge\""` for a string-shaped enum; `"true"`
///     for a boolean). The string-typed slot defers JSON-value-shape
///     typing until a consumer demands the lift (per the
///     IR-grows-under-evidence discipline).
///   - `Note` — human rationale; optional. Surfaces as `note` in
///     the emitted JSON when `Some`.
and SuggestedConfig = {
    Path  : string
    Value : string
    Note  : string option
}


/// Construction helpers for `DiagnosticEntry`. Mirrors the
/// `Attribute.create` / `Kind.create` / `Reference.create` /
/// `Index.create` pattern (slice 5.13.smart-constructor-lift,
/// 2026-05-18) — the smart constructor absorbs field extensions
/// at one site so consumers stay stable when the IR grows.
///
/// **MVP shape** (chapter B.4 slice 6): required fields are the
/// V1-parity quadruple (`source`, `severity`, `code`, `message`);
/// `ssKey`, `metadata`, `suggestedConfig` default to their no-
/// evidence forms (`None`, `Map.empty`, `None`). Consumers override
/// via record-update: `{ DiagnosticEntry.create src sev code msg
/// with SsKey = Some k; SuggestedConfig = Some cfg }`.
[<RequireQualifiedAccess>]
module DiagnosticEntry =

    /// Build a `DiagnosticEntry` with minimum-evidence defaults.
    /// Required: `source`, `severity`, `code`, `message`. Optional
    /// axes default to:
    ///   - `SsKey = None` (catalog-level diagnostic; per-kind
    ///     diagnostics override via record-update)
    ///   - `Metadata = Map.empty` (no structural payload)
    ///   - `SuggestedConfig = None` (no actionable config edit)
    let create
        (source: string)
        (severity: DiagnosticSeverity)
        (code: string)
        (message: string)
        : DiagnosticEntry =
        {
            Source          = source
            Severity        = severity
            Code            = code
            Message         = message
            SsKey           = None
            Metadata        = Map.empty
            SuggestedConfig = None
        }


/// Construction helpers for `SuggestedConfig`. The smart constructor
/// rejects blank `Path` (the operator-facing JSON-path selector must
/// be non-empty for the suggestion to be actionable). Blank `Value`
/// is permitted (some suggestions are "remove this entry" → empty
/// value).
[<RequireQualifiedAccess>]
module SuggestedConfig =

    /// Build a `SuggestedConfig` with a path + value. Rejects blank
    /// `path` with `ValidationError "suggestedConfig.path.empty"`.
    let create (path: string) (value: string) : Result<SuggestedConfig> =
        if System.String.IsNullOrWhiteSpace path then
            Result.failureOf (
                ValidationError.create
                    "suggestedConfig.path.empty"
                    "SuggestedConfig path cannot be blank.")
        else
            Result.success {
                Path  = path
                Value = value
                Note  = None
            }

    /// Build a `SuggestedConfig` with a path + value + note.
    /// Same validation as `create`; the note adds operator-readable
    /// rationale.
    let createWithNote (path: string) (value: string) (note: string) : Result<SuggestedConfig> =
        if System.String.IsNullOrWhiteSpace path then
            Result.failureOf (
                ValidationError.create
                    "suggestedConfig.path.empty"
                    "SuggestedConfig path cannot be blank.")
        else
            Result.success {
                Path  = path
                Value = value
                Note  = if System.String.IsNullOrWhiteSpace note then None else Some note
            }



/// Writer-monadic carrier for diagnostic entries. Single channel for
/// now per `DECISIONS 2026-05-06 — Diagnostics live in a writer
/// parallel to Lineage`. The three-channel split (operator / auditor /
/// developer) arrives when a real consumer demands differentiation —
/// per the codification's "add variants at meaningful inflection
/// points" discipline (DECISIONS 2026-05-13), the channel split is a
/// structural refinement to defer until evidence forces it.
///
/// Composition with Lineage: a pass that emits both lineage events and
/// diagnostic entries returns `Lineage<Diagnostics<'a>>` — lineage of
/// (value paired with diagnostics). The semantic ordering is "first
/// the structural transformation (lineage), then the observer-relevant
/// findings about that transformation (diagnostics inside the value-
/// carrier)." Helpers in the `LineageDiagnostics` module thread the
/// dual bind.
///
/// Not every pass returns this shape. Passes that emit only lineage
/// keep returning `Lineage<'a>`; passes that emit diagnostics opt into
/// the richer shape via a sibling entry point (e.g.,
/// `UniqueIndexPass.runWithDiagnostics` alongside `UniqueIndexPass.run`).
type Diagnostics<'a> = {
    Value   : 'a
    Entries : DiagnosticEntry list
}


/// Construction and composition for `Diagnostics<_>`. Mirrors the
/// `Lineage` module's shape so the two writers compose predictably and
/// readers familiar with one inherit the other's vocabulary.
[<RequireQualifiedAccess>]
module Diagnostics =

    /// Wrap a value with no entries. The unit of the writer monad.
    let ofValue (value: 'a) : Diagnostics<'a> = { Value = value; Entries = [] }

    /// Wrap a value with a single entry. Convenience for callers that
    /// emit exactly one diagnostic.
    let ofValueWith (entry: DiagnosticEntry) (value: 'a) : Diagnostics<'a> =
        { Value = value; Entries = [entry] }

    /// Append a single entry without changing the value.
    let tell (entry: DiagnosticEntry) (m: Diagnostics<'a>) : Diagnostics<'a> =
        { m with Entries = m.Entries @ [entry] }  // LINT-ALLOW: writer-monad `tell` algebraic primitive; same pattern as `Lineage.tell`

    /// Append several entries without changing the value.
    let tellMany (entries: DiagnosticEntry list) (m: Diagnostics<'a>) : Diagnostics<'a> =
        { m with Entries = m.Entries @ entries }

    /// Functor map — preserves the entries untouched, transforms the
    /// value.
    let map (f: 'a -> 'b) (m: Diagnostics<'a>) : Diagnostics<'b> =
        { Value = f m.Value; Entries = m.Entries }

    /// Monadic bind. Entries concatenate chronologically (mirrors
    /// `Lineage.bind` — m's entries first, then the new entries).
    let bind (f: 'a -> Diagnostics<'b>) (m: Diagnostics<'a>) : Diagnostics<'b> =
        let next = f m.Value
        { Value = next.Value; Entries = m.Entries @ next.Entries }

    /// Write a single entry under the unit value. Mirrors `Lineage.write`
    /// — distinct from `tell` (operational) — `write entry` is the
    /// `Diagnostics<unit>` whose entries are `[entry]`. The CE primitive.
    let write (entry: DiagnosticEntry) : Diagnostics<unit> =
        { Value = (); Entries = [entry] }

    /// Write several entries under the unit value.
    let writeMany (entries: DiagnosticEntry list) : Diagnostics<unit> =
        { Value = (); Entries = entries }

    /// True iff no entries have been emitted. Useful for tests asserting
    /// "this pass produces no diagnostics on the empty-policy path."
    let isClean (m: Diagnostics<'a>) : bool =
        List.isEmpty m.Entries

    /// All entries matching a severity. Useful for tests and for
    /// downstream consumers that route by severity (e.g., emit Errors
    /// to stderr; collect Warnings into a manifest).
    let entriesAt (severity: DiagnosticSeverity) (m: Diagnostics<'a>) : DiagnosticEntry list =
        m.Entries |> List.filter (fun e -> e.Severity = severity)


/// `diagnostics { ... }` CE — same algebraic shape as `lineage { ... }`,
/// over `Diagnostics<'a>` (writer over the `(DiagnosticEntry list, @, [])`
/// monoid). Pairs with `lineageDiagnostics { ... }` for the dual writer.
///
/// Single-channel; the three-channel split (operator / auditor /
/// developer) lands when a real consumer demands differentiation per
/// `DECISIONS 2026-05-06`.
type DiagnosticsBuilder() =
    member _.Return(x: 'a) : Diagnostics<'a> = Diagnostics.ofValue x
    member _.ReturnFrom(m: Diagnostics<'a>) : Diagnostics<'a> = m
    member _.Bind(m: Diagnostics<'a>, f: 'a -> Diagnostics<'b>) : Diagnostics<'b> =
        Diagnostics.bind f m
    member _.Zero() : Diagnostics<unit> = Diagnostics.ofValue ()
    member _.Combine(m1: Diagnostics<unit>, m2: Diagnostics<'a>) : Diagnostics<'a> =
        Diagnostics.bind (fun () -> m2) m1
    member _.Delay(f: unit -> Diagnostics<'a>) : Diagnostics<'a> = f ()
    member _.Run(m: Diagnostics<'a>) : Diagnostics<'a> = m

[<AutoOpen>]
module DiagnosticsBuilders =
    /// The `diagnostics { ... }` CE entry point. Open `Projection.Core`
    /// to bring into scope.
    let diagnostics = DiagnosticsBuilder()


/// Composition helpers for the dual writer `Lineage<Diagnostics<_>>`.
/// A pass that emits both lineage and diagnostics returns a value of
/// this shape; this module provides the dual bind that threads both
/// trails chronologically (A24 holds for the Lineage trail; the
/// Diagnostics entries follow the same earliest-first convention).
///
/// **Algebra (chapter-Cluster-B contribution).** `Lineage<Diagnostics<'a>>`
/// is a **WriterT-stacked** writer monad — `WriterT[LineageEvent]
/// (WriterT[DiagnosticEntry] Identity) 'a` in monad-transformer terms.
/// Both layers carry `(List, ++, [])` monoids; both compose chronologically
/// because the monoid is the same. The dual writer is itself a writer
/// monad over the product monoid `(LineageEvent list × DiagnosticEntry
/// list, ⊕, ([],[]))`; `bind` threads through both layers in one step.
/// This is why the monad laws (LineageDiagnostics:left identity, right
/// identity, associativity in `DiagnosticsTests.fs`) hold by construction
/// from the underlying `Lineage` and `Diagnostics` laws — the stacking
/// preserves them.
///
/// **Why this matters.** The writer-fidelity discipline (`DECISIONS
/// 2026-05-30`) requires pass drivers to use `tellDiagnostics` /
/// `Lineage.ofValueAndEvents` rather than manual record-building. The
/// `lineageDiagnostics { ... }` CE (H-002) is the syntactic enforcement:
/// inside the CE, manual record-building is impossible — every value
/// flows through `bind` / `Return` / `write*`. The discipline IS the
/// monad-law-preserving syntactic surface.
[<RequireQualifiedAccess>]
module LineageDiagnostics =

    /// Lift a plain value into the dual writer with empty trails.
    let ofValue (value: 'a) : Lineage<Diagnostics<'a>> =
        Lineage.ofValue (Diagnostics.ofValue value)

    /// Lift a `Lineage<'a>` into the dual writer with no diagnostics.
    /// Useful when an existing lineage-only pass feeds into a
    /// diagnostic-emitting consumer.
    let ofLineage (m: Lineage<'a>) : Lineage<Diagnostics<'a>> =
        Lineage.map Diagnostics.ofValue m

    /// Lift a `Diagnostics<'a>` into the dual writer with no lineage
    /// trail. Useful for adapter-produced diagnostics whose source is
    /// not a V2 pass.
    let ofDiagnostics (m: Diagnostics<'a>) : Lineage<Diagnostics<'a>> =
        Lineage.ofValue m

    /// Functor map over the inner value. Both trails preserved.
    let map (f: 'a -> 'b) (m: Lineage<Diagnostics<'a>>) : Lineage<Diagnostics<'b>> =
        Lineage.map (Diagnostics.map f) m

    /// Monadic bind. Both trails concatenate chronologically:
    ///   - Lineage trail: m.Trail ++ (f m.Value.Value).Trail
    ///   - Diagnostics entries: m.Value.Entries ++ (f m.Value.Value).Value.Entries
    /// The discipline mirrors `Lineage.bind`'s A24 chronological order.
    let bind
        (f: 'a -> Lineage<Diagnostics<'b>>)
        (m: Lineage<Diagnostics<'a>>)
        : Lineage<Diagnostics<'b>> =
        let next = f m.Value.Value
        { Value =
            { Value = next.Value.Value
              Entries = m.Value.Entries @ next.Value.Entries }
          Trail = m.Trail @ next.Trail }

    /// Append a Lineage event without changing the value or
    /// diagnostics.
    let tellLineage (event: LineageEvent) (m: Lineage<Diagnostics<'a>>) : Lineage<Diagnostics<'a>> =
        Lineage.tell event m

    /// Append a Diagnostic entry without changing the value or trail.
    let tellDiagnostic (entry: DiagnosticEntry) (m: Lineage<Diagnostics<'a>>) : Lineage<Diagnostics<'a>> =
        Lineage.map (Diagnostics.tell entry) m

    /// Append several Diagnostic entries.
    let tellDiagnostics (entries: DiagnosticEntry list) (m: Lineage<Diagnostics<'a>>) : Lineage<Diagnostics<'a>> =
        Lineage.map (Diagnostics.tellMany entries) m

    /// The deep payload — the inner value after both writer layers
    /// are stripped. Self-descriptive accessor for the doubly-nested
    /// shape; replaces the `m.Value.Value` access pattern that requires
    /// readers to count `Value` projections to know which writer they
    /// land in.
    let payload (m: Lineage<Diagnostics<'a>>) : 'a = m.Value.Value

    /// All diagnostic entries from the inner Diagnostics writer.
    /// Symmetric with the outer `m.Trail` for the Lineage writer; both
    /// accessors name the trail they return.
    let entries (m: Lineage<Diagnostics<'a>>) : DiagnosticEntry list = m.Value.Entries

    /// The full inner `Diagnostics<'a>` — useful for callers that want
    /// both payload and entries together but do not care about the
    /// lineage trail.
    let diagnostics (m: Lineage<Diagnostics<'a>>) : Diagnostics<'a> = m.Value

    /// Write a single lineage event under the unit value (dual-writer
    /// form of `Lineage.write`). The CE primitive: `do! writeLineage e`.
    let writeLineage (event: LineageEvent) : Lineage<Diagnostics<unit>> =
        Lineage.ofValueWith event (Diagnostics.ofValue ())

    /// Write a single diagnostic entry under the unit value. CE primitive
    /// `do! writeDiagnostic entry` — the dual-writer counterpart to
    /// `Diagnostics.write`.
    let writeDiagnostic (entry: DiagnosticEntry) : Lineage<Diagnostics<unit>> =
        Lineage.ofValue (Diagnostics.ofValueWith entry ())

    /// Write several diagnostic entries under the unit value.
    let writeDiagnostics (entries: DiagnosticEntry list) : Lineage<Diagnostics<unit>> =
        Lineage.ofValue { Value = (); Entries = entries }


/// `lineageDiagnostics { ... }` CE for the dual writer. Same algebraic
/// shape as `lineage { ... }`, over `Lineage<Diagnostics<'a>>`. The
/// writer-fidelity discipline (DECISIONS 2026-05-30) is structurally
/// inherited: bind composes both trails chronologically via
/// `LineageDiagnostics.bind`; manual record-building is impossible
/// inside the CE. (H-002)
///
/// **Worked equivalence (dual-writer):**
/// ```
/// lineageDiagnostics {                       m |> LineageDiagnostics.bind (fun x ->
///     let! x = m                             LineageDiagnostics.writeLineage e
///     do! LineageDiagnostics.writeLineage e  |> LineageDiagnostics.bind (fun () ->
///     do! LineageDiagnostics.writeDiagnostic d   LineageDiagnostics.writeDiagnostic d
///     return x                          ≡    |> LineageDiagnostics.bind (fun () ->
/// }                                              LineageDiagnostics.ofValue x)))
/// ```
type LineageDiagnosticsBuilder() =
    member _.Return(x: 'a) : Lineage<Diagnostics<'a>> = LineageDiagnostics.ofValue x
    member _.ReturnFrom(m: Lineage<Diagnostics<'a>>) : Lineage<Diagnostics<'a>> = m
    member _.Bind
        ( m: Lineage<Diagnostics<'a>>,
          f: 'a -> Lineage<Diagnostics<'b>>)
        : Lineage<Diagnostics<'b>> =
        LineageDiagnostics.bind f m
    member _.Zero() : Lineage<Diagnostics<unit>> = LineageDiagnostics.ofValue ()
    member _.Combine
        ( m1: Lineage<Diagnostics<unit>>,
          m2: Lineage<Diagnostics<'a>>)
        : Lineage<Diagnostics<'a>> =
        LineageDiagnostics.bind (fun () -> m2) m1
    member _.Delay(f: unit -> Lineage<Diagnostics<'a>>) : Lineage<Diagnostics<'a>> = f ()
    member _.Run(m: Lineage<Diagnostics<'a>>) : Lineage<Diagnostics<'a>> = m

[<AutoOpen>]
module LineageDiagnosticsBuilders =
    /// The `lineageDiagnostics { ... }` CE entry point (H-002).
    let lineageDiagnostics = LineageDiagnosticsBuilder()


/// **H-003: Kleisli arrow.** The pipeline IS a Kleisli category over the
/// dual-writer monad `Lineage<Diagnostics<_>>`. Each pass is an arrow
/// `Pass<'a, 'b>`; composition is `Pass.compose` (the `>=>` operator);
/// the identity arrow is `Pass.id`.
///
/// **Algebraic content.** The Kleisli laws hold by construction:
///   - Left identity:  `Pass.id >=> f      = f`
///   - Right identity: `f      >=> Pass.id = f`
///   - Associativity:  `(f >=> g) >=> h    = f >=> (g >=> h)`
///
/// Tested in `KleisliLawTests.fs`; the laws are theorems over
/// `LineageDiagnostics.bind` (Lineage's A24 + Diagnostics' chronological
/// concat).
///
/// **Operational meaning.** The pass-chain fold in
/// `PassChainAdapter.compose` (line 61) is exactly `Pass.composeAll`:
/// folding `bind` over a list of arrows starting from the identity.
/// Naming the alias makes the algebra legible without changing behavior.
///
/// **A18 amended bound.** `Pass<'a, 'b>` is the structural witness that
/// passes consume only `'a` (typically `Catalog` or `ComposeState`) —
/// never `Policy` directly. Policy enters at registration time (see
/// `RegisteredTransforms.allChainStepsFor`) and is closed over before
/// the arrow shape appears. Per A18 amended, Policy is operator intent
/// reified at the registry; the Kleisli arrow carries data intent only.
type Pass<'a, 'b when 'b : equality> = 'a -> Lineage<Diagnostics<'b>>

/// Companion module for the Kleisli arrow type. Names the category-
/// theoretic operations the pipeline already uses. No behavioural
/// change; the alias and module make the structure legible to
/// contributors reading the composition fold for the first time.
[<RequireQualifiedAccess>]
module Pass =

    /// The identity arrow: `'a -> Lineage<Diagnostics<'a>>` produces an
    /// empty trail and no diagnostics. Kleisli law: `id >=> f = f` and
    /// `f >=> id = f` for every `Pass<_, _>`. Operational shape: the
    /// seed of `PassChainAdapter.compose`'s fold. Eta-expanded to dodge
    /// F#'s value restriction on polymorphic value definitions.
    let id<'a when 'a : equality> (a: 'a) : Lineage<Diagnostics<'a>> =
        LineageDiagnostics.ofValue a

    /// Kleisli composition of two arrows: `(f >=> g) a = f a |> bind g`.
    /// Both writers' trails compose chronologically (A24 for Lineage;
    /// same convention for Diagnostics). Associative by the underlying
    /// `bind`'s associativity.
    let compose
        (f: Pass<'a, 'b>)
        (g: Pass<'b, 'c>)
        : Pass<'a, 'c> =
        fun a -> LineageDiagnostics.bind g (f a)

    /// Compose a list of endo-arrows into one. Folds with `compose` over
    /// `Pass.id`; the empty list reduces to `Pass.id`. This is the
    /// algebraic content of `PassChainAdapter.compose` minus the per-
    /// step Bench scoping — the registry-driven pass chain IS this
    /// fold under different operational decoration.
    let composeAll<'a when 'a : equality> (steps: Pass<'a, 'a> list) : Pass<'a, 'a> =
        steps |> List.fold (fun acc step -> compose acc step) id

    /// **Monoidal product (categorical fan-out).** Run two arrows from
    /// the SAME input; return a pair carrying both outputs. The
    /// **`&&&`** of arrow notation. Companion to `compose` (which is
    /// **`>=>`** — sequential composition); together they form the
    /// closed Kleisli category over the dual-writer monad.
    ///
    /// **Algebra.** `product f g` is the categorical product on
    /// Kleisli arrows over the dual-writer monad. Trails + diagnostics
    /// from both arrows concatenate chronologically (A24 amended) —
    /// `f` runs first by left-bias convention, `g` second. The pair
    /// `(b, c)` is the certified-product output.
    ///
    /// **Type.** `product : Pass<'a, 'b> -> Pass<'a, 'c> -> Pass<'a, 'b * 'c>`.
    /// Note the asymmetry with `compose`: `compose` is sequential
    /// (`'a -> 'b -> 'c`); `product` is parallel (`'a -> ('b, 'c)`).
    ///
    /// **Unlocks.** H-006 (parallel pass composition over
    /// `TopologicalOrder.levels`) is the operational deployment of
    /// this primitive at the pass-chain layer — passes on disjoint
    /// `SsKey` partitions compose via `product` rather than `compose`,
    /// dropping the wall-clock dependency. The product is the static
    /// algebra; the SsKey-disjointness check is the dynamic precondition.
    /// H-009 (multi-target fanout) is the realization: a single
    /// pre-fanout `Pass<Catalog, Catalog>` is `product`'d with sibling-Π
    /// emitter arrows.
    let product
        (f: Pass<'a, 'b>)
        (g: Pass<'a, 'c>)
        : Pass<'a, 'b * 'c> =
        fun a ->
            let fResult = f a
            let gResult = g a
            { Value =
                { Value = (LineageDiagnostics.payload fResult, LineageDiagnostics.payload gResult)
                  Entries = (LineageDiagnostics.entries fResult) @ (LineageDiagnostics.entries gResult) }
              Trail = fResult.Trail @ gResult.Trail }

    /// **First arrow projection (`*** id` for the left factor).** Lift
    /// `f : Pass<'a, 'b>` to operate on the left of a pair, leaving
    /// the right unchanged: `first f : Pass<'a * 'c, 'b * 'c>`.
    /// Algebraically the canonical "do f on left; identity on right"
    /// arrow combinator from arrow notation. Useful for in-flight
    /// transformations that only touch one component of a product.
    let first<'a, 'b, 'c when 'b : equality and 'c : equality>
        (f: Pass<'a, 'b>)
        : Pass<'a * 'c, 'b * 'c> =
        fun (a, c) -> f a |> LineageDiagnostics.map (fun b -> (b, c))

    /// **Second arrow projection.** Dual of `first`. Identity on left,
    /// `g` on right: `second g : Pass<'c * 'a, 'c * 'b>`.
    let second<'a, 'b, 'c when 'b : equality and 'c : equality>
        (g: Pass<'a, 'b>)
        : Pass<'c * 'a, 'c * 'b> =
        fun (c, a) -> g a |> LineageDiagnostics.map (fun b -> (c, b))

/// Kleisli infix operators. `f >=> g` is `Pass.compose f g`; open this
/// module at sites where the Kleisli structure should read like the
/// algebra.
module PassOperators =

    /// Kleisli composition. `f >=> g` is `Pass.compose f g`.
    let inline (>=>)
        (f: Pass<'a, 'b>)
        (g: Pass<'b, 'c>)
        : Pass<'a, 'c> =
        Pass.compose f g

    /// Monoidal product (arrow notation fan-out). `f &&& g` is
    /// `Pass.product f g` — run both arrows from the same input;
    /// produce a pair. Companion to `>=>`.
    let inline (&&&)
        (f: Pass<'a, 'b>)
        (g: Pass<'a, 'c>)
        : Pass<'a, 'b * 'c> =
        Pass.product f g


/// **H-004: Certificate as a first-class type wrapper (Cluster B
/// follow-on; 2026-05-22).** Pairs a pipeline output value with the
/// proof of its production — the lineage trail and diagnostic entries.
/// Same algebraic content as `Lineage<Diagnostics<'a>>` (the in-flight
/// dual-writer carrier); the wrapper exists to make the consumer
/// surface read at the boundary.
///
/// **Algebra.** Where `Pass<'a, 'b>` (H-003) is the **in-flight**
/// Kleisli arrow, `Certificate<'b>` is the **terminal** form: what
/// you have AFTER running the arrow chain to completion. The two are
/// structural isomorphisms via `ofLineageDiagnostics` /
/// `toLineageDiagnostics` — same algebraic content, different consumer-
/// facing role.
///
/// **Naming.** "Certificate" emphasizes the role at the consumer
/// boundary: a downstream consumer receives `Certificate<SsdtBundle>`
/// (not `Lineage<Diagnostics<SsdtBundle>>`); the name tells the reader
/// "this is the END of a pipeline, not a stage in one." The trail is
/// the proof; the diagnostics are the observer-relevant findings; the
/// value is the certified artifact.
///
/// **Unlocks.** Multi-target fanout (H-009) becomes
/// `Certificate<SsdtBundle> * Certificate<JsonBundle> *
/// Certificate<DistributionsBundle>` with a shared trail prefix from
/// the pre-fanout pass chain. The HORIZON entry's "Certificate<DDL>"
/// becomes a concrete type the operator can hold.
///
/// **A24-amended applies.** Certificate's combine operation
/// concatenates trails + diagnostics chronologically; the law inherits
/// from the underlying writer monad's chronological-bind property.
///
/// **Manifest layering.** The HORIZON sketch named a `Manifest` field
/// on Certificate. The Manifest type lives in `Projection.Targets.SSDT`
/// (outside Core); Core's Certificate carries only the algebraic
/// content (value + trail + diagnostics). Pipeline/Targets-layer
/// extensions can pair `Certificate<'a>` with a Manifest in a
/// downstream-specific wrapper (e.g.,
/// `type CertifiedBundle = { Certificate : Certificate<SsdtBundle>;
/// Manifest : Manifest }`). The layering keeps Core pure.
type Certificate<'a when 'a : equality> = {
    Value : 'a
    Trail : LineageEvent list
    Diagnostics : DiagnosticEntry list
}

/// Construction, projection, and composition for `Certificate<'a>`.
[<RequireQualifiedAccess>]
module Certificate =

    /// Build a Certificate from raw value + proof. Used at boundary
    /// sites where the trail / diagnostics arrive separately from the
    /// dual-writer carrier (e.g., manifest emission).
    let create
        (value: 'a)
        (trail: LineageEvent list)
        (diagnostics: DiagnosticEntry list)
        : Certificate<'a> =
        { Value = value; Trail = trail; Diagnostics = diagnostics }

    /// Lift a `Lineage<Diagnostics<'a>>` into the certificate form.
    /// Canonical bridge from the in-flight dual-writer carrier to the
    /// terminal-of-pipeline certificate. Inverse of
    /// `toLineageDiagnostics`.
    let ofLineageDiagnostics (m: Lineage<Diagnostics<'a>>) : Certificate<'a> =
        { Value = LineageDiagnostics.payload m
          Trail = m.Trail
          Diagnostics = LineageDiagnostics.entries m }

    /// Project back to the dual-writer carrier. Inverse of
    /// `ofLineageDiagnostics`; the two are structural isomorphisms.
    let toLineageDiagnostics (c: Certificate<'a>) : Lineage<Diagnostics<'a>> =
        Lineage.ofValueAndEvents c.Trail { Value = c.Value; Entries = c.Diagnostics }

    /// Functor map over the certified value. Trail + diagnostics
    /// preserved.
    let map (f: 'a -> 'b) (c: Certificate<'a>) : Certificate<'b> =
        { Value = f c.Value; Trail = c.Trail; Diagnostics = c.Diagnostics }

    /// Combine two certificates into one carrying the pair of values.
    /// Trails + diagnostics concatenate chronologically (A24 amended:
    /// the chronological-bind law applies to certificate composition
    /// via the underlying writer monad). Useful for multi-target
    /// fanout (H-009) — each emitter produces a certificate from the
    /// same pre-fanout state; `combine` pairs them.
    let combine (a: Certificate<'a>) (b: Certificate<'b>) : Certificate<'a * 'b> =
        { Value = (a.Value, b.Value)
          Trail = a.Trail @ b.Trail
          Diagnostics = a.Diagnostics @ b.Diagnostics }

    /// The empty certificate — a value with no trail and no
    /// diagnostics. Useful for combine's identity element when
    /// folding over a sibling-Π set; algebraically the unit of the
    /// certificate-as-writer monad over the product monoid.
    let ofValue (value: 'a) : Certificate<'a> =
        { Value = value; Trail = []; Diagnostics = [] }


/// **H-008: DiagnosticLattice (Cluster B follow-on; 2026-05-22).** A
/// partial order over `DiagnosticEntry` that names two relations
/// downstream consumers need:
///
///   - **Subsumes** — entry `a` subsumes entry `b` when `a`'s Code is
///     a strict prefix of `b`'s Code (with a `.` separator) and they
///     share the SsKey context (`a.SsKey` is `None` OR equals
///     `b.SsKey`). Examples: `tightening.nullability.mandatory`
///     subsumes `tightening.nullability.mandatory.nulls`; an SsKey-less
///     adapter-level diagnostic subsumes its per-kind detail entries.
///
///   - **Precedes** — entry `a` precedes entry `b` if `a` was emitted
///     before `b` in the trail (insertion order). Encoded by the
///     `Diagnostics<_>` chronological-bind law (A24 amended).
///
/// **Algebra.** The two relations form a partial order whose minimal
/// elements are the entries that no other entry subsumes. The
/// minimal-set reduction `minimal m` drops every entry that another
/// entry already covers — the operator-facing triage surface.
///
/// **Why a partial order, not a total order?** Subsumption is by
/// code-prefix; entries with unrelated code roots are incomparable.
/// `tightening.nullability.mandatory.nulls` and
/// `tightening.uniqueIndex.duplicate` neither subsumes the other; both
/// are minimal in any Diagnostics<_> containing both.
///
/// **Subsumption rule (code-prefix + SsKey context):**
///   - `a.Code = c` and `b.Code = c + "." + suffix`  (strict prefix)
///   - AND  `a.SsKey ∈ {None; Some k}` and `b.SsKey ∈ {None; Some k}`
///     (the contexts are compatible; an unattached entry subsumes
///     any attachment of the same Code root)
///
/// **Unlocks.** Operator triage diagnostics (`v2 diagnose` verb;
/// chapter-Cluster-D adjacent) emit `minimal` reductions of the
/// diagnostics writer's output. Resolving a parent entry's underlying
/// condition propagates to its subsumed entries.
type DiagnosticRelation =
    | Subsumes of subsumer: DiagnosticEntry * subsumed: DiagnosticEntry
    | Precedes of earlier: DiagnosticEntry * later: DiagnosticEntry

[<RequireQualifiedAccess>]
module DiagnosticLattice =

    /// The subsumption rule (code-prefix + SsKey-context). `subsumer`'s
    /// Code is a strict prefix of `subsumed`'s Code (with a `.`
    /// separator); SsKey contexts are compatible (either matches OR
    /// the subsumer has no SsKey — the catalog-level catches its
    /// per-kind detail entries).
    let subsumes (subsumer: DiagnosticEntry) (subsumed: DiagnosticEntry) : bool =
        if subsumer.Code = subsumed.Code then false  // strict prefix; equal codes are not subsumption
        elif subsumer.Code.Length >= subsumed.Code.Length then false
        elif not (subsumed.Code.StartsWith(subsumer.Code)) then false
        elif subsumed.Code.[subsumer.Code.Length] <> '.' then false
        else
            // SsKey context compatibility: subsumer = None (catalog-
            // level catches anything) OR exact match.
            match subsumer.SsKey, subsumed.SsKey with
            | None, _ -> true
            | Some k, Some k' -> k = k'
            | Some _, None -> false  // pinned subsumer can't catch unattached

    /// All Subsumes + Precedes relations holding over the entries in
    /// `m`. The Precedes relation follows insertion order (the
    /// chronological-bind law from A24 amended).
    let relations (m: Diagnostics<'a>) : DiagnosticRelation list =
        let entries = m.Entries
        let subs =
            [ for a in entries do
                for b in entries do
                    if not (System.Object.ReferenceEquals(a, b)) && subsumes a b then
                        yield Subsumes (a, b) ]
        // Precedence is the index-order over entries.
        let preceds =
            [ for i in 0 .. entries.Length - 1 do
                for j in i + 1 .. entries.Length - 1 do
                    yield Precedes (entries.[i], entries.[j]) ]
        subs @ preceds

    /// True if no entry in `m` subsumes `entry` (entry is minimal).
    /// Used by `minimal` to filter the lattice's minimal elements.
    let isMinimal (m: Diagnostics<'a>) (entry: DiagnosticEntry) : bool =
        m.Entries
        |> List.forall (fun other ->
            System.Object.ReferenceEquals(other, entry)
            || not (subsumes other entry))

    /// Minimal subset — keeps only entries that no other entry
    /// subsumes. The operator-facing triage surface; collapsing
    /// hierarchical errors to their root cause.
    ///
    /// **Properties** (tested in `DiagnosticsTests.fs`):
    ///   - **Idempotence:** `minimal (minimal m) = minimal m`.
    ///   - **Containment:** every entry in `minimal m` is also in `m`.
    ///   - **Subsumption-completeness:** no entry in `minimal m`
    ///     subsumes any other entry in `minimal m` (the result is an
    ///     antichain in the partial order).
    let minimal (m: Diagnostics<'a>) : Diagnostics<'a> =
        { Value = m.Value
          Entries = m.Entries |> List.filter (isMinimal m) }


/// **H-010: Prism (Cluster B follow-on; 2026-05-22).** A partial
/// bidirectional accessor: `Get` always succeeds; `ReverseGet` may
/// fail. The categorical Prism: a pair (forward, backward) where
/// `forward >> backward = Some ∘ id` modulo a documented set of
/// admissible inputs.
///
/// **Algebra.** Pairs with `Pass<'a, 'b>` (the unidirectional Kleisli
/// arrow) as its bidirectional dual: where `Pass` carries proof of
/// production in one direction, `Prism` carries proof of round-trip
/// in both. The canonical use: bidirectional Catalog ↔ DDL spec —
/// the emitter is `prism.Get`; the reader is `prism.ReverseGet`;
/// round-trip is the algebraic law.
///
/// **Roundtrip law.** `∀ a ∈ Lawful. ReverseGet (Get a) = Some a'`
/// where `a'` is structurally equivalent to `a` modulo the
/// documented lossy fields (i.e., fields the prism cannot recover —
/// these are exactly the `manifest.Unsupported` entries when the
/// prism is the catalog-DDL bridge).
///
/// **Why not a Lens?** A Lens is total in both directions; not every
/// `'b` round-trips to an `'a` (a malformed DDL string isn't always
/// a Catalog). The Prism's `option` return on `ReverseGet` names the
/// asymmetry structurally.
///
/// **Unlocks.** H-058 (ReadSide full-fidelity round-trip), H-093
/// (manifest signing — the prism's lawful subset is the certifiably
/// signable artifact), the canary's PhysicalSchema diff (which
/// already operates the prism informally).
type Prism<'a, 'b> = {
    Get        : 'a -> 'b
    ReverseGet : 'b -> 'a option
}

[<RequireQualifiedAccess>]
module Prism =

    /// Apply the forward direction.
    let get (p: Prism<'a, 'b>) (a: 'a) : 'b = p.Get a

    /// Apply the backward direction (may fail).
    let reverseGet (p: Prism<'a, 'b>) (b: 'b) : 'a option = p.ReverseGet b

    /// True if `a` satisfies the round-trip law: `reverseGet (get a) =
    /// Some a'` where `a' = a` under the chosen equivalence.
    /// `eq` is the equivalence relation that defines lossless-ness for
    /// this prism (often structural equality; sometimes equality modulo
    /// documented lossy fields).
    let roundtrips
        (p: Prism<'a, 'b>)
        (eq: 'a -> 'a -> bool)
        (a: 'a)
        : bool =
        match p.ReverseGet (p.Get a) with
        | Some a' -> eq a a'
        | None    -> false

    /// Split a sample sequence into (lawful, violating) pairs by the
    /// round-trip law. Useful for canary tests: lawful values populate
    /// the certifiable subset; violating values populate the
    /// `manifest.Unsupported` list.
    let partition
        (p: Prism<'a, 'b>)
        (eq: 'a -> 'a -> bool)
        (xs: 'a seq)
        : 'a list * 'a list =
        let lawful = System.Collections.Generic.List<'a>()
        let violating = System.Collections.Generic.List<'a>()
        for a in xs do
            if roundtrips p eq a then lawful.Add(a)
            else violating.Add(a)
        List.ofSeq lawful, List.ofSeq violating

    /// Identity prism — `Get = id`, `ReverseGet = Some`. Lossless by
    /// construction; useful as a smoke-test fixture and as the unit
    /// of prism composition.
    let identity<'a> : Prism<'a, 'a> = {
        Get = id
        ReverseGet = Some
    }

    /// Compose two prisms. Composition holds the round-trip law when
    /// both factors hold it.
    let compose (outer: Prism<'a, 'b>) (inner: Prism<'b, 'c>) : Prism<'a, 'c> = {
        Get = fun a -> inner.Get (outer.Get a)
        ReverseGet = fun c ->
            inner.ReverseGet c
            |> Option.bind outer.ReverseGet
    }


/// **H-015: Lens (Cluster B follow-on; 2026-05-22).** A total
/// bidirectional accessor: `Get` always succeeds; `Set` always
/// succeeds. The categorical Lens — sibling to `Prism<'a, 'b>` (which
/// is partial bidirectional) and completing the optics duo.
///
/// **Algebra.** Where `Prism` is the bidirectional partial dual of
/// `Pass<'a, 'b>` (the unidirectional Kleisli arrow), `Lens<'s, 'a>`
/// is the **total** bidirectional view of a substructure 'a within a
/// supercatalog 's. Used for deep-nested record updates where the
/// existing F# `{ x with Foo = { x.Foo with Bar = ... } }` shape
/// gets verbose at 2+ levels of nesting.
///
/// **Lens laws (property-tested in `DiagnosticsTests.fs`):**
///   - **Get-Set:** `set (get s) s = s` — setting back the gotten
///     value yields the original.
///   - **Set-Get:** `get (set a s) = a` — getting back the set value
///     yields what was set.
///   - **Set-Set:** `set a' (set a s) = set a' s` — the second set
///     overwrites the first.
///
/// **Why a Lens, not a Prism?** A Lens is total — both `Get` and `Set`
/// always succeed. A Prism's `ReverseGet` may fail (partial). For
/// fields that always exist (e.g., `Catalog.Modules`, `Module.Kinds`),
/// the Lens is the correct optic. For fields that may not exist
/// (e.g., a specific SsKey within `Catalog.kindIndex`), use a
/// `Prism<Catalog, Kind>`.
///
/// **Composition.** Lenses compose: `compose (outer : Lens<'s, 'a>)
/// (inner : Lens<'a, 'b>) : Lens<'s, 'b>`. The composed Get/Set thread
/// through both layers — `outerGet >> innerGet` and `innerSet >>
/// outerSet`. The Lens laws hold under composition when they hold for
/// each factor.
///
/// **Unlocks.** Deep-nested updates in passes (Policy.fs, SymmetricClosure.fs)
/// compress massively when the Lens composition is named. Every future
/// deep-update site uses the existing canonical lenses without
/// reinventing the boilerplate.
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
/// 2026-05-22).** Sites that compose deep updates over Catalog →
/// Module → Kind / Attribute / Reference / Index land here as
/// reusable optics; consumers compose them via `Lens.compose` to
/// reach arbitrary depth without re-deriving the boilerplate.
[<RequireQualifiedAccess>]
module CatalogLenses =

    /// `Catalog.Modules`. The outer layer; every deeper catalog lens
    /// composes through this.
    let modules : Lens<Catalog, Module list> = {
        Get = fun c -> c.Modules
        Set = fun ms c -> { c with Modules = ms }
    }

    /// `Catalog.Sequences`. Top-level sequences (separate from kinds).
    let sequences : Lens<Catalog, Sequence list> = {
        Get = fun c -> c.Sequences
        Set = fun ss c -> { c with Sequences = ss }
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

    /// `Kind.Indexes`.
    let indexesOf : Lens<Kind, Index list> = {
        Get = fun k -> k.Indexes
        Set = fun idxs k -> { k with Indexes = idxs }
    }


/// **H-062: PassContext (Reader comonad surface; Cluster B follow-on;
/// 2026-05-22).** A value paired with an environment — the comonadic
/// dual of `Pass<'a, 'b>`. Where `Pass` is the **Kleisli arrow**
/// threading operations forward through writer effects, `PassContext`
/// is the **CoKleisli** carrier threading environments through
/// comonadic projection.
///
/// **Algebra.** `PassContext<'env, 'a>` is the reader comonad
/// (sometimes called the env-comonad or product-comonad):
///   - **extract :** `PassContext<'env, 'a> -> 'a` — get the value
///     out, dropping the environment. The counit.
///   - **ask :** `PassContext<'env, 'a> -> 'env` — read the
///     environment, leaving the value untouched.
///   - **extend :** `(PassContext<'env, 'a> -> 'b) -> PassContext<
///     'env, 'a> -> PassContext<'env, 'b>` — apply a
///     context-aware function to derive a new value, preserving the
///     environment. The dual of monadic bind.
///
/// **Why we ship this primitive.** Many passes currently take
/// `(Policy * Profile)` as a positional pair; the threading at every
/// call site is observer-visible noise. `PassContext<Policy * Profile, 'a>`
/// names the context explicitly — passes that need the context call
/// `ask`; passes that don't, ignore it. The integration with existing
/// pass drivers defers (operator-pull pressure required), but the
/// primitive is in place when the threading site fires its trigger.
///
/// **Comonad laws** (tested in `DiagnosticsTests.fs`):
///   - **Left identity:** `extend extract ctx = ctx`
///   - **Right identity:** `extract (extend f ctx) = f ctx`
///   - **Associativity:** `extend f (extend g ctx) = extend (fun c -> f (extend g c)) ctx`
///
/// **Sibling to Pass<'a, 'b>.** Together they cover both legs of the
/// algebraic spectrum — Pass for forward Kleisli composition; PassContext
/// for comonadic projection of a shared context. Future H-016 (PolicyExpr)
/// adoption may use PassContext to thread the resolved policy through
/// pass evaluation without re-threading at each call.
type PassContext<'env, 'a> = {
    Environment : 'env
    Value : 'a
}

[<RequireQualifiedAccess>]
module PassContext =

    /// Counit of the comonad. Extract the value, dropping the
    /// environment.
    let extract (ctx: PassContext<'env, 'a>) : 'a = ctx.Value

    /// Read the environment without consuming the context. The reader-
    /// comonad's defining operation.
    let ask (ctx: PassContext<'env, 'a>) : 'env = ctx.Environment

    /// Lift a value into a context with a given environment.
    let ofValue (env: 'env) (value: 'a) : PassContext<'env, 'a> =
        { Environment = env; Value = value }

    /// Functor map over the value. Environment preserved.
    let map (f: 'a -> 'b) (ctx: PassContext<'env, 'a>) : PassContext<'env, 'b> =
        { Environment = ctx.Environment; Value = f ctx.Value }

    /// Comonadic extend (dual of monadic bind). Apply a context-aware
    /// function to derive a new value; environment preserved.
    let extend
        (f: PassContext<'env, 'a> -> 'b)
        (ctx: PassContext<'env, 'a>)
        : PassContext<'env, 'b> =
        { Environment = ctx.Environment; Value = f ctx }

    /// Apply a function of the environment to the value. Convenience
    /// for the common reader idiom `ask (>>=) value` rewritten as
    /// `applyEnv f ctx = { ctx with Value = f ctx.Environment ctx.Value }`.
    let applyEnv
        (f: 'env -> 'a -> 'b)
        (ctx: PassContext<'env, 'a>)
        : PassContext<'env, 'b> =
        { Environment = ctx.Environment; Value = f ctx.Environment ctx.Value }

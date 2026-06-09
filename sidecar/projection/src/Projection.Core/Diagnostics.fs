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
///     `adapter:OSSYS`, `adapter:LiveProfiler`)
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

    /// Write several lineage events under the unit value. CE primitive:
    /// `do! writeLineages events` — the multi-event sibling of
    /// `writeLineage`. Symmetric to `writeDiagnostics` over the diagnostic
    /// trail. Earned its place at 2026-06-02 CE-adoption sweep when seven
    /// pass-driver sites (CentralityPass / BoundedContextPass /
    /// SchemaComplexityPass / QueryHintPass / ProfileAnomalyPass /
    /// TopologicalOrderPass ×2) all needed to drain a `LineageEvent list`
    /// into the dual-writer at the pass tail.
    let writeLineages (events: LineageEvent list) : Lineage<Diagnostics<unit>> =
        Lineage.ofValueAndEvents events (Diagnostics.ofValue ())

    /// The **"every kind was visited" lineage epilogue** — the
    /// `DataIntent` writer tail shared by the analytics passes
    /// (Centrality / BoundedContext / SchemaComplexity / ProfileAnomaly /
    /// QueryHint). Each scans the catalog, computes a derived result, and
    /// witnesses its scan by emitting one `Touched` / `DataIntent`
    /// `LineageEvent` per node touched (per A25) alongside any findings it
    /// surfaced.
    ///
    /// `touched` is the ordered list of `SsKey`s the pass observed — the
    /// caller projects it from the source it scanned (graph `nodes`
    /// directly, or `allKinds |> List.map (fun k -> k.SsKey)`). One event
    /// is emitted per key, in order; all are `Touched` / `DataIntent`
    /// because the scan carries no operator opinion (the result is the
    /// pass's opinion, not the per-node witness). `findings` are the
    /// diagnostics the pass surfaced (empty when the pass emits none).
    ///
    /// Behaviourally identical to the hand-rolled
    /// `lineageDiagnostics { do! writeLineages events
    ///                       do! writeDiagnostics findings
    ///                       return result }`
    /// tail it replaces — the events are built here from `passName` /
    /// `passVersion` so the per-pass driver no longer constructs the
    /// `LineageEvent` record by hand (writer-fidelity discipline,
    /// `DECISIONS 2026-05-30`).
    let touchedEpilogue
        (passName: string)
        (passVersion: int)
        (touched: SsKey list)
        (findings: DiagnosticEntry list)
        (result: 'a)
        : Lineage<Diagnostics<'a>> =
        let events =
            touched
            |> List.map (fun key ->
                { PassName       = passName
                  PassVersion    = passVersion
                  SsKey          = key
                  TransformKind  = Touched
                  Classification = DataIntent })
        // Explicit desugaring of
        //   lineageDiagnostics { do! writeLineages events
        //                        do! writeDiagnostics findings
        //                        return result }
        // — the `lineageDiagnostics` CE is declared below this module, so
        // the dual-writer tail is threaded directly through `bind` here
        // (the worked equivalence is documented on the CE type).
        writeLineages events
        |> bind (fun () ->
            writeDiagnostics findings
            |> bind (fun () -> ofValue result))


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
    /// Auto-lift overload (2026-06-02 CE-adoption sweep): a plain
    /// `Lineage<'a>` lifts into the dual-writer via `ofLineage` so
    /// pass-driver tails can chain a `Composition.fanOut`-shaped
    /// `Lineage<DecisionSet>` directly into `do! writeDiagnostics`
    /// without an explicit `let! v = LineageDiagnostics.ofLineage m`
    /// step. Worked precedent: Nullability / UniqueIndex / ForeignKey
    /// pass `run` tails (`let! value = lineage; do! writeDiagnostics
    /// entries; return value`).
    member _.Bind
        ( m: Lineage<'a>,
          f: 'a -> Lineage<Diagnostics<'b>>)
        : Lineage<Diagnostics<'b>> =
        LineageDiagnostics.bind f (LineageDiagnostics.ofLineage m)
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

/// **H-008: DiagnosticLattice.subsumes (Cluster B follow-on; 2026-05-22;
/// trimmed 2026-06-04).** The subsumption predicate over `DiagnosticEntry`:
/// `a` subsumes `b` when `a`'s Code is a strict, separator-bounded prefix
/// of `b`'s Code AND the SsKey contexts are compatible (`a.SsKey` is `None`
/// — catalog-level catches anything — OR equals `b.SsKey`). Example:
/// `tightening.nullability.mandatory` subsumes
/// `tightening.nullability.mandatory.nulls`; an SsKey-less adapter-level
/// entry subsumes its per-kind detail entries. Entries with unrelated code
/// roots are incomparable (it's a partial order, not total).
///
/// **Retirement note.** The full lattice (`DiagnosticRelation`,
/// `relations`, `isMinimal`, the `minimal` antichain reduction) was retired
/// 2026-06-04 — zero production consumers (the `diagnose` verb never
/// materialized). Only `subsumes` is kept, as a documented design note;
/// rebuild the minimal-set reduction with the operator `diagnose` verb when
/// it lands — likely as rollup-with-counts rather than antichain-deletion
/// (operators usually want collapse-but-expand, not discard).
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

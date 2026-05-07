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
type DiagnosticEntry = {
    Source   : string
    Severity : DiagnosticSeverity
    Code     : string
    Message  : string
    SsKey    : SsKey option
    Metadata : Map<string, string>
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
        { m with Entries = m.Entries @ [entry] }

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

    /// True iff no entries have been emitted. Useful for tests asserting
    /// "this pass produces no diagnostics on the empty-policy path."
    let isClean (m: Diagnostics<'a>) : bool =
        List.isEmpty m.Entries

    /// All entries matching a severity. Useful for tests and for
    /// downstream consumers that route by severity (e.g., emit Errors
    /// to stderr; collect Warnings into a manifest).
    let entriesAt (severity: DiagnosticSeverity) (m: Diagnostics<'a>) : DiagnosticEntry list =
        m.Entries |> List.filter (fun e -> e.Severity = severity)


/// Composition helpers for the dual writer `Lineage<Diagnostics<_>>`.
/// A pass that emits both lineage and diagnostics returns a value of
/// this shape; this module provides the dual bind that threads both
/// trails chronologically (A24 holds for the Lineage trail; the
/// Diagnostics entries follow the same earliest-first convention).
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

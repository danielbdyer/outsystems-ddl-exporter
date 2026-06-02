module Projection.Tests.DiagnosticsTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.PassOperators
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers — small named constructors for test entries.
// ---------------------------------------------------------------------------

let private mkKey s = testKey s

let private entry source severity code message =
    { Source   = source
      Severity = severity
      Code     = code
      Message  = message
      SsKey    = None
      Metadata = Map.empty
      SuggestedConfig = None }

let private entryFor key source severity code message =
    { Source   = source
      Severity = severity
      Code     = code
      Message  = message
      SsKey    = Some key
      Metadata = Map.empty
      SuggestedConfig = None }

// ---------------------------------------------------------------------------
// Diagnostics: smart constructors.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.ofValue produces empty entries`` () =
    let m = Diagnostics.ofValue 42
    Assert.Equal(42, m.Value)
    Assert.Empty(m.Entries)

[<Fact>]
let ``Diagnostics.ofValueWith carries one entry`` () =
    let e = entry "TestPass" DiagnosticSeverity.Info "test.code" "hello"
    let m = Diagnostics.ofValueWith e 42
    Assert.Equal(42, m.Value)
    Assert.Single(m.Entries) |> ignore
    Assert.Equal(e, m.Entries.[0])

// ---------------------------------------------------------------------------
// Diagnostics.tell / tellMany — earliest-first append.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.tell appends a single entry chronologically`` () =
    let e1 = entry "TestPass" DiagnosticSeverity.Info "first" "hello"
    let e2 = entry "TestPass" DiagnosticSeverity.Warning "second" "world"
    let m =
        Diagnostics.ofValue 1
        |> Diagnostics.tell e1
        |> Diagnostics.tell e2
    Assert.Equal<DiagnosticEntry list>([e1; e2], m.Entries)

[<Fact>]
let ``Diagnostics.tellMany preserves entry order`` () =
    let e1 = entry "P" DiagnosticSeverity.Info "a" "1"
    let e2 = entry "P" DiagnosticSeverity.Info "b" "2"
    let e3 = entry "P" DiagnosticSeverity.Info "c" "3"
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany [e1; e2; e3]
    Assert.Equal<DiagnosticEntry list>([e1; e2; e3], m.Entries)

// ---------------------------------------------------------------------------
// Diagnostics: functor laws.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.map preserves entries`` () =
    let e = entry "P" DiagnosticSeverity.Warning "c" "m"
    let m =
        Diagnostics.ofValueWith e 1
        |> Diagnostics.map (fun n -> n * 2)
    Assert.Equal(2, m.Value)
    Assert.Equal<DiagnosticEntry list>([e], m.Entries)

[<Fact>]
let ``Diagnostics.map identity = id`` () =
    let e = entry "P" DiagnosticSeverity.Info "x" "y"
    let m = Diagnostics.ofValueWith e 7
    let after = Diagnostics.map id m
    Assert.Equal(m, after)

// ---------------------------------------------------------------------------
// Diagnostics: monad laws — bind concatenates entries.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.bind concatenates entries chronologically`` () =
    let e1 = entry "P1" DiagnosticSeverity.Info "first" "a"
    let e2 = entry "P2" DiagnosticSeverity.Warning "second" "b"
    let m =
        Diagnostics.ofValueWith e1 10
        |> Diagnostics.bind (fun n -> Diagnostics.ofValueWith e2 (n + 1))
    Assert.Equal(11, m.Value)
    Assert.Equal<DiagnosticEntry list>([e1; e2], m.Entries)

[<Fact>]
let ``Diagnostics.bind with empty f produces same trail`` () =
    let e = entry "P" DiagnosticSeverity.Info "c" "m"
    let m =
        Diagnostics.ofValueWith e 1
        |> Diagnostics.bind (fun n -> Diagnostics.ofValue (n + 1))
    Assert.Equal(2, m.Value)
    Assert.Equal<DiagnosticEntry list>([e], m.Entries)

// ---------------------------------------------------------------------------
// Diagnostics: utility predicates.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.isClean true for empty entries`` () =
    Assert.True(Diagnostics.isClean (Diagnostics.ofValue 0))

[<Fact>]
let ``Diagnostics.isClean false when an entry exists`` () =
    let e = entry "P" DiagnosticSeverity.Info "c" "m"
    Assert.False(Diagnostics.isClean (Diagnostics.ofValueWith e 0))

[<Fact>]
let ``Diagnostics.entriesAt filters by severity`` () =
    let e1 = entry "P" DiagnosticSeverity.Info "i1" "info one"
    let e2 = entry "P" DiagnosticSeverity.Warning "w1" "warn one"
    let e3 = entry "P" DiagnosticSeverity.Warning "w2" "warn two"
    let e4 = entry "P" DiagnosticSeverity.Error "x1" "error one"
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany [e1; e2; e3; e4]
    Assert.Equal<DiagnosticEntry list>([e1], Diagnostics.entriesAt DiagnosticSeverity.Info m)
    Assert.Equal<DiagnosticEntry list>([e2; e3], Diagnostics.entriesAt DiagnosticSeverity.Warning m)
    Assert.Equal<DiagnosticEntry list>([e4], Diagnostics.entriesAt DiagnosticSeverity.Error m)

// ---------------------------------------------------------------------------
// LineageDiagnostics: dual writer composition.
// ---------------------------------------------------------------------------

let private lineageEvent (key: SsKey) (kind: TransformKind) =
    // Chapter A.4.7 slice α: dual-writer composition tests are
    // shape-only; `DataIntent` is the test-fixture convention.
    { PassName       = "TestPass"
      PassVersion    = 1
      SsKey          = key
      TransformKind  = kind
      Classification = DataIntent }

[<Fact>]
let ``LineageDiagnostics.ofValue is empty in both trails`` () =
    let m = LineageDiagnostics.ofValue 42
    Assert.Equal(42, LineageDiagnostics.payload m)
    Assert.Empty(m.Trail)
    Assert.Empty(LineageDiagnostics.entries m)

[<Fact>]
let ``LineageDiagnostics.ofLineage preserves the lineage trail`` () =
    let key = mkKey "k"
    let inner = Lineage.ofValueWith (lineageEvent key Touched) 1
    let dual = LineageDiagnostics.ofLineage inner
    Assert.Equal(1, LineageDiagnostics.payload dual)
    Assert.Single(dual.Trail) |> ignore
    Assert.Empty(LineageDiagnostics.entries dual)

[<Fact>]
let ``LineageDiagnostics.ofDiagnostics preserves the diagnostics entries`` () =
    let e = entry "P" DiagnosticSeverity.Info "c" "m"
    let inner = Diagnostics.ofValueWith e 1
    let dual = LineageDiagnostics.ofDiagnostics inner
    Assert.Equal(1, LineageDiagnostics.payload dual)
    Assert.Empty(dual.Trail)
    Assert.Equal<DiagnosticEntry list>([e], LineageDiagnostics.entries dual)

// Self-descriptive accessors for the dual writer. These tests use raw
// record access deliberately — they verify the helpers project the
// structure they claim. Other tests consume the helpers; reaching past
// the helper to .Value.Value is the smell the helpers exist to
// address.

[<Fact>]
let ``LineageDiagnostics.payload returns the deep value (raw structural assertion)`` () =
    let dual = LineageDiagnostics.ofValue 42
    Assert.Equal(dual.Value.Value, LineageDiagnostics.payload dual)
    Assert.Equal(42, LineageDiagnostics.payload dual)

[<Fact>]
let ``LineageDiagnostics.entries returns the diagnostic entries (raw structural assertion)`` () =
    let e1 = entry "P" DiagnosticSeverity.Info "c1" "m1"
    let e2 = entry "P" DiagnosticSeverity.Warning "c2" "m2"
    let dual =
        LineageDiagnostics.ofValue 0
        |> LineageDiagnostics.tellDiagnostic e1
        |> LineageDiagnostics.tellDiagnostic e2
    Assert.Equal<DiagnosticEntry list>(dual.Value.Entries, LineageDiagnostics.entries dual)
    Assert.Equal<DiagnosticEntry list>([e1; e2], LineageDiagnostics.entries dual)

[<Fact>]
let ``LineageDiagnostics.diagnostics returns the inner Diagnostics (raw structural assertion)`` () =
    let e = entry "P" DiagnosticSeverity.Info "c" "m"
    let dual =
        LineageDiagnostics.ofValue 7
        |> LineageDiagnostics.tellDiagnostic e
    let diag = LineageDiagnostics.diagnostics dual
    Assert.Equal(dual.Value, diag)
    Assert.Equal(7, diag.Value)
    Assert.Equal<DiagnosticEntry list>([e], diag.Entries)

[<Fact>]
let ``LineageDiagnostics.tellLineage appends a lineage event without touching diagnostics`` () =
    let key = mkKey "k"
    let evt = lineageEvent key Touched
    let dual =
        LineageDiagnostics.ofValue 1
        |> LineageDiagnostics.tellLineage evt
    Assert.Equal<LineageEvent list>([evt], dual.Trail)
    Assert.Empty(dual.Value.Entries)

[<Fact>]
let ``LineageDiagnostics.tellDiagnostic appends a diagnostic without touching lineage`` () =
    let e = entry "P" DiagnosticSeverity.Warning "c" "m"
    let dual =
        LineageDiagnostics.ofValue 1
        |> LineageDiagnostics.tellDiagnostic e
    Assert.Empty(dual.Trail)
    Assert.Equal<DiagnosticEntry list>([e], dual.Value.Entries)

// ---------------------------------------------------------------------------
// LineageDiagnostics.bind: A24-equivalent for both trails.
// "Earliest-first" holds for lineage trail AND for diagnostic entries.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A24-equivalent: LineageDiagnostics.bind concatenates both trails chronologically`` () =
    let key1 = mkKey "k1"
    let key2 = mkKey "k2"
    let evt1 = lineageEvent key1 Touched
    let evt2 = lineageEvent key2 Touched
    let e1 = entry "P1" DiagnosticSeverity.Info "first" "a"
    let e2 = entry "P2" DiagnosticSeverity.Warning "second" "b"

    let m1 =
        LineageDiagnostics.ofValue 10
        |> LineageDiagnostics.tellLineage evt1
        |> LineageDiagnostics.tellDiagnostic e1

    let f (n: int) : Lineage<Diagnostics<int>> =
        LineageDiagnostics.ofValue (n + 1)
        |> LineageDiagnostics.tellLineage evt2
        |> LineageDiagnostics.tellDiagnostic e2

    let m2 = LineageDiagnostics.bind f m1

    Assert.Equal(11, LineageDiagnostics.payload m2)
    Assert.Equal<LineageEvent list>([evt1; evt2], m2.Trail)
    Assert.Equal<DiagnosticEntry list>([e1; e2], LineageDiagnostics.entries m2)

[<Fact>]
let ``LineageDiagnostics.map preserves both trails`` () =
    let key = mkKey "k"
    let evt = lineageEvent key Touched
    let e = entry "P" DiagnosticSeverity.Info "c" "m"

    let m =
        LineageDiagnostics.ofValue 5
        |> LineageDiagnostics.tellLineage evt
        |> LineageDiagnostics.tellDiagnostic e
        |> LineageDiagnostics.map (fun n -> n * 2)

    Assert.Equal(10, LineageDiagnostics.payload m)
    Assert.Equal<LineageEvent list>([evt], m.Trail)
    Assert.Equal<DiagnosticEntry list>([e], LineageDiagnostics.entries m)

// ---------------------------------------------------------------------------
// Structural commitment — DiagnosticEntry's SsKey field is option, so
// adapter-produced entries that have no IR node to point at construct
// validly without surfacing a fake SsKey.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DiagnosticEntry.SsKey can be None for adapter-level diagnostics`` () =
    let e = entry "adapter:OSSYS" DiagnosticSeverity.Error "adapter.ossys.unparseable" "JSON document failed to parse"
    Assert.Equal(None, e.SsKey)

[<Fact>]
let ``DiagnosticEntry.SsKey carries the IR node for pass-level diagnostics`` () =
    let key = mkKey "AppCore.User.Email"
    let e = entryFor key "UniqueIndexPass" DiagnosticSeverity.Warning "tightening.uniqueIndex.opportunity" "Unique index not enforced"
    Assert.Equal(Some key, e.SsKey)

// ---------------------------------------------------------------------------
// H-053 expansion — `Diagnostics<'a>` monad laws. Mirrors the LineageTests
// triple. Diagnostics is a writer monad over the `(DiagnosticEntry list, @, [])`
// monoid; the laws hold for any underlying value type.
//
//   left identity   : bind f (ofValue x)  =  f x
//   right identity  : bind ofValue m      =  m
//   associativity   : bind g (bind f m)   =  bind (fun x -> bind g (f x)) m
//
// Together with `LineageTests`'s Lineage triple, this asserts that the
// `LineageDiagnostics` stack (tested below) inherits monad-law correctness
// from both factors.
// ---------------------------------------------------------------------------

[<Property>]
let ``Diagnostics monad: left identity`` (x: int) =
    let f y = Diagnostics.ofValueWith (entry "f" DiagnosticSeverity.Info "code" "msg") (y + 1)
    let lhs = Diagnostics.bind f (Diagnostics.ofValue x)
    let rhs = f x
    lhs = rhs

[<Property>]
let ``Diagnostics monad: right identity`` (x: int) =
    let m = Diagnostics.ofValueWith (entry "obs" DiagnosticSeverity.Info "code" "msg") x
    let lhs = Diagnostics.bind Diagnostics.ofValue m
    lhs = m

[<Property>]
let ``Diagnostics monad: associativity`` (x: int) =
    let f y = Diagnostics.ofValueWith (entry "f" DiagnosticSeverity.Info "c1" "m1") (y + 1)
    let g y = Diagnostics.ofValueWith (entry "g" DiagnosticSeverity.Warning "c2" "m2") (y * 3)
    let m = Diagnostics.ofValue x
    let lhs = Diagnostics.bind g (Diagnostics.bind f m)
    let rhs = Diagnostics.bind (fun y -> Diagnostics.bind g (f y)) m
    lhs = rhs

[<Property>]
let ``Diagnostics functor: identity`` (x: int) =
    let m = Diagnostics.ofValueWith (entry "p" DiagnosticSeverity.Info "c" "m") x
    Diagnostics.map id m = m

[<Property>]
let ``Diagnostics functor: composition`` (x: int) =
    let m = Diagnostics.ofValueWith (entry "p" DiagnosticSeverity.Info "c" "m") x
    let f (y: int) = y + 7
    let g (y: int) = y * 11
    Diagnostics.map (g << f) m = Diagnostics.map g (Diagnostics.map f m)

[<Fact>]
let ``Diagnostics.write produces a unit value with one entry`` () =
    let e = entry "p" DiagnosticSeverity.Warning "c" "m"
    let m = Diagnostics.write e
    Assert.Equal((), m.Value)
    Assert.Equal<DiagnosticEntry list>([e], m.Entries)

// ---------------------------------------------------------------------------
// H-002: `diagnostics { ... }` CE builder equivalence with the bind chain.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-002 CE: diagnostics { return x } equals Diagnostics.ofValue x`` (x: int) =
    diagnostics { return x } = Diagnostics.ofValue x

[<Fact>]
let ``H-002 CE: do! write threads entries chronologically`` () =
    let e1 = entry "p1" DiagnosticSeverity.Info "first" "a"
    let e2 = entry "p2" DiagnosticSeverity.Warning "second" "b"
    let actual =
        diagnostics {
            do! Diagnostics.write e1
            do! Diagnostics.write e2
            return 42
        }
    Assert.Equal(42, actual.Value)
    Assert.Equal<DiagnosticEntry list>([e1; e2], actual.Entries)

// ---------------------------------------------------------------------------
// H-053 expansion — `LineageDiagnostics<'a> = Lineage<Diagnostics<'a>>`
// monad laws. The dual writer is itself a writer monad over the product
// monoid `(LineageEvent list × DiagnosticEntry list, ⊕, ([],[]))`; the
// laws thread through both projections (Trail and Entries).
//
// Asserting these laws explicitly is the writer-fidelity discipline's
// formal underwriting (DECISIONS 2026-05-30): if a pass driver uses the
// canonical primitives, both writers compose correctly by construction.
// ---------------------------------------------------------------------------

let private dualEvent (key: SsKey) =
    { PassName       = "Test"
      PassVersion    = 1
      SsKey          = key
      TransformKind  = Touched
      Classification = DataIntent }

let private byValueAndBothTrails (m1: Lineage<Diagnostics<'a>>) (m2: Lineage<Diagnostics<'a>>) : bool =
    LineageDiagnostics.payload m1 = LineageDiagnostics.payload m2
    && m1.Trail = m2.Trail
    && LineageDiagnostics.entries m1 = LineageDiagnostics.entries m2

[<Property>]
let ``LineageDiagnostics monad: left identity`` (x: int) =
    let f y =
        LineageDiagnostics.ofValue (y + 1)
        |> LineageDiagnostics.tellLineage (dualEvent customerKey)
        |> LineageDiagnostics.tellDiagnostic (entry "f" DiagnosticSeverity.Info "c" "m")
    byValueAndBothTrails
        (LineageDiagnostics.bind f (LineageDiagnostics.ofValue x))
        (f x)

[<Property>]
let ``LineageDiagnostics monad: right identity`` (x: int) =
    let m =
        LineageDiagnostics.ofValue x
        |> LineageDiagnostics.tellLineage (dualEvent customerKey)
        |> LineageDiagnostics.tellDiagnostic (entry "obs" DiagnosticSeverity.Info "c" "m")
    byValueAndBothTrails
        (LineageDiagnostics.bind LineageDiagnostics.ofValue m)
        m

[<Property>]
let ``LineageDiagnostics monad: associativity`` (x: int) =
    let f y =
        LineageDiagnostics.ofValue (y + 1)
        |> LineageDiagnostics.tellLineage (dualEvent customerKey)
        |> LineageDiagnostics.tellDiagnostic (entry "f" DiagnosticSeverity.Info "c1" "m1")
    let g y =
        LineageDiagnostics.ofValue (y * 3)
        |> LineageDiagnostics.tellLineage (dualEvent orderKey)
        |> LineageDiagnostics.tellDiagnostic (entry "g" DiagnosticSeverity.Warning "c2" "m2")
    let m = LineageDiagnostics.ofValue x
    byValueAndBothTrails
        (LineageDiagnostics.bind g (LineageDiagnostics.bind f m))
        (LineageDiagnostics.bind (fun y -> LineageDiagnostics.bind g (f y)) m)

// ---------------------------------------------------------------------------
// H-002: `lineageDiagnostics { ... }` CE — algebraic equivalence with the
// bind chain. The CE inherits the dual-writer's monad laws by construction;
// these tests verify the syntactic form preserves the algebra.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-002 CE: lineageDiagnostics { return x } equals LineageDiagnostics.ofValue x`` (x: int) =
    byValueAndBothTrails
        (lineageDiagnostics { return x })
        (LineageDiagnostics.ofValue x)

[<Fact>]
let ``H-002 CE: do! writeLineage + do! writeDiagnostic thread both trails`` () =
    let e = dualEvent customerKey
    let d = entry "P" DiagnosticSeverity.Info "c" "m"
    let actual =
        lineageDiagnostics {
            do! LineageDiagnostics.writeLineage e
            do! LineageDiagnostics.writeDiagnostic d
            return 7
        }
    Assert.Equal(7, LineageDiagnostics.payload actual)
    Assert.Equal<LineageEvent list>([e], actual.Trail)
    Assert.Equal<DiagnosticEntry list>([d], LineageDiagnostics.entries actual)

[<Fact>]
let ``H-002 CE: bind chains compose both trails chronologically`` () =
    let e1 = dualEvent customerKey
    let e2 = dualEvent orderKey
    let d1 = entry "P1" DiagnosticSeverity.Info "first" "a"
    let d2 = entry "P2" DiagnosticSeverity.Warning "second" "b"
    let mStart =
        LineageDiagnostics.ofValue 10
        |> LineageDiagnostics.tellLineage e1
        |> LineageDiagnostics.tellDiagnostic d1
    let actual =
        lineageDiagnostics {
            let! x = mStart
            do! LineageDiagnostics.writeLineage e2
            do! LineageDiagnostics.writeDiagnostic d2
            return x + 1
        }
    Assert.Equal(11, LineageDiagnostics.payload actual)
    Assert.Equal<LineageEvent list>([e1; e2], actual.Trail)
    Assert.Equal<DiagnosticEntry list>([d1; d2], LineageDiagnostics.entries actual)

// ---------------------------------------------------------------------------
// H-003: Kleisli laws for `Pass<'a, 'b>`. The pipeline IS a Kleisli
// category over the dual-writer monad; these tests assert the category
// laws (identity left/right, associativity) on the named primitives.
//
// The laws are theorems over `LineageDiagnostics.bind`'s monad laws —
// asserting them at the Kleisli surface verifies that `Pass.compose` is
// the correct Kleisli composition operator, and `Pass.id` is the correct
// identity arrow. `PassChainAdapter.compose`'s fold is `Pass.composeAll`
// modulo Bench scoping; if these laws hold, the registered chain composes
// correctly by construction.
// ---------------------------------------------------------------------------

let private wrapPass (event: LineageEvent) (entry: DiagnosticEntry) (delta: int) : Pass<int, int> =
    fun n ->
        LineageDiagnostics.ofValue (n + delta)
        |> LineageDiagnostics.tellLineage event
        |> LineageDiagnostics.tellDiagnostic entry

[<Property>]
let ``H-003 Kleisli: left identity (Pass.id >=> f = f)`` (x: int) =
    let f = wrapPass (dualEvent customerKey) (entry "f" DiagnosticSeverity.Info "c" "m") 1
    let lhs = (Pass.id >=> f) x
    let rhs = f x
    byValueAndBothTrails lhs rhs

[<Property>]
let ``H-003 Kleisli: right identity (f >=> Pass.id = f)`` (x: int) =
    let f = wrapPass (dualEvent customerKey) (entry "f" DiagnosticSeverity.Info "c" "m") 1
    let lhs = (f >=> Pass.id) x
    let rhs = f x
    byValueAndBothTrails lhs rhs

[<Property>]
let ``H-003 Kleisli: associativity ((f >=> g) >=> h = f >=> (g >=> h))`` (x: int) =
    let f = wrapPass (dualEvent customerKey) (entry "f" DiagnosticSeverity.Info "c1" "m1") 1
    let g = wrapPass (dualEvent orderKey)    (entry "g" DiagnosticSeverity.Info "c2" "m2") 2
    let h = wrapPass (dualEvent countryKey)  (entry "h" DiagnosticSeverity.Info "c3" "m3") 3
    let lhs = ((f >=> g) >=> h) x
    let rhs = (f >=> (g >=> h)) x
    byValueAndBothTrails lhs rhs

[<Fact>]
let ``H-003 Kleisli: composeAll [] = Pass.id`` () =
    // The empty pass chain is the identity arrow. This is the equivalent
    // of PassChainAdapter.compose [] state = LineageDiagnostics.ofValue state.
    let lhs = (Pass.composeAll<int> []) 42
    let rhs = Pass.id 42
    byValueAndBothTrails lhs rhs |> Assert.True

[<Fact>]
let ``H-003 Kleisli: composeAll [f; g; h] threads chronologically`` () =
    let e1 = dualEvent customerKey
    let e2 = dualEvent orderKey
    let e3 = dualEvent countryKey
    let d1 = entry "P1" DiagnosticSeverity.Info "c1" "m1"
    let d2 = entry "P2" DiagnosticSeverity.Warning "c2" "m2"
    let d3 = entry "P3" DiagnosticSeverity.Info "c3" "m3"
    let f = wrapPass e1 d1 1
    let g = wrapPass e2 d2 10
    let h = wrapPass e3 d3 100
    let composed = Pass.composeAll [f; g; h]
    let result = composed 0
    Assert.Equal(111, LineageDiagnostics.payload result)
    Assert.Equal<LineageEvent list>([e1; e2; e3], result.Trail)
    Assert.Equal<DiagnosticEntry list>([d1; d2; d3], LineageDiagnostics.entries result)

// ---------------------------------------------------------------------------
// H-004: Certificate<'a> as terminal-of-pipeline wrapper. The certificate
// is structurally isomorphic to Lineage<Diagnostics<'a>>; these tests
// assert the isomorphism + the functor/combine laws.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-004 Certificate: ofLineageDiagnostics ∘ toLineageDiagnostics = id`` (x: int) =
    let cert =
        Certificate.create x
            [ dualEvent customerKey ]
            [ entry "P" DiagnosticSeverity.Info "c" "m" ]
    let roundTripped = cert |> Certificate.toLineageDiagnostics |> Certificate.ofLineageDiagnostics
    cert = roundTripped

[<Property>]
let ``H-004 Certificate: toLineageDiagnostics ∘ ofLineageDiagnostics preserves both writers`` (x: int) =
    let e = dualEvent customerKey
    let d = entry "P" DiagnosticSeverity.Info "c" "m"
    let m =
        LineageDiagnostics.ofValue x
        |> LineageDiagnostics.tellLineage e
        |> LineageDiagnostics.tellDiagnostic d
    let roundTripped = m |> Certificate.ofLineageDiagnostics |> Certificate.toLineageDiagnostics
    byValueAndBothTrails m roundTripped

[<Property>]
let ``H-004 Certificate: functor identity (map id = id)`` (x: int) =
    let cert =
        Certificate.create x
            [ dualEvent customerKey ]
            [ entry "P" DiagnosticSeverity.Info "c" "m" ]
    Certificate.map id cert = cert

[<Property>]
let ``H-004 Certificate: functor composition (map (g << f) = map g << map f)`` (x: int) =
    let cert =
        Certificate.create x
            [ dualEvent customerKey ]
            [ entry "P" DiagnosticSeverity.Info "c" "m" ]
    let f (y: int) = y + 1
    let g (y: int) = y * 3
    Certificate.map (g << f) cert = (cert |> Certificate.map f |> Certificate.map g)

[<Fact>]
let ``H-004 Certificate: combine concatenates trails + diagnostics chronologically`` () =
    let e1 = dualEvent customerKey
    let e2 = dualEvent orderKey
    let d1 = entry "P1" DiagnosticSeverity.Info "c1" "m1"
    let d2 = entry "P2" DiagnosticSeverity.Warning "c2" "m2"
    let a = Certificate.create 10 [e1] [d1]
    let b = Certificate.create "ssdt" [e2] [d2]
    let combined = Certificate.combine a b
    Assert.Equal((10, "ssdt"), combined.Value)
    Assert.Equal<LineageEvent list>([e1; e2], combined.Trail)
    Assert.Equal<DiagnosticEntry list>([d1; d2], combined.Diagnostics)

[<Fact>]
let ``H-004 Certificate: combine left-identity (Certificate.ofValue ∘ combine = identity-ish)`` () =
    // For any Certificate<'a> a, combine (ofValue ()) a produces a
    // certificate carrying the tupled value (() , a.Value) with the
    // original trail + diagnostics. The unit at the value level is
    // not literal identity (it changes the type) but the trail/diag
    // identity holds.
    let e = dualEvent customerKey
    let d = entry "P" DiagnosticSeverity.Info "c" "m"
    let a = Certificate.create 42 [e] [d]
    let combined = Certificate.combine (Certificate.ofValue ()) a
    Assert.Equal(((), 42), combined.Value)
    Assert.Equal<LineageEvent list>([e], combined.Trail)
    Assert.Equal<DiagnosticEntry list>([d], combined.Diagnostics)

[<Fact>]
let ``H-004 Certificate: ofValue produces empty trail + diagnostics`` () =
    let cert = Certificate.ofValue 7
    Assert.Equal(7, cert.Value)
    Assert.Empty(cert.Trail)
    Assert.Empty(cert.Diagnostics)

[<Fact>]
let ``H-004 Certificate: empty ofValue is the unit of Pass.id at the boundary`` () =
    // Pass.id state |> ofLineageDiagnostics = Certificate.ofValue state.
    // The terminal form of the identity arrow IS the empty certificate.
    let state = 42
    let viaIdArrow = Pass.id state |> Certificate.ofLineageDiagnostics
    let viaOfValue = Certificate.ofValue state
    Assert.Equal(viaIdArrow, viaOfValue)

// ---------------------------------------------------------------------------
// H-006 (partial; Cluster B follow-on) — `Pass.product` (monoidal product /
// arrow notation `&&&`). Companion to `compose` (which is `>=>`). The static
// algebra ships; the dynamic SsKey-disjointness check for parallel pass
// scheduling defers to a dedicated H-006 slice.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-006 Pass.product: pairs outputs from a shared input`` () =
    let e1 = dualEvent customerKey
    let e2 = dualEvent orderKey
    let d1 = entry "P1" DiagnosticSeverity.Info "c1" "m1"
    let d2 = entry "P2" DiagnosticSeverity.Warning "c2" "m2"
    let f = wrapPass e1 d1 1
    let g = wrapPass e2 d2 10
    let combined = Pass.product f g
    let result = combined 0
    Assert.Equal((1, 10), LineageDiagnostics.payload result)
    Assert.Equal<LineageEvent list>([e1; e2], result.Trail)
    Assert.Equal<DiagnosticEntry list>([d1; d2], LineageDiagnostics.entries result)

[<Property>]
let ``H-006 Pass.product: left bias on trails (f's trail comes first)`` (x: int) =
    let e1 = dualEvent customerKey
    let e2 = dualEvent orderKey
    let d1 = entry "P1" DiagnosticSeverity.Info "c" "m"
    let d2 = entry "P2" DiagnosticSeverity.Info "c" "m"
    let f = wrapPass e1 d1 1
    let g = wrapPass e2 d2 2
    let result = (Pass.product f g) x
    result.Trail = [e1; e2]
    && LineageDiagnostics.entries result = [d1; d2]

[<Fact>]
let ``H-006 Pass.product operator (&&&) equals Pass.product`` () =
    let f = wrapPass (dualEvent customerKey) (entry "f" DiagnosticSeverity.Info "c" "m") 1
    let g = wrapPass (dualEvent orderKey) (entry "g" DiagnosticSeverity.Info "c" "m") 10
    let viaProduct = Pass.product f g 0
    let viaOperator = (f &&& g) 0
    byValueAndBothTrails viaProduct viaOperator |> Assert.True

[<Property>]
let ``H-006 Pass.first: lifts f to left of pair, leaves right unchanged`` (x: int) (c: int) =
    let e = dualEvent customerKey
    let d = entry "f" DiagnosticSeverity.Info "c" "m"
    let f = wrapPass e d 1
    let lifted = Pass.first<int, int, int> f
    let result = lifted (x, c)
    LineageDiagnostics.payload result = (x + 1, c)
    && result.Trail = [e]
    && LineageDiagnostics.entries result = [d]

[<Property>]
let ``H-006 Pass.second: lifts g to right of pair, leaves left unchanged`` (c: int) (x: int) =
    let e = dualEvent orderKey
    let d = entry "g" DiagnosticSeverity.Info "c" "m"
    let g = wrapPass e d 1
    let lifted = Pass.second<int, int, int> g
    let result = lifted (c, x)
    LineageDiagnostics.payload result = (c, x + 1)
    && result.Trail = [e]
    && LineageDiagnostics.entries result = [d]

[<Fact>]
let ``H-006 Pass.product + Pass.id: product with identity is value-injection`` () =
    let f = wrapPass (dualEvent customerKey) (entry "f" DiagnosticSeverity.Info "c" "m") 1
    let withId = Pass.product f Pass.id  // Pass.id : Pass<'a, 'a>
    let result = withId 5
    Assert.Equal((6, 5), LineageDiagnostics.payload result)
    // f's trail comes first; Pass.id contributes nothing
    Assert.Equal(1, result.Trail.Length)
    Assert.Equal(1, (LineageDiagnostics.entries result).Length)

// ---------------------------------------------------------------------------
// H-008: DiagnosticLattice — subsumption + minimal reduction.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-008 DiagnosticLattice: code-prefix with same SsKey subsumes`` () =
    let key = mkKey "K"
    let parent = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability.mandatory" "parent"
    let child = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability.mandatory.nulls" "child"
    Assert.True(DiagnosticLattice.subsumes parent child)
    Assert.False(DiagnosticLattice.subsumes child parent)

[<Fact>]
let ``H-008 DiagnosticLattice: catalog-level (SsKey=None) subsumes per-kind`` () =
    let key = mkKey "K"
    let catalog = entry "P" DiagnosticSeverity.Warning "tightening.nullability" "catalog"
    let perKind = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability.mandatory" "perKind"
    Assert.True(DiagnosticLattice.subsumes catalog perKind)
    Assert.False(DiagnosticLattice.subsumes perKind catalog)

[<Fact>]
let ``H-008 DiagnosticLattice: equal codes are NOT subsumption (strict prefix)`` () =
    let key = mkKey "K"
    let a = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability" "a"
    let b = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability" "b"
    Assert.False(DiagnosticLattice.subsumes a b)
    Assert.False(DiagnosticLattice.subsumes b a)

[<Fact>]
let ``H-008 DiagnosticLattice: different SsKey contexts are incomparable`` () =
    let key1 = mkKey "K1"
    let key2 = mkKey "K2"
    let parent = entryFor key1 "P" DiagnosticSeverity.Warning "tightening.nullability" "parent"
    let child = entryFor key2 "P" DiagnosticSeverity.Warning "tightening.nullability.mandatory" "child"
    Assert.False(DiagnosticLattice.subsumes parent child)
    Assert.False(DiagnosticLattice.subsumes child parent)

[<Fact>]
let ``H-008 DiagnosticLattice: prefix without dot separator is NOT subsumption`` () =
    // "tightening.null" is a substring prefix of "tightening.nullability" but
    // not a separator-bounded code prefix; the rule rejects it.
    let key = mkKey "K"
    let a = entryFor key "P" DiagnosticSeverity.Warning "tightening.null" "a"
    let b = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability" "b"
    Assert.False(DiagnosticLattice.subsumes a b)

[<Fact>]
let ``H-008 DiagnosticLattice: minimal drops subsumed entries (single-level)`` () =
    let key = mkKey "K"
    let parent = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability" "parent"
    let child = entryFor key "P" DiagnosticSeverity.Warning "tightening.nullability.mandatory" "child"
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany [parent; child]
    let reduced = DiagnosticLattice.minimal m
    Assert.Equal<DiagnosticEntry list>([parent], reduced.Entries)

[<Fact>]
let ``H-008 DiagnosticLattice: minimal preserves antichains (no subsumption)`` () =
    let key1 = mkKey "K1"
    let key2 = mkKey "K2"
    let a = entryFor key1 "P" DiagnosticSeverity.Warning "tightening.nullability" "a"
    let b = entryFor key2 "P" DiagnosticSeverity.Warning "tightening.uniqueIndex" "b"
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany [a; b]
    let reduced = DiagnosticLattice.minimal m
    Assert.Equal<DiagnosticEntry list>([a; b], reduced.Entries)

[<Property>]
let ``H-008 DiagnosticLattice: minimal is idempotent`` (codes: NonEmptyArray<NonEmptyString>) =
    let key = mkKey "K"
    let entries =
        codes.Get
        |> Array.toList
        |> List.map (fun (NonEmptyString c) ->
            entryFor key "P" DiagnosticSeverity.Info c c)
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany entries
    let once = DiagnosticLattice.minimal m
    let twice = DiagnosticLattice.minimal once
    once.Entries = twice.Entries

[<Property>]
let ``H-008 DiagnosticLattice: minimal output is contained in input`` (codes: NonEmptyArray<NonEmptyString>) =
    let key = mkKey "K"
    let entries =
        codes.Get
        |> Array.toList
        |> List.map (fun (NonEmptyString c) ->
            entryFor key "P" DiagnosticSeverity.Info c c)
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany entries
    let reduced = DiagnosticLattice.minimal m
    reduced.Entries |> List.forall (fun e -> List.contains e m.Entries)

[<Fact>]
let ``H-008 DiagnosticLattice: relations include Subsumes + Precedes correctly`` () =
    let key = mkKey "K"
    let parent = entryFor key "P" DiagnosticSeverity.Warning "a" "parent"
    let child = entryFor key "P" DiagnosticSeverity.Warning "a.b" "child"
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany [parent; child]
    let rels = DiagnosticLattice.relations m
    Assert.Contains(Subsumes (parent, child), rels)
    Assert.Contains(Precedes (parent, child), rels)

// ---------------------------------------------------------------------------
// H-010: Prism — bidirectional accessor with round-trip law.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-010 Prism.identity: round-trip law holds for every input`` () =
    let p = Prism.identity<int>
    Assert.True(Prism.roundtrips p (=) 42)
    Assert.True(Prism.roundtrips p (=) -7)

[<Fact>]
let ``H-010 Prism: get applies the forward direction`` () =
    let p : Prism<int, string> = {
        Get = string
        ReverseGet = fun s ->
            match System.Int32.TryParse s with
            | true, n -> Some n
            | _ -> None
    }
    Assert.Equal("42", Prism.get p 42)

[<Fact>]
let ``H-010 Prism: reverseGet may fail (partial backward)`` () =
    let p : Prism<int, string> = {
        Get = string
        ReverseGet = fun s ->
            match System.Int32.TryParse s with
            | true, n -> Some n
            | _ -> None
    }
    Assert.Equal(Some 42, Prism.reverseGet p "42")
    Assert.Equal<int option>(None, Prism.reverseGet p "not-a-number")

[<Property>]
let ``H-010 Prism (int↔string): round-trip law holds on all integers`` (n: int) =
    let p : Prism<int, string> = {
        Get = string
        ReverseGet = fun s ->
            match System.Int32.TryParse s with
            | true, x -> Some x
            | _ -> None
    }
    Prism.roundtrips p (=) n

[<Fact>]
let ``H-010 Prism.partition: splits lawful from violating`` () =
    // Construct a prism that ALWAYS fails on negative numbers.
    let p : Prism<int, string> = {
        Get = string
        ReverseGet = fun s ->
            match System.Int32.TryParse s with
            | true, n when n >= 0 -> Some n
            | _ -> None
    }
    let lawful, violating = Prism.partition p (=) [-2; -1; 0; 1; 2]
    Assert.Equal<int list>([0; 1; 2], lawful)
    Assert.Equal<int list>([-2; -1], violating)

[<Property>]
let ``H-010 Prism.compose: composes round-trip law (identity ∘ p = p modulo wrapping)`` (n: int) =
    let inner = Prism.identity<int>
    let composed = Prism.compose inner inner
    Prism.roundtrips composed (=) n

// ---------------------------------------------------------------------------
// H-062: PassContext — reader comonad. Comonad laws.
//
//   left identity   : extend extract ctx = ctx
//   right identity  : extract (extend f ctx) = f ctx
//   associativity   : extend f (extend g ctx) = extend (fun c -> f (extend g c)) ctx
// ---------------------------------------------------------------------------

[<Property>]
let ``H-062 PassContext comonad: left identity (extend extract = id)`` (env: int) (v: int) =
    let ctx = PassContext.ofValue env v
    PassContext.extend PassContext.extract ctx = ctx

[<Property>]
let ``H-062 PassContext comonad: right identity (extract ∘ extend f = f)`` (env: int) (v: int) =
    let ctx = PassContext.ofValue env v
    let f (c: PassContext<int, int>) = c.Value + c.Environment
    PassContext.extract (PassContext.extend f ctx) = f ctx

[<Property>]
let ``H-062 PassContext comonad: associativity`` (env: int) (v: int) =
    let ctx = PassContext.ofValue env v
    let f (c: PassContext<int, int>) = c.Value + 1
    let g (c: PassContext<int, int>) = c.Value * 2
    let lhs = PassContext.extend f (PassContext.extend g ctx)
    let rhs = PassContext.extend (fun c -> f (PassContext.extend g c)) ctx
    lhs = rhs

[<Property>]
let ``H-062 PassContext: ask reads environment without consuming context`` (env: int) (v: int) =
    let ctx = PassContext.ofValue env v
    PassContext.ask ctx = env
    && PassContext.extract ctx = v

[<Property>]
let ``H-062 PassContext functor: map identity = id`` (env: int) (v: int) =
    let ctx = PassContext.ofValue env v
    PassContext.map id ctx = ctx

[<Property>]
let ``H-062 PassContext functor: composition (map (g ∘ f) = map g ∘ map f)`` (env: int) (v: int) =
    let ctx = PassContext.ofValue env v
    let f (x: int) = x + 7
    let g (x: int) = x * 11
    PassContext.map (g << f) ctx = (ctx |> PassContext.map f |> PassContext.map g)

[<Fact>]
let ``H-062 PassContext.applyEnv: applies environment-aware function`` () =
    let ctx = PassContext.ofValue 10 5
    let result = PassContext.applyEnv (fun env v -> env + v) ctx
    Assert.Equal(15, result.Value)
    Assert.Equal(10, result.Environment)

// ---------------------------------------------------------------------------
// H-015: Lens — total bidirectional accessor. Three lens laws + composition.
// ---------------------------------------------------------------------------

let private testRecord (a: int) (b: int) =
    { Source = "p"; Severity = DiagnosticSeverity.Info; Code = "code"; Message = "msg"; SsKey = None; Metadata = Map.empty; SuggestedConfig = None }, (a, b)

[<Property>]
let ``H-015 Lens law: get-set (set (get s) s = s)`` (env: int) (v: int) =
    let lens : Lens<PassContext<int, int>, int> = {
        Get = fun ctx -> ctx.Value
        Set = fun a ctx -> { ctx with Value = a }
    }
    let s = PassContext.ofValue env v
    Lens.set lens (Lens.get lens s) s = s

[<Property>]
let ``H-015 Lens law: set-get (get (set a s) = a)`` (env: int) (v: int) (a: int) =
    let lens : Lens<PassContext<int, int>, int> = {
        Get = fun ctx -> ctx.Value
        Set = fun x ctx -> { ctx with Value = x }
    }
    let s = PassContext.ofValue env v
    Lens.get lens (Lens.set lens a s) = a

[<Property>]
let ``H-015 Lens law: set-set (set a' (set a s) = set a' s)`` (env: int) (v: int) (a: int) (a': int) =
    let lens : Lens<PassContext<int, int>, int> = {
        Get = fun ctx -> ctx.Value
        Set = fun x ctx -> { ctx with Value = x }
    }
    let s = PassContext.ofValue env v
    Lens.set lens a' (Lens.set lens a s) = Lens.set lens a' s

[<Property>]
let ``H-015 Lens.over: modify equals get-modify-set`` (env: int) (v: int) =
    let lens : Lens<PassContext<int, int>, int> = {
        Get = fun ctx -> ctx.Value
        Set = fun x ctx -> { ctx with Value = x }
    }
    let s = PassContext.ofValue env v
    let f x = x + 7
    Lens.over lens f s = Lens.set lens (f (Lens.get lens s)) s

[<Property>]
let ``H-015 Lens.identity: get s = s and set a _ = a`` (v: int) =
    Lens.get Lens.identity<int> v = v
    && Lens.set Lens.identity<int> 42 v = 42

[<Fact>]
let ``H-015 Lens.compose: outer ∘ inner reaches the inner substructure`` () =
    // Compose: PassContext<int, (int, int)>.Value.fst
    let outer : Lens<PassContext<int, (int * int)>, (int * int)> = {
        Get = fun ctx -> ctx.Value
        Set = fun pair ctx -> { ctx with Value = pair }
    }
    let inner : Lens<int * int, int> = {
        Get = fst
        Set = fun a (_, b) -> (a, b)
    }
    let composed = Lens.compose outer inner
    let s = PassContext.ofValue 0 (10, 20)
    Assert.Equal(10, Lens.get composed s)
    let updated = Lens.set composed 99 s
    Assert.Equal((99, 20), updated.Value)
    Assert.Equal(0, updated.Environment)

[<Property>]
let ``H-015 Lens.compose: laws preserve through composition`` (a: int) (b: int) (a': int) =
    let outer : Lens<int * int, int> = {
        Get = fst
        Set = fun a (_, b) -> (a, b)
    }
    let composed = Lens.compose Lens.identity<int * int> outer
    let s = (a, b)
    // set-get
    Lens.get composed (Lens.set composed a' s) = a'
    // get-set
    && Lens.set composed (Lens.get composed s) s = s

// --- Catalog canonical lenses ---

[<Fact>]
let ``H-015 CatalogLenses.modules: get + set roundtrip`` () =
    let catalog = { Modules = []; Sequences = [] }
    Assert.Equal<Module list>([], Lens.get CatalogLenses.modules catalog)
    let result = Lens.set CatalogLenses.modules [] catalog
    Assert.Equal(catalog, result)

[<Fact>]
let ``H-015 CatalogLenses.sequences: get + set roundtrip`` () =
    let catalog = { Modules = []; Sequences = [] }
    Assert.Equal<Sequence list>([], Lens.get CatalogLenses.sequences catalog)

[<Fact>]
let ``H-015 CatalogLenses.columnOf: get + set roundtrip`` () =
    let attr =
        Attribute.create
            (SsKey.synthesized "TEST" "attr" |> Result.value)
            (Name.create "A" |> Result.value)
            PrimitiveType.Integer
    let originalColumn = attr.Column
    Assert.Equal(originalColumn, Lens.get CatalogLenses.columnOf attr)
    let replacement = ColumnRealization.create "renamed" true |> Result.value
    let updated = Lens.set CatalogLenses.columnOf replacement attr
    Assert.Equal(replacement, updated.Column)
    Assert.Equal(attr.SsKey, updated.SsKey)
    Assert.Equal(attr.Name, updated.Name)

// ---------------------------------------------------------------------------
// Validation.duplicateKeyErrors — chapter-Cluster-B algebraic compression
// primitive. Replaces the recurring groupBy + filter > 1 + map error
// boilerplate in Catalog.create (3 sites collapsed).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Validation.duplicateKeyErrors: empty input produces no errors`` () =
    let errors = Validation.duplicateKeyErrors "test.dup" (fun k -> sprintf "%A" k) id []
    Assert.Empty(errors)

[<Fact>]
let ``Validation.duplicateKeyErrors: unique keys produce no errors`` () =
    let errors = Validation.duplicateKeyErrors "test.dup" (fun k -> sprintf "%A" k) id [1; 2; 3]
    Assert.Empty(errors)

[<Fact>]
let ``Validation.duplicateKeyErrors: one duplicate key produces one error`` () =
    let errors = Validation.duplicateKeyErrors "test.dup" (sprintf "duplicate: %d") id [1; 2; 2; 3]
    Assert.Single(errors) |> ignore
    Assert.Equal("test.dup", errors.[0].Code)
    Assert.Equal("duplicate: 2", errors.[0].Message)

[<Fact>]
let ``Validation.duplicateKeyErrors: triplicate produces one error per key`` () =
    let errors = Validation.duplicateKeyErrors "test.dup" (sprintf "dup: %d") id [1; 2; 2; 2; 3; 3]
    Assert.Equal(2, errors.Length)
    let messages = errors |> List.map (fun e -> e.Message) |> List.sort
    Assert.Equal<string list>(["dup: 2"; "dup: 3"], messages)

[<Property>]
let ``Validation.duplicateKeyErrors: preserves first-occurrence order across duplicates`` (xs: NonEmptyArray<int>) =
    // For deterministic Catalog-create error ordering: keys appear in
    // the order of their FIRST occurrence in the input.
    let items = xs.Get |> Array.toList
    let errors = Validation.duplicateKeyErrors "c" (sprintf "%d") id items
    let dupKeys = items |> List.groupBy id |> List.filter (fun (_, g) -> g.Length > 1) |> List.map fst
    let firstOccurrenceOrder =
        dupKeys
        |> List.sortBy (fun k -> List.findIndex ((=) k) items)
    let errorOrder = errors |> List.map (fun e -> e.Message |> int)
    firstOccurrenceOrder = errorOrder

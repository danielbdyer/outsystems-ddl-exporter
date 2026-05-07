module Projection.Tests.DiagnosticsTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Helpers — small named constructors for test entries.
// ---------------------------------------------------------------------------

let private mkKey s = SsKey.original s |> Result.value

let private entry source severity code message =
    { Source   = source
      Severity = severity
      Code     = code
      Message  = message
      SsKey    = None
      Metadata = Map.empty }

let private entryFor key source severity code message =
    { Source   = source
      Severity = severity
      Code     = code
      Message  = message
      SsKey    = Some key
      Metadata = Map.empty }

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
    let e = entry "TestPass" Info "test.code" "hello"
    let m = Diagnostics.ofValueWith e 42
    Assert.Equal(42, m.Value)
    Assert.Single(m.Entries) |> ignore
    Assert.Equal(e, m.Entries.[0])

// ---------------------------------------------------------------------------
// Diagnostics.tell / tellMany — earliest-first append.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.tell appends a single entry chronologically`` () =
    let e1 = entry "TestPass" Info "first" "hello"
    let e2 = entry "TestPass" Warning "second" "world"
    let m =
        Diagnostics.ofValue 1
        |> Diagnostics.tell e1
        |> Diagnostics.tell e2
    Assert.Equal<DiagnosticEntry list>([e1; e2], m.Entries)

[<Fact>]
let ``Diagnostics.tellMany preserves entry order`` () =
    let e1 = entry "P" Info "a" "1"
    let e2 = entry "P" Info "b" "2"
    let e3 = entry "P" Info "c" "3"
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany [e1; e2; e3]
    Assert.Equal<DiagnosticEntry list>([e1; e2; e3], m.Entries)

// ---------------------------------------------------------------------------
// Diagnostics: functor laws.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.map preserves entries`` () =
    let e = entry "P" Warning "c" "m"
    let m =
        Diagnostics.ofValueWith e 1
        |> Diagnostics.map (fun n -> n * 2)
    Assert.Equal(2, m.Value)
    Assert.Equal<DiagnosticEntry list>([e], m.Entries)

[<Fact>]
let ``Diagnostics.map identity = id`` () =
    let e = entry "P" Info "x" "y"
    let m = Diagnostics.ofValueWith e 7
    let after = Diagnostics.map id m
    Assert.Equal(m, after)

// ---------------------------------------------------------------------------
// Diagnostics: monad laws — bind concatenates entries.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Diagnostics.bind concatenates entries chronologically`` () =
    let e1 = entry "P1" Info "first" "a"
    let e2 = entry "P2" Warning "second" "b"
    let m =
        Diagnostics.ofValueWith e1 10
        |> Diagnostics.bind (fun n -> Diagnostics.ofValueWith e2 (n + 1))
    Assert.Equal(11, m.Value)
    Assert.Equal<DiagnosticEntry list>([e1; e2], m.Entries)

[<Fact>]
let ``Diagnostics.bind with empty f produces same trail`` () =
    let e = entry "P" Info "c" "m"
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
    let e = entry "P" Info "c" "m"
    Assert.False(Diagnostics.isClean (Diagnostics.ofValueWith e 0))

[<Fact>]
let ``Diagnostics.entriesAt filters by severity`` () =
    let e1 = entry "P" Info "i1" "info one"
    let e2 = entry "P" Warning "w1" "warn one"
    let e3 = entry "P" Warning "w2" "warn two"
    let e4 = entry "P" Error "x1" "error one"
    let m = Diagnostics.ofValue () |> Diagnostics.tellMany [e1; e2; e3; e4]
    Assert.Equal<DiagnosticEntry list>([e1], Diagnostics.entriesAt Info m)
    Assert.Equal<DiagnosticEntry list>([e2; e3], Diagnostics.entriesAt Warning m)
    Assert.Equal<DiagnosticEntry list>([e4], Diagnostics.entriesAt Error m)

// ---------------------------------------------------------------------------
// LineageDiagnostics: dual writer composition.
// ---------------------------------------------------------------------------

let private lineageEvent (key: SsKey) (kind: TransformKind) =
    { PassName      = "TestPass"
      PassVersion   = 1
      SsKey         = key
      TransformKind = kind }

[<Fact>]
let ``LineageDiagnostics.ofValue is empty in both trails`` () =
    let m = LineageDiagnostics.ofValue 42
    Assert.Equal(42, m.Value.Value)
    Assert.Empty(m.Trail)
    Assert.Empty(m.Value.Entries)

[<Fact>]
let ``LineageDiagnostics.ofLineage preserves the lineage trail`` () =
    let key = mkKey "k"
    let inner = Lineage.ofValueWith (lineageEvent key Touched) 1
    let dual = LineageDiagnostics.ofLineage inner
    Assert.Equal(1, dual.Value.Value)
    Assert.Single(dual.Trail) |> ignore
    Assert.Empty(dual.Value.Entries)

[<Fact>]
let ``LineageDiagnostics.ofDiagnostics preserves the diagnostics entries`` () =
    let e = entry "P" Info "c" "m"
    let inner = Diagnostics.ofValueWith e 1
    let dual = LineageDiagnostics.ofDiagnostics inner
    Assert.Equal(1, dual.Value.Value)
    Assert.Empty(dual.Trail)
    Assert.Equal<DiagnosticEntry list>([e], dual.Value.Entries)

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
    let e = entry "P" Warning "c" "m"
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
    let e1 = entry "P1" Info "first" "a"
    let e2 = entry "P2" Warning "second" "b"

    let m1 =
        LineageDiagnostics.ofValue 10
        |> LineageDiagnostics.tellLineage evt1
        |> LineageDiagnostics.tellDiagnostic e1

    let f (n: int) : Lineage<Diagnostics<int>> =
        LineageDiagnostics.ofValue (n + 1)
        |> LineageDiagnostics.tellLineage evt2
        |> LineageDiagnostics.tellDiagnostic e2

    let m2 = LineageDiagnostics.bind f m1

    Assert.Equal(11, m2.Value.Value)
    Assert.Equal<LineageEvent list>([evt1; evt2], m2.Trail)
    Assert.Equal<DiagnosticEntry list>([e1; e2], m2.Value.Entries)

[<Fact>]
let ``LineageDiagnostics.map preserves both trails`` () =
    let key = mkKey "k"
    let evt = lineageEvent key Touched
    let e = entry "P" Info "c" "m"

    let m =
        LineageDiagnostics.ofValue 5
        |> LineageDiagnostics.tellLineage evt
        |> LineageDiagnostics.tellDiagnostic e
        |> LineageDiagnostics.map (fun n -> n * 2)

    Assert.Equal(10, m.Value.Value)
    Assert.Equal<LineageEvent list>([evt], m.Trail)
    Assert.Equal<DiagnosticEntry list>([e], m.Value.Entries)

// ---------------------------------------------------------------------------
// Structural commitment — DiagnosticEntry's SsKey field is option, so
// adapter-produced entries that have no IR node to point at construct
// validly without surfacing a fake SsKey.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DiagnosticEntry.SsKey can be None for adapter-level diagnostics`` () =
    let e = entry "adapter:OSSYS" Error "adapter.ossys.unparseable" "JSON document failed to parse"
    Assert.Equal(None, e.SsKey)

[<Fact>]
let ``DiagnosticEntry.SsKey carries the IR node for pass-level diagnostics`` () =
    let key = mkKey "AppCore.User.Email"
    let e = entryFor key "UniqueIndexPass" Warning "tightening.uniqueIndex.opportunity" "Unique index not enforced"
    Assert.Equal(Some key, e.SsKey)

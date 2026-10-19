module Projection.Tests.ClassificationCarryThroughTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `TableRename.run` is private; the
// canonical surface is `.registered.Run`. The original `run` returned
// `Result<Lineage<Catalog>>` (validation can fail). This per-file shim
// reconstructs that shape from the registry's
// `Lineage<Diagnostics<Catalog>>` by promoting Error-severity entries
// back to `Result.Error`.
let private trRun (specs: TableRename.RenameSpec list) (c: Catalog) : Result<Lineage<Catalog>> =
    let lineage = (TableRename.registered specs).Run c
    let diag = lineage.Value
    let errors =
        diag.Entries
        |> List.filter (fun e -> e.Severity = DiagnosticSeverity.Error)
    if List.isEmpty errors then
        Result.success (lineage |> Lineage.map (fun d -> d.Value))
    else
        errors
        |> List.map (fun e ->
            { Code = e.Code
              Message = e.Message
              Metadata = e.Metadata |> Map.map (fun _ v -> Some v) })
        |> Error

// Chapter A.4.7' slice η — `TopologicalOrderPass.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<TopologicalOrder>>`. This per-file shim restores the
// `Lineage<TopologicalOrder>` shape so existing assertions keep reading.
let private topoRun (c: Catalog) : Lineage<TopologicalOrder> =
    TopologicalOrderPass.registered.Run c |> Lineage.map (fun d -> d.Value)

// Chapter A.4.7' slice η — `SymmetricClosure.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private scRun (c: Catalog) : Lineage<Catalog> =
    SymmetricClosure.registered.Run c |> Lineage.map (fun d -> d.Value)

// Chapter A.4.7' slice η — `NormalizeStaticPopulations.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private nspRun (c: Catalog) : Lineage<Catalog> =
    NormalizeStaticPopulations.registered.Run c |> Lineage.map (fun d -> d.Value)

// Chapter A.4.7' slice η — per-pass `let run` is private; shims wrap
// `.registered.Run` and unwrap the Diagnostics layer so existing
// assertions on `lineage.Trail` and `lineage.Value` keep reading.
let private nmRun (morphism: NamingMorphism.Morphism) (catalog: Catalog) : Lineage<Catalog> =
    (NamingMorphism.registered morphism).Run catalog
    |> Lineage.map (fun d -> d.Value)

let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// Chapter A.4.7 slice α — Classification carry-through tests.
//
// Witnesses for the A.4.7-prelude small slice (per `CHAPTER_A_4_7_OPEN.md`
// axis 6; `V2_PRODUCTION_CUTOVER.md` §6.4.7 task 3; `DECISIONS 2026-05-15
// (late) — Pillar 9: harvest-dichotomy classification`):
//   - `OverlayAxis` exists with five variants (`Selection | Emission |
//     Insertion | Tightening | Ordering`); the fifth variant `Ordering`
//     is the chapter A.4.7 open's Q9-trigger-fires worked example.
//   - `Classification` exists with two variants (`DataIntent |
//     OperatorIntent of OverlayAxis`).
//   - `LineageEvent.Classification : Classification` is required;
//     writer-fidelity primitives (`Lineage.ofValueWith`, `Lineage.bind`,
//     `Lineage.tellMany`, etc.) propagate it unchanged.
//   - Each pass's events self-classify per the harvest-discipline
//     analysis prose codified in the pass's module docstring.
//
// Slice α establishes the type-system shape; structural enforcement
// (skeleton-purity property test) lands at slice θ when
// `Compose.runWithSkeleton` filters traversal to `DataIntent`-only Sites.
// ---------------------------------------------------------------------------

let private assertAllClassifiedAs
        (expected: Classification)
        (trail: LineageEvent list) : unit =
    Assert.NotEmpty trail
    for event in trail do
        Assert.Equal(expected, event.Classification)

// ---------------------------------------------------------------------------
// Type-system witnesses — the new types compile + variants are usable.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice α: OverlayAxis carries five variants (Selection / Emission / Insertion / Tightening / Ordering)`` () =
    // The fifth variant Ordering is chapter A.4.7 open's Q9-trigger-fires
    // worked example. Constructing each variant by name is the compile-time
    // witness; this test fails to compile if a variant is removed or
    // renamed.
    let axes : OverlayAxis list = [ Selection; Emission; Insertion; Tightening; Ordering ]
    Assert.Equal(5, axes.Length)

[<Fact>]
let ``A.4.7 slice α: Classification carries DataIntent and OperatorIntent of OverlayAxis`` () =
    let dataIntent : Classification = DataIntent
    let operatorIntent : Classification = OperatorIntent Tightening
    // Pattern-match exhaustiveness witness; F# warns (and TreatWarningsAsErrors
    // errors) if a Classification variant is added without a match arm here.
    let isDataIntent c =
        match c with
        | DataIntent -> true
        | OperatorIntent _ -> false
    Assert.True(isDataIntent dataIntent)
    Assert.False(isDataIntent operatorIntent)

[<Fact>]
let ``A.4.7 slice α: OverlayAxis.Ordering destructures from OperatorIntent`` () =
    // Q9-trigger-fires worked example — the fifth OverlayAxis variant
    // is consumed by `TopologicalOrderPass.SelfLoopHandling` (slice ε)
    // and emerges through the Classification = OperatorIntent Ordering
    // path. This test witnesses the destructuring shape.
    let classification = OperatorIntent Ordering
    match classification with
    | OperatorIntent Ordering -> ()
    | other ->
        Assert.Fail(sprintf "Expected OperatorIntent Ordering, got %A" other)

// ---------------------------------------------------------------------------
// Writer-fidelity primitive tests — Classification rides through bind /
// map / tell / tellMany / ofValueWith / ofValueAndEvents unchanged.
// ---------------------------------------------------------------------------

let private mkEvent (classification: Classification) (passName: string) : LineageEvent =
    { PassName       = passName
      PassVersion    = 1
      SsKey          = customerKey
      TransformKind  = Touched
      Classification = classification }

[<Fact>]
let ``A.4.7 slice α: Lineage.ofValueWith preserves Classification on the carried event`` () =
    let event = mkEvent (OperatorIntent Selection) "test"
    let lineage = Lineage.ofValueWith event 42
    Assert.Equal(1, lineage.Trail.Length)
    Assert.Equal(OperatorIntent Selection, lineage.Trail.[0].Classification)

[<Fact>]
let ``A.4.7 slice α: Lineage.ofValueAndEvents preserves mixed Classifications`` () =
    let events = [
        mkEvent DataIntent "p1"
        mkEvent (OperatorIntent Tightening) "p2"
        mkEvent (OperatorIntent Ordering) "p3"
    ]
    let lineage = Lineage.ofValueAndEvents events 0
    Assert.Equal(3, lineage.Trail.Length)
    Assert.Equal(DataIntent, lineage.Trail.[0].Classification)
    Assert.Equal(OperatorIntent Tightening, lineage.Trail.[1].Classification)
    Assert.Equal(OperatorIntent Ordering, lineage.Trail.[2].Classification)

[<Fact>]
let ``A.4.7 slice α: Lineage.bind preserves per-event Classifications across the concatenation`` () =
    let m1 =
        let event = mkEvent DataIntent "p1"
        Lineage.ofValueWith event 1
    let f (x: int) =
        let event = mkEvent (OperatorIntent Emission) "p2"
        Lineage.ofValueWith event (x + 1)
    let bound = m1 |> Lineage.bind f
    // A24 — earliest-first concatenation. The DataIntent event from
    // m1 is at index 0; the OperatorIntent Emission event from f is
    // at index 1. Both Classifications survive the bind unchanged.
    Assert.Equal(2, bound.Trail.Length)
    Assert.Equal(DataIntent, bound.Trail.[0].Classification)
    Assert.Equal(OperatorIntent Emission, bound.Trail.[1].Classification)

[<Fact>]
let ``A.4.7 slice α: Lineage.tellMany preserves Classification on appended events`` () =
    let m0 = Lineage.ofValue 0
    let added = [
        mkEvent DataIntent "p1"
        mkEvent (OperatorIntent Insertion) "p2"
    ]
    let m1 = m0 |> Lineage.tellMany added
    Assert.Equal(2, m1.Trail.Length)
    Assert.Equal(OperatorIntent Insertion, m1.Trail.[1].Classification)

// ---------------------------------------------------------------------------
// Per-pass classification — slice α witnesses for the six passes whose
// invocation is trivial (no operator-supplied policy / intervention list
// required). The registered-intervention passes (Nullability / UniqueIndex
// / ForeignKey / CategoricalUniqueness) and User FK reflow are exercised
// at slice γ when their `.registered` exports are introduced; the helper-
// constant edits are inspected at code-review until the registry property
// tests (slice θ) close the structural loop.
// ---------------------------------------------------------------------------


[<Fact>]
let ``A.4.7 slice α: CanonicalizeIdentity events carry DataIntent`` () =
    let lineage = ciRun sampleCatalog
    assertAllClassifiedAs DataIntent lineage.Trail

[<Fact>]
let ``A.4.7 slice α: NamingMorphism events carry DataIntent`` () =
    // Append a deterministic suffix so the morphism always produces
    // renames (the fixture's "Customer" / "Order" / "Country" names
    // would be untouched by an upper-case morphism).
    let appendUnderscoreV (n: Name) : Name =
        Name.create (System.String.Concat (Name.value n, "_v")) |> Result.value
    let lineage = nmRun appendUnderscoreV sampleCatalog
    assertAllClassifiedAs DataIntent lineage.Trail

[<Fact>]
let ``A.4.7 slice α: NormalizeStaticPopulations events carry DataIntent`` () =
    let lineage = nspRun sampleCatalog
    // The fixture has one Static kind (Country); the pass emits one
    // Touched event for it. Non-empty trail is the witness.
    assertAllClassifiedAs DataIntent lineage.Trail

[<Fact>]
let ``A.4.7 slice α: SymmetricClosure events carry DataIntent`` () =
    let lineage = scRun sampleCatalog
    // The fixture has one directional reference (Order → Customer);
    // the pass creates one inverse and emits one Created event.
    assertAllClassifiedAs DataIntent lineage.Trail

[<Fact>]
let ``A.4.7 slice α: TopologicalOrderPass events carry DataIntent`` () =
    let lineage = topoRun sampleCatalog
    // One Touched event per kind in the graph. The SelfLoopHandling
    // site (an OperatorIntent Ordering site per chapter A.4.7 open's
    // Q9-trigger-fires worked example) affects `buildGraph` but emits
    // no per-event lineage at this slice; Sites distinction lands at
    // slice ε with the registry entry.
    assertAllClassifiedAs DataIntent lineage.Trail

[<Fact>]
let ``A.4.7 slice α: VisibilityMask events carry OperatorIntent Selection`` () =
    // Mask filtering by Origin = Native removes every kind in the
    // fixture (all three are Native-origin). Each removal emits a
    // Removed event classified OperatorIntent Selection — the filter
    // is operator intent on the Selection axis.
    let mask : VisibilityMask.Mask = { Hide = [ VisibilityMask.hideOrigin Origin.Native ] }
    let lineage = (VisibilityMask.registered mask).Run sampleCatalog
    assertAllClassifiedAs (OperatorIntent Selection) lineage.Trail

[<Fact>]
let ``A.4.7 slice α: TableRename events carry OperatorIntent Emission`` () =
    // Rename the Customer kind in the Sales module to a different
    // physical coordinate. The pass emits one PhysicallyRenamed event
    // per rewritten kind; each is classified OperatorIntent Emission —
    // operator-supplied rename specs change the kind's physical form.
    let specs : TableRename.RenameSpec list = [
        { Key    = TableRename.Logical (mkName "Sales", mkName "Customer")
          Target = mkTableId "renamed_schema" "renamed_table" }
    ]
    match trRun specs sampleCatalog with
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "trRun failed: %s" codes)
    | Ok lineage ->
        assertAllClassifiedAs (OperatorIntent Emission) lineage.Trail

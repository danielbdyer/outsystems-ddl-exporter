module Projection.Tests.LineageTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.LineageOperators
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Helpers — lineage events keyed to the synthetic fixture's identities.
// ---------------------------------------------------------------------------

// Chapter A.4.7 slice α: writer-monad-laws tests are algebra-only;
// the `Classification` field's value doesn't affect the laws.
// `DataIntent` is the convention for test-fixture events whose
// payload's typed shape is also incidental.
let private touched (passName: string) (passVersion: int) (key: SsKey) : LineageEvent =
    { PassName       = passName
      PassVersion    = passVersion
      SsKey          = key
      TransformKind  = Touched
      Classification = DataIntent }

let private annotated (passName: string) (passVersion: int) (key: SsKey) (detail: string) : LineageEvent =
    // Chapter-3.6 slice-β: writer-monad-laws tests use the
    // `AnnotationDetail.Label` free-form variant — the algebraic
    // laws (left identity, right identity, associativity) hold for
    // any payload type, so the detail's typed shape is incidental
    // to what the tests prove.
    { PassName       = passName
      PassVersion    = passVersion
      SsKey          = key
      TransformKind  = Annotated (Label detail)
      Classification = DataIntent }

// ---------------------------------------------------------------------------
// Monad laws. Lineage is the writer monad over the (List, ++, []) monoid;
// the laws hold for any underlying value type.
//
//   left identity   : bind f (ofValue x)  =  f x
//   right identity  : bind ofValue m      =  m
//   associativity   : bind g (bind f m)   =  bind (fun x -> bind g (f x)) m
// ---------------------------------------------------------------------------

// `Lineage.byValueAndTrail` is the full-structural equivalence used by
// the monad-laws tests since chapter-3.7 slice α made `=` project to
// `Value` only (A26 cash-out). The laws hold over the writer monad's
// (List, ++, []) monoid AND over the underlying value type, so both
// projections are asserted explicitly.

[<Property>]
let ``Lineage monad: left identity`` (x: int) =
    let f y = Lineage.ofValueWith (touched "f" 1 customerKey) (y + 1)
    Lineage.byValueAndTrail (Lineage.bind f (Lineage.ofValue x)) (f x)

[<Property>]
let ``Lineage monad: right identity`` (x: int) =
    let m = Lineage.ofValueWith (touched "obs" 3 customerKey) x
    Lineage.byValueAndTrail (Lineage.bind Lineage.ofValue m) m

[<Property>]
let ``Lineage monad: associativity`` (x: int) =
    let f y = Lineage.ofValueWith (touched "f" 1 customerKey) (y + 1)
    let g y = Lineage.ofValueWith (touched "g" 2 orderKey)    (y * 3)
    let m = Lineage.ofValue x
    Lineage.byValueAndTrail
        (Lineage.bind g (Lineage.bind f m))
        (Lineage.bind (fun y -> Lineage.bind g (f y)) m)

// ---------------------------------------------------------------------------
// A23: lineage events carry transformation_version.
// Two events differing only in PassVersion are not equal — provenance
// hashes stay stable across pipeline evolution.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A23: lineage event records PassVersion as a first-class field`` () =
    let v1 = touched "canonicalizeIdentity" 1 customerKey
    let v2 = touched "canonicalizeIdentity" 2 customerKey
    Assert.NotEqual<LineageEvent>(v1, v2)
    Assert.Equal(1, v1.PassVersion)
    Assert.Equal(2, v2.PassVersion)

[<Fact>]
let ``A23: events differing only in TransformKind are distinguishable`` () =
    let observed   = touched "p" 1 customerKey
    let annotated' = annotated "p" 1 customerKey "tenant scoped"
    Assert.NotEqual<LineageEvent>(observed, annotated')

// ---------------------------------------------------------------------------
// A24: lineage composition is chronological.
//
// `bind f m` produces the trail `m.Trail ++ (f m.Value).Trail` — m's
// events come first, then f's. This is the convention every pass relies
// on; reversing it would silently break replay.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A24: bind composes trails as m.Trail ++ f.Trail (earliest-first)`` () =
    let e1 = touched "f" 1 customerKey
    let e2 = touched "g" 1 orderKey
    let m = Lineage.ofValueWith e1 0
    let f x = Lineage.ofValueWith e2 (x + 1)
    let result = Lineage.bind f m
    Assert.Equal<LineageEvent list>([ e1; e2 ], result.Trail)

[<Fact>]
let ``A24: longer chains preserve chronological order`` () =
    let e1 = touched "p1" 1 customerKey
    let e2 = touched "p2" 1 orderKey
    let e3 = touched "p3" 1 countryKey
    let m =
        Lineage.ofValueWith e1 0
        |> Lineage.bind (fun x -> Lineage.ofValueWith e2 (x + 1))
        |> Lineage.bind (fun x -> Lineage.ofValueWith e3 (x + 1))
    Assert.Equal<LineageEvent list>([ e1; e2; e3 ], m.Trail)

[<Property>]
let ``A24: trail length grows monotonically under bind`` (x: int) =
    let e = touched "p" 1 customerKey
    let m = Lineage.ofValueWith e x
    let f y = Lineage.ofValueWith e (y + 1)
    (Lineage.bind f m).Trail.Length >= m.Trail.Length

// ---------------------------------------------------------------------------
// A25: every transformation runs inside Lineage<_>. The `tell` and
// `tellMany` helpers let a pass record observations without restructuring
// the value, so the monad fits both transformative and observational
// passes.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A25: tell appends a single event without changing the value`` () =
    let e = touched "obs" 1 customerKey
    let m = Lineage.ofValue 42
    let told = Lineage.tell e m
    Assert.Equal(42, told.Value)
    Assert.Equal<LineageEvent list>([ e ], told.Trail)

[<Fact>]
let ``A25: tellMany appends events in order`` () =
    let e1 = touched "p" 1 customerKey
    let e2 = annotated "p" 1 customerKey "tenant"
    let e3 = touched "p" 1 orderKey
    let told = Lineage.tellMany [ e1; e2; e3 ] (Lineage.ofValue 42)
    Assert.Equal<LineageEvent list>([ e1; e2; e3 ], told.Trail)

[<Fact>]
let ``A25: ofValue produces an empty trail`` () =
    let m = Lineage.ofValue 7
    Assert.Equal<LineageEvent list>([], m.Trail)

// ---------------------------------------------------------------------------
// A26: lineage difference does not affect structural equality of catalog
// nodes. Two `Lineage<Kind>` values with the same `Value.SsKey` but
// different trails are still identity-equal at the catalog level.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A26: different lineage trails do not affect kind identity equality`` () =
    let e1 = touched "passA" 1 customerKey
    let e2 = annotated "passB" 5 customerKey "tenant scoped"
    let lineageA = Lineage.ofValueWith e1 customer
    let lineageB = Lineage.ofValueWith e2 customer
    // Trails differ.
    Assert.NotEqual<LineageEvent list>(lineageA.Trail, lineageB.Trail)
    // Identity at the catalog level is unaffected.
    Assert.True(Kind.byIdentity lineageA.Value lineageB.Value)

// ---------------------------------------------------------------------------
// Functor laws — sanity checks for `map`.
// ---------------------------------------------------------------------------

[<Property>]
let ``Lineage functor: identity`` (x: int) =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    Lineage.byValueAndTrail (Lineage.map id m) m

[<Property>]
let ``Lineage functor: composition`` (x: int) =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    let f (y: int) = y + 7
    let g (y: int) = y * 11
    Lineage.byValueAndTrail (Lineage.map (g << f) m) (Lineage.map g (Lineage.map f m))

[<Fact>]
let ``Lineage map preserves the trail untouched`` () =
    let e = touched "p" 1 customerKey
    let m = Lineage.ofValueWith e 0
    let mapped = Lineage.map (fun x -> x + 1) m
    Assert.Equal<LineageEvent list>([ e ], mapped.Trail)

// ---------------------------------------------------------------------------
// Operator surface: >>= matches Lineage.bind, <!> matches Lineage.map.
// ---------------------------------------------------------------------------

[<Property>]
let ``operator >>= equals Lineage.bind`` (x: int) =
    let f y = Lineage.ofValueWith (touched "f" 1 customerKey) (y + 1)
    let m = Lineage.ofValue x
    Lineage.byValueAndTrail (m >>= f) (Lineage.bind f m)

[<Property>]
let ``operator <!> equals Lineage.map`` (x: int) =
    let f (y: int) = y + 1
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    Lineage.byValueAndTrail (f <!> m) (Lineage.map f m)

// ---------------------------------------------------------------------------
// A26 cash-out (chapter-3.7 slice α): `Lineage<'a>` equality projects
// through `Value` only. Two carriers with identical Value but different
// Trail are equal as `Lineage<'a>`. The catalog-level claim of A26 (Kind
// equality is by SsKey, lineage is metadata) extends symmetrically to
// the writer carrier itself.
// ---------------------------------------------------------------------------

[<Property>]
let ``A26: Lineage equality projects through Value (Trail is metadata)`` (x: int) =
    let e1 = touched "passA" 1 customerKey
    let e2 = annotated "passB" 5 orderKey "tenant scoped"
    let m1 = Lineage.ofValueWith e1 x
    let m2 = Lineage.ofValueWith e2 x
    // Trails differ.
    m1.Trail <> m2.Trail
    // Lineage<'a> equality projects through Value only.
    && m1 = m2
    && Lineage.byValue m1 m2
    // Full structural equality is available and rejects the trail divergence.
    && not (Lineage.byValueAndTrail m1 m2)

[<Fact>]
let ``A26: Lineage equality and hash are consistent under Trail divergence`` () =
    let e1 = touched "passA" 1 customerKey
    let e2 = annotated "passB" 5 orderKey "tenant scoped"
    let m1 = Lineage.ofValueWith e1 42
    let m2 = Lineage.ofValueWith e2 42
    Assert.True (Lineage.byValue m1 m2)
    Assert.Equal (hash m1, hash m2)
    Assert.False (Lineage.byValueAndTrail m1 m2)

// ---------------------------------------------------------------------------
// H-001: `lineage { ... }` CE builder. The CE form must produce values
// algebraically equivalent to the explicit `bind` chain. These tests are
// the structural underwriting that the CE preserves the monad-law-bearing
// algebra.
//
// CE form `lineage { let! x = m; do! Lineage.write e; return x }` must
// equal (Value × Trail) `Lineage.bind (fun x -> Lineage.write e |> Lineage.bind (fun () -> Lineage.ofValue x)) m`.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-001 CE: lineage { return x } equals Lineage.ofValue x`` (x: int) =
    Lineage.byValueAndTrail (lineage { return x }) (Lineage.ofValue x)

[<Property>]
let ``H-001 CE: lineage { let! x = m; return x } equals m`` (x: int) =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    Lineage.byValueAndTrail (lineage { let! y = m in return y }) m

[<Fact>]
let ``H-001 CE: do! Lineage.write threads the event chronologically`` () =
    let e1 = touched "p1" 1 customerKey
    let e2 = touched "p2" 1 orderKey
    let m = Lineage.ofValueWith e1 0
    let actual =
        lineage {
            let! x = m
            do! Lineage.write e2
            return x + 1
        }
    let expected =
        m
        |> Lineage.bind (fun x ->
            Lineage.write e2
            |> Lineage.bind (fun () -> Lineage.ofValue (x + 1)))
    Lineage.byValueAndTrail actual expected |> Assert.True
    Assert.Equal<LineageEvent list>([e1; e2], actual.Trail)
    Assert.Equal(1, actual.Value)

[<Fact>]
let ``H-001 CE: multi-step let! + do! preserves A24 chronological ordering`` () =
    let e1 = touched "p1" 1 customerKey
    let e2 = touched "p2" 1 orderKey
    let e3 = touched "p3" 1 countryKey
    let mA = Lineage.ofValueWith e1 10
    let mB y = Lineage.ofValueWith e3 (y * 2)
    let actual =
        lineage {
            let! a = mA
            do! Lineage.write e2
            let! b = mB a
            return b + 1
        }
    Assert.Equal<LineageEvent list>([e1; e2; e3], actual.Trail)
    Assert.Equal(21, actual.Value)

[<Property>]
let ``H-001 CE: lineage { do! write e; return () } equals Lineage.write e`` () =
    let e = touched "p" 1 customerKey
    let actual = lineage { do! Lineage.write e }
    Lineage.byValueAndTrail actual (Lineage.write e)

[<Fact>]
let ``H-001 CE: Lineage.writeMany threads events as one carrier`` () =
    let e1 = touched "p" 1 customerKey
    let e2 = annotated "p" 1 customerKey "x"
    let e3 = touched "p" 1 orderKey
    let actual =
        lineage {
            do! Lineage.writeMany [e1; e2; e3]
            return 42
        }
    Assert.Equal<LineageEvent list>([e1; e2; e3], actual.Trail)
    Assert.Equal(42, actual.Value)

// ---------------------------------------------------------------------------
// H-005: LineageTree<'a> — branching writer monad. The speculative-
// execution sibling of Lineage<'a>. Tests cover:
//   - Construction (ofLineage / ofValue / branch / fork / bifurcate)
//   - Projection (leaves / paths / commit / tryCommitByPath)
//   - Functor laws (identity, composition)
//   - Monad laws (left identity, right identity, associativity)
//   - Round-trip: commit (ofLineage m) = m (single-leaf isomorphism)
//   - Branch preservation: bind distributes over Fork
//   - Free-monad structure: bind over Fork preserves structure
// ---------------------------------------------------------------------------

// LineageTree property tests use full structural equivalence via
// `LineageTree.byValueAndStructure` because F#-default `=` on the tree
// projects leaves through Value only (per A26 / Lineage<'a>'s custom
// equality); the structural-equality form makes algebraic
// substitutions exactly checkable.

[<Fact>]
let ``H-005 LineageTree.ofLineage produces a single-leaf tree`` () =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) 42
    let tree = LineageTree.ofLineage m
    Assert.Equal<Lineage<int> list>([m], LineageTree.leaves tree)
    Assert.True(LineageTree.isLinear tree)

[<Fact>]
let ``H-005 LineageTree.ofValue produces empty-trail single leaf`` () =
    let tree = LineageTree.ofValue 7
    let leaves = LineageTree.leaves tree
    Assert.Single(leaves) |> ignore
    Assert.Equal(7, leaves.[0].Value)
    Assert.Empty(leaves.[0].Trail)

[<Fact>]
let ``H-005 LineageTree.bifurcate carries both branches in leaves`` () =
    let mA = Lineage.ofValueWith (touched "A" 1 customerKey) 10
    let mB = Lineage.ofValueWith (touched "B" 1 orderKey) 20
    let tree =
        LineageTree.bifurcate
            ("policyA", LineageTree.ofLineage mA)
            ("policyB", LineageTree.ofLineage mB)
    Assert.Equal(2, LineageTree.leafCount tree)
    let leaves = LineageTree.leaves tree
    Assert.Equal<Lineage<int> list>([mA; mB], leaves)

[<Fact>]
let ``H-005 LineageTree.paths labels each leaf with its root-to-leaf path`` () =
    let mA = Lineage.ofValueWith (touched "A" 1 customerKey) 10
    let mB = Lineage.ofValueWith (touched "B" 1 orderKey) 20
    let tree =
        LineageTree.bifurcate
            ("policyA", LineageTree.ofLineage mA)
            ("policyB", LineageTree.ofLineage mB)
    let pathsResult = LineageTree.paths tree
    Assert.Equal<(string list * Lineage<int>) list>(
        [(["policyA"], mA); (["policyB"], mB)],
        pathsResult)

[<Fact>]
let ``H-005 LineageTree.paths handles nested Forks (root-to-leaf label path)`` () =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) 1
    let nested =
        LineageTree.fork
            [ ("outer",
                LineageTree.fork
                    [ ("innerA", LineageTree.ofLineage m)
                      ("innerB", LineageTree.ofLineage m) ]) ]
    let pathsResult = LineageTree.paths nested
    Assert.Equal<string list list>(
        [["outer"; "innerA"]; ["outer"; "innerB"]],
        pathsResult |> List.map fst)

[<Fact>]
let ``H-005 LineageTree.commitFirst returns the leftmost leaf`` () =
    let mA = Lineage.ofValueWith (touched "A" 1 customerKey) 10
    let mB = Lineage.ofValueWith (touched "B" 1 orderKey) 20
    let tree =
        LineageTree.bifurcate
            ("a", LineageTree.ofLineage mA)
            ("b", LineageTree.ofLineage mB)
    Assert.Equal(mA, LineageTree.commitFirst tree)

[<Fact>]
let ``H-005 LineageTree.tryCommitByPath walks the labeled path`` () =
    let mA = Lineage.ofValueWith (touched "A" 1 customerKey) 10
    let mB = Lineage.ofValueWith (touched "B" 1 orderKey) 20
    let tree =
        LineageTree.bifurcate
            ("policyA", LineageTree.ofLineage mA)
            ("policyB", LineageTree.ofLineage mB)
    Assert.Equal(Some mA, LineageTree.tryCommitByPath ["policyA"] tree)
    Assert.Equal(Some mB, LineageTree.tryCommitByPath ["policyB"] tree)
    Assert.Equal<Lineage<int> option>(None, LineageTree.tryCommitByPath ["unknown"] tree)

[<Fact>]
let ``H-005 LineageTree round-trip: commitFirst (ofLineage m) = m`` () =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) 42
    Assert.True(Lineage.byValueAndTrail m (LineageTree.commitFirst (LineageTree.ofLineage m)))

[<Property>]
let ``H-005 LineageTree round-trip (property): commitFirst ∘ ofLineage = id`` (x: int) =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    Lineage.byValueAndTrail m (LineageTree.commitFirst (LineageTree.ofLineage m))

[<Fact>]
let ``H-005 LineageTree.isLinear: single-leaf tree is linear`` () =
    let m = Lineage.ofValue 1
    Assert.True(LineageTree.isLinear (LineageTree.ofLineage m))
    Assert.True(LineageTree.isLinear (LineageTree.fork [("only", LineageTree.ofLineage m)]))

[<Fact>]
let ``H-005 LineageTree.isLinear: multi-branch tree is not linear`` () =
    let m = Lineage.ofValue 1
    let multi =
        LineageTree.fork
            [ ("a", LineageTree.ofLineage m)
              ("b", LineageTree.ofLineage m) ]
    Assert.False(LineageTree.isLinear multi)

[<Fact>]
let ``H-005 LineageTree.isEmpty detects degenerate trees`` () =
    Assert.True(LineageTree.isEmpty (LineageTree.fork []))
    Assert.False(LineageTree.isEmpty (LineageTree.ofValue 1))

[<Fact>]
let ``H-005 LineageTree.commit fails on leaf-less tree (precondition)`` () =
    let empty = LineageTree.fork []
    Assert.Throws<System.ArgumentException>(fun () ->
        LineageTree.commit List.head empty |> ignore) |> ignore

// --- LineageTree functor laws ---

[<Property>]
let ``H-005 LineageTree functor: identity (map id = id)`` (x: int) =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    let tree = LineageTree.ofLineage m
    LineageTree.byValueAndStructure tree (LineageTree.map id tree)

[<Property>]
let ``H-005 LineageTree functor: composition (map (g << f) = map g << map f)`` (x: int) =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    let tree =
        LineageTree.bifurcate
            ("a", LineageTree.ofLineage m)
            ("b", LineageTree.ofLineage m)
    let f (y: int) = y + 7
    let g (y: int) = y * 11
    LineageTree.byValueAndStructure
        (LineageTree.map (g << f) tree)
        (LineageTree.map g (LineageTree.map f tree))

// --- LineageTree monad laws ---
// The branching writer monad's laws hold over the leaves (each carrying
// a Lineage<'a> whose own monad laws are tested above) AND over the
// tree structure (Fork preserves shape under bind).
//
//   left identity   : bind f (ofValue x)  =  f x   (modulo trail prefix)
//   right identity  : bind ofLineage tree =  tree
//   associativity   : bind g (bind f t)   =  bind (fun x -> bind g (f x)) t

[<Property>]
let ``H-005 LineageTree monad: left identity`` (x: int) =
    let f y =
        LineageTree.ofLineage (Lineage.ofValueWith (touched "f" 1 customerKey) (y + 1))
    let lhs = LineageTree.bind f (LineageTree.ofValue x)
    let rhs = f x
    LineageTree.byValueAndStructure lhs rhs

[<Property>]
let ``H-005 LineageTree monad: right identity (bind ofLineage tree = tree)`` (x: int) =
    // For a leaf carrying Lineage<int>, `bind ofLineage` substitutes
    // `ofLineage (ofValue m.Value)` then prepends m.Trail. The result
    // is structurally identical to the original leaf.
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    let tree =
        LineageTree.bifurcate
            ("a", LineageTree.ofLineage m)
            ("b", LineageTree.ofLineage m)
    let result = LineageTree.bind (fun v -> LineageTree.ofValue v) tree
    LineageTree.byValueAndStructure tree result

[<Property>]
let ``H-005 LineageTree monad: associativity`` (x: int) =
    let f y =
        LineageTree.ofLineage (Lineage.ofValueWith (touched "f" 1 customerKey) (y + 1))
    let g y =
        LineageTree.ofLineage (Lineage.ofValueWith (touched "g" 2 orderKey) (y * 3))
    let m = LineageTree.ofValue x
    LineageTree.byValueAndStructure
        (LineageTree.bind g (LineageTree.bind f m))
        (LineageTree.bind (fun y -> LineageTree.bind g (f y)) m)

[<Fact>]
let ``H-005 LineageTree.bind distributes over Fork (preserves branching structure)`` () =
    // Bind on Fork should produce a Fork with the same labels;
    // each branch's continuation is the f-result.
    let mA = Lineage.ofValueWith (touched "A" 1 customerKey) 1
    let mB = Lineage.ofValueWith (touched "B" 1 orderKey) 2
    let tree =
        LineageTree.bifurcate
            ("policyA", LineageTree.ofLineage mA)
            ("policyB", LineageTree.ofLineage mB)
    let f n =
        LineageTree.ofLineage (Lineage.ofValueWith (touched "f" 1 countryKey) (n * 10))
    let result = LineageTree.bind f tree
    // Labels preserved
    let labels =
        match result with
        | Fork branches -> branches |> List.map (fun b -> b.Label)
        | _ -> []
    Assert.Equal<string list>(["policyA"; "policyB"], labels)
    // Values transformed (10 and 20); leaf count unchanged
    let values = LineageTree.leaves result |> List.map (fun l -> l.Value)
    Assert.Equal<int list>([10; 20], values)

[<Fact>]
let ``H-005 LineageTree.bind: leaf trail prepends to continuation (A24 chronological)`` () =
    // The substitution must prepend the existing leaf's trail to every
    // continuation leaf — preserving chronological ordering across the
    // bind boundary.
    let e1 = touched "before" 1 customerKey
    let e2 = touched "after" 1 orderKey
    let leafTree = LineageTree.ofLineage (Lineage.ofValueWith e1 1)
    let f n =
        LineageTree.ofLineage (Lineage.ofValueWith e2 (n + 10))
    let result = LineageTree.bind f leafTree
    match result with
    | Leaf m ->
        Assert.Equal(11, m.Value)
        Assert.Equal<LineageEvent list>([e1; e2], m.Trail)
    | _ -> failwith "expected Leaf result"

// --- Worked example: speculative execution (policy diff shape) ---

[<Fact>]
let ``H-005 LineageTree worked example: bifurcate retains both branches' lineages`` () =
    // The use case that motivates LineageTree: run the same input
    // through two alternative paths; retain both for comparison.
    let baseInput = 10

    let policyA n =
        Lineage.ofValueWith (touched "policyA-applied" 1 customerKey) (n * 2)
    let policyB n =
        Lineage.ofValueWith (touched "policyB-applied" 1 orderKey) (n + 5)

    let tree =
        LineageTree.bifurcate
            ("policyA", LineageTree.ofLineage (policyA baseInput))
            ("policyB", LineageTree.ofLineage (policyB baseInput))

    // Both branches' values are visible via paths.
    let pathsResult = LineageTree.paths tree
    let asMap = pathsResult |> List.map (fun (p, l) -> (List.head p, l.Value)) |> Map.ofList
    Assert.Equal(20, asMap.["policyA"])
    Assert.Equal(15, asMap.["policyB"])

    // Both branches' trails are retained — the diff consumer compares
    // by trail event.
    let trailsByLabel =
        pathsResult |> List.map (fun (p, l) -> (List.head p, l.Trail |> List.map (fun e -> e.PassName)))
        |> Map.ofList
    Assert.Equal<string list>(["policyA-applied"], trailsByLabel.["policyA"])
    Assert.Equal<string list>(["policyB-applied"], trailsByLabel.["policyB"])

// --- lineageTree CE builder equivalence ---

[<Property>]
let ``H-005 CE: lineageTree { return x } = LineageTree.ofValue x`` (x: int) =
    LineageTree.byValueAndStructure
        (lineageTree { return x })
        (LineageTree.ofValue x)

[<Fact>]
let ``H-005 CE: lineageTree let! threads through Leaf trees`` () =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) 10
    let actual =
        lineageTree {
            let! x = LineageTree.ofLineage m
            return x * 2
        }
    let expected =
        LineageTree.bind
            (fun x -> LineageTree.ofValue (x * 2))
            (LineageTree.ofLineage m)
    Assert.True(LineageTree.byValueAndStructure actual expected)
    match actual with
    | Leaf result ->
        Assert.Equal(20, result.Value)
        Assert.Equal<LineageEvent list>([touched "p" 1 customerKey], result.Trail)
    | _ -> failwith "expected Leaf"

[<Fact>]
let ``H-005 CE: lineageTree preserves branching structure under bind`` () =
    let mA = Lineage.ofValueWith (touched "A" 1 customerKey) 1
    let mB = Lineage.ofValueWith (touched "B" 1 orderKey) 2
    let tree =
        LineageTree.bifurcate
            ("a", LineageTree.ofLineage mA)
            ("b", LineageTree.ofLineage mB)
    let actual =
        lineageTree {
            let! x = tree
            return x + 100
        }
    let leafValues = LineageTree.leaves actual |> List.map (fun l -> l.Value)
    Assert.Equal<int list>([101; 102], leafValues)

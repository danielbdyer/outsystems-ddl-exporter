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

let private touched (passName: string) (passVersion: int) (key: SsKey) : LineageEvent =
    { PassName      = passName
      PassVersion   = passVersion
      SsKey         = key
      TransformKind = Touched }

let private annotated (passName: string) (passVersion: int) (key: SsKey) (detail: string) : LineageEvent =
    // Chapter-3.6 slice-β: writer-monad-laws tests use the
    // `AnnotationDetail.Label` free-form variant — the algebraic
    // laws (left identity, right identity, associativity) hold for
    // any payload type, so the detail's typed shape is incidental
    // to what the tests prove.
    { PassName      = passName
      PassVersion   = passVersion
      SsKey         = key
      TransformKind = Annotated (Label detail) }

// ---------------------------------------------------------------------------
// Monad laws. Lineage is the writer monad over the (List, ++, []) monoid;
// the laws hold for any underlying value type.
//
//   left identity   : bind f (ofValue x)  =  f x
//   right identity  : bind ofValue m      =  m
//   associativity   : bind g (bind f m)   =  bind (fun x -> bind g (f x)) m
// ---------------------------------------------------------------------------

[<Property>]
let ``Lineage monad: left identity`` (x: int) =
    let f y = Lineage.ofValueWith (touched "f" 1 customerKey) (y + 1)
    Lineage.bind f (Lineage.ofValue x) = f x

[<Property>]
let ``Lineage monad: right identity`` (x: int) =
    let m = Lineage.ofValueWith (touched "obs" 3 customerKey) x
    Lineage.bind Lineage.ofValue m = m

[<Property>]
let ``Lineage monad: associativity`` (x: int) =
    let f y = Lineage.ofValueWith (touched "f" 1 customerKey) (y + 1)
    let g y = Lineage.ofValueWith (touched "g" 2 orderKey)    (y * 3)
    let m = Lineage.ofValue x
    Lineage.bind g (Lineage.bind f m)
        = Lineage.bind (fun y -> Lineage.bind g (f y)) m

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
    Lineage.map id m = m

[<Property>]
let ``Lineage functor: composition`` (x: int) =
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    let f (y: int) = y + 7
    let g (y: int) = y * 11
    Lineage.map (g << f) m = Lineage.map g (Lineage.map f m)

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
    (m >>= f) = Lineage.bind f m

[<Property>]
let ``operator <!> equals Lineage.map`` (x: int) =
    let f (y: int) = y + 1
    let m = Lineage.ofValueWith (touched "p" 1 customerKey) x
    (f <!> m) = Lineage.map f m

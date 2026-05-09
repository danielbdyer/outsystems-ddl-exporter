module Projection.Tests.SsKeyTests

open System
open Xunit
open Projection.Core

/// Stage 0 (S0.B slice 5.5 per
/// `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md` §3) splits `SsKey`
/// from a two-variant DU (`Original | Derived`) into a four-variant
/// DU (`OssysOriginal | Synthesized | DerivedFrom | V1Mapped`) so the
/// JSON-projection-lossiness bound documented at `AXIOMS.md` A1
/// (session 23 amendment) becomes type-visible. A1's preservation
/// holds unconditionally for `OssysOriginal`, for `V1Mapped`, and for
/// `DerivedFrom` rooted in either; A1 is bounded for `Synthesized`.
///
/// The chapter-3 cross-cutting close fills the AXIOMS amendment body
/// per the Stage 0 scaffolding (S0.F); these tests cover the runtime
/// surface that the amendment will reference.

let private sampleGuid () : Guid =
    Guid.Parse "00000000-0000-0000-0000-000000000001"

let private sampleNamespace () : Guid =
    Guid.Parse "deadbeef-dead-beef-dead-beefdeadbeef"

// ---------------------------------------------------------------------
// Smart constructors
// ---------------------------------------------------------------------

[<Fact>]
let ``S0.B.5: ossysOriginal accepts any GUID; constructor is total`` () =
    let key = SsKey.ossysOriginal (sampleGuid ())
    match key with
    | OssysOriginal g -> Assert.Equal(sampleGuid (), g)
    | other -> Assert.Fail(sprintf "expected OssysOriginal, got %A" other)

[<Fact>]
let ``S0.B.5: synthesized rejects blank source`` () =
    let result = SsKey.synthesized "  " "Customer"
    match result with
    | Failure errors ->
        let codes = errors |> List.map (fun e -> e.Code)
        Assert.Contains("sskey.synth.source.empty", codes)
    | Success _ ->
        Assert.Fail "expected Failure on blank source, got Success"

[<Fact>]
let ``S0.B.5: synthesized rejects blank basis`` () =
    let result = SsKey.synthesized "OS_KIND" ""
    match result with
    | Failure errors ->
        let codes = errors |> List.map (fun e -> e.Code)
        Assert.Contains("sskey.synth.basis.empty", codes)
    | Success _ ->
        Assert.Fail "expected Failure on blank basis, got Success"

[<Fact>]
let ``S0.B.5: synthesized accepts non-blank source and basis`` () =
    // Chapter-3.6 slice-δ: `synthesized` wraps the basis as a
    // single-element typed segment list `[basis]`.
    let result = SsKey.synthesized "OS_KIND" "Customer"
    match result with
    | Success (Synthesized ("OS_KIND", ["Customer"])) -> ()
    | other -> Assert.Fail(sprintf "expected Synthesized (OS_KIND, [\"Customer\"]), got %A" other)

[<Fact>]
let ``S0.B.5: derivedFrom rejects blank reason`` () =
    let parent = SsKey.ossysOriginal (sampleGuid ())
    let result = SsKey.derivedFrom parent ""
    match result with
    | Failure errors ->
        let codes = errors |> List.map (fun e -> e.Code)
        Assert.Contains("sskey.derivedReason.empty", codes)
    | Success _ ->
        Assert.Fail "expected Failure on blank reason, got Success"

[<Fact>]
let ``S0.B.5: fromV1 is total; constructs V1Mapped with both GUIDs`` () =
    let v1 = sampleGuid ()
    let ns = sampleNamespace ()
    match SsKey.fromV1 v1 ns with
    | V1Mapped (g1, g2) ->
        Assert.Equal(v1, g1)
        Assert.Equal(ns, g2)
    | other -> Assert.Fail(sprintf "expected V1Mapped, got %A" other)

// ---------------------------------------------------------------------
// Cross-variant equality (per prescope §3: equality respects provenance)
// ---------------------------------------------------------------------

[<Fact>]
let ``S0.B.5: OssysOriginal g and V1Mapped (g, _) with the same GUID are NOT equal`` () =
    let g = sampleGuid ()
    let ns = sampleNamespace ()
    let ossys = SsKey.ossysOriginal g
    let v1mapped = SsKey.fromV1 g ns
    Assert.NotEqual<SsKey>(ossys, v1mapped)

[<Fact>]
let ``S0.B.5: Synthesized and OssysOriginal with related content are NOT equal`` () =
    let g = sampleGuid ()
    let ossys = SsKey.ossysOriginal g
    let synth = SsKey.synthesized "OS_KIND" (g.ToString "N") |> Result.value
    Assert.NotEqual<SsKey>(ossys, synth)

[<Fact>]
let ``S0.B.5: DerivedFrom equality is structural over parent and reason`` () =
    let parent = SsKey.synthesized "OS_KIND" "Customer" |> Result.value
    let d1 = SsKey.derivedFrom parent "inverse" |> Result.value
    let d2 = SsKey.derivedFrom parent "inverse" |> Result.value
    Assert.Equal<SsKey>(d1, d2)

[<Fact>]
let ``S0.B.5: DerivedFrom with different reasons are NOT equal`` () =
    let parent = SsKey.synthesized "OS_KIND" "Customer" |> Result.value
    let d1 = SsKey.derivedFrom parent "inverse" |> Result.value
    let d2 = SsKey.derivedFrom parent "shadow" |> Result.value
    Assert.NotEqual<SsKey>(d1, d2)

// ---------------------------------------------------------------------
// rootOriginal across variants
// ---------------------------------------------------------------------

[<Fact>]
let ``S0.B.5: rootOriginal on OssysOriginal returns 32-char hex N format`` () =
    let g = sampleGuid ()
    let key = SsKey.ossysOriginal g
    Assert.Equal(g.ToString "N", SsKey.rootOriginal key)

[<Fact>]
let ``S0.B.5: rootOriginal on Synthesized preserves pre-stratification source_basis form`` () =
    let key = SsKey.synthesized "OS_KIND" "Customer" |> Result.value
    Assert.Equal("OS_KIND_Customer", SsKey.rootOriginal key)

[<Fact>]
let ``S0.B.5: rootOriginal on DerivedFrom recurses to the parent`` () =
    let parent = SsKey.synthesized "OS_KIND" "Customer" |> Result.value
    let derived = SsKey.derivedFrom parent "inverse" |> Result.value
    Assert.Equal("OS_KIND_Customer", SsKey.rootOriginal derived)

[<Fact>]
let ``S0.B.5: rootOriginal on V1Mapped returns the V1 GUID hex`` () =
    let v1 = sampleGuid ()
    let key = SsKey.fromV1 v1 (sampleNamespace ())
    Assert.Equal(v1.ToString "N", SsKey.rootOriginal key)

// ---------------------------------------------------------------------
// isDerived: only DerivedFrom returns true; the three leaf variants
// (OssysOriginal, Synthesized, V1Mapped) return false.
// ---------------------------------------------------------------------

[<Fact>]
let ``S0.B.5: isDerived returns false for OssysOriginal, Synthesized, and V1Mapped`` () =
    Assert.False(SsKey.isDerived (SsKey.ossysOriginal (sampleGuid ())))
    Assert.False(SsKey.isDerived (SsKey.synthesized "OS_KIND" "C" |> Result.value))
    Assert.False(SsKey.isDerived (SsKey.fromV1 (sampleGuid ()) (sampleNamespace ())))

[<Fact>]
let ``S0.B.5: isDerived returns true for DerivedFrom`` () =
    let parent = SsKey.synthesized "OS_KIND" "Customer" |> Result.value
    let derived = SsKey.derivedFrom parent "inverse" |> Result.value
    Assert.True(SsKey.isDerived derived)

// ---------------------------------------------------------------------
// derivationReasons: empty for leaf variants; recursive for DerivedFrom.
// ---------------------------------------------------------------------

[<Fact>]
let ``S0.B.5: derivationReasons is empty for the three leaf variants`` () =
    Assert.Empty(SsKey.derivationReasons (SsKey.ossysOriginal (sampleGuid ())))
    Assert.Empty(SsKey.derivationReasons (SsKey.synthesized "OS_KIND" "C" |> Result.value))
    Assert.Empty(SsKey.derivationReasons (SsKey.fromV1 (sampleGuid ()) (sampleNamespace ())))

[<Fact>]
let ``S0.B.5: derivationReasons reads root-to-leaf, oldest first across DerivedFrom chain`` () =
    let parent = SsKey.synthesized "OS_KIND" "Customer" |> Result.value
    let d1 = SsKey.derivedFrom parent "inverse" |> Result.value
    let d2 = SsKey.derivedFrom d1 "shadow" |> Result.value
    Assert.Equal<string list>([ "inverse"; "shadow" ], SsKey.derivationReasons d2)

// Chapter-3.6 slice-δ + DECISIONS pillar 6 (no V2-internal back-compat
// paths): the `SsKey.original` parser-shim and `SsKey.derived` alias
// were retired. Their unit tests are deleted with them — `synthesized`
// / `synthesizedComposite` / `derivedFrom` are the sole typed entry
// points and have their own tests above.

// ---------------------------------------------------------------------
// A1: identity stratification — pattern-matchable variant carries the
// JSON-projection-lossiness bound. The chapter-3 cross-cutting AXIOMS
// amendment will cite this test as the runtime witness.
// ---------------------------------------------------------------------

let private isA1Unconditional (key: SsKey) : bool =
    let rec walk (k: SsKey) =
        match k with
        | OssysOriginal _ | V1Mapped _ -> true
        | Synthesized _ -> false
        | DerivedFrom (parent, _) -> walk parent
    walk key

[<Fact>]
let ``A1 stratification: OssysOriginal honors A1 unconditionally`` () =
    Assert.True(isA1Unconditional (SsKey.ossysOriginal (sampleGuid ())))

[<Fact>]
let ``A1 stratification: V1Mapped honors A1 unconditionally`` () =
    Assert.True(isA1Unconditional (SsKey.fromV1 (sampleGuid ()) (sampleNamespace ())))

[<Fact>]
let ``A1 stratification: Synthesized is bounded (renames produce different SsKeys)`` () =
    Assert.False(isA1Unconditional (SsKey.synthesized "OS_KIND" "OldName" |> Result.value))
    let oldKey = SsKey.synthesized "OS_KIND" "OldName" |> Result.value
    let newKey = SsKey.synthesized "OS_KIND" "NewName" |> Result.value
    Assert.NotEqual<SsKey>(oldKey, newKey)

[<Fact>]
let ``A1 stratification: DerivedFrom inherits the bound from its root`` () =
    let synthRoot = SsKey.synthesized "OS_KIND" "C" |> Result.value
    let synthDerived = SsKey.derivedFrom synthRoot "inverse" |> Result.value
    Assert.False(isA1Unconditional synthDerived)
    let ossysRoot = SsKey.ossysOriginal (sampleGuid ())
    let ossysDerived = SsKey.derivedFrom ossysRoot "inverse" |> Result.value
    Assert.True(isA1Unconditional ossysDerived)

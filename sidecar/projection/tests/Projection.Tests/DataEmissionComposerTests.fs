module Projection.Tests.DataEmissionComposerTests

open Xunit
open Projection.Core
open Projection.Targets.Data

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice η — DataEmissionComposer + EmissionPolicy.DataComposition.
//
// The composer reads `Policy.Emission.DataComposition` and dispatches to
// the three sibling data emitters (StaticSeedsEmitter today; Migration +
// Bootstrap stubbed pending slices ε / ζ). A18 amended (emitters cannot
// consume Policy) is preserved structurally — emitters' signatures
// literally cannot type-check with a Policy parameter; only the composer
// touches Policy.
//
// Tests cover: DU exhaustiveness (compile-time match-shaped); dispatch
// correctness per variant; idempotence vs. direct StaticSeedsEmitter.emit;
// T11 keyset coverage (every catalog kind keyed); T1 byte-determinism;
// hoisted-topo equivalence (composer + emitWithTopo == standalone emit);
// composeWithLineage trail propagation.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkName (s: string) : Name =
    Name.create s |> mustOk

let private mustOkEmit (r: Result<'a, EmitError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e); Unchecked.defaultof<_>

/// Mirrors the StaticSeedsEmitter test fixture — two-row Country with
/// Id PK + Code + Label. Static-modality, no FKs, no cycle.
let private mkCountryKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Country"]
    let idKey = mkKey ["TestModule"; "Country"; "Id"]
    let codeKey = mkKey ["TestModule"; "Country"; "Code"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    let row code label =
        { Identifier = mkKey ["TestModule"; "Country"; "Row"; code]
          Values =
              Map.ofList
                  [ mkName "Id",    code
                    mkName "Code",  code
                    mkName "Label", label ] }
    {
        SsKey    = kindKey
        Name     = mkName "Country"
        Origin   = OsNative
        Modality = [ Static [ row "US" "United States"; row "CA" "Canada" ] ]
        Physical = { Schema = "dbo"; Table = "OSUSR_TEST_COUNTRY" }
        Attributes =
            [
                { SsKey = idKey;    Name = mkName "Id";    Type = Integer
                  Column = { ColumnName = "ID";    IsNullable = false }
                  IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
                { SsKey = codeKey;  Name = mkName "Code";  Type = Text
                  Column = { ColumnName = "CODE";  IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
                { SsKey = labelKey; Name = mkName "Label"; Type = Text
                  Column = { ColumnName = "LABEL"; IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
            ]
        References = []
        Indexes    = []
    }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["TestModule"]
          Name  = mkName "TestModule"
          Kinds = kinds }
    { Modules = [ m ] }

let private policyWith (composition: DataComposition) : Policy =
    { Policy.empty with
        Emission =
            { Policy.empty.Emission with
                EmitData = true
                DataComposition = composition } }

// ---------------------------------------------------------------------------
// DU exhaustiveness + smart-constructor invariants.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DataComposition DU has three variants and EmissionPolicy.create accepts each`` () =
    let p1 = EmissionPolicy.create true true false AllRemaining     |> mustOk
    let p2 = EmissionPolicy.create true true false AllExceptStatic  |> mustOk
    let p3 = EmissionPolicy.create true true false AllData          |> mustOk
    Assert.Equal (AllRemaining,     p1.DataComposition)
    Assert.Equal (AllExceptStatic,  p2.DataComposition)
    Assert.Equal (AllData,          p3.DataComposition)

[<Fact>]
let ``EmissionPolicy.empty defaults DataComposition to AllRemaining (promoted-lane)`` () =
    Assert.Equal (AllRemaining, EmissionPolicy.empty.DataComposition)

[<Fact>]
let ``EmissionPolicy.withDataComposition swaps the composition without disturbing emit booleans`` () =
    let p = EmissionPolicy.dataOnly |> EmissionPolicy.withDataComposition AllExceptStatic
    Assert.Equal (AllExceptStatic, p.DataComposition)
    Assert.False  p.EmitSchema
    Assert.True   p.EmitData
    Assert.False  p.EmitDiagnostics

// ---------------------------------------------------------------------------
// Dispatch correctness — slice η MVP scope.
//
// Today: StaticSeeds is real; Migration + Bootstrap are no-op stubs. The
// composer's union is left-biased favoring populated Static slices; empty
// kinds fall through to the next sibling and ultimately to emptyScript.
// When ε/ζ ship, these tests extend to assert per-emitter coverage; the
// MVP shape covers the dispatch axis (which composition variant fires
// which emitters).
// ---------------------------------------------------------------------------

[<Fact>]
let ``compose AllRemaining: Static seeds fire (Phase1Merges populated for static kind)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let policy = policyWith AllRemaining
    let artifact = DataEmissionComposer.compose policy catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Equal (2, List.length script.Phase1Merges)

[<Fact>]
let ``compose AllExceptStatic: Static skipped (no Phase1Merges even for static kind)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let policy = policyWith AllExceptStatic
    let artifact = DataEmissionComposer.compose policy catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Static skipped + Migration/Bootstrap stubs empty → script is empty.
    Assert.Empty script.Phase1Merges
    Assert.Empty script.Phase2Updates

[<Fact>]
let ``compose AllData: Static fires for every kind (matches AllRemaining for static-only catalog)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let allData = DataEmissionComposer.compose (policyWith AllData) catalog Profile.empty |> mustOkEmit
    let allRemaining = DataEmissionComposer.compose (policyWith AllRemaining) catalog Profile.empty |> mustOkEmit
    // MVP scope: with no Migration/Bootstrap consumers shipped yet,
    // AllData and AllRemaining are observably equivalent. Slice ζ
    // (Bootstrap) will differentiate them by having Bootstrap emit
    // even Static kinds under AllData.
    let s1 = ArtifactByKind.toMap allData |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap allRemaining |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)

// ---------------------------------------------------------------------------
// T11 — every catalog kind appears in the composer's keyset.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11: composer keyset equals Catalog.allKinds keyset for every DataComposition variant`` () =
    let country = mkCountryKind ()
    let regular : Kind =
        { country with
            SsKey    = mkKey ["TestModule"; "Customer"]
            Name     = mkName "Customer"
            Modality = []
            Physical = { Schema = "dbo"; Table = "OSUSR_TEST_CUSTOMER" } }
    let catalog = mkCatalog [ country; regular ]
    let expected =
        Catalog.allKinds catalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    for composition in [ AllRemaining; AllExceptStatic; AllData ] do
        let policy = policyWith composition
        let artifact = DataEmissionComposer.compose policy catalog Profile.empty |> mustOkEmit
        let actual =
            ArtifactByKind.toMap artifact
            |> Map.toList
            |> List.map fst
            |> Set.ofList
        Assert.Equal<Set<SsKey>> (expected, actual)

// ---------------------------------------------------------------------------
// Hoisted-topo equivalence — slice δ improvement #3 cash-out.
//
// `DataEmissionComposer.compose` runs `TopologicalOrderPass` ONCE and
// passes the result through `StaticSeedsEmitter.emitWithTopo`. The
// observable output must equal `StaticSeedsEmitter.emit`'s standalone
// shape (which runs the pass internally).
// ---------------------------------------------------------------------------

[<Fact>]
let ``hoisted-topo equivalence: composer-AllRemaining matches standalone StaticSeedsEmitter.emit`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let standalone = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let composed = DataEmissionComposer.compose (policyWith AllRemaining) catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap standalone |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap composed |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)
    Assert.Equal<DataInsertRow list> (s1.Phase1Merges, s2.Phase1Merges)
    Assert.Equal<DataInsertRow list> (s1.Phase2Updates, s2.Phase2Updates)

// ---------------------------------------------------------------------------
// T1 — composer is byte-deterministic across repeat invocations.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: composer.compose is byte-deterministic across repeat invocations`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let policy = policyWith AllRemaining
    let r1 = DataEmissionComposer.compose policy catalog Profile.empty |> mustOkEmit
    let r2 = DataEmissionComposer.compose policy catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)

// ---------------------------------------------------------------------------
// composeWithLineage — trail-fidelity propagation.
//
// The topo pass emits one `Touched` event per kind; the composer's
// lineage trail must surface these so pipeline-level callers preserve
// the writer-fidelity contract. Slice η's primary improvement on
// slice δ's silent-discard pattern.
// ---------------------------------------------------------------------------

[<Fact>]
let ``composeWithLineage: trail carries one event per kind in catalog`` () =
    let country = mkCountryKind ()
    let regular : Kind =
        { country with
            SsKey    = mkKey ["TestModule"; "Customer"]
            Name     = mkName "Customer"
            Modality = []
            Physical = { Schema = "dbo"; Table = "OSUSR_TEST_CUSTOMER" } }
    let catalog = mkCatalog [ country; regular ]
    let policy = policyWith AllRemaining
    let lineageOfResult =
        DataEmissionComposer.composeWithLineage
            policy catalog Profile.empty
            MigrationDependencyContext.empty
            UserRemapContext.empty
    // TopologicalOrderPass emits one Touched event per kind scanned.
    Assert.Equal (2, List.length lineageOfResult.Trail)

// ---------------------------------------------------------------------------
// Slice θ — partition assertion.
//
// Per pre-scope §5.3: every kind's populated coverage comes from at most
// one sibling emitter under a given DataComposition. Two emitters both
// claiming the same kind under the same composition surfaces as
// `EmitError.OverlappingEmitterCoverage`. Tests cover:
//   - The happy path (Static-only / Migration-only) returns Ok.
//   - The conflict path (kind has Static modality AND a Migration row
//     under AllRemaining) returns Error with both emitter names.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice θ: partition holds when a kind is populated by exactly one emitter (Static only)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let result = DataEmissionComposer.compose (policyWith AllRemaining) catalog Profile.empty
    match result with
    | Ok _ -> ()
    | Error e -> Assert.Fail (sprintf "expected partition success, got %A" e)

[<Fact>]
let ``Slice θ: partition fails (OverlappingEmitterCoverage) when a kind is populated by two sibling emitters`` () =
    let country = mkCountryKind ()  // has Static.Modality with rows
    let catalog = mkCatalog [ country ]
    // Migration also publishes a row for the same Static kind under
    // AllRemaining → overlap.
    let migration =
        { Rows =
            [ { KindKey = country.SsKey
                Identifier = mkKey ["TestModule"; "Country"; "Mig"; "Other"]
                Values = Map.ofList [ mkName "Id", "9"; mkName "Code", "ZZ"; mkName "Label", "Other" ] } ] }
    let result =
        DataEmissionComposer.composeWithMigration
            (policyWith AllRemaining) catalog Profile.empty migration
    match result with
    | Ok _ -> Assert.Fail "expected OverlappingEmitterCoverage error, got Ok"
    | Error (OverlappingEmitterCoverage (k, names)) ->
        Assert.Equal<SsKey> (country.SsKey, k)
        Assert.Contains ("StaticSeeds", names)
        Assert.Contains ("MigrationDependencies", names)
    | Error e -> Assert.Fail (sprintf "expected OverlappingEmitterCoverage, got %A" e)

[<Fact>]
let ``Slice θ: partition holds under AllExceptStatic (Static skipped, Migration populated)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let migration =
        { Rows =
            [ { KindKey = country.SsKey
                Identifier = mkKey ["TestModule"; "Country"; "Mig"; "1"]
                Values = Map.ofList [ mkName "Id", "1"; mkName "Code", "US"; mkName "Label", "United States" ] } ] }
    // AllExceptStatic skips Static, so Migration alone owns the kind.
    let result =
        DataEmissionComposer.composeWithMigration
            (policyWith AllExceptStatic) catalog Profile.empty migration
    match result with
    | Ok artifact ->
        let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
        Assert.Equal (1, List.length script.Phase1Merges)
    | Error e -> Assert.Fail (sprintf "expected partition success, got %A" e)

[<Fact>]
let ``composeWithLineage: payload Result matches compose's Result for the same inputs`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let policy = policyWith AllRemaining
    let viaLineage =
        (DataEmissionComposer.composeWithLineage
            policy catalog Profile.empty
            MigrationDependencyContext.empty
            UserRemapContext.empty).Value
        |> mustOkEmit
    let viaCompose = DataEmissionComposer.compose policy catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap viaLineage |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap viaCompose |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)

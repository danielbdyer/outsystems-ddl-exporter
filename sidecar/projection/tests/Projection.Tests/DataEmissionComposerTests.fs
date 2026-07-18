module Projection.Tests.DataEmissionComposerTests

open Xunit
open Projection.Core
open Projection.Targets.Data
open Projection.Tests.Fixtures

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
              StaticRow.presentValues
                  [ mkName "Id",    code
                    mkName "Code",  code
                    mkName "Label", label ] }
    {
        SsKey    = kindKey
        Name     = mkName "Country"
        Origin   = Native
        Modality = [ Static [ row "US" "United States"; row "CA" "Canada" ] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_COUNTRY"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["TestModule"]
          Name  = mkName "TestModule"
          Kinds = kinds; IsActive = true; ExtendedProperties = [] }
    { Modules = [ m ]; Sequences = [] }

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
let ``compose AllData: Static lane is SKIPPED (Bootstrap covers everything; differs from AllRemaining)`` () =
    // Slice ζ shipped (2026-06-14): Bootstrap now renders a real row source, so
    // `AllData` (Bootstrap covers everything, static included) SKIPS the Static
    // lane — else both lanes claim the static kind and the partition law trips.
    // Through the plain `compose` (no hydrated bootstrap rows), `AllData` yields
    // an EMPTY script for the static kind (Static skipped, Bootstrap row source
    // empty), while `AllRemaining` fires the Static lane (2 merges). The two are
    // now observably DIFFERENT — the differentiation the MVP test anticipated.
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let allData = DataEmissionComposer.compose (policyWith AllData) catalog Profile.empty |> mustOkEmit
    let allRemaining = DataEmissionComposer.compose (policyWith AllRemaining) catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap allData |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap allRemaining |> Map.find country.SsKey
    // AllData with no bootstrap rows: the static kind renders empty.
    Assert.Empty s1.Phase1Merges
    // AllRemaining: the Static lane fires for the static kind.
    Assert.Equal (2, List.length s2.Phase1Merges)
    Assert.NotEqual<string> (s2.Rendered, s1.Rendered)

[<Fact>]
let ``compose AllData WITH a bootstrap row source: Bootstrap covers the static kind (no overlap, partition holds)`` () =
    // The positive half: under `AllData`, a hydrated bootstrap row source makes
    // Bootstrap emit the (otherwise-static) kind — and because the Static lane
    // is skipped, the partition law holds (exactly one lane populates the kind).
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let rows : Map<SsKey, StaticRow list> =
        Map.ofList
            [ country.SsKey,
              [ { Identifier = mkKey ["TestModule"; "Country"; "Row"; "1"]
                  Values = country.Attributes |> List.map (fun a -> a.Name, "1") |> StaticRow.presentValues } ] ]
    let bundle =
        DataEmissionComposer.composeRenderedBundleWithBootstrap
            (policyWith AllData) catalog Profile.empty
            MigrationDependencyContext.empty rows UserRemapContext.empty
        |> mustOkEmit
    // Bootstrap lane carries the MERGE; Static lane is empty (skipped).
    Assert.Contains("MERGE", bundle.Bootstrap)
    Assert.True(System.String.IsNullOrWhiteSpace bundle.StaticSeeds)

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
            Physical = mkTableId "dbo" "OSUSR_TEST_CUSTOMER" }
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
    let standalone = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
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
            Physical = mkTableId "dbo" "OSUSR_TEST_CUSTOMER" }
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
                Values = StaticRow.presentValues [ mkName "Id", "9"; mkName "Code", "ZZ"; mkName "Label", "Other" ] } ] }
    let result =
        DataEmissionComposer.composeFull
            (policyWith AllRemaining) catalog Profile.empty migration UserRemapContext.empty
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
                Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "Code", "US"; mkName "Label", "United States" ] } ] }
    // AllExceptStatic skips Static, so Migration alone owns the kind.
    let result =
        DataEmissionComposer.composeFull
            (policyWith AllExceptStatic) catalog Profile.empty migration UserRemapContext.empty
    match result with
    | Ok artifact ->
        let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
        Assert.Equal (1, List.length script.Phase1Merges)
    | Error e -> Assert.Fail (sprintf "expected partition success, got %A" e)

// ---------------------------------------------------------------------------
// Slice ι — composeRendered + multi-kind cycle global-phase reification.
//
// Per the slice-δ improvement surface item #2: per-kind `Rendered` is
// only deploy-correct for self-FK cycles (1-node SCCs); multi-kind
// cycles need ALL Phase-1 MERGEs (across all kinds in topo order)
// before ANY Phase-2 UPDATE. `composeRendered` produces the global
// cycle-correct deploy text by walking the artifact under the
// hoisted topological order.
// ---------------------------------------------------------------------------

let private normWsCmp (s: string) : string =
    System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim()

[<Fact>]
let ``Slice ι: composeRendered produces non-empty text for static-only catalog`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let result = DataEmissionComposer.composeRendered (policyWith AllRemaining) catalog Profile.empty
    match result with
    | Ok text ->
        Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY] AS [Target]", normWsCmp text)
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e)

[<Fact>]
let ``Slice ι: composeRendered emits Phase-1 (MERGE) of every kind before Phase-2 (UPDATE) of any kind`` () =
    // Build a 2-kind catalog where each kind has populated Static
    // rows AND a self-FK cycle (so Phase-2 UPDATEs surface for both).
    // The global ordering invariant: NO `UPDATE` text appears before
    // ALL `MERGE INTO` texts have been emitted.
    let mkSelfCycleKind (name: string) (table: string) (rowId: string) : Kind =
        let kindKey = mkKey ["TestModule"; name]
        let idKey = mkKey ["TestModule"; name; "Id"]
        let parentKey = mkKey ["TestModule"; name; "ParentId"]
        let refKey = mkKey ["TestModule"; name; "RefSelf"]
        let row =
            { Identifier = mkKey ["TestModule"; name; "Row"; rowId]
              Values =
                  StaticRow.presentValues
                      [ mkName "Id",       rowId
                        mkName "ParentId", rowId ] }
        {
            SsKey    = kindKey
            Name     = mkName name
            Origin   = Native
            Modality = [ Static [ row ] ]
            Physical = mkTableId "dbo" table
            Attributes =
                [
                    { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                    { Attribute.create parentKey (mkName "ParentId") Integer with Column = ColumnRealization.create ("PARENTID") (true) |> Result.value }
                ]
            References =
                [ Reference.create refKey (mkName "RefSelf") parentKey kindKey ]
            Indexes    = []
            Description = None
            IsActive = true
            Triggers = []
            ColumnChecks = []
            ExtendedProperties = []
            }
    let alpha = mkSelfCycleKind "Alpha" "OSUSR_ALPHA" "1"
    let beta = mkSelfCycleKind "Beta" "OSUSR_BETA" "1"
    let catalog = mkCatalog [ alpha; beta ]
    let text =
        DataEmissionComposer.composeRendered (policyWith AllRemaining) catalog Profile.empty
        |> mustOkEmit
    let n = normWsCmp text
    // Both kinds should produce a MERGE and an UPDATE (each has a
    // self-FK cycle on a nullable column → deferred → Phase-2 UPDATE).
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_ALPHA]", n)
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_BETA]", n)
    Assert.Contains ("UPDATE [dbo].[OSUSR_ALPHA]", n)
    Assert.Contains ("UPDATE [dbo].[OSUSR_BETA]", n)
    // The load-bearing global-ordering invariant: every MERGE-INTO
    // index sits before every UPDATE index.
    let mergeIdx1 = n.IndexOf "MERGE INTO [dbo].[OSUSR_ALPHA]"
    let mergeIdx2 = n.IndexOf "MERGE INTO [dbo].[OSUSR_BETA]"
    let updateIdx1 = n.IndexOf "UPDATE [dbo].[OSUSR_ALPHA]"
    let updateIdx2 = n.IndexOf "UPDATE [dbo].[OSUSR_BETA]"
    let lastMerge = max mergeIdx1 mergeIdx2
    let firstUpdate = min updateIdx1 updateIdx2
    Assert.True (lastMerge < firstUpdate,
                 sprintf "expected all MERGEs before any UPDATE, but lastMerge=%d firstUpdate=%d" lastMerge firstUpdate)

[<Fact>]
let ``Slice ι: composeRendered for acyclic catalog has no Phase-2 UPDATE text`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let text =
        DataEmissionComposer.composeRendered (policyWith AllRemaining) catalog Profile.empty
        |> mustOkEmit
    let n = normWsCmp text
    // Country has no FKs → no cycle → no deferred FKs → no Phase-2.
    Assert.Contains ("MERGE INTO", n)
    Assert.DoesNotContain ("UPDATE [dbo].[OSUSR_TEST_COUNTRY]", n)

[<Fact>]
let ``Slice ι: T1 byte-determinism holds for composeRendered`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let policy = policyWith AllRemaining
    let r1 = DataEmissionComposer.composeRendered policy catalog Profile.empty |> mustOkEmit
    let r2 = DataEmissionComposer.composeRendered policy catalog Profile.empty |> mustOkEmit
    Assert.Equal<string> (r1, r2)

// ---------------------------------------------------------------------------
// Slice κ — typed DataInsertRow.Values (pillar 1 strengthening).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice κ: DataInsertRow.Values is typed Map<Name, SqlLiteral>`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let row = List.head script.Phase1Merges
    // Static-IR string "US" became typed `TextLit "US"` per
    // SqlLiteral.ofRaw(Text, "US").
    let codeValue = Map.find (mkName "Code") row.Values
    match codeValue with
    | TextLit raw -> Assert.Equal<string> ("US", raw)
    | other       -> Assert.Fail (sprintf "expected TextLit, got %A" other)

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

// ---------------------------------------------------------------------------
// Chapter 5.13 slice data-emission-registry — cross-emitter global
// Phase-1-then-Phase-2 ordering. Matrix row 160 cash-out: the slice ι
// property previously tested ordering across kinds within a single
// emitter (StaticSeedsEmitter). The cross-emitter case is structurally
// equivalent (composeRenderedFull walks the unioned artifact in topo
// order then concatenates all Phase-1 texts before all Phase-2 texts),
// but the property test surface was missing.
// ---------------------------------------------------------------------------

/// Build a kind WITHOUT static modality but with a self-FK cycle on a
/// nullable column. The fixture is suitable for Migration to populate
/// — the kind has no Static.Modality rows, so StaticSeedsEmitter skips
/// it under AllRemaining; MigrationDependencyContext rows for the kind
/// populate it via the migration sibling.
let private mkLegacyKindForMigration (name: string) (table: string) : Kind =
    let kindKey = mkKey ["TestModule"; name]
    let idKey = mkKey ["TestModule"; name; "Id"]
    let parentKey = mkKey ["TestModule"; name; "ParentId"]
    let refKey = mkKey ["TestModule"; name; "RefSelf"]
    {
        SsKey    = kindKey
        Name     = mkName name
        Origin   = Native
        Modality = []   // NOT Static — Migration populates instead
        Physical = mkTableId "dbo" table
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create parentKey (mkName "ParentId") Integer with Column = ColumnRealization.create ("PARENTID") (true) |> Result.value }
            ]
        References =
            [ Reference.create refKey (mkName "RefSelf") parentKey kindKey ]
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

[<Fact>]
let ``5.13.data-emission-registry: composeRenderedFull global-Phase1-then-Phase2 holds across emitters (matrix row 160)`` () =
    // Cross-emitter scenario:
    //   - Country: Static modality → StaticSeedsEmitter populates
    //   - LegacyOrder: no static modality, but migration context has
    //     a row for it → MigrationDependenciesEmitter populates.
    //     The self-FK on LegacyOrder.ParentId triggers Phase-2
    //     UPDATEs from the migration emitter.
    //   - The partition assertion holds (each kind in exactly one
    //     emitter's coverage).
    //
    // Property: ALL Phase-1 MERGEs (from BOTH emitters, in topo order)
    // appear BEFORE any Phase-2 UPDATE (from either emitter). This
    // is the load-bearing claim for matrix row 160 — cross-emitter
    // global phase ordering IS reified at composeRenderedFull.
    let country = mkCountryKind ()
    let legacy = mkLegacyKindForMigration "LegacyOrder" "OSUSR_TEST_LEGACY_ORDER"
    let catalog = mkCatalog [ country; legacy ]
    let migration : MigrationDependencyContext =
        { Rows =
            [ { KindKey = legacy.SsKey
                Identifier = mkKey ["TestModule"; "LegacyOrder"; "Row"; "1"]
                Values =
                    StaticRow.presentValues
                        [ mkName "Id",       "1"
                          mkName "ParentId", "1" ] } ] }
    let text =
        DataEmissionComposer.composeRenderedFull
            (policyWith AllRemaining)
            catalog
            Profile.empty
            migration
            UserRemapContext.empty
        |> mustOkEmit
    let n = normWsCmp text
    // Both emitters should produce Phase-1 output.
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY]", n)
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_LEGACY_ORDER]", n)
    // Migration's LegacyOrder kind has a self-FK on nullable
    // ParentId → Phase-2 UPDATE surfaces from the Migration emitter.
    Assert.Contains ("UPDATE [dbo].[OSUSR_TEST_LEGACY_ORDER]", n)
    // The load-bearing global-ordering invariant: every MERGE-INTO
    // index sits before every UPDATE index, ACROSS both emitters.
    let mergeIdxCountry = n.IndexOf "MERGE INTO [dbo].[OSUSR_TEST_COUNTRY]"
    let mergeIdxLegacy  = n.IndexOf "MERGE INTO [dbo].[OSUSR_TEST_LEGACY_ORDER]"
    let updateIdxLegacy = n.IndexOf "UPDATE [dbo].[OSUSR_TEST_LEGACY_ORDER]"
    let lastMerge = max mergeIdxCountry mergeIdxLegacy
    Assert.True(
        lastMerge < updateIdxLegacy,
        sprintf
            "cross-emitter global ordering violated: lastMerge=%d updateIdxLegacy=%d"
            lastMerge
            updateIdxLegacy)

[<Fact>]
let ``5.13.data-emission-registry: cross-emitter coverage holds the partition invariant (no overlap)`` () =
    // The cross-emitter case must not trigger
    // OverlappingEmitterCoverage — Country is in Static, LegacyOrder
    // is in Migration; disjoint coverage. This protects the
    // global-ordering claim from being trivially satisfied by an
    // overlap-error path.
    let country = mkCountryKind ()
    let legacy = mkLegacyKindForMigration "LegacyOrder" "OSUSR_TEST_LEGACY_ORDER"
    let catalog = mkCatalog [ country; legacy ]
    let migration : MigrationDependencyContext =
        { Rows =
            [ { KindKey = legacy.SsKey
                Identifier = mkKey ["TestModule"; "LegacyOrder"; "Row"; "1"]
                Values =
                    StaticRow.presentValues
                        [ mkName "Id",       "1"
                          mkName "ParentId", "1" ] } ] }
    let result =
        DataEmissionComposer.composeFull
            (policyWith AllRemaining)
            catalog
            Profile.empty
            migration
            UserRemapContext.empty
    match result with
    | Ok artifact ->
        let map = ArtifactByKind.toMap artifact
        let countryScript = Map.find country.SsKey map
        let legacyScript  = Map.find legacy.SsKey map
        // Country has 2 static rows (US + CA) → StaticSeedsEmitter
        // produces 2 MERGEs; LegacyOrder has 1 migration row →
        // MigrationDependenciesEmitter produces 1 MERGE.
        Assert.Equal(2, List.length countryScript.Phase1Merges)
        Assert.Equal(1, List.length legacyScript.Phase1Merges)
        // Legacy has self-FK on ParentId → migration emitter
        // produces a Phase-2 UPDATE.
        Assert.Equal(1, List.length legacyScript.Phase2Updates)
        Assert.Equal(0, List.length countryScript.Phase2Updates)
    | Error e -> Assert.Fail (sprintf "expected partition success, got %A" e)

[<Fact>]
let ``WP6 step 3: composeRenderedBundle splits the lanes (StaticSeeds vs MigrationData) from one dispatch`` () =
    // Two active lanes ⇒ the per-lane split carries information the fused
    // seed doesn't trivially convey. StaticSeeds holds Country only;
    // MigrationData holds LegacyOrder (MERGE + Phase-2 UPDATE) only.
    let country = mkCountryKind ()
    let legacy = mkLegacyKindForMigration "LegacyOrder" "OSUSR_TEST_LEGACY_ORDER"
    let catalog = mkCatalog [ country; legacy ]
    let migration : MigrationDependencyContext =
        { Rows =
            [ { KindKey = legacy.SsKey
                Identifier = mkKey ["TestModule"; "LegacyOrder"; "Row"; "1"]
                Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "ParentId", "1" ] } ] }
    let bundle =
        DataEmissionComposer.composeRenderedBundleFull
            (policyWith AllRemaining) catalog Profile.empty migration UserRemapContext.empty
        |> mustOkEmit
    let staticN = normWsCmp bundle.StaticSeeds
    let migN = normWsCmp bundle.MigrationData
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY]", staticN)
    Assert.DoesNotContain ("OSUSR_TEST_LEGACY_ORDER", staticN)
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_LEGACY_ORDER]", migN)
    Assert.Contains ("UPDATE [dbo].[OSUSR_TEST_LEGACY_ORDER]", migN)
    Assert.DoesNotContain ("OSUSR_TEST_COUNTRY", migN)
    Assert.True (System.String.IsNullOrWhiteSpace bundle.Bootstrap)
    // Two lanes carry content ⇒ per-lane files are informative.
    Assert.Equal (2, DataEmissionComposer.RenderedDataBundle.nonEmptyLaneCount bundle)
    let files = DataEmissionComposer.RenderedDataBundle.perLaneFiles bundle
    Assert.True (Map.containsKey "Data/StaticSeeds.sql" files)
    Assert.True (Map.containsKey "Data/MigrationData.sql" files)
    Assert.False (Map.containsKey "Data/Bootstrap.sql" files)
    // The fused surface (`composeRenderedFull`, the on-demand single render —
    // the bundle deliberately no longer materializes it) interleaves BOTH
    // lanes' content in topo order: every lane's statements appear in it.
    let fused =
        DataEmissionComposer.composeRenderedFull
            (policyWith AllRemaining) catalog Profile.empty migration UserRemapContext.empty
        |> mustOkEmit
        |> normWsCmp
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY]", fused)
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_LEGACY_ORDER]", fused)
    Assert.Contains ("UPDATE [dbo].[OSUSR_TEST_LEGACY_ORDER]", fused)

[<Fact>]
let ``WP6 step 3: a single active lane makes the fused seed equal that lane (nonEmptyLaneCount = 1)`` () =
    // Only the static lane has content ⇒ the fused seed IS the static lane,
    // so the per-lane split adds nothing and the pipeline omits it.
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let bundle =
        DataEmissionComposer.composeRenderedBundle (policyWith AllRemaining) catalog Profile.empty
        |> mustOkEmit
    Assert.Equal (1, DataEmissionComposer.RenderedDataBundle.nonEmptyLaneCount bundle)
    // Cross-path law: with a single active lane there is nothing to
    // interleave, so the on-demand fused render (`composeRenderedFull`)
    // is byte-identical to that lane's bundle rendering.
    let fused =
        DataEmissionComposer.composeRenderedFull
            (policyWith AllRemaining) catalog Profile.empty
            MigrationDependencyContext.empty UserRemapContext.empty
        |> mustOkEmit
    Assert.Equal<string> (fused, bundle.StaticSeeds)
    Assert.True (System.String.IsNullOrWhiteSpace bundle.MigrationData)
    Assert.True (System.String.IsNullOrWhiteSpace bundle.Bootstrap)

// ---------------------------------------------------------------------------
// Card P2 — the leveled plan is a faithful PARTITION of the fused seed.
//
// The wiring constraint the P2 card pre-derived: the production load leg
// (`Compose.loadLeveledSeedAndRecord` → `Deploy.executeLeveledSeed`) must
// stay faithful to the published `Data/seed.sql` — a partition of the same
// rendered per-kind strings, never a re-composition that can diverge. Both
// `composeRenderedFull` and `composeRenderedLeveled` derive from the SAME
// `dispatchSiblings + unionSiblings` artifact; these witnesses pin the
// claim at the execution plane: the GO-batch SEGMENT multiset the leveled
// plan dispatches equals the segment multiset the fused string deploys
// (within-level order is SsKey-sorted, so order is a permutation — the
// multiset, all-Phase-1-before-Phase-2, and level precedence are the laws).
// ---------------------------------------------------------------------------

/// A static kind with `rowIds` rows and (optionally) a cross-kind FK to
/// `target` on a nullable TARGETID column — the level-graph fixture.
let private mkStaticLevelKind (name: string) (table: string) (target: Kind option) (rowIds: string list) : Kind =
    let kindKey = mkKey ["TestModule"; name]
    let idKey = mkKey ["TestModule"; name; "Id"]
    let targetIdKey = mkKey ["TestModule"; name; "TargetId"]
    let row (rid: string) =
        let baseValues = [ mkName "Id", rid ]
        let values =
            match target with
            | Some _ -> (mkName "TargetId", rid) :: baseValues
            | None   -> baseValues
        { Identifier = mkKey ["TestModule"; name; "Row"; rid]
          Values = StaticRow.presentValues values }
    let fkAttrs =
        match target with
        | Some _ ->
            [ { Attribute.create targetIdKey (mkName "TargetId") Integer with
                  Column = ColumnRealization.create ("TARGETID") (true) |> Result.value } ]
        | None -> []
    let references =
        match target with
        | Some t ->
            [ Reference.create (mkKey ["TestModule"; name; "RefTarget"]) (mkName "RefTarget") targetIdKey t.SsKey ]
        | None -> []
    {
        SsKey    = kindKey
        Name     = mkName name
        Origin   = Native
        Modality = [ Static (rowIds |> List.map row) ]
        Physical = mkTableId "dbo" table
        Attributes =
            { Attribute.create idKey (mkName "Id") Integer with
                Column = ColumnRealization.create ("ID") (false) |> Result.value
                IsPrimaryKey = true
                IsMandatory = true }
            :: fkAttrs
        References = references
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// A static kind with a self-FK cycle on nullable PARENTID — produces a
/// Phase-2 UPDATE, so the partition law covers both phases.
let private mkStaticSelfCycleKind (name: string) (table: string) : Kind =
    let kindKey = mkKey ["TestModule"; name]
    let idKey = mkKey ["TestModule"; name; "Id"]
    let parentKey = mkKey ["TestModule"; name; "ParentId"]
    let row =
        { Identifier = mkKey ["TestModule"; name; "Row"; "1"]
          Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "ParentId", "1" ] }
    {
        SsKey    = kindKey
        Name     = mkName name
        Origin   = Native
        Modality = [ Static [ row ] ]
        Physical = mkTableId "dbo" table
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create parentKey (mkName "ParentId") Integer with Column = ColumnRealization.create ("PARENTID") (true) |> Result.value }
            ]
        References =
            [ Reference.create (mkKey ["TestModule"; name; "RefSelf"]) (mkName "RefSelf") parentKey kindKey ]
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// The ACYCLIC level-graph catalog: Root ← Mid ← Leaf (a 3-deep FK
/// chain) and Indep (no FKs — shares Root's level). `Mode = Topological`
/// ⇒ the mint licenses real multi-member levels.
let private mkAcyclicLeveledCatalog () : Kind list =
    let root  = mkStaticLevelKind "LvlRoot" "OSUSR_TEST_LVL_ROOT" None [ "1"; "2" ]
    let mid   = mkStaticLevelKind "LvlMid"  "OSUSR_TEST_LVL_MID"  (Some root) [ "1"; "2" ]
    let leaf  = mkStaticLevelKind "LvlLeaf" "OSUSR_TEST_LVL_LEAF" (Some mid)  [ "1" ]
    let indep = mkStaticLevelKind "LvlIndep" "OSUSR_TEST_LVL_INDEP" None [ "1" ]
    [ root; mid; leaf; indep ]

/// A pair of static kinds forming a genuinely UNRESOLVABLE cycle: each
/// holds a NULLABLE + ON DELETE RESTRICT FK to the other — strength
/// `Other` on both edges (nullable + Restrict is never breakable), so the
/// v5 weak-feedback resolver refuses by name. Both FK columns are still
/// NULLABLE + same-cycle, so both defer to Phase 2 (the partition law
/// covers both phases). Until 2026-07-07 the fixture used two Weak
/// (nullable + NoAction) edges — the v5 resolver now RESOLVES that shape,
/// so the unresolvable premise moves to the Restrict strength.
let private mkStaticMutualCycleKind (name: string) (table: string) (otherName: string) : Kind =
    let kindKey = mkKey ["TestModule"; name]
    let idKey = mkKey ["TestModule"; name; "Id"]
    let peerKey = mkKey ["TestModule"; name; "PeerId"]
    let row =
        { Identifier = mkKey ["TestModule"; name; "Row"; "1"]
          Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "PeerId", "1" ] }
    {
        SsKey    = kindKey
        Name     = mkName name
        Origin   = Native
        Modality = [ Static [ row ] ]
        Physical = mkTableId "dbo" table
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create peerKey (mkName "PeerId") Integer with Column = ColumnRealization.create ("PEERID") (true) |> Result.value }
            ]
        References =
            [ { Reference.create (mkKey ["TestModule"; name; "RefPeer"]) (mkName "RefPeer") peerKey (mkKey ["TestModule"; otherName]) with OnDelete = Restrict } ]
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// The same graph PLUS an unresolvable cycle. The unresolved SCC puts the
/// order into `Mode = PartialTopological` (v6, 2026-07-18 — the acyclic
/// majority keeps dependency position; before v6 the WHOLE order degraded
/// to Alphabetical) — the mint's degenerate arm either way (singleton
/// groups; no parallelism licensed) and the Phase-2 surface (the deferred
/// cycle FKs re-point by UPDATE).
///
/// Until 2026-07-06 the fixture was ONE nullable self-FK kind — the
/// self-loop resolver rule (the peer-canary finding) now RESOLVES that
/// shape, so the unresolvable premise needs the honest two-weak-edge
/// 2-cycle instead. The self-FK kind stays in the partition-law sweep via
/// `mkStaticSelfCycleKind` where its Phase-2 shape is what matters.
let private mkCycleBearingCatalog () : Kind list =
    mkAcyclicLeveledCatalog ()
    @ [ mkStaticMutualCycleKind "LvlCycA" "OSUSR_TEST_LVL_CYCA" "LvlCycB"
        mkStaticMutualCycleKind "LvlCycB" "OSUSR_TEST_LVL_CYCB" "LvlCycA" ]

let private composeBoth (kinds: Kind list) =
    let catalog = mkCatalog kinds
    let policy = policyWith AllRemaining
    let fused =
        DataEmissionComposer.composeRenderedFull
            policy catalog Profile.empty
            MigrationDependencyContext.empty UserRemapContext.empty
        |> mustOkEmit
    let leveled =
        DataEmissionComposer.composeRenderedLeveled
            policy catalog Profile.empty
            MigrationDependencyContext.empty UserRemapContext.empty
        |> mustOkEmit
    fused, leveled

let private segments (sql: string) =
    Projection.Targets.SSDT.BatchSplitter.splitOnGoLineFold sql |> Array.toList

let private leveledSegmentsOf (leveled: DataEmissionComposer.LeveledDeploymentText) =
    (leveled.Phase1Levels @ leveled.Phase2Levels)
    |> List.collect ParallelSafe.members
    |> List.collect segments

[<Fact>]
let ``P2: composeRenderedLeveled PARTITIONS composeRenderedFull — segment multiset equality (faithful split, never a re-render)`` () =
    // The law holds in EVERY mode: the Topological catalog (real levels),
    // the unresolvable-cycle one (PartialTopological, singleton groups),
    // and the resolved-self-loop one (Topological since 2026-07-06 — the
    // self-loop rule — with a Phase-2 surface).
    for kinds in
        [ mkAcyclicLeveledCatalog ()
          mkCycleBearingCatalog ()
          mkAcyclicLeveledCatalog () @ [ mkStaticSelfCycleKind "LvlCyc" "OSUSR_TEST_LVL_CYC" ] ] do
        let fused, leveled = composeBoth kinds
        let fusedSegments = segments fused |> List.sort
        let leveledSegments = leveledSegmentsOf leveled |> List.sort
        Assert.NotEmpty fusedSegments
        Assert.Equal<string list>(fusedSegments, leveledSegments)

[<Fact>]
let ``P2: the leveled plan deploys FK parents at earlier Phase-1 levels; FK-independent kinds share a level`` () =
    let _, leveled = composeBoth (mkAcyclicLeveledCatalog ())
    let levelIndexOf (table: string) =
        leveled.Phase1Levels
        |> List.findIndex (fun lvl ->
            ParallelSafe.members lvl |> List.exists (fun s -> s.Contains table))
    let root = levelIndexOf "OSUSR_TEST_LVL_ROOT"
    let mid  = levelIndexOf "OSUSR_TEST_LVL_MID"
    let leaf = levelIndexOf "OSUSR_TEST_LVL_LEAF"
    Assert.True(root < mid && mid < leaf,
                sprintf "FK chain must descend levels: root=%d mid=%d leaf=%d" root mid leaf)
    // Indep has no FK edges — it shares the chain root's level, and that
    // group genuinely carries more than one member (the parallel prize).
    Assert.Equal(root, levelIndexOf "OSUSR_TEST_LVL_INDEP")
    let rootMembers = ParallelSafe.members (List.item root leveled.Phase1Levels)
    Assert.True(List.length rootMembers >= 2, "the root level must carry ≥2 concurrent members")
    // Acyclic ⇒ no deferred FKs ⇒ no Phase-2 levels.
    Assert.Empty leveled.Phase2Levels

[<Fact>]
let ``P2: an unresolved cycle anywhere degrades the plan to singleton groups — parallelism is never licensed on an unproven order`` () =
    // One UNRESOLVABLE cycle (the static mutual pair) puts the catalog
    // into Mode = PartialTopological (v6); the members of the cycle carry
    // no intra-cycle precedence proof, so the mint refuses multi-member
    // groups — the leveled deploy degrades to exactly the fused
    // sequential order, never to unproven concurrency. (The P2-wire
    // finding: before the mode guard, this catalog collapsed the real
    // Root←Mid←Leaf FK chain into ONE concurrent group.)
    let _, leveled = composeBoth (mkCycleBearingCatalog ())
    for level in leveled.Phase1Levels @ leveled.Phase2Levels do
        Assert.Equal(1, ParallelSafe.members level |> List.length)
    // The cycle pair's deferred FKs still land in Phase 2.
    Assert.NotEmpty leveled.Phase2Levels

[<Fact>]
let ``P2: a nullable self-FK no longer degrades the order — the self-loop resolves, levels stay licensed, the deferral stays`` () =
    // The 2026-07-06 self-loop rule (the peer-canary finding): the common
    // OutSystems shape (Category.Parent / Employee.Manager) must not cost
    // the estate its topological order — the self-edge breaks for
    // ordering, the resolved SCC keeps the kind in `Cycles`, and the
    // nullable self-FK still re-points by Phase-2 UPDATE.
    let _, leveled =
        composeBoth (mkAcyclicLeveledCatalog () @ [ mkStaticSelfCycleKind "LvlCyc" "OSUSR_TEST_LVL_CYC" ])
    // The acyclic chain's root level still carries ≥2 concurrent members —
    // parallelism is NOT surrendered to the self-loop.
    let rootLevel =
        leveled.Phase1Levels
        |> List.find (fun lvl ->
            ParallelSafe.members lvl |> List.exists (fun s -> s.Contains "OSUSR_TEST_LVL_ROOT"))
    Assert.True(ParallelSafe.members rootLevel |> List.length >= 2,
                "the self-loop must not degrade the acyclic chain's parallel levels")
    // The self-FK still defers: Phase 2 re-points it.
    Assert.NotEmpty leveled.Phase2Levels

[<Fact>]
let ``P2: LeveledDeploymentText.isEmpty parity — no seed statements ⇔ empty plan ⇔ whitespace fused text`` () =
    // A static kind with ZERO rows projects no seed statements: the fused
    // form is whitespace (the load leg's IsNullOrWhiteSpace gate) and the
    // leveled form is the empty plan (the load leg's isEmpty gate).
    let emptyKind = mkStaticLevelKind "LvlEmpty" "OSUSR_TEST_LVL_EMPTY" None []
    let fused, leveled = composeBoth [ emptyKind ]
    Assert.True(System.String.IsNullOrWhiteSpace fused)
    Assert.True(DataEmissionComposer.LeveledDeploymentText.isEmpty leveled)
    Assert.True(DataEmissionComposer.LeveledDeploymentText.isEmpty DataEmissionComposer.LeveledDeploymentText.empty)

// ---------------------------------------------------------------------------
// P2 production wiring — the pipelined Bootstrap arm's identity laws.
//
// The pipelined publish schedule (Pipeline.runWithConfig's gated arm) rests
// on three structural facts, each pinned here at the pure level so the
// docker equivalence test is confirmation rather than the only witness:
//   1. `chainStepsSplitWithPins` partitions the registry chain exactly at
//      `TopologicalOrderPass` and `prefix @ suffix = whole` (the two-phase
//      execution cannot drift from the registry).
//   2. The prefix is PROFILE-INVARIANT and the suffix is CATALOG-PRESERVING
//      (+ topology-preserving): the drain-time render targets computed from
//      the prefix-with-`Profile.empty` equal what the full chain's final
//      state carries under the real profile.
//   3. `BootstrapLane.Prerendered` scripts produced by the drain-time
//      projection (`DataLoadPlan.loadFor` + `StaticSeedsEmitter.renderLoad`
//      under the delete-scope-suppressed lane posture) compose to the SAME
//      bundle as the `Rows` arm over the same rows.
// ---------------------------------------------------------------------------

let private mkRegularCustomerKind () : Kind =
    let country = mkCountryKind ()
    { country with
        SsKey    = mkKey ["TestModule"; "Customer"]
        Name     = mkName "Customer"
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_TEST_CUSTOMER"
        Attributes =
            country.Attributes
            |> List.map (fun a ->
                { a with SsKey = mkKey ["TestModule"; "Customer"; Name.value a.Name] }) }

[<Fact>]
let ``pipelined split: prefix ++ suffix = chainStepsWithPins and the prefix ends at topologicalOrder`` () =
    let prefix, suffix = RegisteredTransforms.chainStepsSplitWithPins Set.empty
    let whole = RegisteredTransforms.chainStepsWithPins Set.empty
    let names (steps: ChainStep list) = steps |> List.map (fun s -> s.Metadata.Name)
    Assert.Equal<string list>(names whole, names prefix @ names suffix)
    Assert.Equal("topologicalOrder", (List.last prefix).Metadata.Name)
    Assert.NotEmpty suffix

[<Fact>]
let ``pipelined split: the prefix is profile-INVARIANT and the suffix preserves catalog + topology`` () =
    let catalog = mkCatalog [ mkCountryKind (); mkRegularCustomerKind () ]
    let policy = policyWith AllRemaining
    let probe =
        ProbeStatus.create System.DateTimeOffset.UnixEpoch 2L Succeeded |> mustOk
    let populatedProfile =
        { Profile.empty with
            Columns =
                [ { AttributeKey = mkKey ["TestModule"; "Country"; "Code"]
                    RowCount = 2L
                    NullCount = 1L
                    MaxObservedLength = None
                    NullCountProbeStatus = probe } ] }
    let prefix, _suffix = RegisteredTransforms.chainStepsSplitWithPins Set.empty
    let composePrefix (profile: Profile) : ComposeState =
        let adapters = prefix |> List.map (ChainStep.build policy profile)
        PassChainAdapter.compose adapters (ComposeState.initial catalog)
        |> LineageDiagnostics.payload
    // Profile-invariance: the prefix's catalog + topology cannot depend on
    // the profile (their Build closures ignore it by construction; this
    // pins the property behaviorally so a future profile-consuming step
    // added before the topo pass trips the pipelined arm's law).
    let viaEmpty = composePrefix Profile.empty
    let viaPopulated = composePrefix populatedProfile
    Assert.Equal(viaEmpty.Catalog, viaPopulated.Catalog)
    Assert.Equal(viaEmpty.TopologicalOrder, viaPopulated.TopologicalOrder)
    // Suffix catalog-preservation: the FULL chain under the populated
    // profile lands on the same catalog + topology the prefix computed —
    // every post-topo step is a decision pass (writes ComposeState
    // decisions, never replaces the catalog), so the drain-time render
    // targets equal the compose-time ones.
    let wholeState =
        PassChainAdapter.compose
            (RegisteredTransforms.allChainStepsFor policy populatedProfile)
            (ComposeState.initial catalog)
        |> LineageDiagnostics.payload
    Assert.Equal(viaEmpty.Catalog, wholeState.Catalog)
    Assert.Equal(viaEmpty.TopologicalOrder, wholeState.TopologicalOrder)

[<Fact>]
let ``BootstrapLane: Prerendered drain-time scripts compose the SAME bundle as the Rows arm`` () =
    let country = mkCountryKind ()
    let customer = mkRegularCustomerKind ()
    let catalog = mkCatalog [ country; customer ]
    let policy = policyWith AllRemaining
    let topo =
        (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle catalog).Value
    let rows : Map<SsKey, StaticRow list> =
        Map.ofList
            [ customer.SsKey,
              [ { Identifier = mkKey ["TestModule"; "Customer"; "Row"; "1"]
                  Values = customer.Attributes |> List.map (fun a -> a.Name, "1") |> StaticRow.presentValues } ] ]
    // The drain-time projection: the same `DataLoadPlan.loadFor` core the
    // batch build folds + the same `renderLoad`, under the bootstrap
    // lane's delete-scope-suppressed posture (what
    // `Hydration.collectBootstrapRenderedUsing` runs per kind at drain).
    let cycleMembers = TopologicalOrder.deferralScopes topo
    let opts =
        DataEmitOptions.withDeleteScope None
            (DataEmitOptions.ofEmissionPolicy policy.Emission)
    let prerendered =
        rows
        |> Map.map (fun key kindRows ->
            let kind = Catalog.kindIndex catalog |> Map.find key
            let load, _ = DataLoadPlan.loadFor cycleMembers SurrogateRemapContext.empty kind kindRows
            StaticSeedsEmitter.renderLoad opts Profile.empty.CdcAwareness kind load)
    let composeWith (lane: DataEmissionComposer.BootstrapLane) =
        DataEmissionComposer.composeRenderedBundleWithBootstrapLaneUsing
            topo policy catalog Profile.empty
            MigrationDependencyContext.empty lane UserRemapContext.empty
        |> mustOkEmit
    let viaRows = composeWith (DataEmissionComposer.BootstrapLane.Rows rows)
    let viaPrerendered = composeWith (DataEmissionComposer.BootstrapLane.Prerendered prerendered)
    // Non-vacuous: the bootstrap lane really carries the customer MERGE.
    Assert.Contains("MERGE", viaRows.Bootstrap)
    Assert.Equal<string>(viaRows.Bootstrap, viaPrerendered.Bootstrap)
    Assert.Equal<string>(viaRows.StaticSeeds, viaPrerendered.StaticSeeds)
    Assert.Equal<string>(viaRows.MigrationData, viaPrerendered.MigrationData)

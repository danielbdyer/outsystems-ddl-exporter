module Projection.Tests.StaticSeedsEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Targets.Data
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice α — StaticSeedsEmitter v0 (V1-shape MERGE).
//
// Per `CHAPTER_4_1_B_OPEN.md` strategic frame axis 8: idempotence + topo-
// order + (slice γ) CDC-silence. This test file covers the slice-α
// surface: MERGE shape parity with V1 + idempotence under repeat
// invocation + T11 keyset coverage. CDC-silence (the chapter signature)
// lands at slice γ canary.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

/// Static-entity fixture with explicit Id values (so VALUES rows are
/// deployable). Three rows × three columns (Id INT PK + Code TEXT +
/// Label TEXT).
let private mkCountryKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Country"]
    let idKey = mkKey ["TestModule"; "Country"; "Id"]
    let codeKey = mkKey ["TestModule"; "Country"; "Code"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    let row code label =
        { Identifier = mkKey ["TestModule"; "Country"; "Row"; code]
          Values =
              StaticRow.presentValues
                  [ mkName "Id",    code  // simulate Id-as-Code for test simplicity
                    mkName "Code",  code
                    mkName "Label", label ] }
    {
        SsKey    = kindKey
        Name     = mkName "Country"
        Origin   = Native
        Modality = [ Static [ row "US" "United States"
                              row "CA" "Canada" ] ]
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

/// Non-static kind (no `Modality.Static` mark); should produce a no-op
/// DataInsertScript per T11 strict-equality keyset.
let private mkRegularKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Customer"]
    let idKey = mkKey ["TestModule"; "Customer"; "Id"]
    let nameKey = mkKey ["TestModule"; "Customer"; "Name"]
    {
        SsKey    = kindKey
        Name     = mkName "Customer"
        Origin   = Native
        Modality = []  // not static
        Physical = mkTableId "dbo" "OSUSR_TEST_CUSTOMER"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true; IsIdentity = true }
                { Attribute.create nameKey (mkName "Name") Text with Column = ColumnRealization.create ("NAME") (false) |> Result.value; IsMandatory = true }
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

/// Whitespace-normalize a rendered SQL string so substring assertions
/// match across formatter variations. ScriptDom's `Sql160ScriptGenerator`
/// emits canonical formatting (line breaks before `AS`, `SET`, etc.) that
/// differs from V1's hand-rolled spacing; the structural-content
/// property (every load-bearing token in expected order) is what these
/// tests check, not byte-exact whitespace.
let private normWs (s: string) : string =
    System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim()

let private mustOkEmit (r: Result<'a, EmitError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e); Unchecked.defaultof<_>

/// DECISIONS 2026-07-19 — the static LANE (`StaticSeedsEmitter.emit` /
/// `emitWithTopo`) now renders cyclic nullable FKs INLINE (single MERGE, no
/// Phase-2), matching V1's `StaticSeedSqlBuilder`. The two-phase deferral
/// MACHINERY it shares with the bootstrap / migration / transfer lanes is
/// unchanged: those lanes reach the renderer through `emitFromPlan` over a plan
/// whose `DeferredFkColumns` are populated (only `emitWithTopo` clears them).
/// The δ / v7-slice-5 tests below pin THAT machinery through `emitFromPlan` —
/// the exact path bootstrap + transfer take — while the static-lane inline
/// behavior is pinned by its own test (`static lane renders a cyclic nullable
/// FK inline`).
let private emitDeferring (catalog: Catalog) : ArtifactByKind<DataInsertScript> =
    let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle catalog).Value
    let rawRows =
        Catalog.allKinds catalog
        |> List.map (fun k -> k.SsKey, Kind.staticPopulations k)
        |> Map.ofList
    let plan = DataLoadPlan.build catalog topo rawRows SurrogateRemapContext.empty
    StaticSeedsEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan
    |> mustOkEmit

[<Fact>]
let ``StaticSeedsEmitter.emit produces one DataInsertScript per kind (T11 keyset)`` () =
    let catalog = mkCatalog [ mkCountryKind (); mkRegularKind () ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.Equal (2, Map.count map)

[<Fact>]
let ``StaticSeedsEmitter.emit produces empty Phase1Merges for non-static kinds`` () =
    let regular = mkRegularKind ()
    let catalog = mkCatalog [ regular ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find regular.SsKey
    Assert.Empty script.Phase1Merges
    Assert.Equal<string> ("", script.Rendered)

// ---------------------------------------------------------------------------
// Staged-source form — a kind above `stagingRowThreshold` (1000) renders the
// `#temp` batch (the error-8623-safe MERGE); below it, the inline form stands.
// ---------------------------------------------------------------------------

[<Fact>]
let ``StaticSeedsEmitter.emit: a kind above the staging threshold renders the atomic #temp batch`` () =
    let catalog =
        StaticCatalogFixtures.staticCatalog "STG" "StgMod" [ "Big" ] "Big" "STG_BIG"
            [ StaticCatalogFixtures.pk "Id" "ID" Integer
              StaticCatalogFixtures.attr "Code" "CODE" Text ]
            [ for i in 1 .. 1001 -> string i, [ string i; sprintf "C%04d" i ] ]
    let kind = Catalog.allKinds catalog |> List.head
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let sql = (ArtifactByKind.toMap artifact |> Map.find kind.SsKey).RenderedPhase1
    Assert.Contains("SET XACT_ABORT ON", sql)               // atomic wrapper
    Assert.Contains("BEGIN TRAN", sql)
    Assert.Contains("CREATE TABLE [#seed_", sql)            // a staging heap is created
    Assert.Contains("USING [#seed_", sql)                   // the MERGE draws from it
    Assert.Contains("ROLLBACK", sql)                        // CATCH cleanup
    Assert.Contains("END CATCH", sql)

// emission.dataStaging modes thread to the staging decision (Step 5).
let private stagingTopo (catalog: Catalog) =
    (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle catalog).Value

let private bigStgCatalog (n: int) : Catalog =
    StaticCatalogFixtures.staticCatalog "STG" "StgMod" [ "Big" ] "Big" "STG_BIG"
        [ StaticCatalogFixtures.pk "Id" "ID" Integer
          StaticCatalogFixtures.attr "Code" "CODE" Text ]
        [ for i in 1 .. n -> string i, [ string i; sprintf "C%05d" i ] ]

let private renderedPhase1With (staging: DataStagingPolicy) (catalog: Catalog) : string =
    let topo = stagingTopo catalog
    let artifact =
        StaticSeedsEmitter.emitWithTopo { DataEmitOptions.defaults with Verification = DataVerification.Standard; Staging = staging; DeleteScope = None } topo catalog Profile.empty
        |> mustOkEmit
    let kind = Catalog.allKinds catalog |> List.head
    (ArtifactByKind.toMap artifact |> Map.find kind.SsKey).RenderedPhase1

[<Fact>]
let ``emission.dataStaging inline: a >threshold kind stays inline (the locked-down escape hatch)`` () =
    let sql = renderedPhase1With { Mode = DataStagingMode.Inline; Threshold = 1000; IndexThreshold = 100000 } (bigStgCatalog 1500)
    Assert.DoesNotContain("#seed_", sql)
    Assert.DoesNotContain("BEGIN TRAN", sql)

[<Fact>]
let ``emission.dataStaging tempTable: a small kind stages anyway (forced staging)`` () =
    let sql = renderedPhase1With { Mode = DataStagingMode.TempTable; Threshold = 1000; IndexThreshold = 100000 } (bigStgCatalog 3)
    Assert.Contains("CREATE TABLE [#seed_", sql)

[<Fact>]
let ``emission.dataStaging auto: a raised threshold keeps a mid-size kind inline`` () =
    let sql = renderedPhase1With { Mode = DataStagingMode.Auto; Threshold = 5000; IndexThreshold = 100000 } (bigStgCatalog 1500)
    Assert.DoesNotContain("#seed_", sql)

// indexThreshold (measured 2026-06-25): a staged kind above `IndexThreshold`
// gets a CLUSTERED INDEX on the `#temp` PK (the ~35% MERGE-join speedup); below
// it, no index (the conservative measured gate; default 100k).
[<Fact>]
let ``emission.dataStaging: a staged kind above indexThreshold gets the clustered #temp index`` () =
    let sql = renderedPhase1With { Mode = DataStagingMode.Auto; Threshold = 1000; IndexThreshold = 1200 } (bigStgCatalog 1500)
    Assert.Contains("CREATE TABLE [#seed_", sql)               // staged
    Assert.Contains("CREATE CLUSTERED INDEX [ix_stg_STG_BIG]", sql)   // and indexed (1500 > 1200)
    Assert.Contains("ON [#seed_STG_BIG]([ID])", sql)           // on the #temp PK

[<Fact>]
let ``emission.dataStaging: a staged kind below indexThreshold gets NO #temp index (the measured gate)`` () =
    let sql = renderedPhase1With { Mode = DataStagingMode.TempTable; Threshold = 1000; IndexThreshold = 100000 } (bigStgCatalog 1500)
    Assert.Contains("CREATE TABLE [#seed_", sql)               // staged (tempTable forces it)
    Assert.DoesNotContain("CLUSTERED INDEX", sql)              // 1500 < 100000 → no index

[<Fact>]
let ``StaticSeedsEmitter.emit: a small kind stays on the inline path (no #temp, no transaction)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let sql = (ArtifactByKind.toMap artifact |> Map.find country.SsKey).RenderedPhase1
    Assert.DoesNotContain("#seed_", sql)
    Assert.DoesNotContain("BEGIN TRAN", sql)

[<Fact>]
let ``StaticSeedsEmitter.emit populates Phase1Merges for Modality.Static kinds`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Equal (2, List.length script.Phase1Merges)

[<Fact>]
let ``StaticSeedsEmitter.emit Phase1Merges carry KindKey + Identifier from StaticRow`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    for row in script.Phase1Merges do
        Assert.Equal<SsKey> (country.SsKey, row.KindKey)

[<Fact>]
let ``WP-3: the OutSystems single-space sentinel — `" "` on a NULLABLE Text attribute seeds as NULL, everything else is faithful`` () =
    // V1 parity (`StaticEntitySeedScriptGenerator.NormalizeValue`): the
    // platform stores a single space in a nullable Text attribute of a
    // static entity to mean "no value". Exactly `" "` + nullable + Text
    // coerces; `''`, multi-space, and mandatory-`" "` pass through.
    let kindKey = mkKey ["TestModule"; "Note"]
    let mkA (n: string) (col: string) (mandatory: bool) =
        { Attribute.create (mkKey ["TestModule"; "Note"; n]) (mkName n) Text with
            Column = ColumnRealization.create col false |> Result.value
            IsMandatory = mandatory }
    let idA =
        { Attribute.create (mkKey ["TestModule"; "Note"; "Id"]) (mkName "Id") Integer with
            Column = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true; IsMandatory = true }
    let row =
        { Identifier = mkKey ["TestModule"; "Note"; "Row"; "1"]
          Values =
            Map.ofList
                [ mkName "Id",       Some "1"
                  mkName "Blank",    Some " "    // nullable + exactly one space → NULL
                  mkName "Spaces",   Some "  "   // two spaces → faithful
                  mkName "Empty",    Some ""     // genuine empty string → N''
                  mkName "Required", Some " " ] } // mandatory → faithful N' '
    let kind =
        { SsKey    = kindKey
          Name     = mkName "Note"
          Origin   = Native
          Modality = [ Static [ row ] ]
          Physical = mkTableId "dbo" "OSUSR_TEST_NOTE"
          Attributes =
            [ idA
              mkA "Blank" "BLANK" false
              mkA "Spaces" "SPACES" false
              mkA "Empty" "EMPTY" false
              mkA "Required" "REQUIRED" true ]
          References = []
          Indexes = []
          Description = None
          IsActive = true
          Triggers = []
          ColumnChecks = []
          ExtendedProperties = [] }
    let catalog = mkCatalog [ kind ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let sql = (ArtifactByKind.toMap artifact |> Map.find kindKey).RenderedPhase1
    // The VALUES tuple: Id, then BLANK → NULL, SPACES → N'  ', EMPTY → N'', REQUIRED → N' '.
    Assert.Contains("NULL", sql)
    Assert.Contains("N'  '", sql)
    Assert.Contains("N''", sql)
    // Exactly ONE single-space literal survives — the MANDATORY one; the
    // nullable single-space cell became the NULL above.
    Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(sql, "N' '").Count)

[<Fact>]
let ``StaticSeedsEmitter.emit Phase2Updates is empty at slice α (no cycle-breaking yet)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Empty script.Phase2Updates

[<Fact>]
let ``StaticSeedsEmitter.emit Rendered MERGE shape contains V1-required clauses`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // V1's six load-bearing MERGE clauses (per `StaticSeedSqlBuilder.cs:211-260`).
    // Per Tier-1 #1 (typed-AST MERGE via ScriptDom): formatting is the
    // canonical Sql160ScriptGenerator output (different whitespace
    // wrapping than V1's hand-rolled). Assertions normalize whitespace
    // so the structural-content property is what's checked, not exact
    // formatting.
    let r = normWs script.Rendered
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY] AS [Target]", r)
    Assert.Contains ("USING", r)
    Assert.Contains ("VALUES", r)
    Assert.Contains ("AS [Source]([ID], [CODE], [LABEL])", r)
    Assert.Contains ("ON [Target].[ID] = [Source].[ID]", r)
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", r)
    Assert.Contains ("WHEN NOT MATCHED THEN INSERT", r)
    Assert.EndsWith ("GO", r)

[<Fact>]
let ``StaticSeedsEmitter.emit Rendered does NOT carry change-detection predicate (slice α; CDC awareness lands at slice β)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Slice α emits V1's WHEN MATCHED THEN UPDATE SET unconditional; the
    // change-detection-predicate (Target.col <> Source.col + null-aware
    // OR conditions) is the slice-β contribution that closes CDC-noise.
    let r = normWs script.Rendered
    Assert.DoesNotContain ("WHEN MATCHED AND", r)
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", r)

[<Fact>]
let ``T1: StaticSeedsEmitter.emit is byte-deterministic across repeat invocations`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let r1 = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let r2 = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)
    Assert.Equal<DataInsertRow list> (s1.Phase1Merges, s2.Phase1Merges)

[<Fact>]
let ``StaticSeedsEmitter.emit formats Text values with N-prefix + single-quote escaping`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Text columns get `N'...'` prefix per `Render.formatSqlLiteral`.
    Assert.Contains ("N'United States'", script.Rendered)
    Assert.Contains ("N'Canada'", script.Rendered)

[<Fact>]
let ``StaticSeedsEmitter.emit formats Integer values without quotes`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Integer values are bare digits per `Render.formatSqlLiteral`.
    // Note: this fixture uses Code-as-Id ("US"/"CA") so the Id column
    // gets formatted via Integer's identity branch — raw value stays
    // bare. Real production fixtures will carry numeric Ids.
    Assert.Contains ("US,", script.Rendered)
    Assert.Contains ("CA,", script.Rendered)

[<Fact>]
let ``T11: StaticSeedsEmitter.emit covers every catalog kind`` () =
    let country = mkCountryKind ()
    let regular = mkRegularKind ()
    let catalog = mkCatalog [ country; regular ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.True (Map.containsKey country.SsKey map)
    Assert.True (Map.containsKey regular.SsKey map)

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice β — CdcAwareness dispatch + change-detection MERGE
// predicate. The load-bearing semantic addition that closes CDC-silence
// on idempotent redeploys (per V2_DRIVER.md highest-stakes claim).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice β: CdcAwareness.empty has no CDC-enabled kinds`` () =
    Assert.True (Set.isEmpty CdcAwareness.empty.CdcEnabled)
    Assert.True (Map.isEmpty CdcAwareness.empty.CdcInstance)

[<Fact>]
let ``Slice β: CdcAwareness.isEnabled true iff key in enabled set`` () =
    let country = mkCountryKind ()
    let regular = mkRegularKind ()
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    Assert.True  (CdcAwareness.isEnabled country.SsKey cdc)
    Assert.False (CdcAwareness.isEnabled regular.SsKey cdc)

[<Fact>]
let ``Slice β: CdcAwareness.captureInstance returns Some when registered`` () =
    let country = mkCountryKind ()
    let cdc =
        CdcAwareness.create
            (Set.ofList [ country.SsKey ])
            (Map.ofList [ country.SsKey, "dbo_OSUSR_TEST_COUNTRY" ])
    Assert.Equal<string option>
        (Some "dbo_OSUSR_TEST_COUNTRY",
         CdcAwareness.captureInstance country.SsKey cdc)

[<Fact>]
let ``Slice β: StaticSeedsEmitter without CDC keeps V1 unconditional WHEN MATCHED`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // CdcAwareness.empty (no kinds enabled) → V1-shape MERGE.
    let r = normWs script.Rendered
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", r)
    Assert.DoesNotContain ("WHEN MATCHED AND", r)

[<Fact>]
let ``Slice β: StaticSeedsEmitter with CDC enabled emits change-detection predicate`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // CDC-enabled → WHEN MATCHED AND ( ... ) THEN UPDATE SET.
    let r = normWs script.Rendered
    Assert.Contains ("WHEN MATCHED AND (", r)
    Assert.Contains (") THEN UPDATE SET", r)
    Assert.DoesNotContain ("WHEN MATCHED THEN UPDATE SET", r)

[<Fact>]
let ``Slice β: change-detection predicate is nullable-aware (NULL-asymmetry both ways)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Per pre-scope §6: NULL ≠ NULL in SQL; the predicate covers both
    // null-asymmetry directions (target NULL vs source NOT NULL, and
    // target NOT NULL vs source NULL). Verify on at least one non-key
    // column.
    let r = normWs script.Rendered
    // ScriptDom emits aliases bracketed (`[Target]` / `[Source]`).
    Assert.Contains ("[Target].[CODE] <> [Source].[CODE]", r)
    Assert.Contains ("[Target].[CODE] IS NULL AND [Source].[CODE] IS NOT NULL", r)
    Assert.Contains ("[Target].[CODE] IS NOT NULL AND [Source].[CODE] IS NULL", r)

[<Fact>]
let ``Slice β: change-detection predicate covers every non-key column`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let r = normWs script.Rendered
    // Both non-key columns (CODE + LABEL) appear in the predicate.
    // ScriptDom emits aliases bracketed (`[Target]` / `[Source]`)
    // because they round-trip through `Identifier.EncodeIdentifier`.
    Assert.Contains ("[Target].[CODE] <> [Source].[CODE]", r)
    Assert.Contains ("[Target].[LABEL] <> [Source].[LABEL]", r)
    // ID is the PK; it should NOT appear in the change-detection
    // predicate (the predicate gates UPDATEs of NON-KEY columns).
    Assert.DoesNotContain ("[Target].[ID] <> [Source].[ID]", r)

[<Fact>]
let ``Slice β: per-kind dispatch — only CDC-enabled kinds get the predicate`` () =
    // Two static kinds in one catalog; only one is CDC-enabled.
    let country = mkCountryKind ()
    // Build a second static kind (Region) with same shape.
    let regionKey = mkKey ["TestModule"; "Region"]
    let region : Kind =
        { country with
            SsKey    = regionKey
            Name     = mkName "Region"
            Physical = mkTableId "dbo" "OSUSR_TEST_REGION" }
    let catalog = mkCatalog [ country; region ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    let countryScript = Map.find country.SsKey map
    let regionScript = Map.find region.SsKey map
    let cR = normWs countryScript.Rendered
    let rR = normWs regionScript.Rendered
    // Country: CDC-enabled → predicate.
    Assert.Contains ("WHEN MATCHED AND (", cR)
    // Region: not CDC-enabled → V1-shape MERGE.
    Assert.DoesNotContain ("WHEN MATCHED AND (", rR)
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", rR)

[<Fact>]
let ``Slice β: T1 byte-determinism holds under CDC dispatch`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let r1 = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile |> mustOkEmit
    let r2 = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice δ — DataInsertScript.Phase2Updates + DeferredFkSet
// (two-phase insertion / cycle-breaking).
//
// V1 reference: `Osm.Emission/PhasedDynamicEntityInsertGenerator.cs:88-148`
// (the empirical foundation V2 inherits) + `IdentifyNullableFKColumns:150`
// (the deferral predicate). V2 algebra: cycle-membership comes from
// `TopologicalOrderPass.Cycles`; nullable cycle-FK columns are NULLed
// in Phase-1 MERGE VALUES and populated in Phase-2 per-row UPDATEs;
// non-cycle / non-nullable FKs are not deferred.
// ---------------------------------------------------------------------------

/// Self-referencing kind: Tree (Id INT PK, Label TEXT, ParentId INT FK
/// nullable → Tree.Id). One Modality.Static row. The self-FK forms a
/// 1-node SCC; per `TopologicalOrderPass.SelfLoopPolicy = TreatAsCycle`
/// the kind appears in `Cycles`. Phase-1 MERGE NULLs ParentId; Phase-2
/// UPDATE re-populates it.
let private mkTreeKind () : Kind =
    let kindKey   = mkKey ["TestModule"; "Tree"]
    let idKey     = mkKey ["TestModule"; "Tree"; "Id"]
    let labelKey  = mkKey ["TestModule"; "Tree"; "Label"]
    let parentKey = mkKey ["TestModule"; "Tree"; "ParentId"]
    let refKey    = mkKey ["TestModule"; "Tree"; "RefParent"]
    let row =
        { Identifier = mkKey ["TestModule"; "Tree"; "Row"; "ROOT"]
          Values =
              StaticRow.presentValues
                  [ mkName "Id",       "1"
                    mkName "Label",    "root"
                    mkName "ParentId", "1" ] }
    {
        SsKey    = kindKey
        Name     = mkName "Tree"
        Origin   = Native
        Modality = [ Static [ row ] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_TREE"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
                { Attribute.create parentKey (mkName "ParentId") Integer with
                    Column = ColumnRealization.create ("PARENTID") (true) |> Result.value }     // nullable → deferrable
            ]
        References =
            [
                Reference.create refKey (mkName "RefParent") parentKey kindKey
            ]
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// Self-referencing kind whose FK column is NOT NULL. Same shape as
/// `mkTreeKind` but `ParentId` is non-nullable. The cycle still
/// surfaces via `TopologicalOrderPass.Cycles`, but slice δ MUST NOT
/// defer the column (NULLing would violate the constraint; V1's
/// `IdentifyNullableFKColumns:184` skips these too).
let private mkRigidTreeKind () : Kind =
    let tree = mkTreeKind ()
    let attrs =
        tree.Attributes
        |> List.map (fun a ->
            if a.Name = mkName "ParentId" then
                { a with Column = { a.Column with IsNullable = false }
                         IsMandatory = true }
            else a)
    { tree with Attributes = attrs }

[<Fact>]
let ``Slice δ: acyclic catalog produces empty DeferredFkSet on every Phase1 row`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    for row in script.Phase1Merges do
        Assert.True (Set.isEmpty row.DeferredFkSet)
    Assert.Empty script.Phase2Updates

[<Fact>]
let ``Slice δ: self-referencing nullable FK populates DeferredFkSet`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = emitDeferring catalog
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    Assert.Equal (1, List.length script.Phase1Merges)
    let phase1 = List.head script.Phase1Merges
    // The nullable self-FK attribute (ParentId) is the deferred column.
    Assert.True (Set.contains (mkName "ParentId") phase1.DeferredFkSet)
    // Other columns NOT deferred (Id PK; Label non-FK).
    Assert.False (Set.contains (mkName "Id") phase1.DeferredFkSet)
    Assert.False (Set.contains (mkName "Label") phase1.DeferredFkSet)

[<Fact>]
let ``Slice δ: self-FK kind produces one Phase2Updates row per Phase1 row`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = emitDeferring catalog
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    Assert.Equal (List.length script.Phase1Merges, List.length script.Phase2Updates)
    // Phase2 rows carry the same (KindKey, Identifier, DeferredFkSet)
    // as their Phase1 counterparts — same logical row, two phases.
    let p1Identities = script.Phase1Merges |> List.map (fun r -> r.KindKey, r.Identifier, r.DeferredFkSet)
    let p2Identities = script.Phase2Updates |> List.map (fun r -> r.KindKey, r.Identifier, r.DeferredFkSet)
    Assert.Equal<(SsKey * SsKey * Set<Name>) list> (p1Identities, p2Identities)

[<Fact>]
let ``static lane renders a cyclic nullable FK inline (no Phase-2; DECISIONS 2026-07-19)`` () =
    // The static-seed LANE (`emit` / `emitWithTopo`) renders a self-referencing
    // nullable FK as a single MERGE carrying the real FK value — NOT the
    // Phase-1-NULL / Phase-2-UPDATE deferral (which the migration / bootstrap /
    // transfer lanes keep — pinned above via `emitDeferring`). V1's
    // `StaticSeedSqlBuilder` shape: single MERGE, no follow-up UPDATE.
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    // No column deferred, no Phase-2 UPDATE.
    Assert.All (script.Phase1Merges, fun row -> Assert.Empty row.DeferredFkSet)
    Assert.Empty script.Phase2Updates
    // The rendered MERGE carries the real FK value (the deferred-NULL form does
    // NOT appear), and there is no standalone Phase-2 UPDATE statement.
    let r = normWs script.Rendered
    Assert.DoesNotContain ("(1, N'root', NULL)", r)
    Assert.DoesNotContain ("UPDATE [dbo].[OSUSR_TEST_TREE]", r)

[<Fact>]
let ``Slice δ: NOT NULL FK in cycle is NOT deferred`` () =
    let rigid = mkRigidTreeKind ()
    let catalog = mkCatalog [ rigid ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find rigid.SsKey
    // Cycle membership holds (TopologicalOrderPass sees the self-edge
    // under TreatAsCycle), but the FK column is non-nullable so V1's
    // deferral predicate (and V2's mirror) refuses to defer.
    let phase1 = List.head script.Phase1Merges
    Assert.True (Set.isEmpty phase1.DeferredFkSet)
    Assert.Empty script.Phase2Updates

[<Fact>]
let ``Slice δ: Phase1 MERGE renders deferred column as NULL in VALUES`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = emitDeferring catalog
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    let r = normWs script.Rendered
    // The MERGE's USING (VALUES (...)) row should carry NULL in the
    // PARENTID slot rather than the row's raw value (1). The Source
    // column list still names PARENTID — only the literal in VALUES
    // changes. Per ScriptDom's Sql160ScriptGenerator NullLiteral output
    // = `NULL`. Look for the row tuple: (1, N'root', NULL).
    Assert.Contains ("(1, N'root', NULL)", r)
    // And the Source column list still names PARENTID structurally.
    Assert.Contains ("AS [Source]([ID], [LABEL], [PARENTID])", r)

[<Fact>]
let ``Slice δ: Phase2 UPDATE references PK in WHERE + deferred column in SET`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = emitDeferring catalog
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    let r = normWs script.Rendered
    Assert.Contains ("UPDATE [dbo].[OSUSR_TEST_TREE]", r)
    // SET clause carries the original (deferred) value — 1.
    Assert.Contains ("SET [PARENTID] = 1", r)
    // WHERE-clause scopes to the row's PK (Id = 1).
    Assert.Contains ("WHERE [ID] = 1", r)

[<Fact>]
let ``Slice δ: 2-cycle with both FKs nullable defers FK column on each kind`` () =
    // Two Static kinds A ↔ B both nullable FK forms a 2-member SCC.
    // Both kinds should receive Phase-2 deferral for their cross-FK.
    let aKey  = mkKey ["TestModule"; "A"]
    let bKey  = mkKey ["TestModule"; "B"]
    let aIdK  = mkKey ["TestModule"; "A"; "Id"]
    let aFkK  = mkKey ["TestModule"; "A"; "BId"]
    let bIdK  = mkKey ["TestModule"; "B"; "Id"]
    let bFkK  = mkKey ["TestModule"; "B"; "AId"]
    let aRefK = mkKey ["TestModule"; "A"; "ToB"]
    let bRefK = mkKey ["TestModule"; "B"; "ToA"]
    let aRow =
        { Identifier = mkKey ["TestModule"; "A"; "Row"; "1"]
          Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "BId", "1" ] }
    let bRow =
        { Identifier = mkKey ["TestModule"; "B"; "Row"; "1"]
          Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "AId", "1" ] }
    let mkAttr ssk name typ col isPk isNull =
        { Attribute.create ssk (mkName name) typ with Column = ColumnRealization.create (col) (isNull) |> Result.value; IsPrimaryKey = isPk; IsMandatory = not isNull }
    let mkRef ssk name srcAttr tgt =
        Reference.create ssk (mkName name) srcAttr tgt
    let aKind : Kind =
        { SsKey = aKey; Name = mkName "A"; Origin = Native
          Modality = [ Static [ aRow ] ]
          Physical = mkTableId "dbo" "OSUSR_A"
          Attributes = [ mkAttr aIdK "Id"  Integer "ID"  true false
                         mkAttr aFkK "BId" Integer "BID" false true ]
          References = [ mkRef aRefK "ToB" aFkK bKey ]
          Indexes    = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let bKind : Kind =
        { SsKey = bKey; Name = mkName "B"; Origin = Native
          Modality = [ Static [ bRow ] ]
          Physical = mkTableId "dbo" "OSUSR_B"
          Attributes = [ mkAttr bIdK "Id"  Integer "ID"  true false
                         mkAttr bFkK "AId" Integer "AID" false true ]
          References = [ mkRef bRefK "ToA" bFkK aKey ]
          Indexes    = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let catalog = mkCatalog [ aKind; bKind ]
    let artifact = emitDeferring catalog
    let m = ArtifactByKind.toMap artifact
    let aScript = Map.find aKey m
    let bScript = Map.find bKey m
    // v7 slice 5 (DECISIONS 2026-07-18) — the exact repair set: the
    // resolver breaks EXACTLY ONE edge of the symmetric weak 2-cycle, and
    // only the BROKEN edge's kind defers its FK column; the other kind
    // loads at its proven order position with the FK inline. (Pre-v7 both
    // deferred — pure Phase-2 norm inflation on the proven side.)
    let aDefers = Set.contains (mkName "BId") (List.head aScript.Phase1Merges).DeferredFkSet
    let bDefers = Set.contains (mkName "AId") (List.head bScript.Phase1Merges).DeferredFkSet
    Assert.True ((aDefers <> bDefers), "exactly one side of the symmetric weak 2-cycle defers")
    // Phase-2 exists exactly on the deferring side (its row carries a
    // non-NULL deferred value).
    let deferring, proven = if aDefers then aScript, bScript else bScript, aScript
    Assert.NotEmpty deferring.Phase2Updates
    Assert.Empty proven.Phase2Updates

[<Fact>]
let ``T1 (slice δ): byte-determinism holds across repeat invocations under cycle-breaking`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let r1 = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let r2 = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find tree.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find tree.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)
    Assert.Equal<DataInsertRow list> (s1.Phase1Merges, s2.Phase1Merges)
    Assert.Equal<DataInsertRow list> (s1.Phase2Updates, s2.Phase2Updates)

[<Fact>]
let ``Slice δ: slice α/β rows pre-existing carry empty DeferredFkSet (acyclic invariant)`` () =
    // Country (slice α/β fixture; no FKs at all) MUST have empty
    // DeferredFkSet on every Phase1 row + empty Phase2Updates. Catches
    // the case where slice δ accidentally populates the field on
    // non-cycle kinds.
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.All (script.Phase1Merges, fun row -> Assert.Empty row.DeferredFkSet)
    Assert.Empty script.Phase2Updates

// ---------------------------------------------------------------------------
// AC-D5 (gap N2): persisted computed columns are SQL-Server-computed and
// must NEVER appear in the CDC-aware MERGE's updatable-column set — an
// `UPDATE SET <computed> = ...` is a hard SQL error, and the column must
// not enter the change-detection predicate or the INSERT column list.
// ---------------------------------------------------------------------------

/// Country fixture with a fourth attribute (`Display`) that is a PERSISTED
/// computed column (`Computed = Some _`). A non-computed sibling (`Code`)
/// is present so the discrimination is sharp: Code IS updatable, Display
/// is NOT.
let private mkComputedColumnKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Country"]
    let idKey = mkKey ["TestModule"; "Country"; "Id"]
    let codeKey = mkKey ["TestModule"; "Country"; "Code"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    let displayKey = mkKey ["TestModule"; "Country"; "Display"]
    let computedCfg = ComputedColumnConfig.create "[CODE] + N' - ' + [LABEL]" true |> mustOk
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
        Modality = [ Static [ row "US" "United States"
                              row "CA" "Canada" ] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_COUNTRY"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
                { Attribute.create displayKey (mkName "Display") Text with Column = ColumnRealization.create ("DISPLAY") (false) |> Result.value; Computed = Some computedCfg }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

[<Fact>]
let ``AC-D5: computed column is excluded from CDC-aware MERGE UPDATE SET + predicate (discriminating)`` () =
    let country = mkComputedColumnKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog profile |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let r = normWs script.Rendered
    // CDC-aware: WHEN MATCHED AND ( ... ) THEN UPDATE SET.
    Assert.Contains ("WHEN MATCHED AND (", r)
    // The persisted computed column DISPLAY must NOT appear anywhere in
    // the MERGE — not in UPDATE SET, not in the change-detection
    // predicate, not in the INSERT/USING column list (gap N2).
    Assert.DoesNotContain ("[DISPLAY]", r)
    // The non-computed sibling CODE IS updatable: it appears in UPDATE SET
    // and in the change-detection predicate. (Discrimination: current
    // pre-fix code emits DISPLAY in both places — this is the red→green.)
    Assert.Contains ("[Target].[CODE] = [Source].[CODE]", r)
    Assert.Contains ("[Target].[CODE] <> [Source].[CODE]", r)

// -- AC-D7 / AC-G4: the emission delete-scope reaches the rendered MERGE -----
// (A3 wire — the policy threads `EmissionPolicy.DeleteScope` → composer →
// `emitWithTopoWith` → per-kind `DeleteScopePolicy.resolveFor`.)

let private topoOf (catalog: Catalog) : TopologicalOrder =
    (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle catalog).Value

[<Fact>]
let ``AC-D7: a delete-scope policy renders the scoped DELETE arm on a kind carrying the column`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let scope : DeleteScopePolicy = { Terms = [ { Column = (ColumnName.create "CODE" |> Result.value); Value = "US" } ] }
    let artifact =
        StaticSeedsEmitter.emitWithTopo { DataEmitOptions.defaults with DeleteScope = (Some scope) } (topoOf catalog) catalog Profile.empty
        |> mustOkEmit
    let r = normWs (ArtifactByKind.toMap artifact |> Map.find country.SsKey).Rendered
    Assert.Contains ("WHEN NOT MATCHED BY SOURCE AND [Target].[CODE] = N'US' THEN DELETE", r)
    // The non-delete arms remain (the scope arm is additive).
    Assert.Contains ("WHEN MATCHED", r)
    Assert.Contains ("WHEN NOT MATCHED THEN INSERT", r)

[<Fact>]
let ``AC-D7: scope term columns resolve case-insensitively (the physical-column contract)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let scope : DeleteScopePolicy = { Terms = [ { Column = (ColumnName.create "code" |> Result.value); Value = "US" } ] }
    let artifact =
        StaticSeedsEmitter.emitWithTopo { DataEmitOptions.defaults with DeleteScope = (Some scope) } (topoOf catalog) catalog Profile.empty
        |> mustOkEmit
    let r = normWs (ArtifactByKind.toMap artifact |> Map.find country.SsKey).Rendered
    Assert.Contains ("THEN DELETE", r)

[<Fact>]
let ``AC-D7: a kind missing the scope column keeps the upsert-only MERGE (faithful omission, not a skip)`` () =
    // No row of Country lies inside a TENANT_ID scope — the kind does not
    // carry the column — so the faithful rendering omits the arm.
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let scope : DeleteScopePolicy = { Terms = [ { Column = (ColumnName.create "TENANT_ID" |> Result.value); Value = "42" } ] }
    let artifact =
        StaticSeedsEmitter.emitWithTopo { DataEmitOptions.defaults with DeleteScope = (Some scope) } (topoOf catalog) catalog Profile.empty
        |> mustOkEmit
    let r = normWs (ArtifactByKind.toMap artifact |> Map.find country.SsKey).Rendered
    Assert.DoesNotContain ("NOT MATCHED BY SOURCE", r)
    Assert.DoesNotContain ("DELETE", r)

[<Fact>]
let ``AC-D7: no scope is byte-identical to the established upsert-only emit`` () =
    let catalog = mkCatalog [ mkCountryKind () ]
    let viaDefault = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let viaExplicitNone =
        StaticSeedsEmitter.emitWithTopo { DataEmitOptions.defaults with DeleteScope = None } (topoOf catalog) catalog Profile.empty |> mustOkEmit
    Assert.Equal<Map<SsKey, DataInsertScript>>(ArtifactByKind.toMap viaDefault, ArtifactByKind.toMap viaExplicitNone)

// -- NM-73 (WP6.6, C2): validate-before-apply drift guard --------------------
// `Standard` is byte-identical (CDC-silence canonical); `ValidateBeforeApply`
// prepends V1 `ValidateThenApply`'s symmetric-EXCEPT THROW guard as its own
// GO batch before the MERGE. Built via the typed parse-template path.

[<Fact>]
let ``NM-73: Standard verification is byte-identical to the established emit`` () =
    let catalog = mkCatalog [ mkCountryKind () ]
    let viaDefault = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let viaStandard =
        StaticSeedsEmitter.emitWithTopo { DataEmitOptions.defaults with Verification = DataVerification.Standard; DeleteScope = None } (topoOf catalog) catalog Profile.empty
        |> mustOkEmit
    Assert.Equal<Map<SsKey, DataInsertScript>>(ArtifactByKind.toMap viaDefault, ArtifactByKind.toMap viaStandard)

[<Fact>]
let ``NM-73: ValidateBeforeApply prepends the symmetric-EXCEPT THROW guard before the MERGE`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact =
        StaticSeedsEmitter.emitWithTopo { DataEmitOptions.defaults with Verification = DataVerification.ValidateBeforeApply; DeleteScope = None } (topoOf catalog) catalog Profile.empty
        |> mustOkEmit
    let rendered = (ArtifactByKind.toMap artifact |> Map.find country.SsKey).Rendered
    let r = normWs rendered
    // V1 ValidateThenApply shape: guarded on a non-empty target, THROW on drift.
    Assert.Contains ("IF EXISTS (SELECT 1 FROM [dbo].[OSUSR_TEST_COUNTRY])", r)
    Assert.Contains ("THROW 50000", r)
    // Symmetric EXCEPT pair (drift in either direction).
    let exceptCount = (r.Split([| "EXCEPT" |], System.StringSplitOptions.None)).Length - 1
    Assert.Equal (2, exceptCount)
    // The guard validates against the same target the MERGE writes.
    Assert.Contains ("[Existing]", r)
    // The guard is its own batch BEFORE the MERGE — it THROWs first.
    Assert.True (rendered.IndexOf("THROW") < rendered.IndexOf("MERGE INTO"), "the drift guard must precede the MERGE")
    // The MERGE still follows intact.
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY] AS [Target]", r)
    Assert.EndsWith ("GO", r)

// ---------------------------------------------------------------------------
// NM-26 — StaticPopulation + StaticSeeds agree on IDENTITY_INSERT bracketing.
// Both now route through `IdentityDisposition.needsIdentityInsert` (any
// IsIdentity attr), so on the same kind they bracket identically. The
// load-bearing case is a NON-PK identity column (a business PK + an
// OutSystems autonumber surrogate): pre-NM-26 StaticSeeds gated on
// AssignedBySink (PK-IDENTITY only) and silently FAILED to bracket it — a
// deploy rejection of the same family as NM-25.
// ---------------------------------------------------------------------------

/// A static kind whose PK is a business/natural key (Code TEXT) plus a
/// non-PK IDENTITY surrogate column (Seq). `IdentityDisposition.ofKind`
/// reads PreservedFromSource; `needsIdentityInsert` reads true.
let private mkNonPkIdentityKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Region"]
    let codeKey = mkKey ["TestModule"; "Region"; "Code"]
    let seqKey  = mkKey ["TestModule"; "Region"; "Seq"]
    let nameKey = mkKey ["TestModule"; "Region"; "Name"]
    let row code seq name =
        { Identifier = mkKey ["TestModule"; "Region"; "Row"; code]
          Values =
              StaticRow.presentValues
                  [ mkName "Code", code
                    mkName "Seq",  seq
                    mkName "Name", name ] }
    {
        SsKey    = kindKey
        Name     = mkName "Region"
        Origin   = Native
        Modality = [ Static [ row "EMEA" "1" "Europe"
                              row "APAC" "2" "Asia Pacific" ] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_REGION"
        Attributes =
            [
                { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create seqKey (mkName "Seq") Integer with Column = ColumnRealization.create ("SEQ") (false) |> Result.value; IsMandatory = true; IsIdentity = true }
                { Attribute.create nameKey (mkName "Name") Text with Column = ColumnRealization.create ("NAME") (false) |> Result.value; IsMandatory = true }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

/// True iff `StaticPopulationEmitter.statements` brackets this catalog's
/// rows with `SetIdentityInsert` toggles.
let private populationBrackets (catalog: Catalog) : bool =
    StaticPopulationEmitter.statements catalog
    |> Seq.exists (function SetIdentityInsert _ -> true | _ -> false)

/// True iff `StaticSeedsEmitter.emit` renders a `SET IDENTITY_INSERT`
/// bracket for the given kind.
let private seedsBracket (catalog: Catalog) (kindKey: SsKey) : bool =
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let rendered = (ArtifactByKind.toMap artifact |> Map.find kindKey).Rendered
    rendered.Contains "IDENTITY_INSERT"

[<Fact>]
let ``NM-26: StaticPopulation and StaticSeeds agree on a NON-PK identity kind (both bracket)`` () =
    let region = mkNonPkIdentityKind ()
    let catalog = mkCatalog [ region ]
    Assert.True (populationBrackets catalog, "StaticPopulation must bracket the non-PK identity column")
    Assert.True (seedsBracket catalog region.SsKey, "StaticSeeds must bracket the non-PK identity column (NM-26 fix)")

[<Fact>]
let ``NM-26: StaticPopulation and StaticSeeds agree on a non-identity kind (neither brackets)`` () =
    // Country here has no IsIdentity attribute — neither emitter brackets.
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    Assert.False (populationBrackets catalog)
    Assert.False (seedsBracket catalog country.SsKey)

// ---------------------------------------------------------------------------
// Row-identity match fallback — `KindColumns.matchColumnNames`. A kind with
// no primary key (the explicitly acknowledged `allowMissingPrimaryKey`
// population) falls back to its writable columns as the MERGE match
// criteria (V1's static-seed fallback), excluding computed, identity, and
// intentionally deferred columns. Data movement only — no synthetic PK is
// claimed. All three data lanes share the one vocabulary, so Phase 1 and
// Phase 2 always agree on row identity.
// ---------------------------------------------------------------------------

/// Acknowledged missing-PK static kind: rows but no primary-key attribute.
/// Carries one identity column and one computed column so the fallback's
/// exclusions are visible in the rendered ON-clause.
let private mkHeapKind () : Kind =
    let kindKey  = mkKey ["TestModule"; "Heap"]
    let codeKey  = mkKey ["TestModule"; "Heap"; "Code"]
    let labelKey = mkKey ["TestModule"; "Heap"; "Label"]
    let seqKey   = mkKey ["TestModule"; "Heap"; "Seq"]
    let calcKey  = mkKey ["TestModule"; "Heap"; "Calc"]
    let row code label =
        { Identifier = mkKey ["TestModule"; "Heap"; "Row"; code]
          Values =
              StaticRow.presentValues
                  [ mkName "Code",  code
                    mkName "Label", label ] }
    {
        SsKey    = kindKey
        Name     = mkName "Heap"
        Origin   = Native
        Modality = [ Static [ row "A" "Alpha"
                              row "B" "Beta" ] ]
        Physical = mkTableId "dbo" "OSUSR_TEST_HEAP"
        Attributes =
            [
                { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
                { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true }
                { Attribute.create seqKey (mkName "Seq") Integer with Column = ColumnRealization.create ("SEQ") (false) |> Result.value; IsMandatory = true; IsIdentity = true }
                { Attribute.create calcKey (mkName "Calc") Integer with Column = ColumnRealization.create ("CALC") (true) |> Result.value; Computed = ComputedColumnConfig.create "([SEQ] * 2)" false |> Result.toOption }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
        }

[<Fact>]
let ``matchColumnNames: true primary keys win when present`` () =
    let country = mkCountryKind ()
    Assert.Equal<string list>([ "ID" ], KindColumns.matchColumnNames Set.empty country)

[<Fact>]
let ``matchColumnNames: a no-PK kind falls back to writable columns minus identity and computed`` () =
    let heap = mkHeapKind ()
    Assert.Equal<string list>([ "CODE"; "LABEL" ], KindColumns.matchColumnNames Set.empty heap)

[<Fact>]
let ``matchColumnNames: the fallback excludes intentionally deferred columns (Phase-2 row identity)`` () =
    let heap = mkHeapKind ()
    Assert.Equal<string list>([ "LABEL" ], KindColumns.matchColumnNames (Set.ofList [ mkName "Code" ]) heap)

[<Fact>]
let ``StaticSeedsEmitter.emit: an acknowledged no-PK kind renders a MERGE matched on its writable columns`` () =
    // Previously an empty PK set aborted the render (`foldBool: empty
    // term list`); the fallback restores V1's all-column match. The
    // identity + computed columns never enter the ON-clause.
    let heap = mkHeapKind ()
    let catalog = mkCatalog [ heap ]
    let artifact = StaticSeedsEmitter.emit DataEmitOptions.defaults catalog Profile.empty |> mustOkEmit
    let sql = normWs (ArtifactByKind.toMap artifact |> Map.find heap.SsKey).Rendered
    Assert.Contains("MERGE INTO [dbo].[OSUSR_TEST_HEAP]", sql)
    Assert.Contains("ON [Target].[CODE] = [Source].[CODE] AND [Target].[LABEL] = [Source].[LABEL]", sql)
    Assert.DoesNotContain("[Target].[SEQ] = [Source].[SEQ]", sql)
    Assert.DoesNotContain("[Target].[CALC] = [Source].[CALC]", sql)

[<Fact>]
let ``MergeRender.renderUpdate: a no-PK kind's Phase-2 row scope excludes the deferred columns`` () =
    // The Phase-2 UPDATE must join back to the Phase-1 row whose deferred
    // columns were intentionally nulled — matching on them would compare
    // the staged real value against NULL and never find the row.
    let heap = mkHeapKind ()
    let deferred = Set.ofList [ mkName "Label" ]
    let typedValues =
        Map.ofList
            [ mkName "Code",  SqlLiteral.ofRaw PrimitiveType.Text (Some "A")
              mkName "Label", SqlLiteral.ofRaw PrimitiveType.Text (Some "Alpha") ]
    let sql = normWs (MergeRender.renderUpdate "emit.test" false heap deferred typedValues)
    Assert.Contains("SET [LABEL] = N'Alpha'", sql)
    Assert.Contains("WHERE [CODE] = N'A'", sql)
    Assert.DoesNotContain("WHERE [LABEL]", sql)
    Assert.DoesNotContain("AND [LABEL]", sql)

// ---------------------------------------------------------------------------
// Acquisition-overlap factorization law — `renderLoad` (the per-kind unit)
// reproduces every script `emitFromPlan` (the batch map) produces. This is
// what lets an overlapped realization render a kind's MERGE the moment its
// rows land: per-kind text depends only on (options, CDC membership, the
// kind, its own load); cross-kind order is assembly's concern.
// ---------------------------------------------------------------------------

[<Fact>]
let ``renderLoad ≡ emitFromPlan per kind: the per-kind render unit reproduces the batch scripts`` () =
    let country = mkCountryKind ()
    let regular = mkRegularKind ()
    let catalog = mkCatalog [ country; regular ]
    let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle catalog).Value
    let rawRows =
        Catalog.allKinds catalog
        |> List.map (fun k -> k.SsKey, Kind.staticPopulations k)
        |> Map.ofList
    let plan = DataLoadPlan.build catalog topo rawRows SurrogateRemapContext.empty
    let artifact =
        StaticSeedsEmitter.emitFromPlan DataEmitOptions.defaults catalog Profile.empty plan
        |> mustOkEmit
    let batch = ArtifactByKind.toMap artifact
    for load in plan.Loads do
        let kind = Catalog.tryFindKind load.Kind catalog |> Option.get
        let perKind =
            StaticSeedsEmitter.renderLoad DataEmitOptions.defaults Profile.empty.CdcAwareness kind load
        Assert.Equal<DataInsertScript>(Map.find load.Kind batch, perKind)

[<Fact>]
let ``renderQuanta ≡ renderLoad over materialized rows at FULL record grain (the positional-render law)`` () =
    // Quanta in attribute order (Id, Code, Label), materialized through
    // the SAME boundary the reader leg uses (`StaticRow.ofQuantum` +
    // `StaticRow.readsideIdentity`) — the two render paths must agree on
    // the whole DataInsertScript (identifiers included), not just text.
    let country = mkCountryKind ()
    let basis = Kind.rowBasis country
    let schemaText = TableId.schemaText country.Physical
    let tableText  = TableId.tableText country.Physical
    let quanta : RowQuantum list =
        [ { Cells = [| ValueSome "1"; ValueSome "US"; ValueSome "United States" |] }
          { Cells = [| ValueSome "2"; ValueSome "CA"; ValueSome "Canada" |] } ]
    let rows =
        quanta
        |> List.mapi (fun i q ->
            StaticRow.ofQuantum basis (StaticRow.readsideIdentity schemaText tableText i) q)
    for deferred in [ Set.empty; Set.singleton (mkName "Label") ] do
        let load : DataLoadKind =
            { Kind              = country.SsKey
              Disposition       = IdentityDisposition.PreservedFromSource
              DeferredFkColumns = deferred
              Rows              = rows }
        let viaRows =
            StaticSeedsEmitter.renderLoad DataEmitOptions.defaults Profile.empty.CdcAwareness country load
        let viaQuanta =
            StaticSeedsEmitter.renderQuanta DataEmitOptions.defaults Profile.empty.CdcAwareness country deferred quanta
        Assert.Equal<DataInsertScript>(viaRows, viaQuanta)

[<Fact>]
let ``v7 slice 5: phase 2 touches exactly the repair set — an all-NULL deferred row renders no UPDATE`` () =
    // A self-referential kind (nullable ParentId) with two rows: one
    // carries a real parent (the repair set), one a NULL parent (landed
    // whole by Phase-1). Phase-2 must carry exactly ONE update.
    let key = mkKey ["SelfRef"]
    let idK = mkKey ["SelfRef"; "Id"]
    let fkK = mkKey ["SelfRef"; "ParentId"]
    let refK = mkKey ["SelfRef"; "ToSelf"]
    let mkAttr ssk name typ col isPk isNull =
        { Attribute.create ssk (mkName name) typ with Column = ColumnRealization.create (col) (isNull) |> Result.value; IsPrimaryKey = isPk; IsMandatory = not isNull }
    let rowWithParent =
        { Identifier = mkKey ["SelfRef"; "row1"]
          Values = StaticRow.presentValues [ mkName "Id", "1"; mkName "ParentId", "2" ] }
    let rowWithoutParent =
        { Identifier = mkKey ["SelfRef"; "row2"]
          Values = StaticRow.presentValues [ mkName "Id", "2" ] }
    let kind : Kind =
        { SsKey = key; Name = mkName "SelfRef"; Origin = Native
          Modality = [ Static [ rowWithParent; rowWithoutParent ] ]
          Physical = mkTableId "dbo" "OSUSR_SELFREF"
          Attributes = [ mkAttr idK "Id" Integer "ID" true false
                         mkAttr fkK "ParentId" Integer "PARENTID" false true ]
          References = [ Reference.create refK (mkName "ToSelf") fkK key ]
          Indexes = []
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    let catalog = mkCatalog [ kind ]
    let artifact = emitDeferring catalog
    let script = ArtifactByKind.toMap artifact |> Map.find key
    Assert.Equal(2, script.Phase1Merges.Length)
    // The repair set is exactly the one row with a non-NULL parent.
    Assert.Equal(1, script.Phase2Updates.Length)
    Assert.Equal(rowWithParent.Identifier, script.Phase2Updates.Head.Identifier)

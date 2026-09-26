module Projection.Tests.MigrationDependenciesEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.Data

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice ε — MigrationDependenciesEmitter v0.
//
// V1 reference: Operator-published legacy-domain rows (per pre-scope §2.2).
// V2 algebra: `Catalog × Profile × MigrationDependencyContext →
// Result<ArtifactByKind<DataInsertScript>>`. Per A18 amended: emitter
// signatures cannot consume Policy; the composition layer
// (DataEmissionComposer) decides whether this emitter fires.
//
// Per Tier-3 hard-requirement Active deferral (DECISIONS 2026-05-10):
// MUST adopt `ScriptDomBuild.buildMergeStatement` precedent from slice α.
// The emitter mirrors StaticSeedsEmitter's MERGE shape; same cycle-
// breaking Phase-1/Phase-2 dispatch as slice δ.
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

/// Country kind shape (mirrors StaticSeedsEmitter test) — but this
/// fixture has NO Static modality. Migration rows arrive via the
/// MigrationDependencyContext channel.
let private mkCountryKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Country"]
    let idKey = mkKey ["TestModule"; "Country"; "Id"]
    let codeKey = mkKey ["TestModule"; "Country"; "Code"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    {
        SsKey    = kindKey
        Name     = mkName "Country"
        Origin   = Native
        Modality = []  // NOT static — populated via Migration channel
        Physical = Projection.Tests.Fixtures.mkTableId "dbo" "OSUSR_TEST_COUNTRY"
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
        IRBuilders.mkModule (mkKey ["TestModule"]) (mkName "TestModule") kinds
    IRBuilders.mkCatalog [ m ]

let private mkMigrationRow (kindKey: SsKey) (idValue: string) (code: string) (label: string) : MigrationDependencyRow =
    {
        KindKey    = kindKey
        Identifier = mkKey ["TestModule"; "Country"; "MigRow"; idValue]
        Values =
            StaticRow.presentValues
                [ mkName "Id",    idValue
                  mkName "Code",  code
                  mkName "Label", label ]
    }

let private normWs (s: string) : string =
    System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim()

// ---------------------------------------------------------------------------
// MigrationDependencyContext — shape and helpers.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MigrationDependencyContext.empty has no rows`` () =
    Assert.Empty MigrationDependencyContext.empty.Rows

[<Fact>]
let ``MigrationDependencyContext.rowsByKind groups rows by KindKey`` () =
    let country = mkCountryKind ()
    let context =
        { Rows =
            [ mkMigrationRow country.SsKey "1" "US" "United States"
              mkMigrationRow country.SsKey "2" "CA" "Canada" ] }
    let grouped = MigrationDependencyContext.rowsByKind context
    Assert.Equal (1, Map.count grouped)
    Assert.Equal (2, List.length (Map.find country.SsKey grouped))

// ---------------------------------------------------------------------------
// Emitter — T11 keyset + dispatch correctness.
// ---------------------------------------------------------------------------

// 2026-06-25 — the migration lane stages large kinds through a `#temp` (the
// shared StagedMerge; closing its 8623 wall). Above the threshold it renders
// the atomic staged batch; below, the inline form stands (byte-identical).
[<Fact>]
let ``MigrationDependenciesEmitter: a >threshold kind renders the staged #temp batch`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ for i in 1 .. 1001 -> mkMigrationRow country.SsKey (string i) (sprintf "C%04d" i) (sprintf "Label %d" i) ] }
    let artifact = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let sql = (ArtifactByKind.toMap artifact |> Map.find country.SsKey).RenderedPhase1
    Assert.Contains("SET XACT_ABORT ON", sql)
    Assert.Contains("CREATE TABLE [#seed_", sql)
    Assert.Contains("USING [#seed_", sql)
    Assert.Contains("END CATCH", sql)

[<Fact>]
let ``MigrationDependenciesEmitter: inline mode keeps a >threshold kind inline (no #temp)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ for i in 1 .. 1001 -> mkMigrationRow country.SsKey (string i) (sprintf "C%04d" i) (sprintf "Label %d" i) ] }
    let opts = { DataEmitOptions.defaults with Staging = { Mode = DataStagingMode.Inline; Threshold = 1000; IndexThreshold = 100000 } }
    let artifact = MigrationDependenciesEmitter.emit opts catalog Profile.empty context |> mustOkEmit
    let sql = (ArtifactByKind.toMap artifact |> Map.find country.SsKey).RenderedPhase1
    Assert.DoesNotContain("#seed_", sql)

[<Fact>]
let ``MigrationDependenciesEmitter.emit produces one DataInsertScript per kind (T11 keyset)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context = MigrationDependencyContext.empty
    let artifact = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.Equal (1, Map.count map)

[<Fact>]
let ``MigrationDependenciesEmitter.emit returns no-op for kinds without migration rows`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context = MigrationDependencyContext.empty
    let artifact = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Empty script.Phase1Merges
    Assert.Equal<string> ("", script.Rendered)

[<Fact>]
let ``MigrationDependenciesEmitter.emit populates Phase1Merges for kinds with migration rows`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows =
            [ mkMigrationRow country.SsKey "1" "US" "United States"
              mkMigrationRow country.SsKey "2" "CA" "Canada" ] }
    let artifact = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Equal (2, List.length script.Phase1Merges)
    for row in script.Phase1Merges do
        Assert.Equal<SsKey> (country.SsKey, row.KindKey)

[<Fact>]
let ``MigrationDependenciesEmitter.emit Rendered MERGE shape contains V1-required clauses`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let artifact = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let r = normWs script.Rendered
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY] AS [Target]", r)
    Assert.Contains ("USING", r)
    Assert.Contains ("VALUES", r)
    Assert.Contains ("AS [Source]([ID], [CODE], [LABEL])", r)
    Assert.Contains ("ON [Target].[ID] = [Source].[ID]", r)
    Assert.Contains ("WHEN NOT MATCHED THEN INSERT", r)
    Assert.EndsWith ("GO", r)

// -- NM-73: validate-before-apply drift guard (the migration lane) -----------
// MigrationDependenciesEmitter mirrors StaticSeedsEmitter's guard: `Standard`
// is byte-identical; `ValidateBeforeApply` prepends V1's symmetric-EXCEPT
// THROW guard as its own GO batch before the MERGE.

let private topoOf (catalog: Catalog) : TopologicalOrder =
    (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle catalog).Value

[<Fact>]
let ``NM-73: MigrationDependenciesEmitter Standard verification is byte-identical to the default emit`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context = { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let viaDefault = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let viaStandard =
        MigrationDependenciesEmitter.emitWithTopo
            { DataEmitOptions.defaults with Verification = DataVerification.Standard } (topoOf catalog) catalog Profile.empty context UserRemapContext.empty
        |> mustOkEmit
    Assert.Equal<Map<SsKey, DataInsertScript>>(ArtifactByKind.toMap viaDefault, ArtifactByKind.toMap viaStandard)

[<Fact>]
let ``NM-73: MigrationDependenciesEmitter ValidateBeforeApply prepends the symmetric-EXCEPT THROW guard before the MERGE`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context = { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let artifact =
        MigrationDependenciesEmitter.emitWithTopo
            { DataEmitOptions.defaults with Verification = DataVerification.ValidateBeforeApply } (topoOf catalog) catalog Profile.empty context UserRemapContext.empty
        |> mustOkEmit
    let rendered = (ArtifactByKind.toMap artifact |> Map.find country.SsKey).Rendered
    let r = normWs rendered
    Assert.Contains ("IF EXISTS (SELECT 1 FROM [dbo].[OSUSR_TEST_COUNTRY])", r)
    Assert.Contains ("THROW 50000", r)
    let exceptCount = (r.Split([| "EXCEPT" |], System.StringSplitOptions.None)).Length - 1
    Assert.Equal (2, exceptCount)
    Assert.True (rendered.IndexOf("THROW") < rendered.IndexOf("MERGE INTO"), "the drift guard must precede the MERGE")

[<Fact>]
let ``MigrationDependenciesEmitter.emit honors CdcAwareness per-kind dispatch (slice β parity)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog profile context |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let r = normWs script.Rendered
    // CDC enabled → predicated MERGE.
    Assert.Contains ("WHEN MATCHED AND (", r)
    Assert.Contains (") THEN UPDATE SET", r)

[<Fact>]
let ``T1: MigrationDependenciesEmitter.emit is byte-deterministic across repeat invocations`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows =
            [ mkMigrationRow country.SsKey "1" "US" "United States"
              mkMigrationRow country.SsKey "2" "CA" "Canada" ] }
    let r1 = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let r2 = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog Profile.empty context |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)
    Assert.Equal<DataInsertRow list> (s1.Phase1Merges, s2.Phase1Merges)

// ---------------------------------------------------------------------------
// Composer integration (slice η + ε).
// ---------------------------------------------------------------------------

let private policyWith (composition: DataComposition) : Policy =
    { Policy.empty with
        Emission =
            { Policy.empty.Emission with
                EmitData = true
                DataComposition = composition } }

[<Fact>]
let ``compose AllRemaining: Migration rows surface alongside Static (when populated)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let artifact =
        DataEmissionComposer.composeFull
            (policyWith AllRemaining) catalog Profile.empty context UserRemapContext.empty
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Country has no Static modality, so under AllRemaining the
    // Migration emitter is the source. Phase1 is populated.
    Assert.Equal (1, List.length script.Phase1Merges)

[<Fact>]
let ``compose AllExceptStatic: Migration rows surface (Static is skipped, Migration fires)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let artifact =
        DataEmissionComposer.composeFull
            (policyWith AllExceptStatic) catalog Profile.empty context UserRemapContext.empty
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Equal (1, List.length script.Phase1Merges)

[<Fact>]
let ``compose AllData: Migration is skipped (only Static fires)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let artifact =
        DataEmissionComposer.composeFull
            (policyWith AllData) catalog Profile.empty context UserRemapContext.empty
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Country has no Static modality; AllData skips Migration; Bootstrap
    // is still a stub today → script is empty.
    Assert.Empty script.Phase1Merges

// ---------------------------------------------------------------------------
// AC-D5 (gap N2): a persisted computed column must NEVER appear in the
// CDC-aware MERGE's updatable-column set (UPDATE SET / change-detection
// predicate) nor the INSERT/USING column list. Mirrors the
// StaticSeedsEmitter AC-D5 test.
// ---------------------------------------------------------------------------

/// Country fixture with a fourth attribute (`Display`) that is a PERSISTED
/// computed column. `Code` is a non-computed sibling so the discrimination
/// is sharp.
let private mkComputedColumnKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Country"]
    let idKey = mkKey ["TestModule"; "Country"; "Id"]
    let codeKey = mkKey ["TestModule"; "Country"; "Code"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    let displayKey = mkKey ["TestModule"; "Country"; "Display"]
    let computedCfg = ComputedColumnConfig.create "[CODE] + N' - ' + [LABEL]" true |> mustOk
    {
        SsKey    = kindKey
        Name     = mkName "Country"
        Origin   = Native
        Modality = []
        Physical = Projection.Tests.Fixtures.mkTableId "dbo" "OSUSR_TEST_COUNTRY"
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
    let context = { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = MigrationDependenciesEmitter.emit DataEmitOptions.defaults catalog profile context |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let r = normWs script.Rendered
    Assert.Contains ("WHEN MATCHED AND (", r)
    // The persisted computed column DISPLAY must NOT appear anywhere (gap N2).
    Assert.DoesNotContain ("[DISPLAY]", r)
    // The non-computed sibling CODE IS updatable + in the predicate.
    Assert.Contains ("[Target].[CODE] = [Source].[CODE]", r)
    Assert.Contains ("[Target].[CODE] <> [Source].[CODE]", r)

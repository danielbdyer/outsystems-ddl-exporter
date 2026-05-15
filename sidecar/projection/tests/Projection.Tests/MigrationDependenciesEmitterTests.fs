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
        Origin   = OsNative
        Modality = []  // NOT static — populated via Migration channel
        Physical = { Schema = "dbo"; Table = "OSUSR_TEST_COUNTRY" }
        Attributes =
            [
                { SsKey = idKey;    Name = mkName "Id";    Type = Integer
                  Column = { ColumnName = "ID";    IsNullable = false }
                  IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None }
                { SsKey = codeKey;  Name = mkName "Code";  Type = Text
                  Column = { ColumnName = "CODE";  IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None }
                { SsKey = labelKey; Name = mkName "Label"; Type = Text
                  Column = { ColumnName = "LABEL"; IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None }
            ]
        References = []
        Indexes    = []
        Description = None
    }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["TestModule"]
          Name  = mkName "TestModule"
          Kinds = kinds }
    { Modules = [ m ] }

let private mkMigrationRow (kindKey: SsKey) (idValue: string) (code: string) (label: string) : MigrationDependencyRow =
    {
        KindKey    = kindKey
        Identifier = mkKey ["TestModule"; "Country"; "MigRow"; idValue]
        Values =
            Map.ofList
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

[<Fact>]
let ``MigrationDependenciesEmitter.emit produces one DataInsertScript per kind (T11 keyset)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context = MigrationDependencyContext.empty
    let artifact = MigrationDependenciesEmitter.emit catalog Profile.empty context |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.Equal (1, Map.count map)

[<Fact>]
let ``MigrationDependenciesEmitter.emit returns no-op for kinds without migration rows`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context = MigrationDependencyContext.empty
    let artifact = MigrationDependenciesEmitter.emit catalog Profile.empty context |> mustOkEmit
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
    let artifact = MigrationDependenciesEmitter.emit catalog Profile.empty context |> mustOkEmit
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
    let artifact = MigrationDependenciesEmitter.emit catalog Profile.empty context |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let r = normWs script.Rendered
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY] AS [Target]", r)
    Assert.Contains ("USING", r)
    Assert.Contains ("VALUES", r)
    Assert.Contains ("AS [Source]([ID], [CODE], [LABEL])", r)
    Assert.Contains ("ON [Target].[ID] = [Source].[ID]", r)
    Assert.Contains ("WHEN NOT MATCHED THEN INSERT", r)
    Assert.EndsWith ("GO", r)

[<Fact>]
let ``MigrationDependenciesEmitter.emit honors CdcAwareness per-kind dispatch (slice β parity)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let context =
        { Rows = [ mkMigrationRow country.SsKey "1" "US" "United States" ] }
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = MigrationDependenciesEmitter.emit catalog profile context |> mustOkEmit
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
    let r1 = MigrationDependenciesEmitter.emit catalog Profile.empty context |> mustOkEmit
    let r2 = MigrationDependenciesEmitter.emit catalog Profile.empty context |> mustOkEmit
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
        DataEmissionComposer.composeWithMigration
            (policyWith AllRemaining) catalog Profile.empty context
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
        DataEmissionComposer.composeWithMigration
            (policyWith AllExceptStatic) catalog Profile.empty context
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
        DataEmissionComposer.composeWithMigration
            (policyWith AllData) catalog Profile.empty context
        |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Country has no Static modality; AllData skips Migration; Bootstrap
    // is still a stub today → script is empty.
    Assert.Empty script.Phase1Merges

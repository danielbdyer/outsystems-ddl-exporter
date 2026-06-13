module Projection.Tests.FullExportDataBundleTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// AC-X1 (part A — the publication/seed leg) — the operator-resolved premise:
// full-export emits the static-entity (+ migration-seed) INSERT scripts for a
// from-fresh-blank-database deploy, and they are NON-OVERWRITING / idempotent
// (a MERGE, CDC-silent on unchanged rows), so a fresh DB or a re-run lands the
// same state. Until now the data emitters (`DataEmissionComposer`) were reachable
// only from tests; this wires them into the full-export bundle behind the
// config's data-emission toggles (`EmissionPolicy.EmitData`). The witnesses
// discriminate: the seed is present + idempotent when enabled, and the bundle is
// byte-identical (empty DataBundle) when disabled.

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %A" es)

let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_X1" parts |> mustOk

/// A static-entity kind with two explicit rows (Id/Code/Label).
let private countryKind () : Kind =
    let kindKey = mkKey ["Mod"; "Country"]
    let idKey = mkKey ["Mod"; "Country"; "Id"]
    let codeKey = mkKey ["Mod"; "Country"; "Code"]
    let labelKey = mkKey ["Mod"; "Country"; "Label"]
    let row code label =
        { Identifier = mkKey ["Mod"; "Country"; "Row"; code]
          Values = Map.ofList [ mkName "Id", code; mkName "Code", code; mkName "Label", label ] }
    {
        SsKey = kindKey
        Name = mkName "Country"
        Origin = Native
        Modality = [ Static [ row "US" "United States"; row "CA" "Canada" ] ]
        Physical = mkTableId "dbo" "OSUSR_X1_COUNTRY"
        Attributes =
            [ { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create ("ID") (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }
              { Attribute.create codeKey (mkName "Code") Text with Column = ColumnRealization.create ("CODE") (false) |> Result.value; IsMandatory = true }
              { Attribute.create labelKey (mkName "Label") Text with Column = ColumnRealization.create ("LABEL") (false) |> Result.value; IsMandatory = true } ]
        References = []
        Indexes = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
    }

let private staticCatalog : Catalog =
    Catalog.create
        [ { SsKey = mkKey ["Mod"]; Name = mkName "Mod"; Kinds = [ countryKind () ]; IsActive = true; ExtendedProperties = [] } ]
        []
    |> mustOk

let private emitDataPolicy : Policy =
    { Policy.empty with
        Emission = { Policy.empty.Emission with EmitData = true; DataComposition = AllRemaining } }

let private projectBundle (policy: Policy) : Compose.Outputs =
    Compose.projectWithState policy Profile.empty EmissionFolders.empty TransformGroups.empty staticCatalog
    |> fst

[<Fact>]
let ``AC-X1: full-export emits the idempotent seed bundle when data emission is enabled`` () =
    let outputs = projectBundle emitDataPolicy
    Assert.True(Map.containsKey "Data/seed.sql" outputs.DataBundle, "Data/seed.sql must be in the bundle when EmitData is on")
    let seed = Map.find "Data/seed.sql" outputs.DataBundle
    // THE NON-OVERWRITING / IDEMPOTENT DISCRIMINATOR: a MERGE with WHEN NOT
    // MATCHED INSERT — not a bare `INSERT INTO ... VALUES` (which would duplicate
    // on a re-run against a non-empty DB). This is the from-fresh-blank-DB-safe
    // shape the premise requires.
    Assert.Contains("MERGE", seed)
    Assert.Contains("WHEN NOT MATCHED", seed)
    // The static rows are carried into the script.
    Assert.Contains("United States", seed)

[<Fact>]
let ``AC-X1: data emission off leaves the bundle byte-identical (empty DataBundle)`` () =
    let outputs = projectBundle Policy.empty
    Assert.True(Map.isEmpty outputs.DataBundle, "DataBundle must be empty when EmitData is off")
    // The schema bundle is still produced (only the data leg is gated).
    Assert.False(Map.isEmpty outputs.SsdtBundle, "the SSDT schema bundle is unaffected by the data toggle")

[<Fact>]
let ``WP6 step 3: a single active lane emits only the fused seed (no redundant per-lane file)`` () =
    // The pipeline path carries no migration/bootstrap context, so only the
    // static lane has content. The fused Data/seed.sql IS that lane, so the
    // per-lane files would byte-duplicate it and are omitted (the >=2-lane
    // self-minimizing rule; DECISIONS 2026-06-13). Per-lane rendering itself
    // is witnessed at the composer level (DataEmissionComposerTests).
    let outputs = projectBundle emitDataPolicy
    Assert.True(Map.containsKey "Data/seed.sql" outputs.DataBundle)
    Assert.False(Map.containsKey "Data/StaticSeeds.sql" outputs.DataBundle, "single lane ⇒ no redundant per-lane file")
    Assert.False(Map.containsKey "Data/MigrationData.sql" outputs.DataBundle)
    Assert.False(Map.containsKey "Data/Bootstrap.sql" outputs.DataBundle)

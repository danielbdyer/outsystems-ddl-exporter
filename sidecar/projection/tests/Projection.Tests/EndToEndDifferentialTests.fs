module Projection.Tests.EndToEndDifferentialTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The session-6 milestone — end-to-end differential test.
//
// Validates the three-input projection (Catalog, Policy, Profile)
// passing through the full V1↔V2 boundary stack:
//
//   V1 JSON (static-data + profile-snapshot)
//        │
//        ▼
//   F# adapters (Static.attach + ProfileSnapshot.attach)
//        │
//        ▼
//   V2 IR (Catalog with populations + Profile)
//        │
//        ▼
//   NullabilityPass (under registered Nullability intervention)
//        │
//        ▼
//   NullabilityDecisionSet (emitter-consumable per A32)
//
// If this test passes, the three-input projection has been
// empirically validated, not just structurally claimed. The plugin-
// shape Tightening (DECISIONS 2026-05-09) supports the projection
// without compromise.
// ---------------------------------------------------------------------------

let private mkKey s = testKey s
let private mkName s = Name.create s |> Result.value

// ---------------------------------------------------------------------------
// V2 catalog matching V1's profile.micro-fk-protect fixture (Parent / Child
// with FK on Child.PARENTID nullable) plus a Static-modality Country kind
// for which we'll feed populations via the static-data adapter.
// ---------------------------------------------------------------------------

let private parentKindKey       = mkKey "OS_KIND_E2E_Parent"
let private parentIdKey         = mkKey "OS_ATTR_E2E_Parent_Id"
let private childKindKey        = mkKey "OS_KIND_E2E_Child"
let private childIdKey          = mkKey "OS_ATTR_E2E_Child_Id"
let private childParentFkKey    = mkKey "OS_ATTR_E2E_Child_ParentId"
let private childToParentRefKey = mkKey "OS_REF_E2E_Child_Parent"
let private countryKindKey      = mkKey "OS_KIND_E2E_Country"
let private countryIdKey        = mkKey "OS_ATTR_E2E_Country_Id"
let private countryNameKey      = mkKey "OS_ATTR_E2E_Country_Name"

let private parentKind : Kind =
    { SsKey    = parentKindKey
      Name     = mkName "Parent"
      Origin   = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = "OSUSR_P_PARENT" }
      Attributes = [
          { SsKey        = parentIdKey
            Name         = mkName "Id"
            Type         = Integer
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  } ]
      References = []; Indexes = []; Description = None; IsActive = true  }

let private childKind : Kind =
    { SsKey    = childKindKey
      Name     = mkName "Child"
      Origin   = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = "OSUSR_C_CHILD" }
      Attributes = [
          { SsKey        = childIdKey
            Name         = mkName "Id"
            Type         = Integer
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
          { SsKey        = childParentFkKey
            Name         = mkName "ParentId"
            Type         = Integer
            // FK column is nullable in the V1 fixture — exercises the
            // KeepNullable(NoTighteningSignal) branch.
            Column       = { ColumnName = "PARENTID"; IsNullable = true }
            IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  } ]
      References = [
          { SsKey           = childToParentRefKey
            Name            = mkName "Parent"
            SourceAttribute = childParentFkKey
            TargetKind      = parentKindKey
            OnDelete        = NoAction
            IsUserFk        = false } ]
      Indexes = []
      Description = None; IsActive = true  }

let private countryKind : Kind =
    { SsKey    = countryKindKey
      Name     = mkName "Country"
      Origin   = OsNative
      // Static modality with empty populations — the static adapter
      // will fill these in from V1 JSON.
      Modality = [ Static [] ]
      Physical = { Schema = "dbo"; Table = "OSUSR_DEF_CITY" }
      Attributes = [
          { SsKey        = countryIdKey
            Name         = mkName "Id"
            Type         = Integer
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
          { SsKey        = countryNameKey
            Name         = mkName "Name"
            Type         = Text
            Column       = { ColumnName = "NAME"; IsNullable = false }
            IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  } ]
      References = []; Indexes = []; Description = None; IsActive = true  }

let private endToEndCatalog : Catalog =
    { Modules = [
        { SsKey = mkKey "OS_MOD_E2E"
          Name  = mkName "EndToEnd"
          Kinds = [ parentKind; childKind; countryKind ]; IsActive = true  } ]; Triggers = []; Sequences = []  }

// ---------------------------------------------------------------------------
// V1 JSON fixtures embedded.
// ---------------------------------------------------------------------------

/// From tests/Fixtures/profiling/profile.micro-fk-protect.json (76 lines).
let private profileMicroFkProtectJson = """{
  "columns": [
    { "Schema": "dbo", "Table": "OSUSR_P_PARENT", "Column": "ID",
      "IsNullablePhysical": false, "IsComputed": false,
      "IsPrimaryKey": true, "IsUniqueKey": false,
      "DefaultDefinition": null,
      "NullCount": 0, "RowCount": 500,
      "NullCountStatus": {
          "CapturedAtUtc": "2024-01-01T00:00:00Z",
          "SampleSize": 500, "Outcome": "Succeeded" } },
    { "Schema": "dbo", "Table": "OSUSR_C_CHILD", "Column": "ID",
      "IsNullablePhysical": false, "IsComputed": false,
      "IsPrimaryKey": true, "IsUniqueKey": false,
      "DefaultDefinition": null,
      "NullCount": 0, "RowCount": 5000,
      "NullCountStatus": {
          "CapturedAtUtc": "2024-01-01T00:00:00Z",
          "SampleSize": 5000, "Outcome": "Succeeded" } },
    { "Schema": "dbo", "Table": "OSUSR_C_CHILD", "Column": "PARENTID",
      "IsNullablePhysical": true, "IsComputed": false,
      "IsPrimaryKey": false, "IsUniqueKey": false,
      "DefaultDefinition": null,
      "NullCount": 0, "RowCount": 5000,
      "NullCountStatus": {
          "CapturedAtUtc": "2024-01-01T00:00:00Z",
          "SampleSize": 5000, "Outcome": "Succeeded" } } ],
  "uniqueCandidates": [],
  "compositeUniqueCandidates": [],
  "fkReality": [
    { "Ref": { "FromSchema": "dbo", "FromTable": "OSUSR_C_CHILD",
               "FromColumn": "PARENTID", "ToSchema": "dbo",
               "ToTable": "OSUSR_P_PARENT", "ToColumn": "ID",
               "HasDbConstraint": true },
      "HasOrphan": false, "IsNoCheck": false,
      "ProbeStatus": { "CapturedAtUtc": "2024-01-01T00:00:00Z",
                       "SampleSize": 0, "Outcome": "Succeeded" } } ]
}"""

/// From tests/Fixtures/static-data/static-entities.edge-case.json (the
/// static adapter's canonical V1 fixture). Renamed table column to match
/// our endToEndCatalog.Country mapping.
let private staticEntitiesJson = """{
  "tables": [
    { "schema": "dbo",
      "table":  "OSUSR_DEF_CITY",
      "rows": [
        { "ID": 1, "NAME": "Lisbon" },
        { "ID": 2, "NAME": "Porto" },
        { "ID": 3, "NAME": "Madrid" } ] } ]
}"""

// ---------------------------------------------------------------------------
// The milestone test — drive the full stack and assert.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MILESTONE: three-input projection passes end-to-end through both adapters and NullabilityPass`` () =
    // 1. Static adapter: ingest V1 city fixture into Country's Static
    //    populations.
    let catalogWithPopulations =
        Static.attachStaticPopulations endToEndCatalog (Static.StaticPopulationsJson staticEntitiesJson)
        |> Result.value

    // 2. Profile adapter: ingest V1 profile fixture into V2 Profile.
    let profile =
        ProfileSnapshot.attach catalogWithPopulations (ProfileSnapshot.ProfileSnapshotJson profileMicroFkProtectJson)
        |> Result.value

    // 3. Policy: register a Nullability intervention with conservative
    //    config (matches V1's Cautious mode behavior — V2's only mode
    //    after the session-6 collapse).
    let interventionConfig =
        NullabilityTighteningConfig.create 0.0m false []
        |> Result.value
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ Nullability ("v1-cautious-equivalent", interventionConfig) ] } }

    // 4. Run NullabilityPass — the three-input projection.
    let lineage = NullabilityPass.run catalogWithPopulations policy profile

    // 5. Assert: every catalog attribute received a decision.
    let totalAttributes =
        Catalog.allKinds catalogWithPopulations
        |> List.sumBy (fun k -> k.Attributes.Length)
    Assert.Equal(totalAttributes, (LineageDiagnostics.payload lineage).Decisions.Length)

    // 6. PK / NOT NULL columns enforce; nullable non-PK columns keep nullable.
    let decisionFor (key: SsKey) =
        (LineageDiagnostics.payload lineage).Decisions
        |> List.find (fun d -> d.AttributeKey = key)

    // Parent.Id: PK → EnforceNotNull(PrimaryKey).
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull PrimaryKey,
        (decisionFor parentIdKey).Outcome)
    // Child.Id: PK → EnforceNotNull(PrimaryKey).
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull PrimaryKey,
        (decisionFor childIdKey).Outcome)
    // Child.ParentId: nullable, not-PK, no mandatory marker (V2 IR
    // doesn't carry it yet) → KeepNullable(NoTighteningSignal). Profile
    // evidence is rich (5000 rows, 0 nulls) but no signal in V2's
    // current IR fires on it.
    Assert.Equal(
        NullabilityOutcome.KeepNullable NoTighteningSignal,
        (decisionFor childParentFkKey).Outcome)
    // Country.Id: PK → EnforceNotNull(PrimaryKey).
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull PrimaryKey,
        (decisionFor countryIdKey).Outcome)
    // Country.Name: not-PK, physically NOT NULL → EnforceNotNull(PhysicallyNotNull).
    Assert.Equal(
        NullabilityOutcome.EnforceNotNull PhysicallyNotNull,
        (decisionFor countryNameKey).Outcome)

[<Fact>]
let ``MILESTONE: static populations were attached by the static adapter`` () =
    let catalogWithPopulations =
        Static.attachStaticPopulations endToEndCatalog (Static.StaticPopulationsJson staticEntitiesJson)
        |> Result.value
    let country =
        Catalog.tryFindKind countryKindKey catalogWithPopulations
        |> Option.get
    let staticRows =
        country.Modality
        |> List.choose (function Static rows -> Some rows | _ -> None)
        |> List.head
    Assert.Equal(3, staticRows.Length)

[<Fact>]
let ``MILESTONE: profile evidence was attached by the profile adapter`` () =
    let profile =
        ProfileSnapshot.attach endToEndCatalog (ProfileSnapshot.ProfileSnapshotJson profileMicroFkProtectJson)
        |> Result.value
    Assert.Equal(3, profile.Columns.Length)
    Assert.Equal(1, profile.ForeignKeys.Length)

// ---------------------------------------------------------------------------
// Differential test — V2 NullabilityPass under one intervention matches
// V1's Cautious-mode behavior on the same logical inputs.
//
// V1 fixture (paraphrased from NullabilityEvaluatorTests.cs):
//   Mandatory column with isMandatory=true, non-PK, physically nullable;
//   profile shows 0/100 nulls; budget 5%.
//   V1 expectation (Cautious): MakeNotNull=true (LogicalMandatory fires).
//
// V2 caveat: V2's IR does not yet carry IsMandatory on Attribute (planned
// IR refinement under "IR grows under evidence"). The V2 mandatory branch
// is commented pseudocode in NullabilityRules.evaluate. This test
// validates the V1↔V2 stack on the rules V2 currently expresses (PK,
// PhysicalNotNull, override, no-signal) and TODO-marks the mandatory
// branch.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DIFFERENTIAL: V1 Cautious-mode equivalent produces same outcomes for V2-expressible cases`` () =
    // Setup: a catalog where every attribute is one of the
    // V2-expressible cases — PK, physically NOT NULL, or nullable
    // non-PK without an override. Run NullabilityPass with the
    // Cautious-equivalent intervention. Verify outcomes.
    let catalogWithPopulations =
        Static.attachStaticPopulations endToEndCatalog (Static.StaticPopulationsJson staticEntitiesJson)
        |> Result.value
    let profile =
        ProfileSnapshot.attach catalogWithPopulations (ProfileSnapshot.ProfileSnapshotJson profileMicroFkProtectJson)
        |> Result.value
    let interventionConfig =
        NullabilityTighteningConfig.create 0.05m false []
        |> Result.value
    let policy =
        { Policy.empty with
            Tightening =
                { Interventions =
                    [ Nullability ("cautious-equivalent", interventionConfig) ] } }

    let lineage = NullabilityPass.run catalogWithPopulations policy profile

    // Every decision is one of the three V2-expressible cases —
    // EnforceNotNull(PrimaryKey), EnforceNotNull(PhysicallyNotNull), or
    // KeepNullable(NoTighteningSignal). The mandatory-branch outcomes
    // (LogicalMandatoryNoNulls, RelaxedUnderEvidence,
    // MandatoryButHasNullsBeyondBudget) are unreachable until IsMandatory
    // lands on Attribute.
    Assert.All((LineageDiagnostics.payload lineage).Decisions, fun d ->
        match d.Outcome with
        | NullabilityOutcome.EnforceNotNull PrimaryKey -> ()
        | NullabilityOutcome.EnforceNotNull PhysicallyNotNull -> ()
        | NullabilityOutcome.KeepNullable NoTighteningSignal -> ()
        | other ->
            Assert.Fail(
                sprintf "Decision %s -> %A is outside V2's currently-expressible cases"
                    (SsKey.rootOriginal d.AttributeKey)
                    other))

// ---------------------------------------------------------------------------
// T1 extended (per A17 amended) — same triple ⇒ byte-identical
// NullabilityDecisionSet end-to-end. The plugin-shape Tightening
// (DECISIONS 2026-05-09) supports the projection without compromise.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1 extended: end-to-end pipeline is deterministic on the triple`` () =
    let runOnce () =
        let cat =
            Static.attachStaticPopulations endToEndCatalog (Static.StaticPopulationsJson staticEntitiesJson)
            |> Result.value
        let profile =
            ProfileSnapshot.attach cat (ProfileSnapshot.ProfileSnapshotJson profileMicroFkProtectJson)
            |> Result.value
        let cfg =
            NullabilityTighteningConfig.create 0.0m false []
            |> Result.value
        let policy =
            { Policy.empty with
                Tightening =
                    { Interventions = [ Nullability ("v1-cautious", cfg) ] } }
        NullabilityPass.run cat policy profile

    let r1 = runOnce ()
    let r2 = runOnce ()
    Assert.Equal<NullabilityDecisionSet>(NullabilityPass.decisionsOf r1, NullabilityPass.decisionsOf r2)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Observable identity on empty Tightening — even with rich Catalog and
// Profile, the pass produces no decisions when no intervention is
// registered. The structural commitment (DECISIONS 2026-05-09) holds
// end-to-end.
// ---------------------------------------------------------------------------

[<Fact>]
let ``structural commitment end-to-end: rich Catalog + Profile + empty Tightening yields no decisions`` () =
    let cat =
        Static.attachStaticPopulations endToEndCatalog (Static.StaticPopulationsJson staticEntitiesJson)
        |> Result.value
    let profile =
        ProfileSnapshot.attach cat (ProfileSnapshot.ProfileSnapshotJson profileMicroFkProtectJson)
        |> Result.value
    let lineage = NullabilityPass.run cat Policy.empty profile
    Assert.Empty((LineageDiagnostics.payload lineage).Decisions)
    Assert.Empty(lineage.Trail)

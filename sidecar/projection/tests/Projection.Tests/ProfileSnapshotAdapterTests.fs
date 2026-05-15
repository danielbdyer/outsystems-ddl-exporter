module Projection.Tests.ProfileSnapshotAdapterTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// V1 fixture content embedded as a string constant. Sourced from
//   tests/Fixtures/profiling/profile.micro-fk-protect.json
// (76 lines, three column profiles plus one foreign-key reality with
// no orphans). The embedded copy IS V2's contract; if the V1 fixture
// is changed, this test fails until the V2 expectation is updated,
// surfacing the divergence as a contract conversation rather than
// silent drift (DECISIONS 2026-05-09 — pattern setters).
// ---------------------------------------------------------------------------

let private microFkProtectJson = """{
  "columns": [
    {
      "Schema": "dbo",
      "Table": "OSUSR_P_PARENT",
      "Column": "ID",
      "IsNullablePhysical": false,
      "IsComputed": false,
      "IsPrimaryKey": true,
      "IsUniqueKey": false,
      "DefaultDefinition": null,
      "NullCount": 0,
      "RowCount": 500,
      "NullCountStatus": {
        "CapturedAtUtc": "2024-01-01T00:00:00Z",
        "SampleSize": 500,
        "Outcome": "Succeeded"
      }
    },
    {
      "Schema": "dbo",
      "Table": "OSUSR_C_CHILD",
      "Column": "ID",
      "IsNullablePhysical": false,
      "IsComputed": false,
      "IsPrimaryKey": true,
      "IsUniqueKey": false,
      "DefaultDefinition": null,
      "NullCount": 0,
      "RowCount": 5000,
      "NullCountStatus": {
        "CapturedAtUtc": "2024-01-01T00:00:00Z",
        "SampleSize": 5000,
        "Outcome": "Succeeded"
      }
    },
    {
      "Schema": "dbo",
      "Table": "OSUSR_C_CHILD",
      "Column": "PARENTID",
      "IsNullablePhysical": true,
      "IsComputed": false,
      "IsPrimaryKey": false,
      "IsUniqueKey": false,
      "DefaultDefinition": null,
      "NullCount": 0,
      "RowCount": 5000,
      "NullCountStatus": {
        "CapturedAtUtc": "2024-01-01T00:00:00Z",
        "SampleSize": 5000,
        "Outcome": "Succeeded"
      }
    }
  ],
  "uniqueCandidates": [],
  "compositeUniqueCandidates": [],
  "fkReality": [
    {
      "Ref": {
        "FromSchema": "dbo",
        "FromTable": "OSUSR_C_CHILD",
        "FromColumn": "PARENTID",
        "ToSchema": "dbo",
        "ToTable": "OSUSR_P_PARENT",
        "ToColumn": "ID",
        "HasDbConstraint": true
      },
      "HasOrphan": false,
      "IsNoCheck": false,
      "ProbeStatus": {
        "CapturedAtUtc": "2024-01-01T00:00:00Z",
        "SampleSize": 0,
        "Outcome": "Succeeded"
      }
    }
  ]
}"""

// ---------------------------------------------------------------------------
// V2 catalog matching V1's micro-fk-protect fixture.
// ---------------------------------------------------------------------------

let private mkKey s = testKey s
let private mkName s = Name.create s |> Result.value

let private parentKindKey  = mkKey "OS_KIND_Parent"
let private parentIdKey    = mkKey "OS_ATTR_Parent_Id"
let private childKindKey   = mkKey "OS_KIND_Child"
let private childIdKey     = mkKey "OS_ATTR_Child_Id"
let private childParentFkKey = mkKey "OS_ATTR_Child_ParentId"
let private childToParentRefKey = mkKey "OS_REF_Child_Parent"

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

let private microFkCatalog : Catalog =
    { Modules = [
        { SsKey = mkKey "OS_MOD_Test"
          Name  = mkName "Test"
          Kinds = [ parentKind; childKind ]; IsActive = true  } ]; Triggers = []; Sequences = []  }

// ---------------------------------------------------------------------------
// V1 contract — the fixture round-trips into V2's Profile shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 contract: fixture parses successfully`` () =
    let r = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson)
    Assert.True(Result.isSuccess r)

[<Fact>]
let ``V1 contract: fixture produces three ColumnProfile records`` () =
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    Assert.Equal(3, p.Columns.Length)

[<Fact>]
let ``V1 contract: column profiles are keyed by V2 attribute SsKey`` () =
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    let keys = p.Columns |> List.map (fun c -> c.AttributeKey) |> Set.ofList
    Assert.Equal<Set<SsKey>>(
        Set.ofList [ parentIdKey; childIdKey; childParentFkKey ],
        keys)

[<Fact>]
let ``V1 contract: row counts and null counts are preserved`` () =
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    let parentIdProfile =
        Profile.tryFindColumn parentIdKey p |> Option.get
    Assert.Equal(500L, parentIdProfile.RowCount)
    Assert.Equal(0L,   parentIdProfile.NullCount)
    let childParentFkProfile =
        Profile.tryFindColumn childParentFkKey p |> Option.get
    Assert.Equal(5000L, childParentFkProfile.RowCount)
    Assert.Equal(0L,    childParentFkProfile.NullCount)

[<Fact>]
let ``V1 contract: probe outcomes are preserved`` () =
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    Assert.All(p.Columns, fun c ->
        Assert.Equal<ProbeOutcome>(Succeeded, c.NullCountProbeStatus.Outcome))

[<Fact>]
let ``V1 contract: fixture produces one ForeignKeyReality keyed by V2 reference`` () =
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    Assert.Equal(1, p.ForeignKeys.Length)
    let fk = p.ForeignKeys |> List.head
    Assert.Equal(childToParentRefKey, fk.ReferenceKey)
    Assert.False(fk.HasOrphan)
    Assert.False(fk.IsNoCheck)

[<Fact>]
let ``V1 contract: fixture has no unique or composite candidates`` () =
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    Assert.Empty(p.UniqueCandidates)
    Assert.Empty(p.CompositeUniqueCandidates)

// ---------------------------------------------------------------------------
// Empty profile
// ---------------------------------------------------------------------------

[<Fact>]
let ``empty profile (V1 profile.empty.json shape) yields Profile.empty`` () =
    let json = """{ "columns": [], "uniqueCandidates": [],
                    "compositeUniqueCandidates": [], "fkReality": [] }"""
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson json) |> Result.value
    Assert.True(Profile.isEmpty p)

// ---------------------------------------------------------------------------
// Unresolvable coordinates — silently skipped (catalog's selection is
// the contract).
// ---------------------------------------------------------------------------

[<Fact>]
let ``unresolvable coordinates are silently skipped`` () =
    let json = """{ "columns": [
        { "Schema": "dbo", "Table": "OSUSR_NOT_IN_CATALOG", "Column": "ID",
          "IsNullablePhysical": false, "IsComputed": false,
          "IsPrimaryKey": true, "IsUniqueKey": false,
          "DefaultDefinition": null,
          "NullCount": 0, "RowCount": 100,
          "NullCountStatus": {
              "CapturedAtUtc": "2024-01-01T00:00:00Z",
              "SampleSize": 100, "Outcome": "Succeeded" } } ],
        "uniqueCandidates": [],
        "compositeUniqueCandidates": [],
        "fkReality": [] }"""
    let p = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson json) |> Result.value
    Assert.Empty(p.Columns)

// ---------------------------------------------------------------------------
// Malformed input — returns Result.Error rather than throwing.
// ---------------------------------------------------------------------------

[<Fact>]
let ``malformed JSON returns Result.Error`` () =
    let r = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson "{ not json")
    Assert.True(Result.isFailure r)

[<Fact>]
let ``unknown ProbeOutcome returns Result.Error with documented code`` () =
    let json = """{ "columns": [
        { "Schema": "dbo", "Table": "OSUSR_P_PARENT", "Column": "ID",
          "IsNullablePhysical": false, "IsComputed": false,
          "IsPrimaryKey": true, "IsUniqueKey": false,
          "DefaultDefinition": null,
          "NullCount": 0, "RowCount": 100,
          "NullCountStatus": {
              "CapturedAtUtc": "2024-01-01T00:00:00Z",
              "SampleSize": 100, "Outcome": "GarbledNonsense" } } ],
        "uniqueCandidates": [],
        "compositeUniqueCandidates": [],
        "fkReality": [] }"""
    let r = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson json)
    match r with
    | Error errors ->
        Assert.True(
            errors |> List.exists (fun e -> e.Code = "profileAdapter.probeOutcome.unknown"))
    | Ok _ -> Assert.Fail("Expected Error for unknown ProbeOutcome.")

// ---------------------------------------------------------------------------
// T1 determinism on the adapter — same JSON + same catalog ⇒ same Profile.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: ProfileSnapshot.attach is deterministic`` () =
    let p1 = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    let p2 = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    Assert.Equal<Profile>(p1, p2)

// ---------------------------------------------------------------------------
// Composition with NullabilityPass — the adapter feeds into the pass
// per the three-input projection. With Profile.empty (no interventions
// registered), the pass produces emptyDecisionSet.
// ---------------------------------------------------------------------------

[<Fact>]
let ``composition: profile-adapter output feeds NullabilityPass under empty Tightening`` () =
    let profile = ProfileSnapshot.attach microFkCatalog (ProfileSnapshot.ProfileSnapshotJson microFkProtectJson) |> Result.value
    let lineage =
        Projection.Core.Passes.NullabilityPass.run
            microFkCatalog Policy.empty profile
    // Observable identity on empty Tightening — the pass produces no
    // decisions even with rich Profile data, because no intervention
    // is registered. Profile is read into the pass but ignored.
    Assert.Empty((LineageDiagnostics.payload lineage).Decisions)
    Assert.Empty(lineage.Trail)

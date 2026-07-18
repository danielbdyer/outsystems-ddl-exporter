module Projection.Tests.NoSilentDropTests

open Xunit
open Projection.Core
open Projection.Adapters.Osm
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Chapter A.0' slice ι — L3-Boundary-NoSilentDrop + IsExternal / Origin
// mapping audit. Chapter-close completion criterion.
//
// L3-Boundary-NoSilentDrop is the chapter's structural exit gate per
// `V2_PRODUCTION_CUTOVER.md` §3.3 + §6.0' / Campaign A.2: every V1
// schema concept enumerated in §3.3 has either (a) a typed Catalog
// field, OR (b) a `Diagnostic.Severity=Error` at the OSSYS-adapter
// boundary. No silent passthrough.
//
// The structural test runs in two parts:
//
//   1. **Per-concept structural witnesses** — for each V1 concept in
//      §3.3, a test asserts the V2 IR has the expected typed home.
//      The test is compile-time guaranteed by the type system: the
//      field-access succeeds iff the field exists; the runtime
//      assertion serves as documentation + a fail-fast canary if the
//      IR shape is rolled back.
//
//   2. **End-to-end JSON-fixture witness** — a fixture exercising
//      every V1-projected axis at once; the resulting Catalog carries
//      every concept across the boundary; the test asserts each axis
//      lands at the expected IR home.
//
// IsExternal / Origin mapping audit (Bucket-B → A upgrade per
// `V2_PRODUCTION_CUTOVER.md` §3.3 row): assert the JSON-path
// `parseOrigin` and rowset-path `parseOriginFromRowset` mapping rules
// preserve the V1 `isExternal=true → V2 Origin ∈
// {ExternalIndirect, ExternalDirect}` invariant. The
// mapping is currently private; the property test exercises it
// through `parseSync` on representative fixtures.
//
// Pillar 9: all axes are DataIntent (source-schema evidence
// preservation). The test surface is the chapter-close witness for
// the structural-completeness of the IR-fidelity body.
// ---------------------------------------------------------------------------

let private mkName' s = Name.create s |> Result.value
let private parseSync (source: CatalogReader.SnapshotSource) : Result<Catalog> =
    TaskSync.run (fun () -> CatalogReader.parse source)
let private firstModule (c: Catalog) : Module = c.Modules |> List.head
let private firstKind (c: Catalog) : Kind = (firstModule c).Kinds |> List.head
let private findAttr name (k: Kind) =
    k.Attributes |> List.find (fun a -> Name.value a.Name = name)

// ---------------------------------------------------------------------------
// Per-concept structural witnesses (§3.3 row-by-row).
//
// Each test asserts the IR carries the field for the named V1 concept.
// Compile-time success on the field-access witnesses the structural
// presence; runtime assertions are the documentation surface.
// ---------------------------------------------------------------------------

let private emptyKind () : Kind =
    let key = SsKey.synthesized "TEST" "K" |> Result.value
    Kind.create key (mkName' "K") (TableId.create "dbo" "T" |> Result.value) []

let private emptyAttr () : Attribute =
    let key = SsKey.synthesized "TEST" "A" |> Result.value
    Attribute.create key (mkName' "A") Integer

[<Fact>]
let ``L3-Boundary-NoSilentDrop: Kind carries Triggers (L3-S4 home)`` () =
    let k = emptyKind ()
    Assert.Empty(k.Triggers)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: Catalog carries Sequences (L3-S5 home)`` () =
    let c = mkCatalog []
    Assert.Empty(c.Sequences)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: Attribute carries DefaultValue (L3-S6 home)`` () =
    let a = emptyAttr ()
    Assert.Equal<SqlLiteral option>(None, a.DefaultValue)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: Attribute carries Computed (L3-S7 home)`` () =
    let a = emptyAttr ()
    Assert.Equal<ComputedColumnConfig option>(None, a.Computed)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: Kind carries ColumnChecks (L3-S8 home)`` () =
    let k = emptyKind ()
    Assert.Empty(k.ColumnChecks)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: Kind carries Description (L3-S9 sub-axiom)`` () =
    let k = emptyKind ()
    Assert.Equal<string option>(None, k.Description)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: Attribute carries Description (L3-S9 sub-axiom)`` () =
    let a = emptyAttr ()
    Assert.Equal<string option>(None, a.Description)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: ExtendedProperties on Module (L3-S9)`` () =
    let key = SsKey.synthesized "TEST" "M" |> Result.value
    let m = mkModule key (mkName' "M") []
    Assert.Empty(m.ExtendedProperties)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: ExtendedProperties on Kind (L3-S9)`` () =
    let k = emptyKind ()
    Assert.Empty(k.ExtendedProperties)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: ExtendedProperties on Attribute (L3-S9)`` () =
    let a = emptyAttr ()
    Assert.Empty(a.ExtendedProperties)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: ExtendedProperties on Index (L3-S9)`` () =
    let key = SsKey.synthesized "TEST" "IX" |> Result.value
    let i = Index.ofKeyColumns key (mkName' "IX") []
    Assert.Empty(i.ExtendedProperties)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: ModalityMark.Temporal variant exists (L3-S4 family)`` () =
    let cfg : TemporalConfig =
        { HistorySchema = None
          HistoryTable  = None
          PeriodStart   = None
          PeriodEnd     = None
          Retention     = Infinite }
    // Compile-time witness: Temporal is a constructible variant of
    // ModalityMark. The runtime test pins the variant + a degenerate
    // payload so a rollback would surface here.
    let mark = Temporal cfg
    let kind = { emptyKind () with Modality = [ mark ] }
    let extracted =
        kind.Modality |> List.tryPick (function Temporal t -> Some t | _ -> None)
    Assert.Equal<TemporalConfig option>(Some cfg, extracted)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: IsActive on Module / Kind / Attribute (L3-S9 sub-axiom)`` () =
    let modKey  = SsKey.synthesized "TEST" "M" |> Result.value
    let m       = mkModule modKey (mkName' "M") []
    let k       = emptyKind ()
    let a       = emptyAttr ()
    // All default to true via IRBuilders.mk*; the slice-β semantic
    // shift retired the boundary filter. Test asserts the carriage
    // exists; the IsActiveCarryThroughTests file asserts the
    // semantics through adapter fixtures.
    Assert.True(m.IsActive)
    Assert.True(k.IsActive)
    Assert.True(a.IsActive)

[<Fact>]
let ``L3-Boundary-NoSilentDrop: TableId carries Catalog (L3-S10 / L3-I10 home)`` () =
    // Chapter A.0' slice θ — V1's `db_catalog` JSON field has a typed
    // home at `TableId.Catalog : string option`. `None` represents the
    // implicit-current-database scope (V1 projects `db_catalog: null`
    // in most fixtures); explicit cross-database references land as
    // `Some catalog`.
    let tid = TableId.create "dbo" "T" |> Result.value
    Assert.Equal<string option>(None, tid.Catalog)
    let withCat = TableId.createWithCatalog "AppDb" "dbo" "T" |> Result.value
    Assert.Equal<string option>(Some "AppDb", withCat.Catalog)

// ---------------------------------------------------------------------------
// End-to-end JSON-fixture witness — every V1-projected axis at once.
//
// One fixture exercises:
//   - Triggers (γ)
//   - DEFAULT (ε)
//   - ExtendedProperties at entity level (ζ)
//   - Descriptions (α)
//   - IsActive at three levels (β)
//   - IsExternal=true → Origin = ExternalIndirect (audit)
//   - All shipped lifts simultaneously
//
// The test asserts the full carriage across the JSON-path boundary in
// one pass; per the chapter axis 2 ("twin-path discipline"), the
// rowset-path sibling fixture in IRFidelityLiftTests + IsActiveCarry-
// ThroughTests covers the rowset boundary.
// ---------------------------------------------------------------------------

let private v1KitchenSinkFixture : string =
    """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [
    { "name": "AppCore", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "User", "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false, "isExternal": true, "isActive": true,
          "description": "Platform user; cross-application identity.",
          "db_catalog": null, "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "description": "PK",
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0 },
            { "name": "Status", "physicalName": "STATUS", "originalName": null,
              "dataType": "Text", "length": 32, "precision": null,
              "scale": null, "default": "active",
              "isMandatory": false, "isIdentifier": false, "isAutoNumber": false,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0 }
          ],
          "relationships": [], "indexes": [],
          "triggers": [
            { "name": "TR_User_Audit", "isDisabled": false,
              "definition": "CREATE TRIGGER TR_User_Audit ON OSUSR_APPCORE_USER AFTER INSERT AS PRINT 'audit'" }
          ],
          "extendedProperties": [
            { "name": "MS_Description", "value": "Platform user table" }
          ] }
      ] }
  ]
}"""

[<Fact>]
let ``L3-Boundary-NoSilentDrop: kitchen-sink fixture carries every shipped axis through JSON path`` () =
    match parseSync (CatalogReader.SnapshotJson v1KitchenSinkFixture) with
    | Error errors ->
        Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok catalog ->
        let m = firstModule catalog
        let k = firstKind catalog
        let idAttr     = findAttr "Id" k
        let statusAttr = findAttr "Status" k
        // α — Descriptions
        Assert.Equal(Some "Platform user; cross-application identity.", k.Description)
        Assert.Equal(Some "PK", idAttr.Description)
        // β — IsActive on all three levels
        Assert.True(m.IsActive)
        Assert.True(k.IsActive)
        Assert.True(idAttr.IsActive)
        // γ — Triggers
        Assert.Equal(1, List.length k.Triggers)
        Assert.Equal<Name>(mkName' "TR_User_Audit", k.Triggers.[0].Name)
        // ε — DefaultValue (Text literal)
        match statusAttr.DefaultValue with
        | Some (TextLit raw) -> Assert.Equal("active", raw)
        | other -> Assert.Fail(sprintf "Expected TextLit 'active'; got %A" other)
        // ζ — ExtendedProperties (entity level)
        Assert.Equal(1, List.length k.ExtendedProperties)
        Assert.Equal("MS_Description", k.ExtendedProperties.[0].Name)
        Assert.Equal(Some "Platform user table", k.ExtendedProperties.[0].Value)
        // Origin audit — isExternal=true → ExternalIndirect
        // (JSON-path placeholder per session-20 amendment; the rowset
        // path's three-way refinement is exercised below).
        Assert.Equal(ExternalIndirect, k.Origin)

// ---------------------------------------------------------------------------
// IsExternal / Origin mapping property tests (Bucket-B → A upgrade).
//
// Three JSON-path scenarios cover the two-way placeholder; three
// rowset-path scenarios cover the three-way real. Together they pin
// the V1 `isExternal` axis to its V2 `Origin` image.
// ---------------------------------------------------------------------------

let private v1FixtureExternalEntity (isExternal: bool) : string =
    sprintf """{
  "exportedAtUtc": "2026-05-16T00:00:00.0000000+00:00",
  "modules": [
    { "name": "M", "isSystem": false, "isActive": true,
      "entities": [
        { "name": "E", "physicalName": "OSUSR_M_E",
          "isStatic": false, "isExternal": %b, "isActive": true,
          "db_catalog": null, "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0 }
          ],
          "relationships": [], "indexes": [], "triggers": [] }
      ] }
  ]
}""" isExternal

[<Fact>]
let ``Origin audit (JSON path): isExternal=false → Native`` () =
    match parseSync (CatalogReader.SnapshotJson (v1FixtureExternalEntity false)) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok c -> Assert.Equal(Native, (firstKind c).Origin)

[<Fact>]
let ``Origin audit (JSON path): isExternal=true → ExternalIndirect (placeholder per session-20)`` () =
    // The JSON path is bound by the JSON-projection-lossiness class:
    // V1's EspaceKind is dropped at the JSON projection layer, so V2
    // collapses external entities to the IS-extension placeholder.
    // The rowset path's three-way real (next test) resolves the bound.
    match parseSync (CatalogReader.SnapshotJson (v1FixtureExternalEntity true)) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok c ->
        let k = firstKind c
        Assert.Equal(ExternalIndirect, k.Origin)
        // Surjectivity into the external-Origin subset: isExternal=true
        // never produces Native.
        Assert.NotEqual<Origin>(Native, k.Origin)

// Rowset-path mappings exercise the three-way real driven by
// `ModuleRow.EspaceKind`. Reuses the existing OssyRowsetReaderTests
// fixture builders.
let private rowsetWith (isExternal: bool) (espaceKind: string option) : OssysRowsetTypes.RowsetBundle =
    let moduleRow : OssysRowsetTypes.ModuleRow =
        { EspaceId = 1; EspaceName = "M"; IsSystemModule = false
          IsActive = true; EspaceKind = espaceKind; EspaceSsKey = None }
    let kindRow : OssysRowsetTypes.KindRow =
        { EntityId = 11; EspaceId = 1; EntityName = "E"
          PhysicalTableName = "OSUSR_M_E"; DbSchema = "dbo"
          IsStatic = false; IsExternal = isExternal; IsSystemEntity = false
          IsActive = true; EntitySsKey = None; PrimaryKeySsKey = None
          Description = None }
    let idRow : OssysRowsetTypes.AttributeRow =
        { AttrId = 111; EntityId = 11; AttrName = "Id"; PhysicalCol = "ID"
          DataType = "Identifier"; DefaultValue = None; IsMandatory = true; IsIdentifier = true
          IsAutoNumber = true; Length = None; Precision = None; Scale = None
          AttrSsKey = None; IsActive = true; Description = None
          OriginalName = None; ExternalDatabaseType = None
          IsComputed = false; ComputedDefinition = None; DefaultConstraintName = None; Order = None; Collation = None; DeployedStorage = None; DeployedIsNullable = None; IsPersisted = false }
    { Modules = [ moduleRow ]; Kinds = [ kindRow ]
      Attributes = [ idRow ]; References = []; Indexes = []; IndexColumns = []; Triggers = []; ColumnChecks = []; Sequences = [] }

[<Fact>]
let ``Origin audit (rowset path): isExternal=false → Native regardless of EspaceKind`` () =
    match parseSync (CatalogReader.SnapshotRowsets (rowsetWith false (Some "eSpace"))) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok c -> Assert.Equal(Native, (firstKind c).Origin)

[<Fact>]
let ``Origin audit (rowset path): isExternal=true + EspaceKind=Extension → ExternalIndirect`` () =
    match parseSync (CatalogReader.SnapshotRowsets (rowsetWith true (Some "Extension"))) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok c -> Assert.Equal(ExternalIndirect, (firstKind c).Origin)

[<Fact>]
let ``Origin audit (rowset path): isExternal=true + EspaceKind absent → ExternalDirect`` () =
    // Per V2 adapter rule 17 (chapter 3.2 slice 3): null EspaceKind on
    // an external entity witnesses absence of an IS step → ExternalDirect.
    match parseSync (CatalogReader.SnapshotRowsets (rowsetWith true None)) with
    | Error errors -> Assert.Fail(sprintf "Expected Ok; got: %A" errors)
    | Ok c -> Assert.Equal(ExternalDirect, (firstKind c).Origin)

[<Fact>]
let ``Origin audit (rowset path): isExternal=true → never Native (Bucket-B → A invariant)`` () =
    // The L3-S3 / IsExternal invariant: `isExternal=true → Origin ∈
    // {ExternalIndirect, ExternalDirect}`, never
    // `Native`. Asserted via the disjunction over the rowset path's
    // three-way real.
    let extensionResult = parseSync (CatalogReader.SnapshotRowsets (rowsetWith true (Some "Extension")))
    let directResult    = parseSync (CatalogReader.SnapshotRowsets (rowsetWith true None))
    let externalOrigins =
        [ extensionResult; directResult ]
        |> List.choose (function
            | Ok c -> Some (firstKind c).Origin
            | Error _ -> None)
    Assert.Equal(2, List.length externalOrigins)
    Assert.All(externalOrigins, fun o -> Assert.NotEqual<Origin>(Native, o))

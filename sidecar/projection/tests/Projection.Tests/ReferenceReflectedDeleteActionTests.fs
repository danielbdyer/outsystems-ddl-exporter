module Projection.Tests.ReferenceReflectedDeleteActionTests

open Xunit
open Projection.Core
open Projection.Adapters.OssysSql
open Projection.Adapters.Osm  // OssysTranslation, OssysRowsetTypes

// ---------------------------------------------------------------------------
// WP-1b (DECISIONS 2026-07-16) — emit the reflected `#FkReality.DeleteAction`.
//
// The deployed FK's ON DELETE action is extracted into
// `#FkReality.DeleteAction` (SQL-Server vocabulary) but, before this WP, was
// consumed nowhere: `Reference.OnDelete` restated the OutSystems model's
// delete-rule code. WP-1b makes DATABASE REALITY win the emitted action for a
// physically-backed FK (`E1` — mirror `sys.foreign_keys`), while a logical-only
// reference (no reflected FK) keeps the model rule; when the model and the
// reflected action disagree the divergence is NAMED, never silently swallowed.
//
// Witness surfaces:
//   1. `OssysTranslation.chooseOnDeleteAction`  — the value the reader emits.
//   2. `OssysTranslation.deleteActionDivergence`— the divergence predicate.
//   3. `MetadataSnapshotRunner.toBundle`        — carries `ReflectedOnDelete`.
//   4. `MetadataSnapshotRunner.deleteRuleDivergences` — the named diagnostic.
// ---------------------------------------------------------------------------

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v    -> v
    | Error e -> failwithf "expected Ok, got errors: %A" e

// --- 1. chooseOnDeleteAction: reflected reality wins; model is the fallback ---

[<Fact>]
let ``WP-1b: a physically-backed FK emits the reflected delete action over the model rule`` () =
    // Model rule "Protect" maps to NO ACTION; the deployed FK reflects CASCADE.
    // Reality wins.
    let action = mustOk (OssysTranslation.chooseOnDeleteAction (Some "Protect") (Some "CASCADE"))
    Assert.Equal(Cascade, action)

[<Fact>]
let ``WP-1b: a logical-only reference (no reflected FK) keeps the model delete rule`` () =
    // No reflected action ⇒ the model's "Delete" → CASCADE stands.
    let action = mustOk (OssysTranslation.chooseOnDeleteAction (Some "Delete") None)
    Assert.Equal(Cascade, action)

[<Fact>]
let ``WP-1b: an unrepresentable reflected action (SET_DEFAULT) falls back to the model rule`` () =
    // V2's ReferenceAction DU can't carry SET_DEFAULT; rather than drop the
    // reference, the model rule ("Protect" → NO ACTION) stands.
    let action = mustOk (OssysTranslation.chooseOnDeleteAction (Some "Protect") (Some "SET_DEFAULT"))
    Assert.Equal(NoAction, action)

// --- 2. deleteActionDivergence: names a real disagreement, stays quiet otherwise ---

[<Fact>]
let ``WP-1b: model NO ACTION vs reflected CASCADE is a named divergence (model, reflected)`` () =
    match OssysTranslation.deleteActionDivergence (Some "Protect") (Some "CASCADE") with
    | Some (modelAction, reflectedAction) ->
        Assert.Equal(NoAction, modelAction)
        Assert.Equal(Cascade, reflectedAction)
    | None -> Assert.True(false, "expected a divergence")

[<Fact>]
let ``WP-1b: agreement (model CASCADE, reflected CASCADE) is not a divergence`` () =
    Assert.Equal(None, OssysTranslation.deleteActionDivergence (Some "Delete") (Some "CASCADE"))

[<Fact>]
let ``WP-1b: a logical-only reference (no reflected FK) is not a divergence`` () =
    Assert.Equal(None, OssysTranslation.deleteActionDivergence (Some "Protect") None)

[<Fact>]
let ``WP-1b: an unmapped model rule is not a divergence (the error surfaces at parse time)`` () =
    Assert.Equal(None, OssysTranslation.deleteActionDivergence (Some "Bogus") (Some "CASCADE"))

// --- 3 & 4. The toBundle seam + the surfaced diagnostic on a real snapshot ---

let private attrRow (attrId: int) (col: string) (deleteRule: string option) : MetadataSnapshotRunner.OssysAttributeRow =
    { AttrId = attrId; EntityId = 11; AttrName = col; AttrSsKey = None
      DataType = Some "Identifier"; Length = None; Precision = None; Scale = None
      DefaultValue = None
      IsMandatory = true; IsActive = true; IsAutoNumber = false
      IsIdentifier = false; RefEntityId = Some 10; OriginalName = None
      ExternalDbType = None; DeleteRule = deleteRule; PhysicalCol = col
      Description = None; Order = None }

let private referenceRow (attrId: int) : MetadataSnapshotRunner.OssysReferenceRow =
    { AttrId = attrId; RefEntityId = Some 10; RefEntityName = Some "Customer"
      RefPhysicalName = Some "OSUSR_C_CUSTOMER" }

let private fkColumnRow (parentAttrId: int) (fkObjectId: int) : MetadataSnapshotRunner.OssysFkColumnRow =
    { EntityId = 11; FkObjectId = fkObjectId; Ordinal = 1
      ParentColumn = "CUSTOMER_ID"; ReferencedColumn = "ID"
      ParentAttrId = Some parentAttrId; ParentAttrName = Some "CustomerId"
      ReferencedAttrId = Some 100; ReferencedAttrName = Some "Id" }

let private fkRealityRow (fkObjectId: int) (deleteAction: string option) : MetadataSnapshotRunner.OssysFkRealityRow =
    { EntityId = 11; FkObjectId = fkObjectId; FkName = "FK_OSUSR_O_ORDER_CUSTOMER_ID"
      DeleteAction = deleteAction; UpdateAction = None
      ReferencedObjectId = 999; ReferencedEntityId = Some 10
      ReferencedSchema = Some "dbo"; ReferencedTable = Some "OSUSR_C_CUSTOMER"
      IsNoCheck = false }

/// A snapshot carrying two references off entity 11 (Order):
///   - AttrId 201: BACKED — model "Protect" (→ NO ACTION), reflected CASCADE.
///   - AttrId 301: LOGICAL-ONLY — no reflected FK, model "Protect".
let private snapshot () : MetadataSnapshotRunner.MetadataSnapshot =
    { Modules = []; Entities = []
      Attributes =
        [ attrRow 201 "CUSTOMER_ID" (Some "Protect")
          attrRow 301 "ALT_CUSTOMER_ID" (Some "Protect") ]
      References =
        [ referenceRow 201
          referenceRow 301 ]
      PhysicalTables = []; ColumnReality = []; ColumnChecks = []; Sequences = []
      PhysColsPresent = []; Indexes = []; IndexColumns = []
      ForeignKeysReality = [ fkRealityRow 5000 (Some "CASCADE") ]
      ForeignKeyColumns  = [ fkColumnRow 201 5000 ]
      Triggers = [] }

let private referenceByAttrId (bundle: OssysRowsetTypes.RowsetBundle) (attrId: int) : OssysRowsetTypes.ReferenceRow =
    bundle.References |> List.find (fun r -> r.AttrId = attrId)

[<Fact>]
let ``WP-1b: toBundle carries the reflected delete action on a backed reference, None on a logical-only one`` () =
    let bundle = MetadataSnapshotRunner.toBundle (snapshot ())
    Assert.Equal(Some "CASCADE", (referenceByAttrId bundle 201).ReflectedOnDelete)
    Assert.Equal(None, (referenceByAttrId bundle 301).ReflectedOnDelete)

[<Fact>]
let ``WP-1b: deleteRuleDivergences names the backed reference whose model rule disagrees with reality`` () =
    let diagnostics = MetadataSnapshotRunner.deleteRuleDivergences (snapshot ())
    let d = Assert.Single(diagnostics)
    Assert.Equal<string>("adapter.ossys.fkReality.deleteActionDivergence", d.Code)
    Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
    Assert.Equal<string option>(Some "CUSTOMER_ID", Map.tryFind "physicalColumn" d.Metadata)
    Assert.Equal<string option>(Some "NO ACTION", Map.tryFind "modelAction" d.Metadata)
    Assert.Equal<string option>(Some "CASCADE", Map.tryFind "reflectedAction" d.Metadata)

[<Fact>]
let ``WP-1b: deleteRuleDivergences is silent when the model rule agrees with reality`` () =
    // Reflect NO ACTION, which is what "Protect" maps to — no disagreement.
    let agreeing =
        { snapshot () with
            ForeignKeysReality = [ fkRealityRow 5000 (Some "NO_ACTION") ] }
    Assert.Empty(MetadataSnapshotRunner.deleteRuleDivergences agreeing)

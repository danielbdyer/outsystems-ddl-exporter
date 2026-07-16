module Projection.Tests.TighteningBindingTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Pipeline
open Projection.Targets.SSDT

/// Chapter C slice C.1 — `TighteningBinding.fromConfig` coverage, as
/// amended by the estate chapter's A6 relaxation-direction re-opening
/// (DECISIONS 2026-07-15, amending 2026-06-22). Verifies the binder
/// correctly maps operator-supplied `Config.TighteningSection` entries
/// onto typed `TighteningPolicy.Interventions`; resolves per-attribute
/// and per-reference override refs against the loaded catalog; splits
/// the nullability class by direction (budget-less relaxation entries
/// bind, budgeted coercion entries stay dropped); and — the A44
/// enforcement law — every key the estate overlay emits binds AND
/// reaches emission: no expressible-but-inert key.

let private v1MinimalJson : string =
    """{
  "exportedAtUtc": "2026-05-20T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "User",
          "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null, "scale": null,
              "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "MiddleName", "physicalName": "MIDDLENAME", "originalName": null,
              "dataType": "Text", "length": 80, "precision": null, "scale": null,
              "default": null, "isMandatory": true, "isIdentifier": false, "isAutoNumber": false,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [], "indexes": [], "triggers": []
        },
        {
          "name": "Order",
          "physicalName": "OSUSR_APPCORE_ORDER",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null, "scale": null,
              "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0, "refEntityId": null,
              "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "UserId", "physicalName": "USER_ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null, "scale": null,
              "default": null, "isMandatory": false, "isIdentifier": false, "isAutoNumber": false,
              "isActive": true, "isReference": 1, "refEntityId": null,
              "refEntity_name": "User", "refEntity_physicalName": "OSUSR_APPCORE_USER",
              "reference_deleteRuleCode": "Protect", "reference_hasDbConstraint": 1,
              "external_dbType": null, "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [], "indexes": [], "triggers": []
        }
      ]
    }
  ]
}"""

let private loadCatalog () : Catalog =
    let task () = Compose.readJson v1MinimalJson
    match TaskSync.run task with
    | Ok c -> c
    | Error errs -> failwithf "test fixture invalid: %A" errs

let private emptyEntry (kind: string) (id: string) : Config.TighteningInterventionEntry =
    {
        Kind                            = kind
        Id                              = id
        NullBudget                      = None
        AllowMandatoryRelaxation        = None
        NullabilityOverrides            = []
        EnforceSingleColumnUnique       = None
        EnforceMultiColumnUnique        = None
        EnableCreation                  = None
        AllowCrossSchema                = None
        AllowNoCheckCreation            = None
        ForeignKeyOverrides             = []
        MinDistinctCountForUniqueness   = None
    }

// ----------------------------------------------------------------------
// Empty / no-section paths
// ----------------------------------------------------------------------

[<Fact>]
let ``C.1: fromConfig with None section yields TighteningPolicy.empty`` () =
    let catalog = loadCatalog ()
    let result = TighteningBinding.fromConfig catalog None
    match result with
    | Ok policy -> Assert.Equal<TighteningIntervention list>([], policy.Interventions)
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``C.1: fromConfig with empty interventions list yields empty policy`` () =
    let catalog = loadCatalog ()
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [] })
    match result with
    | Ok policy -> Assert.Equal<TighteningIntervention list>([], policy.Interventions)
    | Error es -> failwithf "expected Ok, got %A" es

// ----------------------------------------------------------------------
// The nullability direction split (DECISIONS 2026-06-22, as amended
// 2026-07-15 — the estate A6 relaxation-direction re-opening).
// ----------------------------------------------------------------------

[<Fact>]
let ``A6 amendment: a nullability entry naming a nullBudget is the coercion direction and stays DROPPED`` () =
    // nullable→NOT NULL coercion remains the team's modeling decision, not
    // the tool's (DECISIONS 2026-06-22): a budgeted entry creates NO
    // intervention (a no-op; the run proceeds, not refused). The evidence
    // hierarchy remains exercised by NullabilityPassTests directly.
    let catalog = loadCatalog ()
    let entry = { emptyEntry "nullability" "v1-style" with NullBudget = Some 0.05m }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy -> Assert.Equal<TighteningIntervention list>([], policy.Interventions)
    | Error es -> failwithf "expected Ok (coercion direction dropped, not refused), got %A" es

[<Fact>]
let ``A6 amendment: a budget-less nullability entry binds RELAXATION-ONLY with its keepNullable override resolved`` () =
    // The amendment's whole point (A44): `overrides` was expressible-but-
    // inert under the 2026-06-22 drop; a budget-less entry now binds as a
    // RelaxationOnly intervention whose overrides resolve to typed SsKeys.
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "estate-interim" with
            NullabilityOverrides = [ { AttributeRef = "AppCore.User.MiddleName"; Action = "keepNullable" } ] }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        match policy.Interventions with
        | [ TighteningIntervention.Nullability (id, config) ] ->
            Assert.Equal("estate-interim", id)
            Assert.Equal(TighteningDirection.RelaxationOnly, config.Direction)
            match config.Overrides with
            | [ o ] ->
                Assert.Equal(OverrideAction.KeepNullable, o.Action)
                Assert.True(NullabilityTighteningConfig.shouldKeepNullable o.AttributeKey config)
            | other -> failwithf "expected one resolved override, got %A" other
        | other -> failwithf "expected one Nullability intervention, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``A6 amendment: the physical Schema.Table.Column form resolves a keepNullable override too`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "estate-interim" with
            NullabilityOverrides = [ { AttributeRef = "dbo.OSUSR_APPCORE_USER.MIDDLENAME"; Action = "keepNullable" } ] }
    match TighteningBinding.fromConfig catalog (Some { Interventions = [ entry ] }) with
    | Ok policy -> Assert.Equal(1, List.length policy.Interventions)
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``A6 amendment: an unresolvable keepNullable override ref surfaces the structured refusal`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "estate-interim" with
            NullabilityOverrides = [ { AttributeRef = "AppCore.User.NoSuchColumn"; Action = "keepNullable" } ] }
    match TighteningBinding.fromConfig catalog (Some { Interventions = [ entry ] }) with
    | Ok _ -> failwith "expected Error"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "pipeline.tightening.overrideRef.unresolved")

[<Fact>]
let ``C.1: a dropped coercion-direction entry leaves its siblings intact`` () =
    let catalog = loadCatalog ()
    let budgeted = { emptyEntry "nullability" "n" with NullBudget = Some 0.1m }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ budgeted; emptyEntry "foreignKey" "fk" ] })
    match result with
    | Ok policy ->
        Assert.Equal(1, List.length policy.Interventions)
        match List.head policy.Interventions with
        | TighteningIntervention.ForeignKey (id, _) -> Assert.Equal("fk", id)
        | other -> failwithf "expected only ForeignKey, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

// ----------------------------------------------------------------------
// Per-variant binding
// ----------------------------------------------------------------------

[<Fact>]
let ``C.1: uniqueIndex intervention defaults both enforce flags to true`` () =
    let catalog = loadCatalog ()
    let entry = emptyEntry "uniqueIndex" "v1-style"
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        match List.head policy.Interventions with
        | TighteningIntervention.UniqueIndex (id, config) ->
            Assert.Equal("v1-style", id)
            Assert.True(config.EnforceSingleColumnUnique)
            Assert.True(config.EnforceMultiColumnUnique)
        | other -> failwithf "expected UniqueIndex, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``C.1: foreignKey intervention threads the live flags and stays evidence-driven`` () =
    // WP-1d: the inert AllowCrossCatalog / TreatMissingDeleteRuleAsIgnore
    // toggles were removed; the binder threads the three live flags.
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "foreignKey" "fk-ops" with
            EnableCreation = Some true
            AllowCrossSchema = Some false
            AllowNoCheckCreation = Some true }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        match List.head policy.Interventions with
        | TighteningIntervention.ForeignKey (id, config) ->
            Assert.Equal("fk-ops", id)
            Assert.True(config.EnableCreation)
            Assert.False(config.AllowCrossSchema)
            Assert.True(config.AllowNoCheckCreation)
            Assert.Equal(TighteningDirection.EvidenceDriven, config.Direction)
            Assert.Empty config.Overrides
        | other -> failwithf "expected ForeignKey, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``C.1: categoricalUniqueness intervention threads minDistinctCount`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "categoricalUniqueness" "v2-distrib" with
            MinDistinctCountForUniqueness = Some 250L }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        match List.head policy.Interventions with
        | TighteningIntervention.CategoricalUniqueness (id, config) ->
            Assert.Equal("v2-distrib", id)
            Assert.Equal(250L, config.MinDistinctCountForUniqueness)
        | other -> failwithf "expected CategoricalUniqueness, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

// ----------------------------------------------------------------------
// The per-reference overrides (the estate A6 amendment's untrack arm).
// ----------------------------------------------------------------------

[<Fact>]
let ``A6 amendment: a foreignKey entry carrying only referenceOverrides binds the SURGICAL relaxation-only form`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "foreignKey" "estate-untrack" with
            ForeignKeyOverrides = [ { ReferenceRef = "AppCore.Order.UserId"; Action = "keepUntracked" } ] }
    match TighteningBinding.fromConfig catalog (Some { Interventions = [ entry ] }) with
    | Ok policy ->
        match policy.Interventions with
        | [ TighteningIntervention.ForeignKey (id, config) ] ->
            Assert.Equal("estate-untrack", id)
            Assert.Equal(TighteningDirection.RelaxationOnly, config.Direction)
            match config.Overrides with
            | [ o ] ->
                Assert.Equal(ForeignKeyOverrideAction.KeepUntracked, o.Action)
                Assert.True(ForeignKeyTighteningConfig.shouldKeepUntracked o.ReferenceKey config)
            | other -> failwithf "expected one resolved reference override, got %A" other
        | other -> failwithf "expected one ForeignKey intervention, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``A6 amendment: a toggle beside referenceOverrides keeps the evidence-driven direction, overrides still carried`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "foreignKey" "fk-wide" with
            EnableCreation = Some true
            ForeignKeyOverrides = [ { ReferenceRef = "AppCore.Order.UserId"; Action = "keepUntracked" } ] }
    match TighteningBinding.fromConfig catalog (Some { Interventions = [ entry ] }) with
    | Ok policy ->
        match policy.Interventions with
        | [ TighteningIntervention.ForeignKey (_, config) ] ->
            Assert.Equal(TighteningDirection.EvidenceDriven, config.Direction)
            Assert.Equal(1, List.length config.Overrides)
        | other -> failwithf "expected one ForeignKey intervention, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``A6 amendment: an unknown reference override action is refused by name`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "foreignKey" "estate-untrack" with
            ForeignKeyOverrides = [ { ReferenceRef = "AppCore.Order.UserId"; Action = "dropForever" } ] }
    match TighteningBinding.fromConfig catalog (Some { Interventions = [ entry ] }) with
    | Ok _ -> failwith "expected Error"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "pipeline.tightening.referenceOverrideAction.unknown")

[<Fact>]
let ``A6 amendment: a referenceRef naming an attribute that anchors no relationship is refused by name`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "foreignKey" "estate-untrack" with
            ForeignKeyOverrides = [ { ReferenceRef = "AppCore.User.MiddleName"; Action = "keepUntracked" } ] }
    match TighteningBinding.fromConfig catalog (Some { Interventions = [ entry ] }) with
    | Ok _ -> failwith "expected Error"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "pipeline.tightening.referenceRef.noReference")

// ----------------------------------------------------------------------
// Other refusals
// ----------------------------------------------------------------------

[<Fact>]
let ``C.1: unknown intervention kind surfaces structured error`` () =
    let catalog = loadCatalog ()
    let entry = emptyEntry "noSuchKind" "x"
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok _ -> failwith "expected Error"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "pipeline.tightening.intervention.kindUnknown")

// ----------------------------------------------------------------------
// Multi-intervention composition
// ----------------------------------------------------------------------

[<Fact>]
let ``C.1: the coercion direction is dropped; every other entry binds in order`` () =
    let catalog = loadCatalog ()
    let section : Config.TighteningSection =
        { Interventions =
            [ { emptyEntry "nullability" "n1" with NullBudget = Some 0.05m }
              emptyEntry "nullability" "n2"
              emptyEntry "uniqueIndex" "u1"
              emptyEntry "foreignKey" "f1"
              emptyEntry "categoricalUniqueness" "c1" ] }
    let result = TighteningBinding.fromConfig catalog (Some section)
    match result with
    | Ok policy ->
        // n1 names a budget (coercion — filtered); n2 is budget-less
        // (relaxation-only — binds); the other three bind, order preserved.
        Assert.Equal(4, List.length policy.Interventions)
        let kinds =
            policy.Interventions
            |> List.map (function
                | TighteningIntervention.Nullability _ -> "n"
                | TighteningIntervention.UniqueIndex _ -> "u"
                | TighteningIntervention.ForeignKey _ -> "f"
                | TighteningIntervention.CategoricalUniqueness _ -> "c")
        Assert.Equal<string list>([ "n"; "u"; "f"; "c" ], kinds)
    | Error es -> failwithf "expected Ok, got %A" es

// ----------------------------------------------------------------------
// The A44 enforcement law (DECISIONS 2026-07-15, scheduled at wave A6):
// every key the estate overlay emits binds through TighteningBinding AND
// reaches emission — no expressible-but-inert key. Red if the amendment
// is skipped.
// ----------------------------------------------------------------------

[<Fact>]
let ``overlay: every emitted key binds through TighteningBinding and reaches emission — no expressible-but-inert key (A44)`` () =
    let catalog = loadCatalog ()
    // The two entry shapes the estate overlay emits: a budget-less
    // nullability entry carrying a keepNullable override, and a surgical
    // foreignKey entry carrying a keepUntracked reference override.
    let section : Config.TighteningSection =
        { Interventions =
            [ { emptyEntry "nullability" "estate-interim-nullability" with
                  NullabilityOverrides = [ { AttributeRef = "AppCore.User.MiddleName"; Action = "keepNullable" } ] }
              { emptyEntry "foreignKey" "estate-interim-untrack" with
                  ForeignKeyOverrides = [ { ReferenceRef = "AppCore.Order.UserId"; Action = "keepUntracked" } ] } ] }
    let tightening =
        match TighteningBinding.fromConfig catalog (Some section) with
        | Ok p -> p
        | Error es -> failwithf "the overlay-shaped config must bind: %A" es
    let policy = { Policy.empty with Tightening = tightening }
    // The passes discharge the bound posture into decisions...
    let nullDecisions =
        NullabilityPass.decisionsOf ((NullabilityPass.registered policy Profile.empty).Run catalog)
    let fkDecisions =
        ForeignKeyPass.decisionsOf ((ForeignKeyPass.registered policy Profile.empty).Run catalog)
    // ...and the decisions project onto the emitter's overlay.
    let overlay =
        ComposeState.initial catalog
        |> ComposeState.withNullabilityDecisions nullDecisions
        |> ComposeState.withForeignKeyDecisions fkDecisions
        |> DecisionOverlay.ofComposeState
    Assert.Equal(1, Set.count overlay.KeepNullable)
    Assert.Equal(1, Set.count overlay.DropFk)
    // The coercion direction stays dropped: nothing tightens, nothing
    // rides NOCHECK — the posture only relaxes what it names.
    Assert.Empty overlay.EnforceNotNull
    Assert.Empty overlay.NoCheckFk
    // Emission: the named column loosens to NULL; the named relationship
    // is not created. The baseline run proves both are the posture's
    // doing, not the fixture's.
    let lineOf (marker: string) (text: string) : string =
        text.Split('\n') |> Array.find (fun l -> l.Contains marker)
    let relaxed = SsdtDdlEmitter.statementsWith overlay catalog |> Render.toText
    Assert.DoesNotContain("NOT NULL", lineOf "[MIDDLENAME]" relaxed)
    Assert.DoesNotContain("FOREIGN KEY", relaxed)
    let baseline = SsdtDdlEmitter.statements catalog |> Render.toText
    Assert.Contains("NOT NULL", lineOf "[MIDDLENAME]" baseline)
    Assert.Contains("FOREIGN KEY", baseline)

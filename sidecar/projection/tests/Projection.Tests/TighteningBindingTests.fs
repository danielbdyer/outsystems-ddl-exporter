module Projection.Tests.TighteningBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter C slice C.1 — `TighteningBinding.fromConfig` coverage.
/// Verifies the binder correctly maps operator-supplied
/// `Config.TighteningSection` entries onto typed
/// `TighteningPolicy.Interventions`; resolves per-attribute override
/// refs (both logical and physical paths) against the loaded catalog;
/// surfaces structured errors on unresolvable refs + unknown kinds.

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
        AllowCrossCatalog               = None
        TreatMissingDeleteRuleAsIgnore  = None
        AllowNoCheckCreation            = None
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
// Per-variant binding
// ----------------------------------------------------------------------

[<Fact>]
let ``C.1: nullability intervention with no overrides binds with defaults`` () =
    let catalog = loadCatalog ()
    let entry = emptyEntry "nullability" "v1-style"
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        Assert.Equal(1, List.length policy.Interventions)
        match List.head policy.Interventions with
        | TighteningIntervention.Nullability (id, config) ->
            Assert.Equal("v1-style", id)
            Assert.Equal(0m, config.NullBudget)
            Assert.False(config.AllowMandatoryRelaxation)
            Assert.Equal<TighteningOverride list>([], config.Overrides)
        | other -> failwithf "expected Nullability, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``C.1: nullability intervention with explicit nullBudget and allowMandatoryRelaxation honored`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "ops" with
            NullBudget = Some 0.001m
            AllowMandatoryRelaxation = Some true }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        match List.head policy.Interventions with
        | TighteningIntervention.Nullability (_, config) ->
            Assert.Equal(0.001m, config.NullBudget)
            Assert.True(config.AllowMandatoryRelaxation)
        | other -> failwithf "expected Nullability, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

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
let ``C.1: foreignKey intervention threads all 5 flags`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "foreignKey" "fk-ops" with
            EnableCreation = Some true
            AllowCrossSchema = Some false
            AllowCrossCatalog = Some false
            TreatMissingDeleteRuleAsIgnore = Some true
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
            Assert.False(config.AllowCrossCatalog)
            Assert.True(config.TreatMissingDeleteRuleAsIgnore)
            Assert.True(config.AllowNoCheckCreation)
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
// Override resolution
// ----------------------------------------------------------------------

[<Fact>]
let ``C.1: override attributeRef resolves via logical Module.Entity.Attribute form`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "v1-style" with
            NullabilityOverrides = [
                { AttributeRef = "AppCore.User.MiddleName"; Action = "keepNullable" }
            ] }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        match List.head policy.Interventions with
        | TighteningIntervention.Nullability (_, config) ->
            Assert.Equal(1, List.length config.Overrides)
            Assert.Equal(KeepNullable, (List.head config.Overrides).Action)
        | other -> failwithf "expected Nullability, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``C.1: override attributeRef resolves via physical Schema.Table.Column form`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "v1-style" with
            NullabilityOverrides = [
                { AttributeRef = "dbo.OSUSR_APPCORE_USER.MIDDLENAME"; Action = "keepNullable" }
            ] }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok policy ->
        match List.head policy.Interventions with
        | TighteningIntervention.Nullability (_, config) ->
            Assert.Equal(1, List.length config.Overrides)
        | other -> failwithf "expected Nullability, got %A" other
    | Error es -> failwithf "expected Ok, got %A" es

[<Fact>]
let ``C.1: unresolvable override attributeRef surfaces structured error`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "v1-style" with
            NullabilityOverrides = [
                { AttributeRef = "NoSuchModule.NoSuchEntity.NoSuchAttribute"
                  Action       = "keepNullable" }
            ] }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok _ -> failwith "expected Error"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "pipeline.tightening.overrideRef.unresolved")

[<Fact>]
let ``C.1: malformed override attributeRef (not 3-part) surfaces structured error`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "v1-style" with
            NullabilityOverrides = [
                { AttributeRef = "TooFew"; Action = "keepNullable" }
            ] }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok _ -> failwith "expected Error"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "pipeline.tightening.overrideRef.shape")

[<Fact>]
let ``C.1: unknown override action surfaces structured error`` () =
    let catalog = loadCatalog ()
    let entry =
        { emptyEntry "nullability" "v1-style" with
            NullabilityOverrides = [
                { AttributeRef = "AppCore.User.MiddleName"; Action = "forceNotNull" }
            ] }
    let result =
        TighteningBinding.fromConfig
            catalog
            (Some { Interventions = [ entry ] })
    match result with
    | Ok _ -> failwith "expected Error"
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "pipeline.tightening.overrideAction.unknown")

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
let ``C.1: all four intervention kinds in one config produce all four typed interventions`` () =
    let catalog = loadCatalog ()
    let section : Config.TighteningSection =
        { Interventions =
            [ emptyEntry "nullability" "n1"
              emptyEntry "uniqueIndex" "u1"
              emptyEntry "foreignKey" "f1"
              emptyEntry "categoricalUniqueness" "c1" ] }
    let result = TighteningBinding.fromConfig catalog (Some section)
    match result with
    | Ok policy ->
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

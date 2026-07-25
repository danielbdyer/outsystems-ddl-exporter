module Projection.Tests.BridgeRowStagingBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// The bridge-retarget / bridge-row-staging BINDERS against a real catalog — the
/// three surfaces a config declaration is resolved through, each fail-closed:
///
///   * **FK discovery** (`reference.allReferencesTo`) — one declaration expands to
///     one plan per reference in the RESOLVED model scope whose target is the named
///     entity, so "retarget every FK pointing at this table" needs no hand
///     enumeration. Deterministic order; matching nothing is a NAMED refusal, never
///     a silent no-op. The explicit form (`{ module, entity, relationship }`) still
///     binds exactly one.
///   * **`BridgeKeyDeclaredUnique`** — a single-column UNIQUE index/constraint must
///     be DECLARED on the bridge key. Distinct from the data-level duplicate count:
///     SQL Server refuses `REFERENCES <bridge>(<col>)` without one, so a retarget
///     that clears every other check would still fail at DEPLOY. A composite unique
///     index does not satisfy it.
///   * **`scope`** — `referenced` (default, minimal blast radius) vs
///     `allSourceRows` (every source row, so a later child insert naming any source
///     row cannot violate the retargeted FK).

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error (es: ValidationError list) -> failwithf "expected Ok, got %A" es

let private mustFail (r: Result<'a>) : ValidationError list =
    match r with
    | Ok _ -> failwith "expected a refusal, got Ok"
    | Error es -> es

let private hasCode (code: string) (es: ValidationError list) = es |> List.exists (fun e -> e.Code = code)
let private messageOf (code: string) (es: ValidationError list) =
    es |> List.tryFind (fun e -> e.Code = code) |> Option.map (fun e -> e.Message) |> Option.defaultValue ""

// ---------------------------------------------------------------------------
// The fixture: a source kind, a bridge kind whose key is a NON-pk column, and
// TWO child kinds carrying THREE references to the source — so discovery has
// something to discover and the explicit form has one row to pick out.
// ---------------------------------------------------------------------------

let private attrJson (name: string) (physical: string) (dataType: string) (mandatory: bool) (isPk: bool) =
    sprintf """{ "name": "%s", "physicalName": "%s", "originalName": null,
                 "dataType": "%s", "length": null, "precision": null, "scale": null,
                 "default": null, "isMandatory": %b, "isIdentifier": %b, "isAutoNumber": %b,
                 "isActive": true, "isReference": 0, "refEntityId": null,
                 "refEntity_name": null, "refEntity_physicalName": null,
                 "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
                 "external_dbType": null, "physical_isPresentButInactive": 0 }"""
            name physical dataType mandatory isPk isPk

let private refJson (name: string) (physical: string) (target: string) (targetPhysical: string) =
    sprintf """{ "name": "%s", "physicalName": "%s", "originalName": null,
                 "dataType": "Identifier", "length": null, "precision": null, "scale": null,
                 "default": null, "isMandatory": false, "isIdentifier": false, "isAutoNumber": false,
                 "isActive": true, "isReference": 1, "refEntityId": 42,
                 "refEntity_name": "%s", "refEntity_physicalName": "%s",
                 "reference_deleteRuleCode": null, "reference_hasDbConstraint": 1,
                 "external_dbType": null, "physical_isPresentButInactive": 0 }"""
            name physical target targetPhysical

let private entityJson (name: string) (physical: string) (attrs: string list) (indexes: string) =
    sprintf """{ "name": "%s", "physicalName": "%s", "isStatic": false, "isExternal": false,
                 "isActive": true, "db_catalog": null, "db_schema": "dbo",
                 "attributes": [ %s ], "relationships": [], "indexes": [ %s ], "triggers": [] }"""
            name physical (String.concat ", " attrs) indexes

/// `bridgeKeyIndex` selects how (or whether) uniqueness is DECLARED on the
/// bridge key: none / single-column unique / composite unique.
type private BridgeKeyIndex =
    | NoIndex
    | SingleColumnUnique
    | CompositeUnique
    | SingleColumnNonUnique

let private modelJson (bridgeKeyIndex: BridgeKeyIndex) (mandatoryExtra: bool) : string =
    let ix =
        match bridgeKeyIndex with
        | NoIndex -> ""
        | SingleColumnUnique ->
            """{ "name": "UQ_Bridge_PartyRef", "isPrimary": false, "isUnique": true,
                 "columns": [ { "attribute": "PartyRef", "ordinal": 0 } ] }"""
        | CompositeUnique ->
            """{ "name": "UQ_Bridge_PartyRef_Tenant", "isPrimary": false, "isUnique": true,
                 "columns": [ { "attribute": "PartyRef", "ordinal": 0 }, { "attribute": "TenantRef", "ordinal": 1 } ] }"""
        | SingleColumnNonUnique ->
            """{ "name": "IX_Bridge_PartyRef", "isPrimary": false, "isUnique": false,
                 "columns": [ { "attribute": "PartyRef", "ordinal": 0 } ] }"""
    let bridgeAttrs =
        [ attrJson "Id" "ID" "Identifier" true true
          attrJson "PartyRef" "PARTYREF" "Identifier" true false
          attrJson "TenantRef" "TENANTREF" "Identifier" true false
          attrJson "ExternalRef" "EXTERNALREF" "Text" false false
          attrJson "Label" "LABEL" "Text" true false ]
        @ (if mandatoryExtra then [ attrJson "CategoryRef" "CATEGORYREF" "Identifier" true false ] else [])
    sprintf """{
  "exportedAtUtc": "2026-07-25T00:00:00.0000000+00:00",
  "modules": [
    { "name": "Core", "isSystem": false, "isActive": true,
      "entities": [ %s, %s, %s, %s ] } ] }"""
        (entityJson "Party" "OSUSR_CORE_PARTY"
            [ attrJson "Id" "ID" "Identifier" true true
              attrJson "ExternalRef" "EXTERNALREF" "Text" false false
              attrJson "Label" "LABEL" "Text" true false ] "")
        (entityJson "PartyBridge" "OSUSR_CORE_PARTYBRIDGE" bridgeAttrs ix)
        // Two references to Party on one child, one on another — three discoverable.
        (entityJson "Ticket" "OSUSR_CORE_TICKET"
            [ attrJson "Id" "ID" "Identifier" true true
              refJson "OpenedBy" "OPENEDBY" "Party" "OSUSR_CORE_PARTY"
              refJson "ClosedBy" "CLOSEDBY" "Party" "OSUSR_CORE_PARTY" ] "")
        (entityJson "Invoice" "OSUSR_CORE_INVOICE"
            [ attrJson "Id" "ID" "Identifier" true true
              refJson "IssuedBy" "ISSUEDBY" "Party" "OSUSR_CORE_PARTY" ] "")

let private loadCatalog (ix: BridgeKeyIndex) (mandatoryExtra: bool) : Catalog =
    match (Compose.readJson (modelJson ix mandatoryExtra)).GetAwaiter().GetResult() with
    | Ok c -> c
    | Error es -> failwithf "fixture invalid: %A" es

// ---------------------------------------------------------------------------
// Config text — the operator's declaration.
// ---------------------------------------------------------------------------

let private cfg (retargetRef: string) (stagingExtras: string) : Config.Config =
    sprintf """{
  "model": { "path": "ignored.json" },
  "output": { "dir": "out/" },
  "overrides": {
    "bridgeRetargets": [
      { "id": "party-bridge", "reference": %s,
        "bridge": { "module": "Core", "entity": "PartyBridge", "attribute": "PartyRef" } }
    ],
    "bridgeRowStaging": [
      { "id": "party-supply"%s,
        "source": { "module": "Core", "entity": "Party",       "key": "Id",       "identity": "ExternalRef" },
        "bridge": { "module": "Core", "entity": "PartyBridge", "key": "PartyRef", "identity": "ExternalRef" },
        "insertMappings":  [ { "bridge": "Label", "from": "Label" } ],
        "insertConstants": [ { "bridge": "TenantRef", "value": 1 } ] }
    ]
  }
}""" retargetRef stagingExtras
    |> Config.parse
    |> mustOk

let private explicitRef = """{ "module": "Core", "entity": "Ticket", "relationship": "OpenedBy" }"""
let private discoveryRef = """{ "allReferencesTo": { "module": "Core", "entity": "Party" } }"""

// ===========================================================================
// FK discovery — `reference.allReferencesTo`
// ===========================================================================

[<Fact>]
let ``discovery: allReferencesTo expands to one plan per reference targeting the entity`` () =
    let catalog = loadCatalog SingleColumnUnique false
    let policy = mustOk (BridgeRetargetBinding.bindAll catalog Map.empty (cfg discoveryRef "").Overrides.BridgeRetargets)
    // Three references target Party: Ticket.OpenedBy, Ticket.ClosedBy, Invoice.IssuedBy.
    Assert.Equal(3, List.length policy.Plans)
    // Each expanded plan is separately addressable — the declared id plus the
    // reference's own coordinate — so evidence/artifacts/lineage never collide.
    let ids = policy.Plans |> List.map (fun p -> p.Profile.RetargetId) |> List.sort
    Assert.Equal<string list>(
        [ "party-bridge/Core.Invoice.IssuedBy"; "party-bridge/Core.Ticket.ClosedBy"; "party-bridge/Core.Ticket.OpenedBy" ],
        ids)
    // Every plan retargets through the SAME bridge attribute.
    Assert.Single(policy.Plans |> List.map (fun p -> p.BridgeAttributeKey) |> List.distinct) |> ignore

[<Fact>]
let ``discovery: the expansion is deterministic (same model, same order)`` () =
    let catalog = loadCatalog SingleColumnUnique false
    let ids () =
        (mustOk (BridgeRetargetBinding.bindAll catalog Map.empty (cfg discoveryRef "").Overrides.BridgeRetargets)).Plans
        |> List.map (fun p -> p.Profile.RetargetId)
    Assert.Equal<string list>(ids (), ids ())

[<Fact>]
let ``discovery: matching NOTHING is a named refusal, never a silent no-op`` () =
    // PartyBridge is a real entity but no reference targets it, so a discovery
    // declaration aimed at it would derive nothing — refused rather than inert.
    let catalog = loadCatalog SingleColumnUnique false
    let noTargets = """{ "allReferencesTo": { "module": "Core", "entity": "PartyBridge" } }"""
    let errs = mustFail (BridgeRetargetBinding.bindAll catalog Map.empty (cfg noTargets "").Overrides.BridgeRetargets)
    Assert.True(hasCode "pipeline.config.bridgeRetargets.discoveryEmpty" errs)

[<Fact>]
let ``discovery: an unresolvable target entity is a named refusal`` () =
    let catalog = loadCatalog SingleColumnUnique false
    let ghost = """{ "allReferencesTo": { "module": "Core", "entity": "Ghost" } }"""
    let errs = mustFail (BridgeRetargetBinding.bindAll catalog Map.empty (cfg ghost "").Overrides.BridgeRetargets)
    Assert.True(hasCode "pipeline.config.bridgeRetargets.entity.notFound" errs)

[<Fact>]
let ``the explicit reference form still binds exactly one plan`` () =
    let catalog = loadCatalog SingleColumnUnique false
    let policy = mustOk (BridgeRetargetBinding.bindAll catalog Map.empty (cfg explicitRef "").Overrides.BridgeRetargets)
    let plan = List.exactlyOne policy.Plans
    // The explicit form keeps the DECLARED id verbatim (no coordinate suffix), so
    // an existing evidence file keyed by that id keeps working.
    Assert.Equal("party-bridge", plan.Profile.RetargetId)

[<Fact>]
let ``discovery feeds ONE staging lane: every discovered FK links to the same declaration`` () =
    let catalog = loadCatalog SingleColumnUnique false
    let resolved = List.exactlyOne (mustOk (BridgeRowStagingBinding.fromConfig catalog (cfg discoveryRef "")))
    // All three discovered references drive the one supply lane, so the referenced
    // keys are read across every retargeting FK column in a single acquisition.
    Assert.Equal(3, List.length resolved.Links)
    let fkColumns =
        resolved.Links |> List.map (fun l -> ColumnRealization.columnNameText l.FkAttribute.Column) |> List.sort
    Assert.Equal<string list>([ "CLOSEDBY"; "ISSUEDBY"; "OPENEDBY" ], fkColumns)

// ===========================================================================
// BridgeKeyDeclaredUnique — the DEPLOY-time gate
// ===========================================================================

let private declaredUniqueOf (ix: BridgeKeyIndex) : bool =
    let catalog = loadCatalog ix false
    let policy = mustOk (BridgeRetargetBinding.bindAll catalog Map.empty (cfg explicitRef "").Overrides.BridgeRetargets)
    (List.exactlyOne policy.Plans).Profile.BridgeKeyDeclaredUnique

[<Fact>]
let ``declared uniqueness: a single-column UNIQUE index on the bridge key satisfies the check`` () =
    Assert.True(declaredUniqueOf SingleColumnUnique)

[<Fact>]
let ``declared uniqueness: no index at all does NOT satisfy it (SQL Server would refuse the FK)`` () =
    Assert.False(declaredUniqueOf NoIndex)

[<Fact>]
let ``declared uniqueness: a COMPOSITE unique index does NOT satisfy it`` () =
    // A single-column FK cannot target one member of a multi-column key.
    Assert.False(declaredUniqueOf CompositeUnique)

[<Fact>]
let ``declared uniqueness: a non-unique single-column index does NOT satisfy it`` () =
    Assert.False(declaredUniqueOf SingleColumnNonUnique)

[<Fact>]
let ``declared uniqueness BLOCKS the retarget even when every DATA fact is clean`` () =
    // The failure shape this check exists to prevent: data-unique, no nulls, full
    // coverage, evidence supplied — and the deploy would still fail. It must BLOCK.
    let dataClean : BridgeRetargetEvidence =
        { UnresolvedThroughBridge = 0L; BrokenOriginalParent = 0L; OrphanedBridgeRows = 0L
          PayloadConflicts = 0L; BridgeKeyDuplicates = 0L; BridgeKeyNulls = 0L
          IdentityEvidence = BridgeIdentityEvidence.Present }
    let catalog = loadCatalog NoIndex false
    let evidence = Map.ofList [ "party-bridge", dataClean ]
    let policy = mustOk (BridgeRetargetBinding.bindAll catalog evidence (cfg explicitRef "").Overrides.BridgeRetargets)
    let decision = BridgeRetarget.evaluate (List.exactlyOne policy.Plans).Profile
    Assert.False(BridgeRetarget.retargetCleared decision)
    match decision.Retargeting with
    | BridgeReadiness.Blocked cs -> Assert.Contains(BridgeCheck.BridgeKeyDeclaredUnique, cs)
    | other -> Assert.Fail(sprintf "expected Blocked, got %A" other)
    // ... and with the index declared, the same evidence CLEARS it.
    let sound = loadCatalog SingleColumnUnique false
    let policy2 = mustOk (BridgeRetargetBinding.bindAll sound evidence (cfg explicitRef "").Overrides.BridgeRetargets)
    Assert.True(BridgeRetarget.retargetCleared (BridgeRetarget.evaluate (List.exactlyOne policy2.Plans).Profile))

// ===========================================================================
// scope — referenced (default) vs allSourceRows
// ===========================================================================

[<Fact>]
let ``scope: absent defaults to Referenced (the minimal blast radius)`` () =
    let entry = List.exactlyOne (cfg explicitRef "").Overrides.BridgeRowStaging
    Assert.Equal(Config.BridgeStagingScope.Referenced, entry.Scope)

[<Fact>]
let ``scope: allSourceRows parses and threads to the resolved staging`` () =
    let c = cfg explicitRef """, "scope": "allSourceRows" """
    Assert.Equal(Config.BridgeStagingScope.AllSourceRows, (List.exactlyOne c.Overrides.BridgeRowStaging).Scope)
    let catalog = loadCatalog SingleColumnUnique false
    let resolved = List.exactlyOne (mustOk (BridgeRowStagingBinding.fromConfig catalog c))
    Assert.Equal(Config.BridgeStagingScope.AllSourceRows, resolved.Scope)

[<Fact>]
let ``scope: the value is case-insensitive`` () =
    let c = cfg explicitRef """, "scope": "AllSourceRows" """
    Assert.Equal(Config.BridgeStagingScope.AllSourceRows, (List.exactlyOne c.Overrides.BridgeRowStaging).Scope)

[<Fact>]
let ``scope: an unrecognized value is a named refusal, never a silent default`` () =
    let json = """{ "model": { "path": "m.json" }, "output": { "dir": "out/" },
                    "overrides": { "bridgeRowStaging": [
                      { "id": "x", "scope": "everything",
                        "source": { "module": "Core", "entity": "Party",       "key": "Id",       "identity": "ExternalRef" },
                        "bridge": { "module": "Core", "entity": "PartyBridge", "key": "PartyRef", "identity": "ExternalRef" } } ] } }"""
    let errs = mustFail (Config.parse json)
    Assert.True(hasCode "pipeline.config.overrides.bridgeRowStaging.scopeUnknown" errs
                || hasCode "overrides.bridgeRowStaging.scopeUnknown" errs
                || errs |> List.exists (fun e -> e.Code.EndsWith "scopeUnknown"))

// ===========================================================================
// The mandatory-coverage guard still names what a complete insert row needs.
// ===========================================================================

[<Fact>]
let ``coverage: an uncovered mandatory bridge column refuses at bind time and names it`` () =
    // CategoryRef is mandatory on the bridge and covered by neither key, identity,
    // mapping, nor constant — so no complete row could be built.
    let catalog = loadCatalog SingleColumnUnique true
    let errs = mustFail (BridgeRowStagingBinding.fromConfig catalog (cfg explicitRef ""))
    Assert.True(hasCode "pipeline.config.bridgeRowStaging.mandatoryUncovered" errs)
    Assert.Contains("CategoryRef", messageOf "pipeline.config.bridgeRowStaging.mandatoryUncovered" errs)

// ===========================================================================
// The SHIPPED sample is executable documentation — it must parse.
//
// No test previously parsed any `examples/*.sample.json`, so a sample could drift
// from the parser silently and an operator copying it would hit a refusal that
// looks like their mistake. (The same drift class as a hand-maintained refusal
// hint: two places state the config shape, only one is checked.) This pins the
// bridge samples to the live parser.
// ===========================================================================

/// Walk up from the test assembly to the directory holding `examples/`.
let private examplesDir () : string option =
    let rec up (dir: System.IO.DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = System.IO.Path.Combine(d.FullName, "examples")
            if System.IO.Directory.Exists candidate then Some candidate
            else up (Option.ofObj d.Parent)
    up (Some (System.IO.DirectoryInfo(System.AppContext.BaseDirectory)))

[<Fact>]
let ``the shipped cutover sample parses and carries the documented shape`` () =
    match examplesDir () with
    | None -> ()   // not laid out as the repo (packaged run) — nothing to check
    | Some dir ->
        let path = System.IO.Path.Combine(dir, "bridge-retarget-cutover.sample.json")
        if not (System.IO.File.Exists path) then Assert.Fail(sprintf "missing sample: %s" path)
        let cfg = mustOk (Config.parse (System.IO.File.ReadAllText path))
        // The retarget uses the DISCOVERY form.
        let rt = List.exactlyOne cfg.Overrides.BridgeRetargets
        match rt.Reference with
        | Config.BridgeRetargetReference.AllReferencesTo _ -> ()
        | other -> Assert.Fail(sprintf "sample should demonstrate discovery, got %A" other)
        // The staging declares the wider scope, a mapping, and a constant.
        let st = List.exactlyOne cfg.Overrides.BridgeRowStaging
        Assert.Equal(Config.BridgeStagingScope.AllSourceRows, st.Scope)
        Assert.NotEmpty st.InsertMappings
        Assert.NotEmpty st.InsertConstants
        // Both gates are greenlit in the sample, and the opt-in group is on.
        let modes = cfg.Emission.Signoff |> List.map (fun a -> WriteSignoff.modeLabel a.Mode) |> List.sort
        Assert.Equal<string list>([ "bridge-retarget"; "bridge-row-staging" ], modes)
        Assert.Contains("BridgeRetarget", cfg.Policy.TransformGroups |> List.map (fun g -> g.Name))

[<Fact>]
let ``the shipped evidence sample parses (the file-based alternative)`` () =
    match examplesDir () with
    | None -> ()
    | Some dir ->
        let path = System.IO.Path.Combine(dir, "bridge-retarget-evidence.sample.json")
        if not (System.IO.File.Exists path) then Assert.Fail(sprintf "missing sample: %s" path)
        let loaded = mustOk (BridgeRetargetBinding.loadEvidence (Some { Path = path }))
        // Two worked entries: one that clears, one that stays blocked.
        Assert.Equal(2, Map.count loaded)
        Assert.True(loaded |> Map.exists (fun _ e -> e.UnresolvedThroughBridge = 0L && e.BridgeKeyNulls = 0L))
        Assert.True(loaded |> Map.exists (fun _ e -> e.UnresolvedThroughBridge > 0L))

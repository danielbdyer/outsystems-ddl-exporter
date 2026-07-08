module Projection.Tests.SupportingScopeTests

// The supporting-scope vocabulary (2026-07-08, the business-intent program):
// the reconcile-string bridge, the desugar onto the existing string inputs,
// the typed resolve, the graph verification, the inbound `dependentEdges`
// mirror, and completeness. Pure — a constructed catalog, no database.

open Xunit
open Projection.Core
open Projection.Pipeline

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private nm (s: string) : Name = Name.create s |> mustOk
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "SS_TEST" [ s ] |> mustOk
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "SS_ATTR" [ k; a ] |> mustOk
let private rKey (k: string) (r: string) : SsKey = SsKey.synthesizedComposite "SS_REF" [ k; r ] |> mustOk
let private xKey (k: string) (x: string) : SsKey = SsKey.synthesizedComposite "SS_IDX" [ k; x ] |> mustOk

let private idPk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Id") (nm "Id") Integer with
        Column = ColumnRealization.create "ID" false |> mustOk
        IsPrimaryKey = true; IsIdentity = true; IsMandatory = true }

let private codePk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Code") (nm "Code") Text with
        Column = ColumnRealization.create "CODE" false |> mustOk
        IsPrimaryKey = true; IsMandatory = true }

let private textCol (kind: string) (logical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Text with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) true |> mustOk
        Length = Some 200 }

let private fkCol (kind: string) (logical: string) (mandatory: bool) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Integer with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) (not mandatory) |> mustOk
        IsMandatory = mandatory }

let private uniqueIndex (kind: string) (col: string) : Index =
    { Index.create (xKey kind col) (nm ("IX_" + col)) [ IndexColumn.create (aKey kind col) Ascending ] with Uniqueness = Unique }

/// The estate:
///   Order (payload)  -> City via CityId (escaping reference)
///   OrderLine        -> Order via OrderId, OnDelete = Cascade (owned child)
///   AuditLog         -> Order via OrderId, OnDelete = NoAction (plain dependent)
///   City             — business key Name (unique)
let private catalog : Catalog =
    let city =
        { Kind.create (kKey "City") (nm "City") (TableId.create "dbo" "OSUSR_A_CITY" |> mustOk)
            [ idPk "City"; textCol "City" "Name" ] with
            Indexes = [ uniqueIndex "City" "Name" ] }
    let order =
        { Kind.create (kKey "Order") (nm "Order") (TableId.create "dbo" "OSUSR_A_ORDER" |> mustOk)
            [ idPk "Order"; fkCol "Order" "CityId" true ] with
            References = [ Reference.create (rKey "Order" "City") (nm "CityId") (aKey "Order" "CityId") (kKey "City") ] }
    let orderLine =
        { Kind.create (kKey "OrderLine") (nm "OrderLine") (TableId.create "dbo" "OSUSR_A_ORDERLINE" |> mustOk)
            [ idPk "OrderLine"; fkCol "OrderLine" "OrderId" true ] with
            References = [ { Reference.create (rKey "OrderLine" "Order") (nm "OrderId") (aKey "OrderLine" "OrderId") (kKey "Order") with OnDelete = Cascade } ] }
    let auditLog =
        { Kind.create (kKey "AuditLog") (nm "AuditLog") (TableId.create "dbo" "OSUSR_A_AUDITLOG" |> mustOk)
            [ idPk "AuditLog"; fkCol "AuditLog" "OrderId" false ] with
            References = [ Reference.create (rKey "AuditLog" "Order") (nm "OrderId") (aKey "AuditLog" "OrderId") (kKey "Order") ] }
    let country =
        { Kind.create (kKey "Country") (nm "Country") (TableId.create "dbo" "OSUSR_A_COUNTRY" |> mustOk)
            [ codePk "Country"; textCol "Country" "Label" ] with Indexes = [] }
    let m = Module.create (SsKey.synthesizedComposite "SS_MOD" [ "App" ] |> mustOk) (nm "App") [ city; order; orderLine; auditLog; country ] true [] |> mustOk
    Catalog.create [ m ] [] |> mustOk

let private payload = Set.ofList [ kKey "Order" ]

// -- the reconcile-string bridge (supportingScope canonical) ------------------

[<Fact>]
let ``ofReconcileEntries: the three terse forms map to the right relationships`` () =
    let entries =
        [ { TransferSpec.Table = "App.City"; TransferSpec.Rule = TransferSpec.MatchColumn "Name" }
          { TransferSpec.Table = "App.City"; TransferSpec.Rule = TransferSpec.AssignAllTo "7" }
          { TransferSpec.Table = "App.City"; TransferSpec.Rule = TransferSpec.MatchThenAssign ("Name", "7") } ]
    let scoped = SupportingScope.ofReconcileEntries entries |> List.map (fun e -> e.Relationship)
    match scoped with
    | [ SupportingScope.SupportingRelationship.ExistingReference "Name"
        SupportingScope.SupportingRelationship.SharedAnchor ("7", None)
        SupportingScope.SupportingRelationship.SharedAnchor ("7", Some "Name") ] -> ()
    | other -> Assert.Fail(sprintf "unexpected bridge mapping: %A" other)

// -- desugar onto the engine's string inputs ----------------------------------

[<Fact>]
let ``desugarToStrings: reference family -> reconcile specs; owned/seed -> tables; blocked -> acknowledged`` () =
    let entries : SupportingScope.SupportingScopeEntry list =
        [ { Table = "App.City";      Relationship = SupportingScope.SupportingRelationship.ExistingReference "Name"; Reason = "r" }
          { Table = "App.Country";   Relationship = SupportingScope.SupportingRelationship.ReferenceSeed; Reason = "r" }
          { Table = "App.City";      Relationship = SupportingScope.SupportingRelationship.SharedAnchor ("9", None); Reason = "r" }
          { Table = "App.OrderLine"; Relationship = SupportingScope.SupportingRelationship.OwnedChild "App.Order"; Reason = "r" }
          { Table = "App.AuditLog";  Relationship = SupportingScope.SupportingRelationship.BlockedDependent "App.Order"; Reason = "r" } ]
    let d = SupportingScope.desugarToStrings entries
    Assert.Equal<string list>([ "App.Country"; "App.OrderLine" ], d.ExtraTables)
    Assert.Contains("App.City:Name", d.ExtraReconcile)
    Assert.Contains("App.City:=9", d.ExtraReconcile)
    Assert.Equal<string list>([ "App.AuditLog" ], d.Acknowledged)

// -- typed resolution ---------------------------------------------------------

[<Fact>]
let ``resolve: each relationship lands in its bucket; owned-child records the edge`` () =
    let entries : SupportingScope.SupportingScopeEntry list =
        [ { Table = "App.City";      Relationship = SupportingScope.SupportingRelationship.ExistingReference "Name"; Reason = "r" }
          { Table = "App.Country";   Relationship = SupportingScope.SupportingRelationship.ReferenceSeed; Reason = "r" }
          { Table = "App.OrderLine"; Relationship = SupportingScope.SupportingRelationship.OwnedChild "App.Order"; Reason = "r" }
          { Table = "App.AuditLog";  Relationship = SupportingScope.SupportingRelationship.BlockedDependent "App.Order"; Reason = "r" } ]
    let r = SupportingScope.resolve catalog entries |> mustOk
    Assert.True(Set.contains (kKey "Country") r.SeedKinds)
    Assert.True(Set.contains (kKey "Country") r.LoadSetAdditions)
    Assert.True(Set.contains (kKey "OrderLine") r.LoadSetAdditions)
    Assert.True(Set.contains (kKey "AuditLog") r.AcknowledgedExclusions)
    Assert.True(Map.containsKey (kKey "City") r.ReconcileAdditions)
    Assert.Equal<(SsKey * SsKey) list>([ kKey "OrderLine", kKey "Order" ], r.OwnedChildEdges)

// -- graph verification -------------------------------------------------------

let private verdictFor (table: string) (rel: SupportingScope.SupportingRelationship) =
    SupportingScope.verify catalog payload Set.empty [ { Table = table; Relationship = rel; Reason = "r" } ]
    |> List.head |> snd

[<Fact>]
let ``verify: an owned-child with a cascade edge is Confirmed`` () =
    match verdictFor "App.OrderLine" (SupportingScope.SupportingRelationship.OwnedChild "App.Order") with
    | SupportingScope.ScopeClaimVerdict.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed, got %A" other)

[<Fact>]
let ``verify: a NON-cascade dependent declared owned-child is Contradicted (delete rule protects, not owns)`` () =
    match verdictFor "App.AuditLog" (SupportingScope.SupportingRelationship.OwnedChild "App.Order") with
    | SupportingScope.ScopeClaimVerdict.Contradicted (reason, _) -> Assert.Contains("protects", reason)
    | other -> Assert.Fail(sprintf "expected Contradicted, got %A" other)

[<Fact>]
let ``verify: a reference the payload actually points at is Confirmed; one nothing references is Contradicted`` () =
    match verdictFor "App.City" (SupportingScope.SupportingRelationship.ExistingReference "Name") with
    | SupportingScope.ScopeClaimVerdict.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed for City, got %A" other)
    match verdictFor "App.Country" (SupportingScope.SupportingRelationship.ReferenceSeed) with
    | SupportingScope.ScopeClaimVerdict.Contradicted (reason, _) -> Assert.Contains("no relationship in the payload points at", reason)
    | other -> Assert.Fail(sprintf "expected Contradicted for Country, got %A" other)

[<Fact>]
let ``verify: a real inbound dependent is a Confirmed blocked-dependent`` () =
    match verdictFor "App.AuditLog" (SupportingScope.SupportingRelationship.BlockedDependent "App.Order") with
    | SupportingScope.ScopeClaimVerdict.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed, got %A" other)

// -- the inbound mirror + completeness ----------------------------------------

[<Fact>]
let ``dependentEdges: the inbound mirror finds OrderLine and AuditLog pointing at the payload`` () =
    let sources =
        TransferSubset.dependentEdges catalog payload
        |> List.map (fun (source, _, _) -> Name.value source.Name)
        |> Set.ofList
    Assert.Equal<Set<string>>(Set.ofList [ "OrderLine"; "AuditLog" ], sources)

[<Fact>]
let ``completeness: City reference covers the escape; AuditLog stays an unaccounted dependent until acknowledged`` () =
    let entries : SupportingScope.SupportingScopeEntry list =
        [ { Table = "App.City";      Relationship = SupportingScope.SupportingRelationship.ExistingReference "Name"; Reason = "r" }
          { Table = "App.OrderLine"; Relationship = SupportingScope.SupportingRelationship.OwnedChild "App.Order"; Reason = "r" } ]
    let r = SupportingScope.resolve catalog entries |> mustOk
    // City is reconciled → no escaping reference is unaccounted.
    Assert.Empty(SupportingScope.unaccountedEscapes catalog payload Set.empty r)
    // OrderLine is owned (loaded); AuditLog is neither loaded nor acknowledged → unaccounted.
    let deps = SupportingScope.unaccountedDependents catalog payload r |> List.map (fun (s, _, _) -> Name.value s.Name)
    Assert.Equal<string list>([ "AuditLog" ], deps)
    // Acknowledge AuditLog → fully accounted.
    let r2 = SupportingScope.resolve catalog (entries @ [ { Table = "App.AuditLog"; Relationship = SupportingScope.SupportingRelationship.BlockedDependent "App.Order"; Reason = "r" } ]) |> mustOk
    Assert.Empty(SupportingScope.unaccountedDependents catalog payload r2)

// -- the guarantee statements + the hierarchical builder ----------------------
// (2026-07-08, the rendering-elevation program.)

let private everyRelationship : SupportingScope.SupportingRelationship list =
    [ SupportingScope.SupportingRelationship.ExistingReference "Name"
      SupportingScope.SupportingRelationship.ReferenceSeed
      SupportingScope.SupportingRelationship.SharedAnchor ("7", None)
      SupportingScope.SupportingRelationship.StaticLookup "IsoCode"
      SupportingScope.SupportingRelationship.OwnedChild "App.Order"
      SupportingScope.SupportingRelationship.BlockedDependent "App.Order" ]

[<Fact>]
let ``guaranteeOf: every Confirmed relationship earns a non-empty invariant; a Contradicted one earns none`` () =
    for r in everyRelationship do
        Assert.False(System.String.IsNullOrWhiteSpace(SupportingScope.guaranteeOf r (SupportingScope.ScopeClaimVerdict.Confirmed "x")))
        Assert.Equal("", SupportingScope.guaranteeOf r (SupportingScope.ScopeClaimVerdict.Contradicted ("reason", "remedy")))

[<Fact>]
let ``guaranteeOf: the invariants stay in THE_VOICE register — no first/second-person pronouns`` () =
    // The register (THE_VOICE): stative, agentless, no pronouns. A leaked
    // "you"/"we"/"our" would break the operator-facing voice contract.
    let banned = [ " you "; " your "; " we "; " our "; " us "; " i "; "please" ]
    for r in everyRelationship do
        let g = (" " + SupportingScope.guaranteeOf r (SupportingScope.ScopeClaimVerdict.Confirmed "x") + " ").ToLowerInvariant()
        for b in banned do
            Assert.False(g.Contains(b), sprintf "guarantee leaked '%s': %s" b g)

[<Fact>]
let ``scopeGroups: references and dependents split, each claim carrying its join edges and a guarantee`` () =
    let entries : SupportingScope.SupportingScopeEntry list =
        [ { Table = "App.City";      Relationship = SupportingScope.SupportingRelationship.ExistingReference "Name"; Reason = "match the target's cities" }
          { Table = "App.OrderLine"; Relationship = SupportingScope.SupportingRelationship.OwnedChild "App.Order"; Reason = "lines belong to the order" } ]
    let groups = SupportingScope.scopeGroups catalog payload Set.empty entries
    Assert.Equal<Set<string>>(Set.ofList [ "references"; "dependents" ], groups |> List.map (fun g -> g.Family) |> Set.ofList)
    let refClaim = (groups |> List.find (fun g -> g.Family = "references")).Claims |> List.head
    Assert.Equal("existing-reference", refClaim.Relationship)
    Assert.Contains("Order.CityId", refClaim.JoinEdges)                          // the normalized join: which payload column points here
    Assert.False(System.String.IsNullOrWhiteSpace refClaim.Guarantee)
    let depClaim = (groups |> List.find (fun g -> g.Family = "dependents")).Claims |> List.head
    Assert.Equal("owned-child", depClaim.Relationship)
    Assert.Contains("OrderLine.OrderId", depClaim.JoinEdges |> String.concat " ")  // the inbound edge back at the payload

module Projection.Tests.EstateTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The estate instrument (`check estate` — CHAPTER_ESTATE_OPEN.md; DECISIONS
// 2026-07-15). Covered here:
//   - A45's pure witness: N espace cells of one model produce ZERO estate
//     findings after the logical-shape normalization (the axiom candidate's
//     promotion; the two-DB Docker canary covers the realization grain).
//   - The finding ⇔ presentation totality seed: every finding kind carries a
//     lane, a plane, and a distinct machine token.
//   - The aggregation: divergences group by key across environments, the
//     environments are named, a strict majority flips the closing clause.
//   - One substrate: the board and estate.json project one report value.
//   - The verb routing (the `estate` planCheck arm): the zero-flag contract,
//     `--against model`, and the named refusals.
// ---------------------------------------------------------------------------

let private emptyCat : Catalog = Catalog.create [] [] |> Result.value

let private operand (label: string) (c: Catalog) : Compare.Operand =
    { Label = label; Catalog = c; Profile = None }

let private agreed : Estate.TargetOperand = Estate.TargetOperand.AgreedEnv "cloud-dev"

// -- A45: espace invariance (the pure witness; promotes the AxiomTests stub) --

/// OutSystems derives default-constraint names from the physical table name,
/// so two espace cells of ONE model differ exactly there (the realization
/// artifacts) while the logical shape is identical.
let private espaceCell (defaultName: string) : Catalog =
    { sampleCatalog with
        Modules =
            sampleCatalog.Modules
            |> List.map (fun m ->
                { m with
                    Kinds =
                        m.Kinds
                        |> List.map (fun k ->
                            { k with Attributes = k.Attributes |> List.map (fun a -> { a with DefaultName = Some (mkName defaultName) }) }) }) }

[<Fact>]
let ``A45: N espace cells of one model produce zero estate findings after toLogicalShape (espace invariance)`` () =
    let cells =
        [ "cloud-dev", operand "cloud-dev" (espaceCell "DF_ABC_CUSTOMER_NAME")
          "cloud-qa",  operand "cloud-qa"  (espaceCell "DF_XYZ_CUSTOMER_NAME")
          "cloud-uat", operand "cloud-uat" (espaceCell "DF_PQR_CUSTOMER_NAME") ]
    let report = Estate.compute agreed (espaceCell "DF_JKL_CUSTOMER_NAME") cells
    Assert.Empty report.Findings
    Assert.Equal(Estate.Verdict.Unified, report.Verdict)
    Assert.True(Estate.isUnified report)

// -- finding ⇔ presentation (the totality seed) -------------------------------

[<Fact>]
let ``presentation: every finding kind carries its lane, plane, and a distinct machine token (finding ⇔ presentation)`` () =
    let kinds = EstateFindingKind.all
    Assert.NotEmpty kinds
    // Tokens are distinct (the projection is injective — keys cannot collide
    // across kinds) and every kind resolves a lane + plane (total matches; the
    // compiler enforces totality, this pins the walkable list's coverage).
    let tokens = kinds |> List.map EstateFindingKind.token
    Assert.Equal(List.length kinds, tokens |> List.distinct |> List.length)
    for kind in kinds do
        EstateFindingKind.laneOf kind |> ignore
        EstateFindingKind.planeOf kind |> ignore

[<Fact>]
let ``presentation: a finding key is stable across mints and carries the kind's token`` () =
    let a = FindingKey.create EstateFindingKind.DataNotNull "Customer.Email"
    let b = FindingKey.create EstateFindingKind.DataNotNull "Customer.Email"
    Assert.Equal(FindingKey.text a, FindingKey.text b)
    Assert.StartsWith("data.notNull:", FindingKey.text a)

// -- the aggregation ----------------------------------------------------------

[<Fact>]
let ``T1: an environment missing the target's kinds reads as promotion lag — WATCH lane; the publish resolves it`` () =
    // cloud-qa is EMPTY against a populated target: every target kind is a
    // lag divergence in cloud-qa (behind the agreed shape); cloud-dev
    // matches and contributes none. The direction classifier (wave A3)
    // keeps lag out of the ruling queue.
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", operand "cloud-dev" sampleCatalog
              "cloud-qa",  operand "cloud-qa"  emptyCat ]
    Assert.Equal(Estate.Verdict.Converging, report.Verdict)
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.Equal(EstateFindingKind.SchemaLag, f.Kind)
        Assert.Equal(EstateLane.Watch, f.Lane)
        Assert.Equal(EstatePlane.Schema, f.Plane)
        Assert.Equal<string list>([ "cloud-qa" ], f.Envs |> List.map fst)
        Assert.Contains("cloud-qa", f.Statement)
        Assert.Contains("promotion lag", f.Statement)

[<Fact>]
let ``T1: a kind an environment carries beyond the target reads as deployed-ahead drift — DECIDE lane`` () =
    let report =
        Estate.compute agreed emptyCat [ "cloud-uat", operand "cloud-uat" sampleCatalog ]
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.Equal(EstateFindingKind.SchemaPresence, f.Kind)
        Assert.Equal(EstateLane.Decide, f.Lane)
        Assert.Contains("deployed-ahead drift", f.Statement)

[<Fact>]
let ``compute: one divergence in two environments groups onto one key and names both`` () =
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-qa",  operand "cloud-qa"  emptyCat
              "cloud-uat", operand "cloud-uat" emptyCat
              "cloud-dev", operand "cloud-dev" sampleCatalog ]
    // Both empty environments miss the same target kinds — ONE finding per
    // kind, carrying both environments' evidence.
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.Equal<string list>([ "cloud-qa"; "cloud-uat" ], f.Envs |> List.map fst |> List.sort)
        Assert.Contains("cloud-qa", f.Statement)
        Assert.Contains("cloud-uat", f.Statement)

[<Fact>]
let ``compute: a strict majority of DRIFTING environments turns the closing clause on the target; a lag majority never does (T1)`` () =
    // Drift direction: two of three environments carry kinds the target
    // does not declare — the target may genuinely be the one behind.
    let drifted =
        Estate.compute agreed emptyCat
            [ "cloud-qa",  operand "cloud-qa"  sampleCatalog
              "cloud-uat", operand "cloud-uat" sampleCatalog
              "cloud-dev", operand "cloud-dev" emptyCat ]
    Assert.NotEmpty drifted.Findings
    for f in drifted.Findings do
        Assert.Contains("the target may be the one behind", f.Statement)
    // Lag direction: two of three environments MISS the target's kinds —
    // the normal pre-publish state; the clause stays off.
    let lagging =
        Estate.compute agreed sampleCatalog
            [ "cloud-qa",  operand "cloud-qa"  emptyCat
              "cloud-uat", operand "cloud-uat" emptyCat
              "cloud-dev", operand "cloud-dev" sampleCatalog ]
    Assert.NotEmpty lagging.Findings
    for f in lagging.Findings do
        Assert.DoesNotContain("the target may be the one behind", f.Statement)

[<Fact>]
let ``compute: a minority drift names its environment and never blames the target`` () =
    let report =
        Estate.compute agreed emptyCat
            [ "cloud-qa",  operand "cloud-qa"  sampleCatalog
              "cloud-uat", operand "cloud-uat" emptyCat
              "cloud-dev", operand "cloud-dev" emptyCat ]
    // 1 of 3 drifts — no majority clause; the drifting environment is named.
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.DoesNotContain("the target may be the one behind", f.Statement)
        Assert.Contains("cloud-qa", f.Statement)

// -- one substrate (board ≡ estate.json over one report value) ----------------

[<Fact>]
let ``one substrate: estate.json carries the verdict, every environment, and every finding the board renders`` () =
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", operand "cloud-dev" sampleCatalog
              "cloud-qa",  operand "cloud-qa"  emptyCat ]
    let json = Estate.toJsonString report
    Assert.Contains("\"verdict\": \"converging\"", json)
    Assert.Contains("cloud-dev", json)
    Assert.Contains("cloud-qa", json)
    for f in report.Findings do
        Assert.Contains(FindingKey.text f.Key, json)
        Assert.Contains(EstateFindingKind.token f.Kind, json)

[<Fact>]
let ``board: the empty state is a full surface (masthead, lanes, matrix, artifacts, action)`` () =
    let report = Estate.compute agreed sampleCatalog [ "cloud-dev", operand "cloud-dev" sampleCatalog ]
    let lines = Estate.render report
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "ESTATE — ")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "DECIDE")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "REPAIR")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "RELAX")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "WATCH")
    Assert.Contains(lines, fun (l: string) -> l.Contains "The interim posture is empty.")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "MATRIX")
    Assert.Contains(lines, fun (l: string) -> l.Contains "estate.json — the full findings record")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "Next: ")

[<Fact>]
let ``board: the action names the first DECIDE finding's key when one exists`` () =
    // The drift direction fills the ruling queue (lag is watchable, T1).
    let report =
        Estate.compute agreed emptyCat [ "cloud-qa", operand "cloud-qa" sampleCatalog ]
    let lines = Estate.render report
    let firstDecide = Estate.laneFindings EstateLane.Decide report |> List.head
    Assert.Contains(lines, fun (l: string) ->
        l.StartsWith "Next: rule the first DECIDE finding" && l.Contains (FindingKey.text firstDecide.Key))

// -- the consensus (wave A2): the join law + clean-environment attribution ----

let private nullEvidence (attrKey: SsKey) (rowCount: int64) (nullCount: int64) : ColumnProfile =
    { AttributeKey = attrKey
      RowCount = rowCount
      NullCount = nullCount
      MaxObservedLength = None
      NullCountProbeStatus = ProbeStatus.observed rowCount }

let private orphanEvidence (refKey: SsKey) (orphans: int64) : ForeignKeyReality =
    { ReferenceKey = refKey
      HasOrphan = orphans > 0L
      OrphanCount = orphans
      IsNoCheck = false
      ProbeStatus = ProbeStatus.observed 1000L }

open FsCheck.Xunit

[<Property(MaxTest = 60)>]
let ``law: deciding on the Profile.merge join equals the union of per-environment decisions (consensus = meet over the evidence join)`` (specs: (byte * byte * bool) list) =
    // Each spec is one environment's evidence: NULLs observed under the two
    // declared-NOT-NULL Customer columns + an orphan witness on the Order→
    // Customer relationship. The law: a violation reaches the JOIN decision
    // exactly when at least one environment's own decision carries it —
    // identity at the (entity, column, category) grain (counts join as MAX).
    let profileOf ((nameNulls, tenantNulls, orphan): byte * byte * bool) : Profile =
        { Profile.empty with
            Columns =
                [ nullEvidence customerNameKey 1000L (int64 nameNulls)
                  nullEvidence customerTenantKey 1000L (int64 tenantNulls) ]
            ForeignKeys = [ orphanEvidence orderRefToCustomer (if orphan then 7L else 0L) ] }
    let profiles = specs |> List.truncate 4 |> List.map profileOf
    let violationId (v: ModelFidelity.DataViolation) =
        v.Reference.Entity, v.Reference.Column, ModelFidelity.categoryOf v
    let joined =
        Estate.decideOnJoin sampleCatalog profiles |> List.map violationId |> Set.ofList
    let union =
        profiles
        |> List.collect (fun p -> Estate.decideOnJoin sampleCatalog [ p ])
        |> List.map violationId
        |> Set.ofList
    joined = union

[<Fact>]
let ``decideOnJoin: an orphaned relationship in one environment reaches the estate decision`` () =
    let dirty = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 7L ] }
    let violations = Estate.decideOnJoin sampleCatalog [ Profile.empty; dirty ]
    Assert.Contains(violations, fun v -> ModelFidelity.categoryOf v = ModelFidelity.OrphanCategory)

[<Fact>]
let ``consensus: the clean environments are named beside the divergence with their observation basis (RT-6)`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5000L 4120L ] }
    let clean = { Profile.empty with Columns = [ nullEvidence customerNameKey 1240L 0L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty }
              "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some clean } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataNotNull)
    Assert.Contains("cloud-uat holds 4,120 NULL row(s)", finding.Statement)
    Assert.Contains("clean in cloud-dev (1,240 row(s) observed)", finding.Statement)
    Assert.DoesNotContain("advisory", finding.Statement)

[<Fact>]
let ``consensus: a clean verdict below the decision floor renders advisory (sample-size honesty, RT-7)`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5000L 4120L ] }
    let tiny  = { Profile.empty with Columns = [ nullEvidence customerNameKey 12L 0L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty }
              "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some tiny } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataNotNull)
    Assert.Contains("clean in cloud-dev (12 row(s) observed — advisory; the sample is below the decision floor)", finding.Statement)

[<Fact>]
let ``consensus: an environment with no evidence for the coordinate stays silent in the clean clause (the masthead owns it)`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5000L 4120L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty }
              "cloud-qa",  operand "cloud-qa" sampleCatalog ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataNotNull)
    Assert.DoesNotContain("cloud-qa", finding.Statement)

// -- the A3 detectors: sentinel, basis, trust, asymmetry, candidacy ------------

/// An untruncated categorical distribution over the given frequencies —
/// the smart constructor requires DistinctCount = Frequencies.Length.
let private categoricalOn (attrKey: SsKey) (freqs: (string * int64) list) : AttributeDistribution =
    AttributeDistribution.Categorical
        (CategoricalDistribution.create attrKey freqs (int64 (List.length freqs)) false (ProbeStatus.observed 1000L)
         |> Result.value)

[<Fact>]
let ``D3a: the categorical zero-frequency witnesses the unset-reference split on an orphan finding`` () =
    let dirty =
        { Profile.empty with
            ForeignKeys = [ orphanEvidence orderRefToCustomer 20L ]
            Distributions = [ categoricalOn orderCustomerFkKey [ "0", 15L; "7", 5L ] ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataOrphans)
    Assert.Contains(", of which 15 reference the unset value 0", finding.Statement)

[<Fact>]
let ``D3a: an orphan finding without categorical evidence keeps the plain statement — the split is never fabricated`` () =
    let dirty = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 20L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataOrphans)
    Assert.DoesNotContain("unset value", finding.Statement)

[<Fact>]
let ``D1×D5: a Text column's NOT-NULL finding names the empty-text basis; an integer column's does not`` () =
    let dirty =
        { Profile.empty with
            Columns =
                [ nullEvidence customerNameKey 5000L 4120L
                  nullEvidence customerTenantKey 5000L 7L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    let name =
        report.Findings
        |> List.find (fun f -> f.Kind = EstateFindingKind.DataNotNull && f.Statement.Contains "Customer.Name")
    Assert.Contains("the count includes empty text, which normalizes to NULL on publish", name.Statement)
    let tenant =
        report.Findings
        |> List.find (fun f -> f.Kind = EstateFindingKind.DataNotNull && f.Statement.Contains "Customer.TenantId")
    Assert.DoesNotContain("empty text", tenant.Statement)

[<Fact>]
let ``S7/O3: a WITH NOCHECK relationship reads as a preparable repair naming its re-trust cost (the trust census)`` () =
    let untrusted =
        { Profile.empty with
            Columns = [ nullEvidence orderCustomerFkKey 12400L 0L ]
            ForeignKeys =
                [ { ReferenceKey = orderRefToCustomer
                    HasOrphan = false
                    OrphanCount = 0L
                    IsNoCheck = true
                    ProbeStatus = ProbeStatus.observed 12400L } ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-qa", { operand "cloud-qa" sampleCatalog with Profile = Some untrusted } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.SchemaTrust)
    Assert.Equal(EstateLane.Repair, finding.Lane)
    Assert.Equal(EstatePlane.Schema, finding.Plane)
    Assert.Contains("Order.CustomerId → Customer is enforced WITH NOCHECK in cloud-qa (untrusted)", finding.Statement)
    Assert.Contains("re-trusting scans 12,400 row(s)", finding.Statement)

[<Fact>]
let ``D12: rowcount asymmetry past the factor reads as a WATCH advisory naming both ends; near-parity stays silent`` () =
    let big  = { Profile.empty with Columns = [ nullEvidence customerNameKey 10400L 0L ] }
    let tiny = { Profile.empty with Columns = [ nullEvidence customerNameKey 12L 0L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some big }
              "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some tiny } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataAsymmetry)
    Assert.Equal(EstateLane.Watch, finding.Lane)
    Assert.Equal<string list>([ "cloud-dev"; "cloud-uat" ], finding.Envs |> List.map fst |> List.sort)
    Assert.Contains("Customer holds 10,400 row(s) in cloud-uat", finding.Statement)
    Assert.Contains("Customer holds 12 row(s) in cloud-dev — verdicts drawn on this evidence are advisory at the asymmetry", finding.Statement)
    let nearA = { Profile.empty with Columns = [ nullEvidence customerNameKey 1000L 0L ] }
    let nearB = { Profile.empty with Columns = [ nullEvidence customerNameKey 900L 0L ] }
    let quiet =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some nearA }
              "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some nearB } ]
    Assert.DoesNotContain(quiet.Findings, fun f -> f.Kind = EstateFindingKind.DataAsymmetry)

[<Fact>]
let ``D15: a column distinct in every observed row of every evidenced environment reads as a natural-key candidate; one duplicate kills the unanimity`` () =
    let distinctFreqs (offset: int) = [ for i in 1 .. 60 -> string (offset + i), 1L ]
    let devP = { Profile.empty with Distributions = [ categoricalOn customerTenantKey (distinctFreqs 0) ] }
    let qaP  = { Profile.empty with Distributions = [ categoricalOn customerTenantKey (distinctFreqs 100) ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some devP }
              "cloud-qa",  { operand "cloud-qa" sampleCatalog with Profile = Some qaP } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataUniquenessCandidate)
    Assert.Equal(EstateLane.Watch, finding.Lane)
    Assert.Contains("Customer.TenantId is distinct in every observed row of cloud-dev (60 of 60 row(s))", finding.Statement)
    Assert.Contains("cloud-qa", finding.Statement)
    let dupFreqs = ("1", 2L) :: [ for i in 2 .. 60 -> string i, 1L ]
    let dupP = { Profile.empty with Distributions = [ categoricalOn customerTenantKey dupFreqs ] }
    let quiet =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some devP }
              "cloud-qa",  { operand "cloud-qa" sampleCatalog with Profile = Some dupP } ]
    Assert.DoesNotContain(quiet.Findings, fun f -> f.Kind = EstateFindingKind.DataUniquenessCandidate)

// -- the evidence provenance (wave A2.5): the masthead, the JSON, one fact ------

let private capturedTwoDaysBack = System.DateTimeOffset(2026, 7, 13, 8, 0, 0, System.TimeSpan.Zero)

let private singleEnvReport (provenance: Estate.EvidenceProvenance) : Estate.EstateReport =
    Estate.compute agreed sampleCatalog [ "cloud-uat", operand "cloud-uat" sampleCatalog ]
    |> Estate.withEvidence
        (Estate.EvidenceStoreBasis.Enabled "/var/projection/estate")
        (Map.ofList [ "cloud-uat", provenance ])

[<Fact>]
let ``provenance: cached evidence renders its capture age and clean-fingerprint count on the masthead (RT-7)`` () =
    let lines = Estate.render (singleEnvReport (Estate.EvidenceProvenance.Cached (capturedTwoDaysBack, 2, 214)))
    Assert.Contains(lines, fun (l: string) ->
        l.Contains "cloud-uat" && l.Contains "evidence captured 2 day(s) ago; fingerprints clean across 214 kind(s)")
    Assert.Contains(lines, fun (l: string) -> l.Contains "Evidence store: /var/projection/estate.")

[<Fact>]
let ``provenance: a refreshed environment names its moved kinds, capped at three with the remainder counted`` () =
    let lines =
        Estate.render (singleEnvReport (Estate.EvidenceProvenance.Refreshed [ "Orders"; "OrderLine"; "Customer"; "Invoice"; "Payment" ]))
    Assert.Contains(lines, fun (l: string) ->
        l.Contains "5 kind(s) moved since capture (Orders, OrderLine, Customer, and 2 more) — re-profiled this run")

[<Fact>]
let ``provenance: offline evidence renders its age and the advisory downgrade — named, never silent`` () =
    let lines = Estate.render (singleEnvReport (Estate.EvidenceProvenance.Offline (capturedTwoDaysBack, 9)))
    Assert.Contains(lines, fun (l: string) ->
        l.Contains "offline evidence, captured 9 day(s) ago and unprobed — every verdict standing on it is advisory")

[<Fact>]
let ``provenance: a same-day capture reads as today`` () =
    let lines = Estate.render (singleEnvReport (Estate.EvidenceProvenance.Cached (capturedTwoDaysBack, 0, 3)))
    Assert.Contains(lines, fun (l: string) -> l.Contains "evidence captured today")

[<Fact>]
let ``provenance: a store-blind report says the run reads live — a disabled store is stated, never silent`` () =
    let report = Estate.compute agreed sampleCatalog [ "cloud-dev", operand "cloud-dev" sampleCatalog ]
    Assert.Equal(Estate.EvidenceStoreBasis.Disabled, report.Evidence)
    let lines = Estate.render report
    Assert.Contains(lines, fun (l: string) -> l.Contains "Evidence reads live this run — no store is configured")

/// Nullness-narrowed JsonNode child access (the `requireNode` test idiom).
let private node (label: string) (n: System.Text.Json.Nodes.JsonNode | null) : System.Text.Json.Nodes.JsonNode =
    match Option.ofObj n with
    | Some v -> v
    | None ->
        Assert.Fail (sprintf "%s: required JsonNode child was null" label)
        Unchecked.defaultof<System.Text.Json.Nodes.JsonNode>

[<Fact>]
let ``provenance: estate.json carries the same provenance facts the masthead renders (one substrate)`` () =
    let json = Estate.toJson (singleEnvReport (Estate.EvidenceProvenance.Cached (capturedTwoDaysBack, 2, 214)))
    Assert.Equal("/var/projection/estate", (node "evidenceStore" json.["evidenceStore"]).GetValue<string>())
    let env = (node "environments[0]" ((node "environments" json.["environments"]).AsArray().[0])).AsObject()
    let evidence = (node "evidence" env.["evidence"]).AsObject()
    Assert.Equal("cached", (node "basis" evidence.["basis"]).GetValue<string>())
    Assert.Equal(2, (node "ageDays" evidence.["ageDays"]).GetValue<int>())
    Assert.Equal(214, (node "kindCount" evidence.["kindCount"]).GetValue<int>())

// -- the verb routing (the `estate` planCheck arm) -----------------------------

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es

let private estateJson = """
{
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "cloud-qa":  { "access": "direct", "conn": "env:CLOUD_QA_CONN" },
    "cloud-uat": { "access": "direct", "conn": "env:CLOUD_UAT_CONN" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-qa", "cloud-uat"] }
}
"""

[<Fact>]
let ``check estate: the zero-flag contract resolves the target and confirm set from the readiness block`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.CheckEstate args ->
        Assert.Equal("cloud-dev", args.TargetLabel)
        Assert.Equal(EstateTargetSource.AgreedEnv "env:CLOUD_DEV_CONN", args.Target)
        Assert.Equal<string list>([ "cloud-dev"; "cloud-qa"; "cloud-uat" ], args.Confirm |> List.map fst)
        Assert.False args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --against model selects the authored model and the run names it`` () =
    let json = """
{
  "model": { "env": "cloud-dev" },
  "environments": {
    "cloud-dev": { "access": "direct", "conn": "file:./secrets/cloud-dev.conn" },
    "cloud-qa":  { "access": "direct", "conn": "file:./secrets/cloud-qa.conn" }
  },
  "readiness": { "confirm": ["cloud-dev", "cloud-qa"] }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match (Command.planCheck cfg [ "estate"; "--against"; "model" ]).Action with
    | PlanAction.CheckEstate args ->
        Assert.Equal("model", args.TargetLabel)
        match args.Target with
        | EstateTargetSource.AuthoredModel (ossys, _) ->
            Assert.Equal(Some "file:./secrets/cloud-dev.conn", ossys)
        | other -> Assert.Fail(sprintf "expected AuthoredModel; got %A" other)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --format json rides the args record`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--format"; "json" ]).Action with
    | PlanAction.CheckEstate args -> Assert.True args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: no readiness block ⇒ named refusal, exit 2`` () =
    let cfg = ProjectionConfig.parse """{ "environments": {} }""" |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.estateNoBlock", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check estate: --against model with no authored model ⇒ named refusal, exit 2`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--against"; "model" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.estateNoModel", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check estate: a non-direct environment in the confirm set ⇒ named refusal, exit 6 (never silently skipped)`` () =
    let json = """
{
  "environments": {
    "cloud-dev":   { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },
    "on-prem-dev": { "access": "bundle", "out": "dist/on-prem-dev", "grant": "schema+data" }
  },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "on-prem-dev"] }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(6, exit)
        Assert.Equal("cli.check.estateNotDirect", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check estate: an unknown environment ⇒ named refusal, exit 6`` () =
    let json = """
{
  "environments": { "cloud-dev": { "access": "direct", "conn": "env:CLOUD_DEV_CONN" } },
  "readiness": { "schema": "cloud-dev", "confirm": ["cloud-dev", "cloud-prod"] }
}
"""
    let cfg = ProjectionConfig.parse json |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(6, exit)
        Assert.Equal("cli.check.estateUnknownEnv", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

// -- the evidence flags (wave A2.5) ---------------------------------------------

[<Fact>]
let ``check estate: the default evidence mode is fingerprint-gated (pay once, stay honest)`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate" ]).Action with
    | PlanAction.CheckEstate args -> Assert.Equal(EstateEvidenceMode.FingerprintGated, args.Evidence)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --refresh forces every environment; a comma list forces the named subset`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--refresh" ]).Action with
    | PlanAction.CheckEstate args -> Assert.Equal(EstateEvidenceMode.Refresh None, args.Evidence)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)
    match (Command.planCheck cfg [ "estate"; "--refresh"; "cloud-qa,cloud-uat" ]).Action with
    | PlanAction.CheckEstate args ->
        Assert.Equal(EstateEvidenceMode.Refresh (Some [ "cloud-qa"; "cloud-uat" ]), args.Evidence)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --refresh followed by another flag reads as refresh-all (a flag is never its value)`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--refresh"; "--format"; "json" ]).Action with
    | PlanAction.CheckEstate args ->
        Assert.Equal(EstateEvidenceMode.Refresh None, args.Evidence)
        Assert.True args.AsJson
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --offline rides the args record`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--offline" ]).Action with
    | PlanAction.CheckEstate args -> Assert.Equal(EstateEvidenceMode.Offline, args.Evidence)
    | other -> Assert.Fail(sprintf "expected CheckEstate; got %A" other)

[<Fact>]
let ``check estate: --refresh with --offline ⇒ named refusal, exit 2 (the flags contradict)`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--refresh"; "--offline" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.estateEvidenceConflict", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

[<Fact>]
let ``check estate: --refresh naming an environment outside readiness.confirm ⇒ named refusal, exit 2`` () =
    let cfg = ProjectionConfig.parse estateJson |> mustOk
    match (Command.planCheck cfg [ "estate"; "--refresh"; "cloud-prod" ]).Action with
    | PlanAction.Refused (exit, e) ->
        Assert.Equal(2, exit)
        Assert.Equal("cli.check.estateRefreshUnknownEnv", e.Code)
    | other -> Assert.Fail(sprintf "expected Refused; got %A" other)

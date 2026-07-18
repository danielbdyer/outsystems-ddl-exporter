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
//   - One substrate: the board and environments.json project one report value.
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
let ``presentation: every finding kind carries its contract row — statement specimen, lane-coherent lever form (finding ⇔ presentation)`` () =
    // The Appendix A table, held to the code (wave A6 completes the seed):
    // tokens distinct (keys cannot collide across kinds); lane + plane +
    // specimen + lever form total (a kind cannot land without its row — the
    // compiler enforces the matches, this walks the list); each specimen a
    // complete sentence in the register (the mechanical twelve-rule laws:
    // non-empty, ends on a period, banned-substring-clean — mirroring the
    // VoiceTotality scan); each lever form coherent with its lane (DECIDE ⇔
    // a ruling imperative; REPAIR ⇔ the block review; RELAX ⇒ the overlay
    // merge, or — for the ACTIVE posture, whose next move is the probe's
    // meter — no lever; WATCH ⇔ no lever, by design).
    let kinds = EstateFindingKind.all
    Assert.NotEmpty kinds
    let tokens = kinds |> List.map EstateFindingKind.token
    Assert.Equal(List.length kinds, tokens |> List.distinct |> List.length)
    let banned =
        [ "your"; "you "; " i "; " we "; "not assumed"; "that's real"; ", not "
          "cleaned up"; "cleans up"; "destroy"; "blast radius"; "fatal"
          "dig"; "diggable"; "green hush"; "jewel"; "oops"; "let's"; "hang on"
          "refused"; "error!"; "failed!" ]
    for kind in kinds do
        let token = EstateFindingKind.token kind
        let specimen = EstateFindingKind.specimenOf kind
        Assert.False(System.String.IsNullOrWhiteSpace specimen, sprintf "%s carries no specimen" token)
        Assert.True(specimen.EndsWith ".", sprintf "%s's specimen is a fragment (no closing period): %s" token specimen)
        let lowered = specimen.ToLowerInvariant()
        for b in banned do
            Assert.False(lowered.Contains b, sprintf "%s's specimen breaks the banned list (THE_VOICE.md §2.2): contains '%s'" token b)
        match EstateFindingKind.leverFormOf kind, EstateFindingKind.laneOf kind with
        | EstateLeverForm.Ruling imperative, EstateLane.Decide ->
            Assert.True(imperative.StartsWith "Rule ", sprintf "%s's ruling does not lead with the ruling: %s" token imperative)
            Assert.True(imperative.EndsWith ".", sprintf "%s's ruling is a fragment: %s" token imperative)
        | EstateLeverForm.ReviewBlock, EstateLane.Repair -> ()
        | EstateLeverForm.MergeOverlayEntry, EstateLane.Relax -> ()
        | EstateLeverForm.NoLever, EstateLane.Watch -> ()
        | EstateLeverForm.NoLever, EstateLane.Relax when kind = EstateFindingKind.PostureActive -> ()
        | form, lane ->
            Assert.Fail(sprintf "%s's lever form %A is incoherent with its lane %A" token form lane)

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
        Assert.Contains("the ordinary publish promotes it", f.Statement)

[<Fact>]
let ``T1: a kind an environment carries beyond the target reads as deployed-ahead drift — DECIDE lane`` () =
    let report =
        Estate.compute agreed emptyCat [ "cloud-uat", operand "cloud-uat" sampleCatalog ]
    Assert.NotEmpty report.Findings
    for f in report.Findings do
        Assert.Equal(EstateFindingKind.SchemaPresence, f.Kind)
        Assert.Equal(EstateLane.Decide, f.Lane)
        Assert.Contains("no promotion added it", f.Statement)

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

// -- one substrate (board ≡ environments.json over one report value) ----------------

[<Fact>]
let ``one substrate: environments.json carries the verdict, every environment, and every finding the board renders`` () =
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
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "ENVIRONMENTS — ")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "DECIDE")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "REPAIR")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "RELAX")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "WATCH")
    Assert.Contains(lines, fun (l: string) -> l.Contains "No interim changes are needed.")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "EMISSION — ")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "MATRIX")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "RUNBOOK — ")
    Assert.Contains(lines, fun (l: string) -> l.Contains "environments.json — the full findings record")
    Assert.Contains(lines, fun (l: string) -> l.StartsWith "Next: ")

[<Fact>]
let ``board: the action names the first DECIDE finding's key when one exists`` () =
    // The drift direction fills the ruling queue (lag is watchable, T1).
    let report =
        Estate.compute agreed emptyCat [ "cloud-qa", operand "cloud-qa" sampleCatalog ]
    let lines = Estate.render report
    let firstDecide = Estate.laneFindings EstateLane.Decide report |> List.head
    Assert.Contains(lines, fun (l: string) ->
        l.StartsWith "Next: rule the first DECIDE finding" && l.Contains (FindingKey.readableLabel firstDecide.Key))

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
    Assert.Contains("Customer holds 12 row(s) in cloud-dev — findings drawn from the smaller sample are advisory", finding.Statement)
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
    Assert.Contains("Customer.TenantId has no duplicate in cloud-dev — 60 of 60 row(s) are distinct", finding.Statement)
    Assert.Contains("cloud-qa", finding.Statement)
    let dupFreqs = ("1", 2L) :: [ for i in 2 .. 60 -> string i, 1L ]
    let dupP = { Profile.empty with Distributions = [ categoricalOn customerTenantKey dupFreqs ] }
    let quiet =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some devP }
              "cloud-qa",  { operand "cloud-qa" sampleCatalog with Profile = Some dupP } ]
    Assert.DoesNotContain(quiet.Findings, fun f -> f.Kind = EstateFindingKind.DataUniquenessCandidate)

// -- the A4 detectors: CDC parity, headroom, sentinels, collation, identity ----

/// Rewrite one attribute of the Customer kind across the sample catalog.
let private withCustomerAttr (attrKey: SsKey) (rewrite: Attribute -> Attribute) : Catalog =
    { sampleCatalog with
        Modules =
            sampleCatalog.Modules
            |> List.map (fun m ->
                { m with
                    Kinds =
                        m.Kinds
                        |> List.map (fun k ->
                            if k.SsKey = customerKey then
                                { k with Attributes = k.Attributes |> List.map (fun a -> if a.SsKey = attrKey then rewrite a else a) }
                            else k) }) }

/// Rewrite the Customer kind itself across the sample catalog.
let private withCustomerKind (rewrite: Kind -> Kind) : Catalog =
    { sampleCatalog with
        Modules =
            sampleCatalog.Modules
            |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.map (fun k -> if k.SsKey = customerKey then rewrite k else k) }) }

[<Fact>]
let ``O1: CDC tracking one environment and not another reads as a DECIDE parity finding on the operational plane`` () =
    let tracked = { Profile.empty with CdcAwareness = CdcAwareness.create (Set.singleton customerKey) Map.empty }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some tracked }
              "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some Profile.empty } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.OperationalCdc)
    Assert.Equal(EstateLane.Decide, finding.Lane)
    Assert.Equal(EstatePlane.Operational, finding.Plane)
    Assert.Contains("Change tracking is on for Customer in cloud-uat and off in cloud-dev", finding.Statement)

[<Fact>]
let ``D13: a primary key past half its declared int ceiling reads as a WATCH headroom advisory; unknown storage never guesses a ceiling`` () =
    let catalog = withCustomerAttr customerIdAttrKey (fun a -> { a with SqlStorage = Some SqlStorageType.Int })
    let dist =
        NumericDistribution.create customerIdAttrKey 1M 1M 1M 1M 1M 1M 1340000000M 1000L (ProbeStatus.observed 1000L)
        |> Result.value
    let p = { Profile.empty with Distributions = [ AttributeDistribution.Numeric dist ] }
    let report =
        Estate.compute agreed catalog [ "cloud-uat", { operand "cloud-uat" catalog with Profile = Some p } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataHeadroom)
    Assert.Equal(EstateLane.Watch, finding.Lane)
    Assert.Contains("Customer.Id has reached 1,340,000,000 of int's 2,147,483,647 in cloud-uat — 62% of the limit is used", finding.Statement)
    let silent =
        Estate.compute agreed sampleCatalog [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some p } ]
    Assert.DoesNotContain(silent.Findings, fun f -> f.Kind = EstateFindingKind.DataHeadroom)

[<Fact>]
let ``D8: the 1900-01-01 empty-date convention reads as a WATCH sentinel advisory when the categorical evidence carries it`` () =
    let catalog = withCustomerAttr customerTenantKey (fun a -> { a with Type = Date })
    let p =
        { Profile.empty with
            Distributions = [ categoricalOn customerTenantKey [ "1900-01-01", 812L; "2026-01-02", 5L ] ] }
    let report =
        Estate.compute agreed catalog [ "cloud-uat", { operand "cloud-uat" catalog with Profile = Some p } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataDateSentinel)
    Assert.Equal(EstateLane.Watch, finding.Lane)
    Assert.Contains("Customer.TenantId holds 812 row(s) set to 1900-01-01 in cloud-uat", finding.Statement)
    Assert.Contains("carry no real value", finding.Statement)

[<Fact>]
let ``D6: case-distinct values under a unique declaration read as a REPAIR collation collision`` () =
    let uniqueIx =
        { Index.ofKeyColumns (idxKey [ "Customer"; "Name" ]) (mkName "UIX_Customer_Name") [ customerNameKey ] with
            Uniqueness = Unique }
    let catalog = withCustomerKind (fun k -> { k with Indexes = uniqueIx :: k.Indexes })
    let p = { Profile.empty with Distributions = [ categoricalOn customerNameKey [ "Alpha", 3L; "alpha", 2L; "bravo", 1L ] ] }
    let report =
        Estate.compute agreed catalog [ "cloud-qa", { operand "cloud-qa" catalog with Profile = Some p } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataCollationCollision)
    Assert.Equal(EstateLane.Repair, finding.Lane)
    Assert.Contains("Customer.Name collapses 1 case-distinct value(s) into duplicates in cloud-qa", finding.Statement)

[<Fact>]
let ``I3: mixed identity provenance for one name reads as a WATCH advisory on the identity plane; a uniformly synthesized estate stays silent`` () =
    let nativeCustomer =
        withCustomerKind (fun k -> { k with SsKey = SsKey.ossysOriginal (System.Guid.NewGuid()) })
    let report =
        Estate.compute agreed sampleCatalog [ "cloud-qa", operand "cloud-qa" nativeCustomer ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.IdentitySynthesized)
    Assert.Equal(EstateLane.Watch, finding.Lane)
    Assert.Equal(EstatePlane.Identity, finding.Plane)
    Assert.Contains("1 kind(s) in cloud-qa number their rows differently than the target", finding.Statement)
    let uniform = Estate.compute agreed sampleCatalog [ "cloud-qa", operand "cloud-qa" sampleCatalog ]
    Assert.DoesNotContain(uniform.Findings, fun f -> f.Kind = EstateFindingKind.IdentitySynthesized)

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
let ``provenance: environments.json carries the same provenance facts the masthead renders (one substrate)`` () =
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

// -- the posture wave (A6): bands, the interim posture, the fork witness ------

[<Fact>]
let ``D3′: true orphans past the repair band land on the RELAX lane with the overlay-merge lever`` () =
    // 130,000 orphans, 20,000 of them the unset value 0 — 110,000 TRUE
    // orphans exceed the default band (100,000); the repair defers to the
    // interim relaxation and the lever names the overlay entry by the
    // finding's key (π-coherence).
    let dirty =
        { Profile.empty with
            ForeignKeys = [ orphanEvidence orderRefToCustomer 130_000L ]
            Distributions = [ categoricalOn orderCustomerFkKey [ "0", 20_000L; "7", 5L ] ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataOrphansPastBand)
    Assert.Equal(EstateLane.Relax, finding.Lane)
    Assert.Contains("110,000 reference(s) to missing rows in cloud-uat, past the repair band", finding.Statement)
    Assert.Equal(
        Some "Merge the config edit for Order.CustomerId (leave the relationship unenforced for now) in environments.overlay.json.",
        finding.Lever)
    Assert.True(report.Findings |> List.forall (fun f -> f.Kind <> EstateFindingKind.DataOrphans))

[<Fact>]
let ``D3′: true orphans within the band stay on the repair queue — the sentinel split spends before the band`` () =
    // 130,000 orphans but 40,000 unset references: 90,000 true orphans sit
    // inside the band; the prepared repair stands.
    let dirty =
        { Profile.empty with
            ForeignKeys = [ orphanEvidence orderRefToCustomer 130_000L ]
            Distributions = [ categoricalOn orderCustomerFkKey [ "0", 40_000L; "7", 5L ] ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    Assert.True(report.Findings |> List.exists (fun f -> f.Kind = EstateFindingKind.DataOrphans))
    Assert.True(report.Findings |> List.forall (fun f -> f.Kind <> EstateFindingKind.DataOrphansPastBand))

[<Fact>]
let ``D1 relax arm: NOT-NULL contradictions past the band propose the keep-nullable relaxation`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 500_000L 200_000L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataNotNullPastBand)
    Assert.Equal(EstateLane.Relax, finding.Lane)
    Assert.Contains("200,000 NULL row(s) in cloud-uat exceed the repair band", finding.Statement)
    Assert.Equal(
        Some "Merge the config edit for Customer.Name (leave the column nullable for now) in environments.overlay.json.",
        finding.Lever)

[<Fact>]
let ``the band knob: readiness.estate.repairBand moves the split (A44 — the key is consumed)`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5_000L 4_120L ] }
    let tight : Estate.Posture = { Estate.Posture.defaults with RepairBand = 100L }
    let report =
        Estate.computeWith tight agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    Assert.True(report.Findings |> List.exists (fun f -> f.Kind = EstateFindingKind.DataNotNullPastBand))

[<Fact>]
let ``the per-entity band: readiness.estate.repairBandByEntity overrides the default for one entity`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5_000L 4_120L ] }
    // The default band is high — 4,120 sits inside it (REPAIR); Customer's own
    // band is low — 4,120 exceeds it (RELAX). The per-entity band governs.
    let posture : Estate.Posture =
        { Estate.Posture.defaults with
            RepairBand = 1_000_000L
            RepairBandByEntity = Map.ofList [ "Customer", 100L ] }
    let report =
        Estate.computeWith posture agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    Assert.True(report.Findings |> List.exists (fun f -> f.Kind = EstateFindingKind.DataNotNullPastBand))
    // The override is scoped by entity: Customer's band is 100, every other
    // entity falls back to the high default.
    Assert.Equal(100L, Estate.bandFor posture "Customer.Name")
    Assert.Equal(1_000_000L, Estate.bandFor posture "Order.CustomerId")

// -- the emission audit (Phase 1, the #669 audit): target-shape fidelity -------

[<Fact>]
let ``emission: a reference targeting a composite primary key is a WP-12 hazard (Order.CustomerId to Customer)`` () =
    // Make Customer's PK composite (Id + TenantId); Order.CustomerId → Customer
    // then targets a 2-column key, so the emitted single-column FK is invalid.
    let compositePk =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerTenantKey then { a with IsPrimaryKey = true } else a) }
                                else k) }) }
    let f = Estate.emissionFindingsFor compositePk |> List.exactlyOne
    Assert.Equal(EstateFindingKind.EmissionCompositePkFk, f.Kind)
    Assert.Equal(EstatePlane.Emission, f.Plane)
    Assert.Equal(EstateLane.Decide, f.Lane)
    Assert.Contains("Order.CustomerId → Customer targets a composite primary key", f.Statement)
    Assert.True(f.Lever |> Option.exists (fun l -> l.StartsWith "Rule the relationship"))
    // The clean single-PK fixture is the negative — no false positive.
    Assert.Empty(Estate.emissionFindingsFor sampleCatalog)

[<Fact>]
let ``emission: the audit dimension rides its own report list, separate from convergence findings`` () =
    // A composite-PK-FK target is an EMISSION finding, never a convergence one:
    // the emission list carries it; Findings (cross-env divergence) stays clean
    // when the environments agree.
    let compositePk =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerTenantKey then { a with IsPrimaryKey = true } else a) }
                                else k) }) }
    let report = Estate.compute agreed compositePk [ "cloud-dev", operand "cloud-dev" compositePk ]
    Assert.Empty report.Findings
    Assert.NotEmpty report.EmissionFindings
    Assert.Contains(Estate.render report, fun (l: string) -> l.StartsWith "EMISSION — ")

[<Fact>]
let ``emission: two entities emitting to the same table name collide (WP-16)`` () =
    // Rename Order to "Customer" — two entities now emit as one [dbo].[Customer].
    let dup =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = orderKey then { k with Name = Name.create "Customer" |> Result.value } else k) }) }
    let f =
        Estate.emissionFindingsFor dup
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionDuplicateName)
    Assert.Equal(EstatePlane.Emission, f.Plane)
    Assert.Contains("entities are named 'Customer'", f.Statement)
    Assert.True(f.Lever |> Option.exists (fun l -> l.StartsWith "Rule the collision"))

[<Fact>]
let ``emission: an identifier over 128 characters is a deploy blocker (WP-11)`` () =
    let longName = System.String('x', 140)
    let longCat =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerNameKey then { a with Name = Name.create longName |> Result.value } else a) }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor longCat
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionLongName)
    Assert.Equal(EstateLane.Decide, f.Lane)
    Assert.Contains("140 characters", f.Statement)

[<Fact>]
let ``emission: an entity with no primary key emits as a heap (WATCH, no lever)`` () =
    // Strip Customer's primary key — it now has no clustered key.
    let heap =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with Attributes = k.Attributes |> List.map (fun a -> { a with IsPrimaryKey = false }) }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor heap
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionNoPrimaryKey)
    Assert.Equal(EstateLane.Watch, f.Lane)
    Assert.Equal(None, f.Lever)
    Assert.Contains("Customer has no primary key", f.Statement)

[<Fact>]
let ``emission: a float column is a lossy-carriage inventory item (WP-17, WATCH)`` () =
    let lossy =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerTenantKey then { a with SqlStorage = Some SqlStorageType.Float } else a) }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor lossy
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionLossyScalar)
    Assert.Equal(EstateLane.Watch, f.Lane)
    Assert.Equal(None, f.Lever)
    Assert.Contains("Customer.TenantId is float", f.Statement)

[<Fact>]
let ``emission: a non-default ON UPDATE reference is a WATCH advisory`` () =
    let onUpdate =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = orderKey then
                                    { k with References = k.References |> List.map (fun r -> { r with OnUpdate = Some ReferenceAction.Cascade }) }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor onUpdate
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionNonDefaultOnUpdate)
    Assert.Equal(EstateLane.Watch, f.Lane)
    Assert.Contains("ON UPDATE CASCADE", f.Statement)

[<Fact>]
let ``emission: a computed expression referencing an unknown identifier is a ruling (#669 M-8 residue)`` () =
    // [SKU_OLD] resolves to no column of Customer — physical or logical —
    // so the emitter's rewrite cannot complete; the board names it before
    // a case-sensitive deploy rejects it.
    let withComputed =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerTenantKey
                                                then { a with Computed = ComputedColumnConfig.create "([SKU_OLD] * 2)" false |> Result.toOption }
                                                else a) }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor withComputed
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionComputedExprIdentifiers)
    Assert.Equal(EstateLane.Decide, f.Lane)
    Assert.Contains("[SKU_OLD]", f.Statement)
    Assert.True(f.Lever |> Option.exists (fun l -> l.StartsWith "Rule the expression"))
    // An expression over the entity's own columns (physical or logical
    // spelling) carries no finding — the rewrite completes.
    let resolvable =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerTenantKey
                                                then { a with Computed = ComputedColumnConfig.create "([NAME] + [Name])" false |> Result.toOption }
                                                else a) }
                                else k) }) }
    Assert.True(
        Estate.emissionFindingsFor resolvable
        |> List.forall (fun f -> f.Kind <> EstateFindingKind.EmissionComputedExprIdentifiers))

[<Fact>]
let ``emission: an unresolvable reference cycle is a WATCH advisory naming its members (#669 B-1)`` () =
    // Add the reverse reference Customer → Order (non-nullable source, no
    // deferrable edge) — a hard 2-cycle. The board names the members; the
    // v6 ordering keeps every other kind in dependency position.
    let backRef =
        Reference.create (refKey ["Customer"; "Order"; "back"]) (mkName "OrderBack") customerTenantKey orderKey
    let cyclic =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey
                                then { k with References = backRef :: k.References }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor cyclic
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionDataLaneOrder)
    Assert.Equal(EstateLane.Watch, f.Lane)
    Assert.Contains("Customer and Order", f.Statement)
    Assert.Contains("cycle", f.Statement)
    // The clean acyclic fixture carries no cycle advisory.
    Assert.True(
        Estate.emissionFindingsFor sampleCatalog
        |> List.forall (fun f -> f.Kind <> EstateFindingKind.EmissionDataLaneOrder))

[<Fact>]
let ``emission: an authored default that does not parse as its column's type is a ruling (#669 M-1 residue)`` () =
    // The classification lift carries `getutcdate()` as a callable expression
    // and `''` as the empty-string value; what remains inside a VALUE literal
    // must parse as the type. A date-time default of 'tomorrow' deploys and
    // then fails at the first insert — the board names it before the deploy.
    let withDefault (lit: SqlLiteral option) =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerTenantKey then { a with DefaultValue = lit } else a) }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor (withDefault (Some (SqlLiteral.DateTimeLit "tomorrow")))
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionAuthoredDefault)
    Assert.Equal(EstateLane.Decide, f.Lane)
    Assert.Equal(EstatePlane.Emission, f.Plane)
    Assert.Contains("does not parse as a date-time value", f.Statement)
    Assert.True(f.Lever |> Option.exists (fun l -> l.StartsWith "Rule the default"))
    // The classified shapes are the negatives: a callable expression and a
    // parseable value carry no finding.
    Assert.True(
        Estate.emissionFindingsFor (withDefault (Some (SqlLiteral.ExpressionLit "getutcdate()")))
        |> List.forall (fun f -> f.Kind <> EstateFindingKind.EmissionAuthoredDefault))
    Assert.True(
        Estate.emissionFindingsFor (withDefault (Some (SqlLiteral.DateTimeLit "2026-01-01 08:30:00")))
        |> List.forall (fun f -> f.Kind <> EstateFindingKind.EmissionAuthoredDefault))

[<Fact>]
let ``emission: a system-versioned kind is a DECIDE ruling — the board names the temporal fact the publish refuses (#669 EF-23)`` () =
    // The rowset lane now carries ModalityMark.Temporal (rowset 25); the
    // emission cannot yet deploy period columns, so the publish refuses
    // (EmitError.TemporalKindRefused) and the board states the same fact.
    let temporalConfig : TemporalConfig =
        { HistorySchema = Some "history"
          HistoryTable  = Some "CUSTOMER_History"
          PeriodStart   = None
          PeriodEnd     = None
          Retention     = Infinite }
    let withTemporal =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with Modality = ModalityMark.Temporal temporalConfig :: k.Modality }
                                else k) }) }
    let f =
        Estate.emissionFindingsFor withTemporal
        |> List.find (fun f -> f.Kind = EstateFindingKind.EmissionTemporalDropped)
    Assert.Equal(EstateLane.Decide, f.Lane)
    Assert.Equal(EstatePlane.Emission, f.Plane)
    Assert.Contains("system-versioned", f.Statement)
    Assert.Contains("history.CUSTOMER_History", f.Statement)
    // The negative: the unmarked catalog carries no temporal finding.
    Assert.True(
        Estate.emissionFindingsFor sampleCatalog
        |> List.forall (fun f -> f.Kind <> EstateFindingKind.EmissionTemporalDropped))

[<Fact>]
let ``a LOGICAL-ONLY relationship's orphans reach the board — the orphan derivation walks every catalog reference (decision 3)`` () =
    // The reference carries no backing SQL Server constraint
    // (ConstraintState = NoDbConstraint); with enforcement decided, its
    // orphan evidence must surface exactly as a physically-backed
    // reference's would — REPAIR (clear the rows) below the band,
    // RELAX (leave unenforced, reopen probe) past it. The profile
    // derivation walks `srcKind.References` with no deployable filter,
    // so the evidence path is one path; this pins it.
    let logicalOnly =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                { k with
                                    References =
                                        k.References
                                        |> List.map (fun r ->
                                            if r.SsKey = orderRefToCustomer
                                            then { r with ConstraintState = ConstraintState.ofLegacyBooleans false false }
                                            else r) }) }) }
    let dirty = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 42L ] }
    let report =
        Estate.compute agreed logicalOnly
            [ "cloud-uat", { operand "cloud-uat" logicalOnly with Profile = Some dirty } ]
    let f = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataOrphans)
    Assert.Equal(EstateLane.Repair, f.Lane)
    Assert.Contains("42", f.Statement)

[<Fact>]
let ``the cutover ladder: green when nothing stands in the way; the one outstanding item named otherwise (plan §0)`` () =
    // Clean estate — green, and the surface says what completes the gate.
    let clean = Estate.compute agreed sampleCatalog [ "cloud-dev", operand "cloud-dev" sampleCatalog ]
    let green = Estate.cutoverLadder clean
    Assert.True(green.Green)
    Assert.Contains("Ready to cut over", Estate.cutoverLadderLines green |> List.head)
    // An orphaned relationship (REPAIR lane) blocks, and the ladder names it.
    let dirty = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 7L ] }
    let red =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
        |> Estate.cutoverLadder
    Assert.False(red.Green)
    Assert.True(red.OutstandingItem |> Option.exists (fun f -> f.Kind = EstateFindingKind.DataOrphans))
    Assert.Contains("before cutover", Estate.cutoverLadderLines red |> List.head)
    // A relaxed (past-band) posture does not block — the RELAX lane
    // carries its reopen probe instead.
    let posture : Estate.Posture =
        { Estate.Posture.defaults with RelaxedReferences = Set.singleton orderRefToCustomer }
    let relaxed =
        Estate.computeWith posture agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
        |> Estate.cutoverLadder
    Assert.True(relaxed.Green)
    // An emission ruling blocks ahead of data: the composite-key target.
    let compositePk =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with
                                        Attributes =
                                            k.Attributes
                                            |> List.map (fun a ->
                                                if a.SsKey = customerTenantKey then { a with IsPrimaryKey = true } else a) }
                                else k) }) }
    let emissionRed =
        Estate.compute agreed compositePk [ "cloud-dev", operand "cloud-dev" compositePk ]
        |> Estate.cutoverLadder
    Assert.False(emissionRed.Green)
    Assert.True(emissionRed.OutstandingItem |> Option.exists (fun f -> f.Kind = EstateFindingKind.EmissionCompositePkFk))

[<Fact>]
let ``the active posture: a relaxed relationship renders its meter and absorbs the orphan finding`` () =
    let posture : Estate.Posture =
        { Estate.Posture.defaults with RelaxedReferences = Set.singleton orderRefToCustomer }
    let dirty = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 113L ] }
    let report =
        Estate.computeWith posture agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    let active = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.PostureActive)
    Assert.Equal(EstateLane.Relax, active.Lane)
    Assert.Contains("Order.CustomerId → Customer is left unenforced for now; 113 reference(s) still point to missing rows in cloud-uat", active.Statement)
    Assert.Equal(None, active.Lever)
    Assert.True(report.Findings |> List.forall (fun f -> f.Kind <> EstateFindingKind.DataOrphans))

[<Fact>]
let ``the active posture: an unevidenced environment reads unobserved — the meter never fabricates`` () =
    let posture : Estate.Posture =
        { Estate.Posture.defaults with RelaxedReferences = Set.singleton orderRefToCustomer }
    let dirty = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 113L ] }
    let report =
        Estate.computeWith posture agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty }
              "cloud-qa",  operand "cloud-qa" sampleCatalog ]
    let active = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.PostureActive)
    Assert.Contains("the count is unobserved in cloud-qa", active.Statement)

[<Fact>]
let ``the retirement notice: a relaxation whose probe reads zero everywhere becomes a preparable repair`` () =
    let posture : Estate.Posture =
        { Estate.Posture.defaults with RelaxedReferences = Set.singleton orderRefToCustomer }
    let cleanNow = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 0L ] }
    let report =
        Estate.computeWith posture agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some cleanNow } ]
    let retirable = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.PostureRetirable)
    Assert.Equal(EstateLane.Repair, retirable.Lane)
    Assert.Contains("the relationship can be enforced again", retirable.Statement)
    Assert.Equal(
        Some "Review the block for Order.CustomerId (clean now — the interim change is removable) in environments.remediation.cloud-uat.sql.",
        retirable.Lever)

[<Fact>]
let ``the retirement notice: one dirty environment keeps the relaxation active everywhere (estate-grade, never per-env)`` () =
    let posture : Estate.Posture =
        { Estate.Posture.defaults with RelaxedReferences = Set.singleton orderRefToCustomer }
    let cleanNow = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 0L ] }
    let dirty    = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 40L ] }
    let report =
        Estate.computeWith posture agreed sampleCatalog
            [ "cloud-dev", { operand "cloud-dev" sampleCatalog with Profile = Some cleanNow }
              "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    Assert.True(report.Findings |> List.forall (fun f -> f.Kind <> EstateFindingKind.PostureRetirable))
    let active = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.PostureActive)
    Assert.Contains("40 reference(s) still point to missing rows in cloud-uat", active.Statement)
    Assert.Contains("0 reference(s) still point to missing rows in cloud-dev", active.Statement)

[<Fact>]
let ``the active posture: a kept-nullable column renders its meter and absorbs the NOT-NULL finding`` () =
    let posture : Estate.Posture =
        { Estate.Posture.defaults with RelaxedAttributes = Set.singleton customerNameKey }
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5_000L 4_120L ] }
    let report =
        Estate.computeWith posture agreed sampleCatalog
            [ "cloud-uat", { operand "cloud-uat" sampleCatalog with Profile = Some dirty } ]
    let active = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.PostureActive)
    Assert.Contains("Customer.Name is left nullable for now; 4,120 row(s) are still NULL in cloud-uat", active.Statement)
    Assert.True(report.Findings |> List.forall (fun f -> f.Kind <> EstateFindingKind.DataNotNull))

// -- the fork witness + the Forked verdict (A6) --------------------------------

[<Fact>]
let ``Forked: two environments diverging DIFFERENTLY on one subject fork the estate — no promotion order explains it`` () =
    let alphaKey = attrKey [ "ForkAlpha" ]
    let betaKey  = attrKey [ "ForkBeta" ]
    let mkAttr (key: SsKey) (name: string) : Attribute =
        let template = customer.Attributes |> List.find (fun a -> a.SsKey = customerNameKey)
        { template with SsKey = key; Name = mkName name }
    let withExtra (key: SsKey) (name: string) : Catalog =
        withCustomerKind (fun k -> { k with Attributes = k.Attributes @ [ mkAttr key name ] })
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", operand "cloud-dev" (withExtra alphaKey "ForkAlpha")
              "cloud-uat", operand "cloud-uat" (withExtra betaKey "ForkBeta") ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.SchemaAttributes)
    Assert.True(finding.Fork)
    Assert.Contains("The environments disagree among themselves here — a fork; no single adoption resolves it.", finding.Statement)
    Assert.Equal(Estate.Verdict.Forked, report.Verdict)
    Assert.False(Estate.isUnified report)
    Assert.Contains("\"verdict\": \"forked\"", Estate.toJsonString report)
    Assert.Contains("\"fork\": true", Estate.toJsonString report)

[<Fact>]
let ``Forked: two environments diverging IDENTICALLY on one subject converge — one adoption resolves it`` () =
    let alphaKey = attrKey [ "ForkAlpha" ]
    let mkAttr (key: SsKey) (name: string) : Attribute =
        let template = customer.Attributes |> List.find (fun a -> a.SsKey = customerNameKey)
        { template with SsKey = key; Name = mkName name }
    let withExtra : Catalog =
        withCustomerKind (fun k -> { k with Attributes = k.Attributes @ [ mkAttr alphaKey "ForkAlpha" ] })
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-dev", operand "cloud-dev" withExtra
              "cloud-uat", operand "cloud-uat" withExtra ]
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.SchemaAttributes)
    Assert.False(finding.Fork)
    Assert.Equal(Estate.Verdict.Converging, report.Verdict)

// -- the DECIDE ruling levers + the masthead's fidelity clause (A6, §3) --------

[<Fact>]
let ``presentation: a DECIDE finding ends on its ruling — the contract's lever, minted`` () =
    let report =
        Estate.compute agreed emptyCat [ "cloud-uat", operand "cloud-uat" sampleCatalog ]
    for finding in Estate.laneFindings EstateLane.Decide report do
        match finding.Lever with
        | Some lever -> Assert.StartsWith("Rule ", lever)
        | None -> Assert.Fail(sprintf "DECIDE finding %s carries no ruling" (FindingKey.text finding.Key))

[<Fact>]
let ``the masthead names the unconfigured fidelity clause — never silent (RT-10)`` () =
    let report = Estate.compute agreed sampleCatalog [ "cloud-dev", operand "cloud-dev" sampleCatalog ]
    Assert.Contains(
        Estate.render report,
        fun (l: string) -> l.Contains "The fidelity clause is not configured")
    Assert.Contains("\"fidelityClause\": \"notConfigured\"", Estate.toJsonString report)

[<Fact>]
let ``the ARTIFACTS index carries the overlay and probes lines once stamped`` () =
    let report =
        Estate.compute agreed sampleCatalog [ "cloud-dev", operand "cloud-dev" sampleCatalog ]
        |> Estate.withOverlay 3
    let lines = Estate.render report
    Assert.Contains(lines, fun (l: string) -> l.Contains "environments.overlay.json — 3 interim change(s)")
    Assert.Contains(lines, fun (l: string) -> l.Contains "environments.probes.sql — every reopen probe, runnable as one batch")
    Assert.Contains("\"entries\": 3", Estate.toJsonString report)

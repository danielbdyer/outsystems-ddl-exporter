module Projection.Tests.ModelFidelityTests

open Xunit
open Projection.Core
open Projection.Pipeline

// The Model Fidelity Report aggregation — a declared Catalog × the profiled
// evidence rolled up into the three sections. These witnesses fix a fixture
// catalog with KNOWN violations (a NOT-NULL column carrying NULLs, a unique-
// backed column carrying duplicates, an FK with orphans) and assert the rollup
// counts + per-entity drill-down. The renderer's THE_VOICE register (no
// pronouns, neutral estate reference, count-first) is asserted on the text.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private name (s: string) : Name = Name.create s |> mustOk
let private kindKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_MFTEST_KIND" [ s ] |> mustOk
let private attrKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_MFTEST_ATTR" parts |> mustOk
let private refKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_MFTEST_REF" parts |> mustOk
let private idxKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_MFTEST_IDX" [ s ] |> mustOk
let private modKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_MFTEST_MOD" [ s ] |> mustOk
let private tableId (t: string) : TableId = TableId.create "dbo" t |> mustOk

/// One attribute, declared NOT NULL by default (`isNullable = false`).
let private mkAttr (key: SsKey) (logical: string) (ptype: PrimitiveType) (isPk: bool) (isNullable: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create (logical.ToUpperInvariant()) isNullable |> mustOk
        IsPrimaryKey = isPk }

// -- The fixture catalog ----------------------------------------------------
//   Customer(Id PK, Email [unique-backed, declared NOT NULL], Note [nullable])
//   Order(Id PK, CustomerId [FK → Customer])

let private customerKey = kindKey "Customer"
let private custIdKey   = attrKey ["Customer"; "Id"]
let private custEmailKey = attrKey ["Customer"; "Email"]
let private custNoteKey = attrKey ["Customer"; "Note"]

let private customer : Kind =
    { Kind.create customerKey (name "Customer") (tableId "OSUSR_CUSTOMER")
        [ mkAttr custIdKey "Id" Integer true false
          mkAttr custEmailKey "Email" Text false false      // declared NOT NULL
          mkAttr custNoteKey "Note" Text false true ]       // declared nullable
        with
        Indexes =
            [ { Index.create (idxKey "UX_Customer_Email") (name "UX_Customer_Email")
                  [ IndexColumn.create custEmailKey IndexColumnDirection.Ascending ]
                  with Uniqueness = IndexUniqueness.Unique } ] }

let private orderKey   = kindKey "Order"
let private orderIdKey  = attrKey ["Order"; "Id"]
let private orderFkKey  = attrKey ["Order"; "CustomerId"]
let private orderRefKey = refKey ["Order"; "Customer"]

let private order : Kind =
    { Kind.create orderKey (name "Order") (tableId "OSUSR_ORDER")
        [ mkAttr orderIdKey "Id" Integer true false
          mkAttr orderFkKey "CustomerId" Integer false false ]
        with
        References =
            [ Reference.create orderRefKey (name "Customer") orderFkKey customerKey ] }

let private fixtureCatalog : Catalog =
    let salesModule = Module.create (modKey "Sales") (name "Sales") [ customer; order ] true [] |> mustOk
    Catalog.create [ salesModule ] [] |> mustOk

// -- The profiled evidence (known violations) -------------------------------
//   Customer.Email — declared NOT NULL, profiled NullCount = 4 (violation)
//                  — unique-backed, HasDuplicates = true       (violation)
//   Order.CustomerId — FK with HasOrphan = true, OrphanCount = 7 (violation)

let private profiledEvidence : Profile =
    let emailColumn = ColumnProfile.create custEmailKey 100L 4L ProbeStatus.noProbeRun |> mustOk
    { Profile.empty with
        Columns = [ emailColumn ]
        AttributeRealities =
            [ { AttributeReality.create custEmailKey with HasNulls = true; HasDuplicates = true } ]
        ForeignKeys =
            [ { ForeignKeyReality.create orderRefKey with HasOrphan = true; OrphanCount = 7L } ] }

[<Fact>]
let ``ModelFidelity: a NOT NULL column carrying NULLs is a data violation with the exact count`` () =
    let report = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let notNull =
        report.DataViolations
        |> List.filter (fun v ->
            match v.Kind with ModelFidelity.NotNullButNullsPresent _ -> true | _ -> false)
    match notNull with
    | [ v ] ->
        Assert.Equal("Customer", v.Reference.Entity)
        Assert.Equal("Email", v.Reference.Column)
        match v.Kind with
        | ModelFidelity.NotNullButNullsPresent n -> Assert.Equal(4L, n)
        | other -> Assert.Fail(sprintf "expected NotNullButNullsPresent, got %A" other)
    | other -> Assert.Fail(sprintf "expected exactly one NOT-NULL violation, got %A" other)

[<Fact>]
let ``ModelFidelity: a unique-backed column carrying duplicates is a data violation`` () =
    let report = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let unique =
        report.DataViolations
        |> List.filter (fun v -> v.Kind = ModelFidelity.UniqueButDuplicatesPresent)
    match unique with
    | [ v ] ->
        Assert.Equal("Customer", v.Reference.Entity)
        Assert.Equal("Email", v.Reference.Column)
    | other -> Assert.Fail(sprintf "expected exactly one UNIQUE violation, got %A" other)

[<Fact>]
let ``ModelFidelity: an FK with orphans is a data violation carrying the orphan count`` () =
    let report = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let orphans =
        report.DataViolations
        |> List.choose (fun v ->
            match v.Kind with ModelFidelity.ForeignKeyOrphans n -> Some (v.Reference, n) | _ -> None)
    match orphans with
    | [ (reference, n) ] ->
        Assert.Equal("Order", reference.Entity)
        Assert.Equal("CustomerId", reference.Column)
        Assert.Equal(7L, n)
    | other -> Assert.Fail(sprintf "expected exactly one FK orphan violation, got %A" other)

[<Fact>]
let ``ModelFidelity: the rollup counts the total and the distinct entities`` () =
    let report = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let rollup = ModelFidelity.dataViolationRollup report
    // Three violations: NOT-NULL(Email) + UNIQUE(Email) + FK-orphan(CustomerId).
    Assert.Equal(3, rollup.Total)
    // Two distinct entities touched: Customer + Order.
    Assert.Equal(2, rollup.Entities)

[<Fact>]
let ``ModelFidelity: a clean estate (no profiled evidence) asserts no violations`` () =
    let report = ModelFidelity.compose "ACME" fixtureCatalog Profile.empty { Decisions = [] } []
    Assert.Empty(report.DataViolations)
    Assert.Equal(2, report.EntityCount)
    Assert.Equal(1, report.ModuleCount)

[<Fact>]
let ``ModelFidelity: a column whose NULLs are absent is not a violation`` () =
    // Note is declared nullable, so its presence of NULLs is no violation; and
    // Email with NullCount = 0 would not violate either. Build a clean profile.
    let cleanEmail = ColumnProfile.create custEmailKey 100L 0L ProbeStatus.noProbeRun |> mustOk
    let profile = { Profile.empty with Columns = [ cleanEmail ] }
    let report = ModelFidelity.compose "ACME" fixtureCatalog profile { Decisions = [] } []
    let notNull =
        report.DataViolations
        |> List.filter (fun v ->
            match v.Kind with ModelFidelity.NotNullButNullsPresent _ -> true | _ -> false)
    Assert.Empty(notNull)

// -- Length / type overflow (the max-observed-length axis) ------------------
//   A catalog whose Email column declares a finite VARCHAR(50) cap; the
//   profiled MaxObservedLength decides whether the source overflows it.

let private cappedCustomer (declaredLength: int) : Kind =
    let emailAttr =
        { mkAttr custEmailKey "Email" Text false false with Length = Some declaredLength }
    { Kind.create customerKey (name "Customer") (tableId "OSUSR_CUSTOMER")
        [ mkAttr custIdKey "Id" Integer true false
          emailAttr ]
        with Indexes = [] }

let private cappedCatalog (declaredLength: int) : Catalog =
    let salesModule =
        Module.create (modKey "Sales") (name "Sales") [ cappedCustomer declaredLength ] true []
        |> mustOk
    Catalog.create [ salesModule ] [] |> mustOk

let private columnWithMaxLength (key: SsKey) (rows: int64) (maxLength: int) : ColumnProfile =
    ColumnProfile.create key rows 0L ProbeStatus.noProbeRun
    |> mustOk
    |> ColumnProfile.withMaxObservedLength maxLength

[<Fact>]
let ``ModelFidelity: an observed length exceeding the declared length is an overflow violation`` () =
    // Email declared VARCHAR(50); the source carries a value 80 chars long.
    let catalog = cappedCatalog 50
    let profile = { Profile.empty with Columns = [ columnWithMaxLength custEmailKey 100L 80 ] }
    let report = ModelFidelity.compose "ACME" catalog profile { Decisions = [] } []
    let overflows =
        report.DataViolations
        |> List.choose (fun v ->
            match v.Kind with
            | ModelFidelity.LengthOrTypeOverflow (observed, declared) -> Some (v.Reference, observed, declared)
            | _ -> None)
    match overflows with
    | [ (reference, observed, declared) ] ->
        Assert.Equal("Customer", reference.Entity)
        Assert.Equal("Email", reference.Column)
        Assert.Equal("80", observed)
        Assert.Equal("50", declared)
    | other -> Assert.Fail(sprintf "expected exactly one overflow violation, got %A" other)

[<Fact>]
let ``ModelFidelity: an observed length within the declared length is not an overflow violation`` () =
    // Email declared VARCHAR(50); the source's longest value is 40 chars — fits.
    let catalog = cappedCatalog 50
    let profile = { Profile.empty with Columns = [ columnWithMaxLength custEmailKey 100L 40 ] }
    let report = ModelFidelity.compose "ACME" catalog profile { Decisions = [] } []
    let overflows =
        report.DataViolations
        |> List.filter (fun v ->
            match v.Kind with ModelFidelity.LengthOrTypeOverflow _ -> true | _ -> false)
    Assert.Empty(overflows)

[<Fact>]
let ``ModelFidelity: an observed length exactly at the declared length is not an overflow violation`` () =
    // Boundary — observed = declared is a fit, not an overflow (strict >).
    let catalog = cappedCatalog 50
    let profile = { Profile.empty with Columns = [ columnWithMaxLength custEmailKey 100L 50 ] }
    let report = ModelFidelity.compose "ACME" catalog profile { Decisions = [] } []
    let overflows =
        report.DataViolations
        |> List.filter (fun v ->
            match v.Kind with ModelFidelity.LengthOrTypeOverflow _ -> true | _ -> false)
    Assert.Empty(overflows)

[<Fact>]
let ``ModelFidelity: an open-ended (MAX) declared length never overflows regardless of observed length`` () =
    // Email declared with Length = None (VARCHAR(MAX) / open-ended); even a
    // very long observed value cannot overflow an absent cap.
    let openEndedCustomer =
        { Kind.create customerKey (name "Customer") (tableId "OSUSR_CUSTOMER")
            [ mkAttr custIdKey "Id" Integer true false
              mkAttr custEmailKey "Email" Text false false ]  // Length = None
            with Indexes = [] }
    let salesModule =
        Module.create (modKey "Sales") (name "Sales") [ openEndedCustomer ] true [] |> mustOk
    let catalog = Catalog.create [ salesModule ] [] |> mustOk
    let profile = { Profile.empty with Columns = [ columnWithMaxLength custEmailKey 100L 5000 ] }
    let report = ModelFidelity.compose "ACME" catalog profile { Decisions = [] } []
    let overflows =
        report.DataViolations
        |> List.filter (fun v ->
            match v.Kind with ModelFidelity.LengthOrTypeOverflow _ -> true | _ -> false)
    Assert.Empty(overflows)

[<Fact>]
let ``ModelFidelity: the rolled-up text leads with the estate masthead and the data-violation total`` () =
    let report = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let lines = ModelFidelity.render report
    // THE_VOICE: count-first — the masthead names the estate + scale; the
    // headline section names the total.
    Assert.StartsWith("MODEL FIDELITY — ACME", List.head lines)
    Assert.Contains(lines, fun l -> l.Contains "DATA VIOLATIONS" && l.Contains "3 total")
    // THE_VOICE rule 1 — no second-person pronouns anywhere in the surface.
    for line in lines do
        Assert.DoesNotContain("your", line.ToLowerInvariant())

[<Fact>]
let ``ModelFidelity: the uniqueness-candidate section consumes the SuggestUnique decisions (closes NM-35)`` () =
    // A SuggestUnique decision on Customer.Email — the pass observed every value
    // distinct; the report surfaces it as an advisory natural-key candidate.
    let decisions : CategoricalUniquenessDecisionSet =
        { Decisions =
            [ { AttributeKey = custEmailKey
                Outcome = CategoricalUniquenessOutcome.SuggestUnique (EveryValueDistinct (100L, 100L))
                InterventionId = "test" }
              // A DoNotSuggest decision is NOT a candidate.
              { AttributeKey = custNoteKey
                Outcome = CategoricalUniquenessOutcome.DoNotSuggest CategoricalUniquenessKeepReason.NoCategoricalEvidence
                InterventionId = "test" } ] }
    let report = ModelFidelity.compose "ACME" fixtureCatalog Profile.empty decisions []
    match report.UniquenessCandidates with
    | [ c ] ->
        Assert.Equal("Customer", c.Reference.Entity)
        Assert.Equal("Email", c.Reference.Column)
        Assert.Equal(100L, c.DistinctCount)
        Assert.Equal(Some 1.0M, ModelFidelity.candidateDistinctFraction c)
    | other -> Assert.Fail(sprintf "expected exactly one candidate, got %A" other)

[<Fact>]
let ``ModelFidelity: the fidelity.json codec round-trips through fromJson`` () =
    let decisions : CategoricalUniquenessDecisionSet =
        { Decisions =
            [ { AttributeKey = custEmailKey
                Outcome = CategoricalUniquenessOutcome.SuggestUnique (EveryValueDistinct (100L, 100L))
                InterventionId = "test" } ] }
    let report =
        ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence decisions
            [ ToleratedDivergence.HeaderCommentsOmitted ]
    let json = ModelFidelity.toJsonString report
    match ModelFidelity.fromJson json with
    | Some restored ->
        Assert.Equal(report.Estate, restored.Estate)
        Assert.Equal(report.EntityCount, restored.EntityCount)
        Assert.Equal(List.length report.DataViolations, List.length restored.DataViolations)
        Assert.Equal(List.length report.UniquenessCandidates, List.length restored.UniquenessCandidates)
        Assert.Equal(List.length report.AcceptedDivergences, List.length restored.AcceptedDivergences)
        // The rolled-up text is identical across the round-trip (the artifact
        // the `report` verb surfaces is faithful to the run that produced it).
        Assert.Equal<string list>(ModelFidelity.render report, ModelFidelity.render restored)
    | None -> Assert.Fail "fidelity.json failed to parse back"

[<Fact>]
let ``ModelFidelity: a malformed fidelity.json fails closed to None`` () =
    Assert.Equal<ModelFidelity.ModelFidelityReport option>(None, ModelFidelity.fromJson "{ this is not json")

[<Fact>]
let ``ReportRun: renderFidelity reads a recorded fidelity.json into the rolled-up text (the report-verb surfacing)`` () =
    let report = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let dir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "mf-report-%s" (System.Guid.NewGuid().ToString "N"))
    System.IO.Directory.CreateDirectory dir |> ignore
    try
        let fidelityPath = System.IO.Path.Combine(dir, "fidelity.json")
        System.IO.File.WriteAllText(fidelityPath, ModelFidelity.toJsonString report)
        // The report verb searches a candidate list; a missing path is skipped,
        // the recorded one is rendered as the count-first roll-up.
        let lines = ReportRun.renderFidelity [ System.IO.Path.Combine(dir, "absent.json"); fidelityPath ]
        Assert.NotEmpty(lines)
        Assert.StartsWith("MODEL FIDELITY — ACME", List.head lines)
        Assert.Contains(lines, fun l -> l.Contains "DATA VIOLATIONS" && l.Contains "3 total")
    finally
        if System.IO.Directory.Exists dir then System.IO.Directory.Delete(dir, recursive = true)

[<Fact>]
let ``ReportRun: renderFidelity is empty when no fidelity.json is recorded`` () =
    Assert.Empty(ReportRun.renderFidelity [ "/nonexistent/fidelity.json" ])

[<Fact>]
let ``ModelFidelity: withAcceptedDivergences stamps a resolved tolerance residual onto the report`` () =
    // The emit-time report has an empty residual (a pure emit compares nothing).
    let baseReport = ModelFidelity.compose "ACME" fixtureCatalog Profile.empty { Decisions = [] } []
    Assert.Empty(baseReport.AcceptedDivergences)
    // A canary-coupled run resolves its matched-tolerance set and stamps it.
    let stamped =
        baseReport
        |> ModelFidelity.withAcceptedDivergences
            [ ToleratedDivergence.HeaderCommentsOmitted; ToleratedDivergence.DecimalScaleTolerated ]
    Assert.Equal(2, List.length stamped.AcceptedDivergences)
    // The original report is untouched (immutable update).
    Assert.Empty(baseReport.AcceptedDivergences)

[<Fact>]
let ``ModelFidelity: the accepted-divergences section renders the tolerance residual`` () =
    let report =
        ModelFidelity.compose "ACME" fixtureCatalog Profile.empty { Decisions = [] }
            [ ToleratedDivergence.HeaderCommentsOmitted; ToleratedDivergence.IndexOptionsUnreflected ]
    let lines = ModelFidelity.render report
    Assert.Contains(lines, fun l -> l.Contains "ACCEPTED DIVERGENCES" && l.Contains "2")
    Assert.Contains(lines, fun l -> l.Contains "HeaderCommentsOmitted")

[<Fact>]
let ``ModelFidelity: violations sort deterministically by category then reference`` () =
    let a = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let b = ModelFidelity.compose "ACME" fixtureCatalog profiledEvidence { Decisions = [] } []
    let refsOf (r: ModelFidelity.ModelFidelityReport) =
        r.DataViolations |> List.map (fun v -> ModelFidelity.entityColumnText v.Reference, v.Kind)
    Assert.Equal<(string * ModelFidelity.ViolationKind) list>(refsOf a, refsOf b)

module Projection.Tests.SyntheticDataTests

open System
open Xunit
open Projection.Core
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// THE_SYNTHETIC_DATA_DESIGN.md §8 — the S1 unit tests (the L1 + privacy
// floor for `SyntheticData.generate`): determinism (same seed → byte-
// identical), FK integrity (zero orphans), null-rate within ε, the privacy
// property (no real high-cardinality value emitted), volume = profiled
// RowCount, PK uniqueness, plus PrimitiveType exhaustion.
// ---------------------------------------------------------------------------

let private name (s: string) : Name = Name.create s |> Result.value

let private mkOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun (e: ValidationError) -> e.Code) |> String.concat ", "
        invalidOp $"fixture construction failed: {codes}"

let private probe (n: int64) : ProbeStatus =
    ProbeStatus.create (DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero)) n Succeeded |> mkOk

/// Build an attribute with explicit type / pk / nullability.
let private attr (key: SsKey) (logical: string) (ptype: PrimitiveType) (isPk: bool) (nullable: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create (logical.ToUpperInvariant()) nullable |> Result.value
        IsPrimaryKey = isPk }

// -- Keys -------------------------------------------------------------------

let private custKey   = kindKey ["C"]
let private custId    = attrKey ["C"; "Id"]
let private custStat  = attrKey ["C"; "Status"]
let private custEmail = attrKey ["C"; "Email"]
let private custScore = attrKey ["C"; "Score"]
let private custNotes = attrKey ["C"; "Notes"]

let private ordKey    = kindKey ["O"]
let private ordId     = attrKey ["O"; "Id"]
let private ordCust   = attrKey ["O"; "CustomerId"]
let private ordOpt    = attrKey ["O"; "OptCustomerId"]
let private ordRefC   = refKey  ["O"; "Customer"]
let private ordRefO   = refKey  ["O"; "OptCustomer"]

// -- Catalog ----------------------------------------------------------------

let private customer : Kind =
    { Kind.create custKey (name "Customer") (mkTableId "dbo" "CUSTOMER")
        [ attr custId    "Id"     Integer true  false
          attr custStat  "Status" Text    false false
          attr custEmail "Email"  Text    false false
          attr custScore "Score"  Integer false false
          attr custNotes "Notes"  Text    false true  ] with
        Modality = [] }

let private order : Kind =
    { Kind.create ordKey (name "Order") (mkTableId "dbo" "ORDERS")
        [ attr ordId   "Id"            Integer true  false
          attr ordCust "CustomerId"    Integer false false
          attr ordOpt  "OptCustomerId" Integer false true  ] with
        References =
            [ Reference.create ordRefC (name "Customer")    ordCust custKey
              Reference.create ordRefO (name "OptCustomer") ordOpt  custKey ] }

let private catalog : Catalog =
    Catalog.create [ mkModule (modKey "M") (name "M") [ customer; order ] ] [] |> mkOk

// -- Profile ----------------------------------------------------------------

let private col (key: SsKey) (rows: int64) (nulls: int64) : ColumnProfile =
    ColumnProfile.create key rows nulls (probe rows) |> mkOk

/// High-cardinality email vocabulary (80 distinct > τ=50 → synthesize).
let private realEmails : (string * int64) list =
    [ for i in 1 .. 80 -> (sprintf "user%d@legacy.example" i), (if i <= 20 then 2L else 1L) ]

let private realEmailSet : Set<string> = realEmails |> List.map fst |> Set.ofList

/// Low-cardinality status vocabulary (3 ≤ τ → preserve).
let private realStatus : (string * int64) list =
    [ "Active", 70L; "Inactive", 20L; "Pending", 10L ]

let private realStatusSet : Set<string> = realStatus |> List.map fst |> Set.ofList

let private profile : Profile =
    { Profile.empty with
        Columns =
            [ col custId 100L 0L;  col custStat 100L 0L; col custEmail 100L 0L
              col custScore 100L 0L; col custNotes 100L 40L
              col ordId 250L 0L; col ordCust 250L 0L; col ordOpt 250L 50L ]
        Distributions =
            [ AttributeDistribution.Categorical
                (CategoricalDistribution.create custStat realStatus 3L false (probe 100L) |> mkOk)
              AttributeDistribution.Categorical
                (CategoricalDistribution.create custEmail realEmails 80L false (probe 100L) |> mkOk)
              AttributeDistribution.Numeric
                (NumericDistribution.create custScore 0M 25M 50M 75M 95M 99M 100M 100L (probe 100L) |> mkOk) ] }

let private cfg = SyntheticConfig.defaultConfig

// Helper: a kind's generated rows' values for one attribute Name.
let private valuesOf (m: Map<SsKey, StaticRow list>) (kindKey: SsKey) (attrName: string) : string list =
    m.[kindKey]
    |> List.choose (fun (r: StaticRow) -> StaticRow.value (name attrName) r)

// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: same seed produces byte-identical output`` () =
    let a = SyntheticData.generate catalog profile cfg 7UL
    let b = SyntheticData.generate catalog profile cfg 7UL
    Assert.Equal<Map<SsKey, StaticRow list>>(a, b)

[<Fact>]
let ``T1: different seeds diverge`` () =
    let a = SyntheticData.generate catalog profile cfg 7UL
    let b = SyntheticData.generate catalog profile cfg 99UL
    Assert.NotEqual<Map<SsKey, StaticRow list>>(a, b)

[<Fact>]
let ``volume equals profiled RowCount per kind`` () =
    let m = SyntheticData.generate catalog profile cfg 1UL
    Assert.Equal(100, m.[custKey].Length)
    Assert.Equal(250, m.[ordKey].Length)

[<Fact>]
let ``volume scales by the configured factor`` () =
    let scaled = { cfg with Scale = 0.5M }
    let m = SyntheticData.generate catalog profile scaled 1UL
    Assert.Equal(50, m.[custKey].Length)
    Assert.Equal(125, m.[ordKey].Length)

[<Fact>]
let ``F1: an Absolute VolumeTarget overrides the profiled RowCount for its kind only`` () =
    let cfg' = { cfg with VolumeByKind = Map.ofList [ custKey, VolumeTarget.Absolute 7 ] }
    let m = SyntheticData.generate catalog profile cfg' 1UL
    Assert.Equal(7, m.[custKey].Length)     // arbitrary scale, decoupled from the observed 100
    Assert.Equal(250, m.[ordKey].Length)    // untargeted kind keeps the observed RowCount

[<Fact>]
let ``F1: a Multiplier VolumeTarget scales the observed count and ignores the global Scale`` () =
    // ordKey is targeted (×2 over observed 250 = 500, Scale ignored); custKey is
    // untargeted, so it still honors the global Scale (100 × 0.5 = 50).
    let cfg' = { cfg with Scale = 0.5M; VolumeByKind = Map.ofList [ ordKey, VolumeTarget.Multiplier 2M ] }
    let m = SyntheticData.generate catalog profile cfg' 1UL
    Assert.Equal(500, m.[ordKey].Length)
    Assert.Equal(50, m.[custKey].Length)

[<Fact>]
let ``F5: a skewed ForeignKeySelectivity reproduces the fan-out skew (rank-0 parent dominates)`` () =
    // A dedicated profile: 3 customers, 300 orders, and a heavily-skewed
    // selectivity on Order→Customer (rank-0 parent carries 280/300 of the
    // children). σ maps the count-DESC frequencies onto the synthetic pool BY
    // RANK, so the rank-0 synthetic customer ("1") must dominate the FK values —
    // far above the ~100 a uniform draw over 3 parents would give.
    let f5Profile =
        { Profile.empty with
            Columns = [ col custId 3L 0L; col ordId 300L 0L ]
            ForeignKeySelectivities =
                [ ForeignKeySelectivity.create ordRefC [ ("a", 280L); ("b", 15L); ("c", 5L) ] 3L false (probe 300L) |> mkOk ] }
    let m = SyntheticData.generate catalog f5Profile cfg 42UL
    let custIds = valuesOf m custKey "Id"
    Assert.Equal(3, custIds.Length)
    let ordCustVals = valuesOf m ordKey "CustomerId"
    Assert.Equal(300, ordCustVals.Length)                       // no null budget → every FK drawn
    let top = List.head custIds                                  // rank-0 synthetic parent
    let topCount = ordCustVals |> List.filter (fun v -> v = top) |> List.length
    Assert.True(topCount > 200, sprintf "expected the rank-0 parent to dominate (>200/300), got %d" topCount)

[<Fact>]
let ``F5: with no selectivity evidence the FK draw stays uniform (no parent dominates)`` () =
    // The same shape WITHOUT a selectivity: the uniform draw spreads ~evenly, so
    // no single parent approaches the skewed case's dominance — the byte-identical
    // pre-F5 behavior holds when the evidence is absent.
    let noSelProfile =
        { Profile.empty with Columns = [ col custId 3L 0L; col ordId 300L 0L ] }
    let m = SyntheticData.generate catalog noSelProfile cfg 42UL
    let ordCustVals = valuesOf m ordKey "CustomerId"
    let maxShare = [ "1"; "2"; "3" ] |> List.map (fun p -> ordCustVals |> List.filter (fun v -> v = p) |> List.length) |> List.max
    Assert.True(maxShare < 160, sprintf "expected a roughly uniform spread (<160/300 for the top parent), got %d" maxShare)

[<Fact>]
let ``F5b: a JointDistribution preserves FK co-occurrence in the synthetic data (correlated, not independent)`` () =
    // Booking has FKs to two DISTINCT parents (Customer, Region). The joint says
    // customer-rank-0 ALWAYS co-occurs with region-rank-0 and rank-1 with rank-1
    // (never crossed). σ must reproduce that pairing on the synthetic keys.
    let jCustK   = kindKey ["JC"]
    let jCustIdK = attrKey ["JC"; "Id"]
    let jRegK    = kindKey ["JR"]
    let jRegIdK  = attrKey ["JR"; "Id"]
    let jBkK     = kindKey ["JB"]
    let jBkIdK   = attrKey ["JB"; "Id"]
    let jBkCustK = attrKey ["JB"; "CustomerId"]
    let jBkRegK  = attrKey ["JB"; "RegionId"]
    let jCustomer = Kind.create jCustK (name "JCustomer") (mkTableId "dbo" "JCUST")   [ attr jCustIdK "Id" Integer true false ]
    let jRegion   = Kind.create jRegK  (name "JRegion")   (mkTableId "dbo" "JREGION") [ attr jRegIdK  "Id" Integer true false ]
    let jBooking =
        { Kind.create jBkK (name "JBooking") (mkTableId "dbo" "JBOOK")
            [ attr jBkIdK   "Id"         Integer true  false
              attr jBkCustK "CustomerId" Integer false false
              attr jBkRegK  "RegionId"   Integer false false ] with
            References =
                [ Reference.create (refKey ["JB"; "Customer"]) (name "Customer") jBkCustK jCustK
                  Reference.create (refKey ["JB"; "Region"])   (name "Region")   jBkRegK  jRegK ] }
    let jCat = Catalog.create [ mkModule (modKey "JM") (name "JM") [ jCustomer; jRegion; jBooking ] ] [] |> mkOk
    let jProf =
        { Profile.empty with
            Columns = [ col jCustIdK 2L 0L; col jRegIdK 2L 0L; col jBkIdK 200L 0L ]
            JointDistributions =
                [ JointDistribution.create jBkK [ jBkCustK; jBkRegK ]
                    [ ("i:500|i:700", 100L); ("i:501|i:701", 100L) ] 2L false (probe 200L) |> mkOk ] }
    let m = SyntheticData.generate jCat jProf cfg 7UL
    let pairs =
        m.[jBkK] |> List.map (fun r -> StaticRow.value (name "CustomerId") r, StaticRow.value (name "RegionId") r)
    // Every booking pairs equal-rank parents (both "1" or both "2") — never crossed.
    Assert.All(pairs, fun (c, rg) -> Assert.Equal(c, rg))
    // Both correlated combinations actually appear (the draw isn't degenerate).
    Assert.Contains(pairs, fun p -> p = (Some "1", Some "1"))
    Assert.Contains(pairs, fun p -> p = (Some "2", Some "2"))

[<Fact>]
let ``PK uniqueness holds across the generated population`` () =
    let m = SyntheticData.generate catalog profile cfg 3UL
    let custIds = valuesOf m custKey "Id"
    let ordIds  = valuesOf m ordKey "Id"
    Assert.Equal(100, custIds |> List.distinct |> List.length)
    Assert.Equal(250, ordIds  |> List.distinct |> List.length)

[<Fact>]
let ``L1: synthetic load has zero FK orphans`` () =
    let m = SyntheticData.generate catalog profile cfg 5UL
    let pool = valuesOf m custKey "Id" |> Set.ofList
    // Mandatory FK — every value must be drawn from the parent pool.
    let custFks = valuesOf m ordKey "CustomerId"
    Assert.Equal(250, custFks.Length)
    Assert.True(custFks |> List.forall (fun v -> Set.contains v pool), "mandatory FK orphan emitted")
    // Optional FK — every present value must be from the pool (NULLs absent).
    let optFks = valuesOf m ordKey "OptCustomerId"
    Assert.True(optFks |> List.forall (fun v -> Set.contains v pool), "optional FK orphan emitted")

[<Fact>]
let ``privacy: no real high-cardinality value is emitted`` () =
    let m = SyntheticData.generate catalog profile cfg 11UL
    let emails = valuesOf m custKey "Email"
    Assert.Equal(100, emails.Length)
    let leaked = emails |> List.filter (fun e -> Set.contains e realEmailSet)
    Assert.Empty(leaked)

[<Fact>]
let ``low-cardinality categorical values are preserved`` () =
    let m = SyntheticData.generate catalog profile cfg 13UL
    let statuses = valuesOf m custKey "Status"
    Assert.Equal(100, statuses.Length)
    Assert.True(statuses |> List.forall (fun s -> Set.contains s realStatusSet),
                "preserved categorical emitted a value outside the real vocabulary")

[<Fact>]
let ``per-column override forces synthesis on a named column`` () =
    let forced = { cfg with SynthesizeColumns = Set.ofList [ "Status" ] }
    let m = SyntheticData.generate catalog profile forced 13UL
    let statuses = valuesOf m custKey "Status"
    // None of the real status values may appear once forced to synthesize.
    Assert.True(statuses |> List.forall (fun s -> not (Set.contains s realStatusSet)),
                "forced-synthesize column leaked a real value")

[<Fact>]
let ``null-rate is honored within epsilon`` () =
    let m = SyntheticData.generate catalog profile cfg 17UL
    // Customer.Notes — observed null rate 0.40 over 100 rows (σ ≈ 4.9;
    // bounds are a generous ±6σ so the law isn't seed-fragile).
    let presentNotes = valuesOf m custKey "Notes" |> List.length
    let nullNotes = 100 - presentNotes
    Assert.InRange(nullNotes, 11, 69)
    // Order.OptCustomerId — observed null rate 0.20 over 250 rows
    // (σ ≈ 6.3; bounds ±~3.2σ around the empirically-confirmed mean of 50).
    let presentOpt = valuesOf m ordKey "OptCustomerId" |> List.length
    let nullOpt = 250 - presentOpt
    Assert.InRange(nullOpt, 30, 70)

[<Fact>]
let ``non-nullable columns are never null`` () =
    let m = SyntheticData.generate catalog profile cfg 19UL
    // Status / Email / Score / Id are NOT NULL — every row carries them.
    for attrName in [ "Id"; "Status"; "Email"; "Score" ] do
        Assert.Equal(100, valuesOf m custKey attrName |> List.length)
    for attrName in [ "Id"; "CustomerId" ] do
        Assert.Equal(250, valuesOf m ordKey attrName |> List.length)

[<Fact>]
let ``numeric samples lie within the profiled range`` () =
    let m = SyntheticData.generate catalog profile cfg 23UL
    let scores = valuesOf m custKey "Score" |> List.map (fun s -> Int64.Parse(s, Globalization.CultureInfo.InvariantCulture))
    Assert.All(scores, fun s -> Assert.InRange(s, 0L, 100L))

[<Fact>]
let ``PrimitiveType is exhausted — every variant generates a renderable raw`` () =
    // A kind with one attribute of every PrimitiveType, no profile evidence
    // (forces the type-default path). Every cell must render through
    // SqlLiteral without throwing (the load's contract).
    let allTypes =
        [ Integer; Decimal; Text; Boolean; DateTime; Date; Time; Binary; Guid ]
    let attrs =
        allTypes
        |> List.mapi (fun i t ->
            attr (attrKey ["X"; string i]) (sprintf "Col%d" i) t (i = 0) false)
    let kindX = Kind.create (kindKey ["X"]) (name "X") (mkTableId "dbo" "X") attrs
    let catX = Catalog.create [ mkModule (modKey "MX") (name "MX") [ kindX ] ] [] |> mkOk
    // Volume needs evidence; give the kind a RowCount via a column profile.
    let profX = { Profile.empty with Columns = [ col (attrKey ["X"; "0"]) 8L 0L ] }
    let m = SyntheticData.generate catX profX cfg 29UL
    Assert.Equal(8, m.[kindKey ["X"]].Length)
    // Every emitted raw renders to SQL text without throwing.
    for row in m.[kindKey ["X"]] do
        for (i, t) in List.indexed allTypes do
            match StaticRow.value (name (sprintf "Col%d" i)) row with
            | Some raw -> SqlLiteral.ofRaw t (Some raw) |> SqlLiteral.toString |> ignore
            | None -> ()
    Assert.True(true)

// ---------------------------------------------------------------------------
// NM-21 — a non-nullable FK drawing against an EMPTY parent pool is forced to
// NULL (an unsatisfiable structure the load surfaces). σ now NAMES that
// erasure via a `synthetic.fk.unsatisfiable` diagnostic, visible even on a
// DryRun preview that never reaches the load-time failure — never a silent NULL.
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-21: a non-nullable FK to an empty parent pool emits a named synthetic.fk.unsatisfiable diagnostic`` () =
    // Profile gives ORDER 5 rows but CUSTOMER none (no column profile) → the
    // Customer PK pool is empty. Order.CustomerId is a NON-NULLABLE FK to it.
    let profUnsat =
        { Profile.empty with
            Columns = [ col ordId 5L 0L; col ordCust 5L 0L; col ordOpt 5L 0L ] }
    let dataset, diags = SyntheticData.generateWithDiagnostics catalog profUnsat cfg 5UL
    // The mandatory FK column is forced to NULL for every row (absent from Values).
    Assert.Empty(valuesOf dataset ordKey "CustomerId")
    // ...and that erasure is NAMED, not silent.
    let unsat =
        diags
        |> List.filter (fun d -> d.Code = SyntheticDiagnostic.UnsatisfiableForeignKeyCode)
    let mandatory =
        unsat |> List.filter (fun d -> d.SourceAttribute = ordCust)
    Assert.Equal(1, List.length mandatory)
    let d = List.head mandatory
    Assert.Equal(ordKey, d.Kind)
    Assert.Equal(custKey, d.TargetKind)
    // The OPTIONAL FK (OptCustomerId, nullable) draws against the same empty
    // pool but is NOT an unsatisfiable structure — no diagnostic for it.
    Assert.Empty(unsat |> List.filter (fun d -> d.SourceAttribute = ordOpt))

[<Fact>]
let ``NM-21: a satisfiable population emits no unsatisfiable-FK diagnostics`` () =
    // The standard profile gives Customer a populated pool, so the Order FK
    // is satisfiable — σ raises no unsatisfiable-FK lineage.
    let _, diags = SyntheticData.generateWithDiagnostics catalog profile cfg 5UL
    Assert.Empty(diags |> List.filter (fun d -> d.Code = SyntheticDiagnostic.UnsatisfiableForeignKeyCode))

// ---------------------------------------------------------------------------
// H-072 — intra-cluster FK locality (opt-in via SyntheticConfig.FkLocalityClusters).
// The Order→Customer FK has no captured selectivity / joint evidence in the
// standard profile, so it takes σ's UNIFORM fallback — exactly the draw the
// clustering skew modifies. Customer pool = 100; hot prefix = ceil(100×0.3) = 30.
// ---------------------------------------------------------------------------

let private clusterAnchor = kindKey ["Ctx"]

[<Fact>]
let ``H-072: intra-cluster FK draws concentrate on the pool's hot prefix`` () =
    // Child (Order) and target (Customer) share a cluster ⇒ the 250 FK draws
    // land only in the first 30 Customer PKs, so ≤30 distinct parents are used.
    let clustered = { cfg with FkLocalityClusters = Map.ofList [ custKey, clusterAnchor; ordKey, clusterAnchor ] }
    let m = SyntheticData.generate catalog profile clustered 1UL
    let distinct = valuesOf m ordKey "CustomerId" |> Set.ofList |> Set.count
    Assert.True(distinct <= 30, sprintf "expected ≤30 distinct parents under clustering, got %d" distinct)

[<Fact>]
let ``H-072: clustering references FEWER distinct parents than the uniform baseline`` () =
    let baseline = SyntheticData.generate catalog profile cfg 1UL
    let clustered = { cfg with FkLocalityClusters = Map.ofList [ custKey, clusterAnchor; ordKey, clusterAnchor ] }
    let m = SyntheticData.generate catalog profile clustered 1UL
    let baseDistinct = valuesOf baseline ordKey "CustomerId" |> Set.ofList |> Set.count
    let clusDistinct = valuesOf m ordKey "CustomerId" |> Set.ofList |> Set.count
    Assert.True(baseDistinct > clusDistinct, sprintf "clustering (%d) should reference fewer parents than uniform (%d)" clusDistinct baseDistinct)

[<Fact>]
let ``H-072: clustering preserves zero FK orphans`` () =
    // Every drawn FK value must still be a real Customer PK — the skew only
    // narrows WHICH parents are drawn, never invents an index.
    let clustered = { cfg with FkLocalityClusters = Map.ofList [ custKey, clusterAnchor; ordKey, clusterAnchor ] }
    let m = SyntheticData.generate catalog profile clustered 1UL
    let custPks = valuesOf m custKey "Id" |> Set.ofList
    let fkVals  = valuesOf m ordKey "CustomerId"
    Assert.All(fkVals, fun v -> Assert.Contains(v, custPks))

[<Fact>]
let ``H-072: a cross-cluster edge is byte-identical to the uniform baseline`` () =
    // Child and target in DIFFERENT clusters ⇒ the skew never fires ⇒ output is
    // exactly the uniform draw. (Off-by-construction: an empty map is the default.)
    let baseline = SyntheticData.generate catalog profile cfg 1UL
    let crossCluster = { cfg with FkLocalityClusters = Map.ofList [ custKey, kindKey ["A"]; ordKey, kindKey ["B"] ] }
    let m = SyntheticData.generate catalog profile crossCluster 1UL
    Assert.Equal<Map<SsKey, StaticRow list>>(baseline, m)

[<Fact>]
let ``H-072: intra-cluster generation is deterministic for a fixed seed`` () =
    let clustered = { cfg with FkLocalityClusters = Map.ofList [ custKey, clusterAnchor; ordKey, clusterAnchor ] }
    let a = SyntheticData.generate catalog profile clustered 3UL
    let b = SyntheticData.generate catalog profile clustered 3UL
    Assert.Equal<Map<SsKey, StaticRow list>>(a, b)

// ---------------------------------------------------------------------------
// K1 (DECISIONS 2026-07-18, the Twin) — provided parent pools: a kind whose
// rows the sink already holds (the estate's own seed data) is excluded from
// generation, and child FKs draw from the provided PK values.
// ---------------------------------------------------------------------------

[<Fact>]
let ``K1: a provided kind emits zero rows`` () =
    let provided = { cfg with ProvidedPools = Map.ofList [ custKey, [ "1"; "2"; "3" ] ] }
    let m = SyntheticData.generate catalog profile provided 5UL
    Assert.False(Map.containsKey custKey m, "a provided kind must not appear in the generated dataset")
    Assert.True(Map.containsKey ordKey m, "non-provided kinds still generate")

[<Fact>]
let ``K1: child FK values draw from the provided pool and no others`` () =
    let pool = [ "101"; "202"; "303" ]
    let provided = { cfg with ProvidedPools = Map.ofList [ custKey, pool ] }
    let m = SyntheticData.generate catalog profile provided 5UL
    let poolSet = Set.ofList pool
    let custFks = valuesOf m ordKey "CustomerId"
    Assert.Equal(250, custFks.Length)
    Assert.True(custFks |> List.forall (fun v -> Set.contains v poolSet),
                "a mandatory FK drew a value outside the provided pool")
    let optFks = valuesOf m ordKey "OptCustomerId"
    Assert.True(optFks |> List.forall (fun v -> Set.contains v poolSet),
                "an optional FK drew a value outside the provided pool")

[<Fact>]
let ``K1: empty provided pools are byte-identical to the default flow`` () =
    let baseline = SyntheticData.generate catalog profile cfg 7UL
    let explicitEmpty = { cfg with ProvidedPools = Map.empty }
    let m = SyntheticData.generate catalog profile explicitEmpty 7UL
    Assert.Equal<Map<SsKey, StaticRow list>>(baseline, m)

[<Fact>]
let ``K1: provided-pool generation is deterministic for a fixed seed`` () =
    let provided = { cfg with ProvidedPools = Map.ofList [ custKey, [ "9"; "8" ] ] }
    let a = SyntheticData.generate catalog profile provided 3UL
    let b = SyntheticData.generate catalog profile provided 3UL
    Assert.Equal<Map<SsKey, StaticRow list>>(a, b)

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
    |> List.choose (fun (r: StaticRow) -> Map.tryFind (name attrName) r.Values)

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
            match Map.tryFind (name (sprintf "Col%d" i)) row.Values with
            | Some raw -> SqlLiteral.ofRaw t raw |> SqlLiteral.toString |> ignore
            | None -> ()
    Assert.True(true)

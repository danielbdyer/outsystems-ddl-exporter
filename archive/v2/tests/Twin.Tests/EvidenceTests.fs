module Twin.Tests.EvidenceTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core

// ---------------------------------------------------------------------------
// THE_TWIN.md §6 evidence — rebinding both directions, the tier projection's
// literal-freedom (law 3), the codec round-trip, layering, and the merge
// collision backstop.
// ---------------------------------------------------------------------------

let private ok (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected success, got: %A" (es |> List.map (fun e -> e.Code, e.Metadata))

let private codes (r: Result<'a>) : string list =
    match r with Ok _ -> [] | Error es -> es |> List.map (fun e -> e.Code)

let private name (s: string) : Name = Name.create s |> Result.value

let private attr (key: SsKey) (logical: string) (column: string) (ptype: PrimitiveType) (isPk: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create column false |> Result.value
        IsPrimaryKey = isPk }

let private probe (n: int64) : ProbeStatus = ProbeStatus.observed n

// The CAPTURE-side catalog (physical rendition flavor: OSUSR realizations,
// logical names) and the TWIN-side catalog (logical realizations).
let private custKey = kindKey ["C"]
let private ordKey  = kindKey ["O"]
let private custStat = attrKey ["C"; "Status"]
let private custId = attrKey ["C"; "Id"]
let private ordId = attrKey ["O"; "Id"]
let private ordCust = attrKey ["O"; "CustomerId"]
let private ordRef = refKey ["O"; "Customer"]

let private captureCustomer : Kind =
    { Kind.create custKey (name "Customer") (mkTableId "dbo" "OSUSR_ABC_CUSTOMER")
        [ attr custId "Id" "ID" Integer true
          attr custStat "Status" "STATUS" Text false ] with
        Modality = [] }

let private captureOrder : Kind =
    { Kind.create ordKey (name "Order") (mkTableId "dbo" "OSUSR_ABC_ORDER")
        [ attr ordId "Id" "ID" Integer true
          attr ordCust "CustomerId" "CUSTOMERID" Integer false ] with
        References = [ Reference.create ordRef (name "Customer") ordCust custKey ] }

let private captureCatalog : Catalog =
    Catalog.create [ mkModule (modKey "M") (name "M") [ captureCustomer; captureOrder ] ] [] |> Result.value

/// The rendition seam: physical capture kinds map to estate coordinates
/// through their LOGICAL names.
let private keepByLogicalName (k: Kind) : string option =
    Some (System.String.Concat("dbo.", Name.value k.Name))

// Twin-side catalog: same logical shape, logical physical names, FRESH keys
// (the twin's ReadSide synthesizes its own) — the rebind must survive that.
let private twinCustomer : Kind =
    { Kind.create (kindKey ["TC"]) (name "Customer") (mkTableId "dbo" "Customer")
        [ attr (attrKey ["TC"; "Id"]) "Id" "Id" Integer true
          attr (attrKey ["TC"; "Status"]) "Status" "Status" Text false ] with
        Modality = [] }

let private twinOrder : Kind =
    { Kind.create (kindKey ["TO"]) (name "Order") (mkTableId "dbo" "Order")
        [ attr (attrKey ["TO"; "Id"]) "Id" "Id" Integer true
          attr (attrKey ["TO"; "CustomerId"]) "CustomerId" "CustomerId" Integer false ] with
        References = [ Reference.create (refKey ["TO"; "Customer"]) (name "Customer") (attrKey ["TO"; "CustomerId"]) (kindKey ["TC"]) ] }

let private twinIndex =
    CatalogIndex.ofCatalog
        (Catalog.create [ mkModule (modKey "T") (name "T") [ twinCustomer; twinOrder ] ] [] |> Result.value)

let private fanShape : NumericDistribution =
    NumericDistribution.create ordRef 1m 2m 3m 4m 5m 6m 7m 25L (probe 25L) |> Result.value

let private capturedProfile : Profile =
    { Profile.empty with
        Columns =
            [ ColumnProfile.create custId 50L 0L (probe 50L) |> Result.value
              ColumnProfile.create custStat 50L 5L (probe 50L) |> Result.value |> ColumnProfile.withMaxObservedLength 8
              ColumnProfile.create ordId 120L 0L (probe 120L) |> Result.value
              ColumnProfile.create ordCust 120L 0L (probe 120L) |> Result.value ]
        Distributions =
            [ AttributeDistribution.Categorical
                (CategoricalDistribution.create custStat [ "XSECRETACTIVE", 40L; "XSECRETDORMANT", 10L ] 2L false (probe 50L) |> Result.value)
              AttributeDistribution.Numeric
                (NumericDistribution.create ordId 1m 30m 60m 90m 114m 119m 120m 120L (probe 120L) |> Result.value) ]
        ForeignKeyCardinalities = [ ForeignKeyCardinality.create ordRef fanShape ] }

let private richPack : EvidencePack =
    Evidence.ofProfile "uat" captureCatalog keepByLogicalName capturedProfile

[<Fact>]
let ``ofProfile rebinds engine evidence to estate coordinates`` () =
    Assert.Equal(RichTier, richPack.Tier)
    let customer = richPack.Tables |> List.find (fun t -> t.Table = "dbo.Customer")
    Assert.Equal(50L, customer.RowCount)
    let status = customer.Columns |> List.find (fun c -> c.Column = "STATUS")
    Assert.Equal(5L, status.NullCount)
    Assert.Equal(Some 8, status.MaxLength)
    Assert.Equal(2, List.length status.Frequencies)
    let fan = List.exactlyOne richPack.FanOuts
    Assert.Equal("dbo.Order", fan.ChildTable)
    Assert.Equal("dbo.Customer", fan.ParentTable)

[<Fact>]
let ``law 3: the shape tier carries no captured literal`` () =
    let shape = Evidence.deriveShape richPack
    Assert.Equal(ShapeTier, shape.Tier)
    let json = Evidence.serialize shape
    // No categorical value survives.
    Assert.DoesNotContain("XSECRET", json)
    // No numeric percentile literal survives (the ordId distribution's
    // distinctive interior percentiles).
    Assert.DoesNotContain("114", json)
    Assert.DoesNotContain("119", json)
    // Structure remains: counts, null rates, distinct counts, fan-out.
    let customer = shape.Tables |> List.find (fun t -> t.Table = "dbo.Customer")
    let status = customer.Columns |> List.find (fun c -> c.Column = "STATUS")
    Assert.Equal(Some 2L, status.DistinctCount)
    Assert.Equal(1, List.length shape.FanOuts)

[<Fact>]
let ``the codec round-trips a full pack`` () =
    let restored = ok (Evidence.deserialize (Evidence.serialize richPack))
    Assert.Equal(richPack.Tier, restored.Tier)
    Assert.Equal<string list>(richPack.Sources, restored.Sources)
    Assert.Equal<TableEvidence list>(richPack.Tables, restored.Tables)
    Assert.Equal<FanOutEvidence list>(richPack.FanOuts, restored.FanOuts)

[<Fact>]
let ``toProfile binds a pack against the twin catalog by coordinate`` () =
    let profile = ok (Evidence.toProfile twinIndex richPack)
    // Bound to the TWIN's keys, not the capture keys.
    match Profile.tryFindColumn (attrKey ["TC"; "Status"]) profile with
    | Some c -> Assert.Equal(50L, c.RowCount); Assert.Equal(5L, c.NullCount)
    | None -> failwith "the Status column did not bind to the twin catalog"
    match Profile.tryFindCategorical (attrKey ["TC"; "Status"]) profile with
    | Some cat -> Assert.Equal(2L, cat.DistinctCount)
    | None -> failwith "the categorical evidence did not bind"
    Assert.Equal(1, List.length profile.ForeignKeyCardinalities)

[<Fact>]
let ``law 2: a pack naming an absent column refuses by name`` () =
    let broken =
        { richPack with
            Tables =
                richPack.Tables
                |> List.map (fun t ->
                    if t.Table = "dbo.Customer" then
                        { t with Columns = t.Columns @ [ { Column = "Ghost"; RowCount = 1L; NullCount = 0L; MaxLength = None; DistinctCount = None; Truncated = false; Frequencies = []; Numeric = None } ] }
                    else t) }
    Assert.Contains("twin.coordinate.column.unknown", codes (Evidence.toProfile twinIndex broken))

[<Fact>]
let ``layer: the richer profile replaces per attribute and unions the rest`` () =
    let shapeProfile = ok (Evidence.toProfile twinIndex (Evidence.deriveShape richPack))
    let richProfile = ok (Evidence.toProfile twinIndex richPack)
    let layered = Evidence.layer shapeProfile richProfile
    match Profile.tryFindCategorical (attrKey ["TC"; "Status"]) layered with
    | Some cat -> Assert.Equal(2, List.length cat.Frequencies)
    | None -> failwith "the rich categorical must win the layer"
    // No duplicate column entries after layering.
    let statusColumns = layered.Columns |> List.filter (fun c -> c.AttributeKey = attrKey ["TC"; "Status"])
    Assert.Equal(1, List.length statusColumns)

[<Fact>]
let ``merge refuses a table claimed by two packs`` () =
    let a = { Evidence.emptyPack RichTier with Tables = [ { Table = "dbo.Customer"; RowCount = 1L; Columns = [] } ] }
    let b = { Evidence.emptyPack RichTier with Tables = [ { Table = "DBO.CUSTOMER"; RowCount = 2L; Columns = [] } ] }
    Assert.Contains("twin.evidence.mergeCollision", codes (Evidence.merge [ a; b ]))

[<Fact>]
let ``evidencedKinds names exactly the kinds carrying column evidence`` () =
    let profile = ok (Evidence.toProfile twinIndex richPack)
    let evidenced = Evidence.evidencedKinds twinIndex profile
    Assert.Contains(kindKey ["TC"], evidenced)
    Assert.Contains(kindKey ["TO"], evidenced)

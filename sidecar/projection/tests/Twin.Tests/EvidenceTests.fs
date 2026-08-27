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
        ForeignKeyCardinalities = [ ForeignKeyCardinality.create ordRef fanShape ]
        // The four reality axes the pack carries through (C1): an orphaned
        // edge, a duplicated column (on both engine axes), FK selectivity
        // whose parent-key VALUES must never reach the pack, and a joint
        // whose tuple values are rich-only.
        ForeignKeys =
            [ { ReferenceKey = ordRef
                HasOrphan    = true
                OrphanCount  = 3L
                IsNoCheck    = false
                ProbeStatus  = probe 120L } ]
        AttributeRealities =
            [ { AttributeKey         = custStat
                IsNullableInDatabase = true
                HasNulls             = true
                HasDuplicates        = true
                HasOrphans           = false
                IsPresentButInactive = false } ]
        UniqueCandidates =
            [ { AttributeKey = custStat
                HasDuplicate = true
                ProbeStatus  = probe 50L } ]
        ForeignKeySelectivities =
            [ ForeignKeySelectivity.create ordRef [ "XSECRETP7", 70L; "XSECRETP8", 50L ] 2L false (probe 120L) |> Result.value ]
        JointDistributions =
            [ JointDistribution.create ordKey [ ordCust; ordId ]
                [ "XSECRETJ7|XSECRETJ1", 90L; "XSECRETJ8|XSECRETJ2", 30L ] 2L false (probe 120L) |> Result.value ] }

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
let ``ofProfile carries the reality axes to coordinates`` () =
    let orphan = List.exactlyOne richPack.Orphans
    Assert.Equal("dbo.Order", orphan.ChildTable)
    Assert.Equal("CUSTOMERID", orphan.ChildColumn)
    Assert.Equal("dbo.Customer", orphan.ParentTable)
    Assert.Equal(3L, orphan.OrphanCount)
    let sel = List.exactlyOne richPack.Selectivities
    Assert.Equal(2L, sel.DistinctCount)
    // The parent-key VALUES are dropped at the capture boundary; only the
    // count vector travels.
    Assert.Equal<int64 list>([ 70L; 50L ], sel.Counts)
    let joint = List.exactlyOne richPack.Joints
    Assert.Equal("dbo.Order", joint.Table)
    Assert.Equal<string list>([ "CUSTOMERID"; "ID" ], joint.Columns)
    Assert.Equal(2, List.length joint.Frequencies)
    let status =
        richPack.Tables
        |> List.find (fun t -> t.Table = "dbo.Customer")
        |> fun t -> t.Columns |> List.find (fun c -> c.Column = "STATUS")
    Assert.True status.HasDuplicates

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
    // No selectivity parent-key value and no joint tuple value survives —
    // selectivity values never entered the pack; joints drop at derive.
    Assert.DoesNotContain("XSECRETP", json)
    Assert.DoesNotContain("XSECRETJ", json)
    // Structure remains: counts, null rates, distinct counts, fan-out,
    // orphan counts, selectivity count vectors, duplicate flags.
    let customer = shape.Tables |> List.find (fun t -> t.Table = "dbo.Customer")
    let status = customer.Columns |> List.find (fun c -> c.Column = "STATUS")
    Assert.Equal(Some 2L, status.DistinctCount)
    Assert.True status.HasDuplicates
    Assert.Equal(1, List.length shape.FanOuts)
    Assert.Equal(1, List.length shape.Orphans)
    Assert.Equal(1, List.length shape.Selectivities)
    Assert.Empty shape.Joints

[<Fact>]
let ``the codec round-trips a full pack`` () =
    let restored = ok (Evidence.deserialize (Evidence.serialize richPack))
    Assert.Equal(richPack.Tier, restored.Tier)
    Assert.Equal<string list>(richPack.Sources, restored.Sources)
    Assert.Equal<TableEvidence list>(richPack.Tables, restored.Tables)
    Assert.Equal<FanOutEvidence list>(richPack.FanOuts, restored.FanOuts)
    Assert.Equal<OrphanEvidence list>(richPack.Orphans, restored.Orphans)
    Assert.Equal<SelectivityEvidence list>(richPack.Selectivities, restored.Selectivities)
    Assert.Equal<JointEvidence list>(richPack.Joints, restored.Joints)

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
let ``toProfile binds the reality axes into what the mint reads`` () =
    let profile = ok (Evidence.toProfile twinIndex richPack)
    let fk = List.exactlyOne profile.ForeignKeys
    Assert.True fk.HasOrphan
    Assert.Equal(3L, fk.OrphanCount)
    Assert.Equal(refKey ["TO"; "Customer"], fk.ReferenceKey)
    let sel = List.exactlyOne profile.ForeignKeySelectivities
    Assert.Equal(2L, sel.DistinctCount)
    // Rank labels are fabricated at the rebind; the mint draws by rank
    // and never reads them.
    Assert.Equal<(string * int64) list>([ "#1", 70L; "#2", 50L ], sel.Frequencies)
    let joint = List.exactlyOne profile.JointDistributions
    Assert.Equal<SsKey list>([ attrKey ["TO"; "CustomerId"]; attrKey ["TO"; "Id"] ], joint.AttributeKeys)
    let reality = List.exactlyOne profile.AttributeRealities
    Assert.True reality.HasDuplicates
    Assert.True reality.HasNulls
    let unique = List.exactlyOne profile.UniqueCandidates
    Assert.True unique.HasDuplicate
    Assert.Equal(attrKey ["TC"; "Status"], unique.AttributeKey)

[<Fact>]
let ``an orphan on an edge the estate does not carry binds nothing and refuses nothing`` () =
    // The twin catalog WITHOUT the Order → Customer reference — the FK-add
    // case: the orphan and selectivity evidence exists precisely because
    // the constraint is not there yet.
    let orderNoRef =
        { Kind.create (kindKey ["TO"]) (name "Order") (mkTableId "dbo" "Order")
            [ attr (attrKey ["TO"; "Id"]) "Id" "Id" Integer true
              attr (attrKey ["TO"; "CustomerId"]) "CustomerId" "CustomerId" Integer false ] with
            References = [] }
    let noRefIndex =
        CatalogIndex.ofCatalog
            (Catalog.create [ mkModule (modKey "T") (name "T") [ twinCustomer; orderNoRef ] ] [] |> Result.value)
    // The fan-out rides the SAME unconstrained edge (a capture-side
    // reference records fan-out AND orphan reality together): it too
    // binds nothing and refuses nothing — σ has no relationship to
    // attach the cardinality to, so the shape stays pack-side. The
    // three-environment rehearsal found the earlier refusal here: a
    // lawfully clamped merged pack carries this edge by design.
    let profile = ok (Evidence.toProfile noRefIndex richPack)
    Assert.Empty profile.ForeignKeys
    Assert.Empty profile.ForeignKeyCardinalities
    Assert.Empty profile.ForeignKeySelectivities
    // The joint needs no reference and still binds.
    Assert.Equal(1, List.length profile.JointDistributions)

[<Fact>]
let ``an old-shape pack deserializes with empty reality axes`` () =
    let oldJson =
        """{
  "tier": "rich",
  "sources": ["uat"],
  "tables": [
    { "table": "dbo.Customer", "rowCount": 5,
      "columns": [ { "column": "Status", "rowCount": 5, "nullCount": 1 } ] }
  ],
  "fanOuts": []
}"""
    let pack = ok (Evidence.deserialize oldJson)
    Assert.Empty pack.Orphans
    Assert.Empty pack.Selectivities
    Assert.Empty pack.Joints
    let status = (List.exactlyOne pack.Tables).Columns |> List.exactlyOne
    Assert.False status.HasDuplicates

[<Fact>]
let ``a pack with no reality axes serializes with no reality keys`` () =
    let plain =
        { Evidence.emptyPack RichTier with
            Sources = [ "dev" ]
            Tables =
                [ { Table = "dbo.Customer"; RowCount = 5L
                    Columns =
                        [ { Column = "Status"; RowCount = 5L; NullCount = 1L; MaxLength = None
                            DistinctCount = None; Truncated = false; HasDuplicates = false
                            Frequencies = []; Numeric = None; Text = None; ConditionalNulls = None } ] } ] }
    let json = Evidence.serialize plain
    Assert.DoesNotContain("orphans", json)
    Assert.DoesNotContain("selectivities", json)
    Assert.DoesNotContain("joints", json)
    Assert.DoesNotContain("hasDuplicates", json)
    Assert.Equal(plain, ok (Evidence.deserialize json))

[<Fact>]
let ``law 2: a pack naming an absent column refuses by name`` () =
    let broken =
        { richPack with
            Tables =
                richPack.Tables
                |> List.map (fun t ->
                    if t.Table = "dbo.Customer" then
                        { t with Columns = t.Columns @ [ { Column = "Ghost"; RowCount = 1L; NullCount = 0L; MaxLength = None; DistinctCount = None; Truncated = false; HasDuplicates = false; Frequencies = []; Numeric = None; Text = None; ConditionalNulls = None } ] }
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
    // The reality axes survive the layer — a rich pack's orphans and
    // selectivities are never lost under a shape base.
    Assert.Equal(1, List.length layered.ForeignKeys)
    Assert.Equal(1, List.length layered.ForeignKeySelectivities)
    Assert.Equal(1, List.length layered.JointDistributions)
    Assert.Equal(1, List.length layered.AttributeRealities)
    Assert.Equal(1, List.length layered.UniqueCandidates)

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

// -- The string-plane axis (F1) ---------------------------------------------

[<Fact>]
let ``the text shape round-trips through the codec, counts only`` () =
    let pack =
        { Evidence.emptyPack RichTier with
            Sources = [ "dev" ]
            Tables =
                [ { Table = "dbo.Customer"; RowCount = 5L
                    Columns =
                        [ { Column = "Email"; RowCount = 5L; NullCount = 0L; MaxLength = Some 20
                            DistinctCount = None; Truncated = false; HasDuplicates = false
                            Frequencies = []; Numeric = None
                            Text =
                                Some
                                    { EmptyCount = 2L; TrailingSpaceCount = 1L; CaseCollisions = 3L
                                      LengthP50 = Some 8; LengthP90 = Some 15 }
                            ConditionalNulls = None } ] } ] }
    let json = Evidence.serialize pack
    Assert.Contains("\"text\"", json)
    Assert.Contains("trailingSpace", json)
    Assert.Contains("caseCollisions", json)
    Assert.Equal(pack, ok (Evidence.deserialize json))
    // Law 3: the shape tier keeps the whole record — every field is a count.
    let shaped = Evidence.deriveShape pack
    let shapedCol = (List.exactlyOne shaped.Tables).Columns |> List.exactlyOne
    Assert.True shapedCol.Text.IsSome

[<Fact>]
let ``a column without string counts serializes without the text key`` () =
    let pack =
        { Evidence.emptyPack RichTier with
            Sources = [ "dev" ]
            Tables =
                [ { Table = "dbo.Customer"; RowCount = 5L
                    Columns =
                        [ { Column = "Email"; RowCount = 5L; NullCount = 0L; MaxLength = None
                            DistinctCount = None; Truncated = false; HasDuplicates = false
                            Frequencies = []; Numeric = None; Text = None; ConditionalNulls = None } ] } ] }
    Assert.DoesNotContain("\"text\"", Evidence.serialize pack)

[<Fact>]
let ``a pre-F1 pack (no text key) deserializes with the shape absent`` () =
    let json =
        """{ "tier": "rich", "sources": ["dev"],
             "tables": [ { "table": "dbo.Customer", "rowCount": 5,
                           "columns": [ { "column": "Email", "rowCount": 5, "nullCount": 1 } ] } ] }"""
    let pack = ok (Evidence.deserialize json)
    let c = (List.exactlyOne pack.Tables).Columns |> List.exactlyOne
    Assert.True c.Text.IsNone
    Assert.True c.ConditionalNulls.IsNone

// -- The conditional-null structure (F2) ------------------------------------

[<Fact>]
let ``the conditional-null structure round-trips rich and the shape derivation drops it`` () =
    let pack =
        { Evidence.emptyPack RichTier with
            Sources = [ "qa" ]
            Tables =
                [ { Table = "dbo.Customer"; RowCount = 20L
                    Columns =
                        [ { Column = "Rating"; RowCount = 20L; NullCount = 6L; MaxLength = None
                            DistinctCount = None; Truncated = false; HasDuplicates = false
                            Frequencies = []; Numeric = None; Text = None
                            ConditionalNulls =
                                Some
                                    { Partner = "Name"
                                      Rates = [ "XSECRETCOMMON", 6L, 12L; "XSECRETRARE", 0L, 8L ] } } ] } ] }
    let json = Evidence.serialize pack
    Assert.Contains("\"conditionalNulls\"", json)
    Assert.Contains("\"partner\"", json)
    Assert.Contains("XSECRETCOMMON", json)
    Assert.Equal(pack, ok (Evidence.deserialize json))
    // Law 3 boundary: the partner VALUES are literals, so the shape tier
    // drops the whole structure — unlike the string counts, which it keeps.
    let shaped = Evidence.deriveShape pack
    let c = (List.exactlyOne shaped.Tables).Columns |> List.exactlyOne
    Assert.True c.ConditionalNulls.IsNone
    Assert.DoesNotContain("XSECRET", Evidence.serialize shaped)

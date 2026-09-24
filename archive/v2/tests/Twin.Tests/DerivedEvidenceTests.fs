module Twin.Tests.DerivedEvidenceTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core

// ---------------------------------------------------------------------------
// THE_TWIN — the schema-derived evidence floor (zero-configuration realism).
// The laws, each a fact below:
//   floor-binds        the pack derived from a catalog binds back against it
//   captured-wins      layered under captured evidence, the floor yields
//                      wherever the capture speaks and fills where it does not
//   length-safety      every derived value fits the declared width; a width
//                      that collapses the vocabulary derives nothing
//   preserve-threshold every derived vocabulary stays ≤ 50 distinct values
//   nullability        NOT NULL derives zero nulls; nullable derives a rate
//   determinism        the same catalog derives the same pack
//   name-routing       email-, amount-, status-named columns get their shapes
//   surrogates-skipped a primary key derives nothing (σ owns surrogates)
// ---------------------------------------------------------------------------

let private ok (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected success, got: %A" (es |> List.map (fun e -> e.Code, e.Metadata))

let private name (s: string) : Name = Name.create s |> Result.value

let private attrOf (key: SsKey) (logical: string) (column: string) (ptype: PrimitiveType) (pk: bool) (nullable: bool) (len: int option) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create column nullable |> Result.value
        IsPrimaryKey = pk
        Length       = len }

let private custKey = kindKey ["DC"]
let private idKey = attrKey ["DC"; "Id"]
let private emailKey = attrKey ["DC"; "Email"]
let private statusKey = attrKey ["DC"; "Status"]
let private totalKey = attrKey ["DC"; "Total"]
let private placedKey = attrKey ["DC"; "PlacedOn"]
let private tinyKey = attrKey ["DC"; "Tiny"]
let private flagKey = attrKey ["DC"; "Flag"]

let private customer : Kind =
    { Kind.create custKey (name "Customer") (mkTableId "dbo" "Customer")
        [ attrOf idKey "Id" "Id" Integer true false None
          attrOf emailKey "Email" "Email" Text false true (Some 250)
          attrOf statusKey "Status" "Status" Text false false (Some 20)
          attrOf totalKey "Total" "Total" Decimal false false None
          attrOf placedKey "PlacedOn" "PlacedOn" DateTime false false None
          attrOf tinyKey "Tiny" "Tiny" Text false true (Some 1)
          attrOf flagKey "Flag" "Flag" Boolean false false None ] with
        Modality = [] }

let private catalog : Catalog =
    Catalog.create [ mkModule (modKey "M") (name "M") [ customer ] ] [] |> Result.value

let private index = CatalogIndex.ofCatalog catalog

let private floorProfile () : Profile =
    ok (Evidence.toProfile index (DerivedEvidence.pack catalog))

[<Fact>]
let ``floor-binds: the derived pack binds back against its own catalog`` () =
    let profile = floorProfile ()
    Assert.True(Profile.tryFindColumn emailKey profile |> Option.isSome)
    Assert.True(Profile.tryFindColumn statusKey profile |> Option.isSome)

[<Fact>]
let ``captured-wins: the floor yields wherever captured evidence speaks and fills where it does not`` () =
    let capturedPack : EvidencePack =
        { Tier = RichTier
          Sources = [ "capture" ]
          Tables =
            [ { Table = "dbo.Customer"
                RowCount = 5L
                Columns =
                  [ { Column = "Status"; RowCount = 5L; NullCount = 0L; MaxLength = Some 20
                      DistinctCount = Some 2L; Truncated = false
                      Frequencies = [ "Live", 3L; "Dormant", 2L ]; Numeric = None } ] } ]
          FanOuts = [] }
    let captured = ok (Evidence.toProfile index capturedPack)
    let layered = Evidence.layer (floorProfile ()) captured
    match Profile.tryFindCategorical statusKey layered with
    | None -> failwith "captured Status categorical missing after layering"
    | Some cat ->
        Assert.Equal<Set<string>>(Set.ofList [ "Live"; "Dormant" ], cat.Frequencies |> List.map fst |> Set.ofList)
    match Profile.tryFindCategorical emailKey layered with
    | None -> failwith "floor Email categorical missing after layering"
    | Some cat -> Assert.True(cat.Frequencies |> List.forall (fun (v, _) -> v.Contains "@"))

[<Fact>]
let ``length-safety: derived values fit the declared width, and a collapsing width derives nothing`` () =
    let statusAttr = customer.Attributes |> List.find (fun a -> a.SsKey = statusKey)
    match DerivedEvidence.deriveColumn statusAttr with
    | None -> failwith "Status should derive a vocabulary"
    | Some c -> Assert.True(c.Frequencies |> List.forall (fun (v, _) -> v.Length <= 20))
    let tinyAttr = customer.Attributes |> List.find (fun a -> a.SsKey = tinyKey)
    Assert.True(DerivedEvidence.deriveColumn tinyAttr |> Option.isNone)

[<Fact>]
let ``preserve-threshold: every derived vocabulary stays at or under 50 distinct values`` () =
    for table in (DerivedEvidence.pack catalog).Tables do
        for c in table.Columns do
            if not (List.isEmpty c.Frequencies) then
                Assert.InRange(List.length c.Frequencies, 2, 50)
                Assert.Equal(Some (int64 (List.length c.Frequencies)), c.DistinctCount)
                Assert.Equal(List.length c.Frequencies, c.Frequencies |> List.map fst |> List.distinct |> List.length)

[<Fact>]
let ``nullability: NOT NULL derives zero nulls; nullable derives a modest rate`` () =
    let column keyName =
        (DerivedEvidence.pack catalog).Tables
        |> List.collect (fun t -> t.Columns)
        |> List.find (fun c -> c.Column = keyName)
    Assert.Equal(0L, (column "Status").NullCount)
    let email = column "Email"
    Assert.True(email.NullCount > 0L && email.NullCount * 5L <= email.RowCount)

[<Fact>]
let ``determinism: the same catalog derives the same pack`` () =
    Assert.Equal<EvidencePack>(DerivedEvidence.pack catalog, DerivedEvidence.pack catalog)

[<Fact>]
let ``name-routing: email, amount, status, and chronological columns get their shapes`` () =
    let profile = floorProfile ()
    match Profile.tryFindCategorical emailKey profile with
    | None -> failwith "Email should derive a categorical"
    | Some cat -> Assert.True(cat.Frequencies |> List.forall (fun (v, _) -> v.Contains "@"))
    match Profile.tryFindNumeric totalKey profile with
    | None -> failwith "Total should derive a numeric shape"
    | Some num -> Assert.True(num.Min >= 0m && num.Max > num.Min)
    match Profile.tryFindCategorical statusKey profile with
    | None -> failwith "Status should derive a vocabulary"
    | Some cat -> Assert.InRange(List.length cat.Frequencies, 2, 6)
    Assert.True(Profile.tryFindNumeric placedKey profile |> Option.isSome)

[<Fact>]
let ``surrogates-skipped: a primary key derives nothing`` () =
    let idAttr = customer.Attributes |> List.find (fun a -> a.SsKey = idKey)
    Assert.True(DerivedEvidence.deriveColumn idAttr |> Option.isNone)
    let flagAttr = customer.Attributes |> List.find (fun a -> a.SsKey = flagKey)
    Assert.True(DerivedEvidence.deriveColumn flagAttr |> Option.isNone)

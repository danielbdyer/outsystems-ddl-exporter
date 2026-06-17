module Projection.Tests.FakerRealizationTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// THE_SYNTHETIC_DATA_FUZZING.md §5 (slice F2) — the Faker boundary realization:
// PII-typed columns are rewritten to coherent realistic values, seeded per row
// identity (referential consistency + determinism); Bogus stays OUTSIDE Core.

let private name (s: string) : Name = Name.create s |> Result.value
let private mkOk r = match r with Ok v -> v | Error es -> invalidOp (sprintf "fixture: %A" (es: ValidationError list))

let private attr (key: SsKey) (logical: string) : Attribute =
    { Attribute.create key (name logical) Text with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) true |> Result.value }

let private emailKey  = attrKey ["P"; "Email"]
let private nameKey   = attrKey ["P"; "FullName"]
let private statusKey = attrKey ["P"; "Status"]
let private kKey      = kindKey ["P"]

let private catalog : Catalog =
    let person =
        Kind.create kKey (name "Person") (mkTableId "dbo" "PERSON")
            [ attr emailKey "Email"; attr nameKey "FullName"; attr statusKey "Status" ]
    Catalog.create [ mkModule (modKey "M") (name "M") [ person ] ] [] |> mkOk

let private correction : Correction =
    Correction.create
        [ CorrectionEntry.Pii (emailKey, PiiKind.Email)
          CorrectionEntry.Pii (nameKey,  PiiKind.PersonName) ] |> mkOk

let private rowId (i: int) : SsKey = SsKey.synthesizedComposite "FK_ROW" [ string i ] |> mkOk

let private dataset : Map<SsKey, StaticRow list> =
    let row i =
        { Identifier = rowId i
          Values = Map.ofList [ name "Email", "syn:x:0"; name "FullName", "syn:y:0"; name "Status", "Active" ] }
    Map.ofList [ kKey, [ row 0; row 1; row 2 ] ]

let private valuesOf (m: Map<SsKey, StaticRow list>) (col: string) : string list =
    m.[kKey] |> List.choose (fun (r: StaticRow) -> Map.tryFind (name col) r.Values)

[<Fact>]
let ``F2: PII columns realize to Faker shapes (email has an at-sign, name non-empty); non-PII untouched`` () =
    let realized = FakerRealization.realizePii catalog correction dataset
    Assert.All(valuesOf realized "Email",    fun e -> Assert.Contains("@", e))
    Assert.All(valuesOf realized "FullName", fun n -> Assert.False(System.String.IsNullOrWhiteSpace n))
    Assert.All(valuesOf realized "Status",   fun s -> Assert.Equal("Active", s))   // non-PII untouched

[<Fact>]
let ``F2: privacy — the synthesized token is replaced (no syn: token survives in a PII column)`` () =
    let realized = FakerRealization.realizePii catalog correction dataset
    Assert.All(valuesOf realized "Email",    fun e -> Assert.False(e.StartsWith "syn:"))
    Assert.All(valuesOf realized "FullName", fun n -> Assert.False(n.StartsWith "syn:"))

[<Fact>]
let ``F2: determinism — the same dataset realizes byte-identically`` () =
    let a = FakerRealization.realizePii catalog correction dataset
    let b = FakerRealization.realizePii catalog correction dataset
    Assert.Equal<Map<SsKey, StaticRow list>>(a, b)

[<Fact>]
let ``F2: distinct rows (distinct seeds) yield distinct fake identities`` () =
    let realized = FakerRealization.realizePii catalog correction dataset
    let pairs =
        realized.[kKey]
        |> List.map (fun r -> Map.find (name "Email") r.Values, Map.find (name "FullName") r.Values)
    Assert.Equal(3, pairs.Length)
    Assert.True(pairs |> List.distinct |> List.length >= 2, "distinct rows should yield distinct fake identities")

[<Fact>]
let ``F2: an empty correction returns the dataset verbatim`` () =
    let realized = FakerRealization.realizePii catalog Correction.empty dataset
    Assert.Equal<Map<SsKey, StaticRow list>>(dataset, realized)

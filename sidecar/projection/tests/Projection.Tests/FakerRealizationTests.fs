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

// -- F-Faker: the coordinate-addressed, tunable generators -------------------

let private coord m e a = AttributeCoordinate.create m e a
let private fk m e a g : CorrectionEntry = CorrectionEntry.Faker (coord m e a, { Generator = g; Locale = Option.None })

[<Fact>]
let ``F-Faker: a coordinate-bound Email generator realizes only that column to an email shape`` () =
    let corr = Correction.create [ fk "M" "Person" "Email" FakerGenerator.Email ] |> mkOk
    let realized = FakerRealization.realize catalog corr dataset
    Assert.All(valuesOf realized "Email",  fun e -> Assert.Contains("@", e))
    Assert.All(valuesOf realized "Status", fun s -> Assert.Equal("Active", s))   // unbound untouched

[<Fact>]
let ``F-Faker: a Constant generator overrides every cell with the fixed value`` () =
    let corr = Correction.create [ fk "M" "Person" "Status" (FakerGenerator.Constant "REDACTED") ] |> mkOk
    Assert.All(valuesOf (FakerRealization.realizeFaker catalog corr dataset) "Status", fun s -> Assert.Equal("REDACTED", s))

[<Fact>]
let ``F-Faker: a Mask (Redact) stars the preserved value; KeepLast keeps the tail`` () =
    // the dataset's Status cell is the real value "Active" (6 chars).
    let redact = Correction.create [ fk "M" "Person" "Status" (FakerGenerator.Mask MaskRule.Redact) ] |> mkOk
    Assert.All(valuesOf (FakerRealization.realizeFaker catalog redact dataset) "Status", fun s -> Assert.Equal("******", s))
    let keep = Correction.create [ fk "M" "Person" "Status" (FakerGenerator.Mask (MaskRule.KeepLast 2)) ] |> mkOk
    Assert.All(valuesOf (FakerRealization.realizeFaker catalog keep dataset) "Status", fun s -> Assert.Equal("****ve", s))

[<Fact>]
let ``F-Faker: IntBetween realizes a parseable integer within range`` () =
    let corr = Correction.create [ fk "M" "Person" "Status" (FakerGenerator.IntBetween (10, 20)) ] |> mkOk
    Assert.All(valuesOf (FakerRealization.realizeFaker catalog corr dataset) "Status", fun s ->
        match System.Int32.TryParse s with
        | true, v -> Assert.InRange(v, 10, 20)
        | _ -> Assert.Fail(sprintf "not an int: %s" s))

[<Fact>]
let ``F-Faker: referential consistency — FirstName is part of the row's FullName (one coherent person)`` () =
    // two PERSON-based generators on the SAME row read one materialized person.
    let corr = Correction.create [ fk "M" "Person" "Email" FakerGenerator.FirstName; fk "M" "Person" "FullName" FakerGenerator.FullName ] |> mkOk
    let realized = FakerRealization.realizeFaker catalog corr dataset
    for r in realized.[kKey] do
        let first = Map.find (name "Email") r.Values     // the Email column now holds a FirstName
        let full  = Map.find (name "FullName") r.Values
        Assert.Contains(first, full)

[<Fact>]
let ``F-Faker: determinism — the same dataset realizes byte-identically`` () =
    let corr = Correction.create [ fk "M" "Person" "Email" FakerGenerator.Email; fk "M" "Person" "FullName" FakerGenerator.FullName ] |> mkOk
    Assert.Equal<Map<SsKey, StaticRow list>>(FakerRealization.realize catalog corr dataset, FakerRealization.realize catalog corr dataset)

[<Fact>]
let ``F-Faker: realize applies Faker AFTER Pii (the more-specific binding wins)`` () =
    // Pii(Email) → a fake email; Faker(Email, Constant) overwrites it (applied last).
    let corr =
        Correction.create
            [ CorrectionEntry.Pii (emailKey, PiiKind.Email)
              fk "M" "Person" "Email" (FakerGenerator.Constant "OVERRIDE") ] |> mkOk
    Assert.All(valuesOf (FakerRealization.realize catalog corr dataset) "Email", fun e -> Assert.Equal("OVERRIDE", e))

[<Fact>]
let ``F-Faker: an empty Faker set returns the dataset verbatim`` () =
    Assert.Equal<Map<SsKey, StaticRow list>>(dataset, FakerRealization.realizeFaker catalog Correction.empty dataset)

module Twin.Tests.SectorPaintTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core

// ---------------------------------------------------------------------------
// The sector realization (F3): σ's global rows are repainted into
// per-environment subpopulations — contiguous slices proportional to each
// sector's recorded volume, vocabularies re-drawn by QUOTA so every recorded
// value lands its proportional presence deterministically. The paint never
// touches nullness, key spaces, references, or the empty-string sentinel.
// ---------------------------------------------------------------------------

let private name (s: string) : Name = Name.create s |> Result.value

let private attrOf (key: SsKey) (logical: string) (column: string) (ptype: PrimitiveType) (isPk: bool) (identity: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create column false |> Result.value
        IsPrimaryKey = isPk
        IsIdentity   = identity }

let private person : Kind =
    { Kind.create (kindKey ["SP"]) (name "Person") (mkTableId "dbo" "Person")
        [ attrOf (attrKey ["SP"; "Id"]) "Id" "Id" Integer true true
          attrOf (attrKey ["SP"; "Name"]) "Name" "Name" Text false false
          attrOf (attrKey ["SP"; "Tag"]) "Tag" "Tag" Text false false ] with
        Modality = [] }

/// A kind whose one text column sources an enforced reference — the
/// paint must never repoint it.
let private membership : Kind =
    { Kind.create (kindKey ["SM"]) (name "Membership") (mkTableId "dbo" "Membership")
        [ attrOf (attrKey ["SM"; "Id"]) "Id" "Id" Integer true true
          attrOf (attrKey ["SM"; "PersonCode"]) "PersonCode" "PersonCode" Text false false ] with
        References =
            [ Reference.create (refKey ["SM"; "Person"]) (name "Person") (attrKey ["SM"; "PersonCode"]) (kindKey ["SP"]) ] }

let private row (i: int) (values: (string * string option) list) : StaticRow =
    { Identifier = attrKey ["ROW"; string i]
      Values = values |> List.map (fun (n, v) -> name n, v) |> Map.ofList }

let private col (n: string) (rows: int64) (freqs: (string * int64) list) : ColumnEvidence =
    { Column = n; RowCount = rows; NullCount = 0L; MaxLength = None
      DistinctCount = None; Truncated = false; HasDuplicates = false
      Frequencies = freqs; Numeric = None; Text = None; ConditionalNulls = None }

let private sectorOf (label: string) (tableName: string) (rows: int64) (freqs: (string * int64) list) : string * EvidencePack =
    label,
    { Evidence.emptyPack RichTier with
        Sources = [ label ]
        Tables = [ { Table = tableName; RowCount = rows; Columns = [ col "Name" rows freqs ] } ] }

let private personRows (n: int) : StaticRow list =
    [ for i in 1 .. n -> row i [ "Id", Some (string i); "Name", Some "Seed"; "Tag", Some "T" ] ]

let private nameOf (r: StaticRow) : string option = Map.find (name "Name") r.Values

[<Fact>]
let ``the allocation is exact, proportional, and earliest-tie deterministic`` () =
    Assert.Equal<int list>([ 11; 8; 6 ], SectorPaint.allocate 25 [ 25L; 20L; 15L ])
    Assert.Equal<int list>([ 5; 5 ], SectorPaint.allocate 10 [ 1L; 1L ])
    // The odd unit goes to the EARLIEST equal remainder.
    Assert.Equal<int list>([ 6; 5 ], SectorPaint.allocate 11 [ 1L; 1L ])
    Assert.Equal<int list>([ 0; 0 ], SectorPaint.allocate 10 [ 0L; 0L ])
    Assert.Equal(7, SectorPaint.allocate 7 [ 3L; 2L; 2L ] |> List.sum)

[<Fact>]
let ``sector slices are contiguous, proportional, and vocabulary-pure`` () =
    let dataset = Map.ofList [ person.SsKey, personRows 10 ]
    let sectors =
        [ sectorOf "dev" "dbo.Person" 5L [ "DevOnly", 5L ]
          sectorOf "qa" "dbo.Person" 5L [ "QaOnly", 5L ] ]
    let painted = SectorPaint.realize 7UL [ "dbo.Person", person ] sectors dataset
    let rows = Map.find person.SsKey painted |> List.toArray
    for i in 0 .. 4 do Assert.Equal(Some "DevOnly", nameOf rows.[i])
    for i in 5 .. 9 do Assert.Equal(Some "QaOnly", nameOf rows.[i])
    // The unlisted column passes through untouched.
    Assert.Equal(Some "T", Map.find (name "Tag") rows.[0].Values)

[<Fact>]
let ``every recorded value lands its quota — never a chance draw`` () =
    let dataset = Map.ofList [ person.SsKey, personRows 10 ]
    let sectors = [ sectorOf "dev" "dbo.Person" 10L [ "Common", 8L; "Rare", 2L ] ]
    let painted = SectorPaint.realize 7UL [ "dbo.Person", person ] sectors dataset
    let names = Map.find person.SsKey painted |> List.map nameOf
    Assert.Equal(8, names |> List.filter (fun v -> v = Some "Common") |> List.length)
    Assert.Equal(2, names |> List.filter (fun v -> v = Some "Rare") |> List.length)

[<Fact>]
let ``the paint is deterministic per seed`` () =
    let dataset = Map.ofList [ person.SsKey, personRows 12 ]
    let sectors = [ sectorOf "dev" "dbo.Person" 12L [ "A", 6L; "B", 6L ] ]
    let bound = [ "dbo.Person", person ]
    Assert.Equal<Map<SsKey, StaticRow list>>(
        SectorPaint.realize 7UL bound sectors dataset,
        SectorPaint.realize 7UL bound sectors dataset)
    Assert.NotEqual<Map<SsKey, StaticRow list>>(
        SectorPaint.realize 7UL bound sectors dataset,
        SectorPaint.realize 8UL bound sectors dataset)

[<Fact>]
let ``NULL cells stay NULL and quotas cover only the non-null cells`` () =
    let rows =
        [ row 1 [ "Id", Some "1"; "Name", None; "Tag", Some "T" ]
          row 2 [ "Id", Some "2"; "Name", Some "Seed"; "Tag", Some "T" ]
          row 3 [ "Id", Some "3"; "Name", Some "Seed"; "Tag", Some "T" ] ]
    let dataset = Map.ofList [ person.SsKey, rows ]
    let sectors = [ sectorOf "dev" "dbo.Person" 3L [ "V", 1L ] ]
    let painted = SectorPaint.realize 7UL [ "dbo.Person", person ] sectors dataset
    let out = Map.find person.SsKey painted
    Assert.Equal(None, nameOf out.[0])
    Assert.Equal(Some "V", nameOf out.[1])
    Assert.Equal(Some "V", nameOf out.[2])

[<Fact>]
let ``the empty-string sentinel never rides the paint`` () =
    let dataset = Map.ofList [ person.SsKey, personRows 6 ]
    let sectors = [ sectorOf "qa" "dbo.Person" 6L [ "", 4L; "Real", 2L ] ]
    let painted = SectorPaint.realize 7UL [ "dbo.Person", person ] sectors dataset
    let names = Map.find person.SsKey painted |> List.map nameOf
    Assert.All(names, fun v -> Assert.Equal(Some "Real", v))

[<Fact>]
let ``an enforced-reference source column is never repainted`` () =
    let rows = [ for i in 1 .. 4 -> row i [ "Id", Some (string i); "PersonCode", Some "K1" ] ]
    let dataset = Map.ofList [ membership.SsKey, rows ]
    let sectors =
        [ "dev",
          { Evidence.emptyPack RichTier with
              Sources = [ "dev" ]
              Tables =
                  [ { Table = "dbo.Membership"; RowCount = 4L
                      Columns = [ col "PersonCode" 4L [ "Other", 4L ] ] } ] } ]
    let painted = SectorPaint.realize 7UL [ "dbo.Membership", membership ] sectors dataset
    let out = Map.find membership.SsKey painted
    Assert.All(out, fun r -> Assert.Equal(Some "K1", Map.find (name "PersonCode") r.Values))

[<Fact>]
let ``the empties-carrying sector takes the tail slice`` () =
    // The empty-floor witness claims the global tail, so the sector
    // whose evidence recorded the empties owns it — every other
    // sector's vocabulary stays intact by construction.
    let dataset = Map.ofList [ person.SsKey, personRows 10 ]
    let withEmpties (label: string) (vocab: (string * int64) list) : string * EvidencePack =
        label,
        { Evidence.emptyPack RichTier with
            Sources = [ label ]
            Tables =
                [ { Table = "dbo.Person"; RowCount = 5L
                    Columns =
                        [ { col "Name" 5L vocab with
                              Text =
                                  Some
                                      { EmptyCount = 2L; TrailingSpaceCount = 0L; CaseCollisions = 0L
                                        LengthP50 = None; LengthP90 = None } } ] } ] }
    let sectors =
        [ withEmpties "aa" [ "EmptyLand", 5L ]
          sectorOf "zz" "dbo.Person" 5L [ "CleanLand", 5L ] ]
    let painted = SectorPaint.realize 7UL [ "dbo.Person", person ] sectors dataset
    let rows = Map.find person.SsKey painted |> List.toArray
    // "aa" recorded the empties, so despite its label it paints LAST.
    for i in 0 .. 4 do Assert.Equal(Some "CleanLand", nameOf rows.[i])
    for i in 5 .. 9 do Assert.Equal(Some "EmptyLand", nameOf rows.[i])

[<Fact>]
let ``no sectors, or no sector evidence for the kind, is the identity`` () =
    let dataset = Map.ofList [ person.SsKey, personRows 4 ]
    Assert.Equal<Map<SsKey, StaticRow list>>(
        dataset, SectorPaint.realize 7UL [ "dbo.Person", person ] [] dataset)
    let foreign = [ sectorOf "dev" "dbo.Elsewhere" 5L [ "X", 5L ] ]
    Assert.Equal<Map<SsKey, StaticRow list>>(
        dataset, SectorPaint.realize 7UL [ "dbo.Person", person ] foreign dataset)

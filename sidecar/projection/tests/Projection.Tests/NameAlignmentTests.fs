module Projection.Tests.NameAlignmentTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// The cloned-module (name-aligned) peer-leg pass (`NameAlignment.align`,
// 2026-07-09). Two CLONED modules — same-named entities with identical
// attributes but DISTINCT native GUIDs — cannot align by SsKey. `align`
// rewrites the SOURCE contract's SsKeys to the SINK's by NAME (within an
// operator-declared module map), so the aligned pair diffs to zero and the
// existing SsKey-keyed engine runs unchanged. The core witness is
// `CatalogDiff.between (align …) clone |> isEmpty` — the clone pair, once
// aligned, is one shape. Each dependent concern that cannot be established
// safely refuses BY NAME (`alignment.*`).
// ---------------------------------------------------------------------------

// FSharp.Core's `Result` cases collide with `DiagnosticSeverity.Error` under
// an open `Projection.Core`; the alias forces case resolution (the
// `CatalogDiffTests` pattern).
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private nm (s: string) : Name = Name.create s |> Result.value
let private tid (t: string) : TableId = TableId.create "dbo" t |> Result.value

let private codesOf (r: Result<'a>) : string list =
    match r with
    | FsResult.Ok _ -> []
    | FsResult.Error es -> es |> List.map (fun (e: ValidationError) -> e.Code)

/// Build a two-entity catalog (City ← Customer.CityId) with GUIDs minted off
/// `guidPrefix` and physical names off `tablePrefix`, so a SOURCE ("a"/"A")
/// and its CLONE ("b"/"B") share every NAME but no GUID and no physical name.
let private buildCatalog (guidPrefix: string) (tablePrefix: string) (moduleName: string) : Catalog =
    let key (n: int) : SsKey =
        OssysOriginal (System.Guid (sprintf "%s%07d-0000-0000-0000-000000000000" guidPrefix n))
    let attr (n: int) (col: string) (ptype: PrimitiveType) (isPk: bool) : Attribute =
        { Attribute.create (key n) (nm col) ptype with
            Column       = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
            IsPrimaryKey = isPk
            IsMandatory  = isPk }
    let cityId   = attr 10 "Id"   Integer true
    let cityName = attr 11 "Name" Text    false
    let city =
        { Kind.create (key 1) (nm "City") (tid (sprintf "OSUSR_%s_CITY" tablePrefix)) [ cityId; cityName ] with
            Indexes = [ Index.ofKeyColumns (key 30) (nm "UX_City_Name") [ cityName.SsKey ] ] }
    let custId     = attr 20 "Id"     Integer true
    let custName   = attr 21 "Name"   Text    false
    let custCityId = attr 22 "CityId" Integer false
    let customer =
        { Kind.create (key 2) (nm "Customer") (tid (sprintf "OSUSR_%s_CUSTOMER" tablePrefix)) [ custId; custName; custCityId ] with
            References = [ Reference.create (key 40) (nm "FK_Customer_City") custCityId.SsKey city.SsKey ] }
    mkCatalog [ mkModule (key 100) (nm moduleName) [ city; customer ] ]

let private source = buildCatalog "a" "A" "Sales"
let private clone  = buildCatalog "b" "B" "SalesClone"
let private map    = Map.ofList [ "Sales", "SalesClone" ]

let private findKind (c: Catalog) (name: string) : Kind =
    Catalog.allKinds c |> List.find (fun k -> Name.value k.Name = name)

[<Fact>]
let ``align rewrites the clone pair to ONE shape — the aligned source diffs to zero against the sink`` () =
    match NameAlignment.align map None source clone with
    | FsResult.Error es -> Assert.Fail(sprintf "align must succeed for a true clone: %A" (es |> List.map (fun e -> e.Code)))
    | FsResult.Ok aligned ->
        // The core witness: the clone pair, once name-aligned, is structurally
        // identical at every SsKey-keyed channel (kinds/attrs/refs/indexes).
        Assert.True(CatalogDiff.between aligned clone |> CatalogDiff.isEmpty,
                    "the aligned source must diff to zero against the clone")
        // And the shape gate — the live-seam judge — agrees the pair is one shape.
        Assert.True((PeerTransfer.shapeGate None aligned clone) |> function FsResult.Ok _ -> true | _ -> false)

[<Fact>]
let ``align re-points a foreign key by identity — the aligned FK targets the SINK's entity SsKey`` () =
    match NameAlignment.align map None source clone with
    | FsResult.Error es -> Assert.Fail(sprintf "%A" es)
    | FsResult.Ok aligned ->
        let alignedCust = findKind aligned "Customer"
        let sinkCity    = findKind clone "City"
        let sinkCust    = findKind clone "Customer"
        let fk = alignedCust.References |> List.exactlyOne
        // TargetKind remapped to the sink's City; SourceAttribute to the sink's CityId.
        Assert.Equal(sinkCity.SsKey, fk.TargetKind)
        let sinkCityId = sinkCust.Attributes |> List.find (fun a -> Name.value a.Name = "CityId")
        Assert.Equal(sinkCityId.SsKey, fk.SourceAttribute)

[<Fact>]
let ``align refuses alignment.attribute.shapeDivergence when a name-matched attribute differs`` () =
    // The clone widens City.Name from Text to Integer — a facet the diff detects.
    let divergentClone =
        buildCatalog "b" "B" "SalesClone"
    // Rebuild with City.Name typed Integer by rewriting the clone in place.
    let mutated =
        { divergentClone with
            Modules =
                divergentClone.Modules |> List.map (fun m ->
                    { m with Kinds = m.Kinds |> List.map (fun k ->
                                if Name.value k.Name = "City" then
                                    { k with Attributes = k.Attributes |> List.map (fun a ->
                                                if Name.value a.Name = "Name" then { a with Type = Integer } else a) }
                                else k) }) }
    let r = NameAlignment.align map None source mutated
    Assert.Contains("alignment.attribute.shapeDivergence", codesOf r)

[<Fact>]
let ``align refuses alignment.entity.unmatched when a source entity has no sink counterpart`` () =
    // The clone module carries only Customer — City is absent.
    let cityless =
        { clone with
            Modules = clone.Modules |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.filter (fun k -> Name.value k.Name <> "City") }) }
    let r = NameAlignment.align map None source cityless
    Assert.Contains("alignment.entity.unmatched", codesOf r)

[<Fact>]
let ``align refuses alignment.entity.ambiguous when the mapped sink module holds two same-named entities`` () =
    // Duplicate City in the clone module — the correspondence is ambiguous.
    let dup =
        { clone with
            Modules = clone.Modules |> List.map (fun m ->
                let city = m.Kinds |> List.find (fun k -> Name.value k.Name = "City")
                { m with Kinds = m.Kinds @ [ { city with SsKey = OssysOriginal (System.Guid "c0000001-0000-0000-0000-000000000000") } ] }) }
    let r = NameAlignment.align map None source dup
    Assert.Contains("alignment.entity.ambiguous", codesOf r)

[<Fact>]
let ``align refuses alignment.attribute.unmatched when a source attribute is absent on the sink kind`` () =
    // The clone's Customer drops the Name attribute.
    let dropped =
        { clone with
            Modules = clone.Modules |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.map (fun k ->
                            if Name.value k.Name = "Customer" then
                                { k with Attributes = k.Attributes |> List.filter (fun a -> Name.value a.Name <> "Name") }
                            else k) }) }
    let r = NameAlignment.align map None source dropped
    Assert.Contains("alignment.attribute.unmatched", codesOf r)

[<Fact>]
let ``align refuses alignment.module.unmatched when a mapped module is absent`` () =
    let r = NameAlignment.align (Map.ofList [ "Sales", "NotThere" ]) None source clone
    Assert.Contains("alignment.module.unmatched", codesOf r)

[<Fact>]
let ``align refuses alignment.mapEmpty — ByName with no map is not a silent no-op`` () =
    let r = NameAlignment.align Map.empty None source clone
    Assert.Contains("alignment.mapEmpty", codesOf r)

[<Fact>]
let ``align does NOT refuse drift OUTSIDE the strict scope — only the transferred set must be a clean clone`` () =
    // The clone's Customer has a drifted attribute (Name retyped Integer), but
    // the transfer moves only City. Scoped to City, align succeeds (Customer is
    // re-keyed best-effort, its drift ignored); strict-over-all (None) refuses.
    let driftedClone =
        { clone with
            Modules = clone.Modules |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.map (fun k ->
                            if Name.value k.Name = "Customer" then
                                { k with Attributes = k.Attributes |> List.map (fun a ->
                                            if Name.value a.Name = "Name" then { a with Type = Integer } else a) }
                            else k) }) }
    let cityKey = (findKind source "City").SsKey
    match NameAlignment.align map (Some (Set.singleton cityKey)) source driftedClone with
    | FsResult.Ok _ -> ()
    | FsResult.Error es -> Assert.Fail(sprintf "scoped align must ignore out-of-scope drift: %A" (es |> List.map (fun e -> e.Code)))
    // The same drift IS a blocker when the whole estate is in scope.
    Assert.Contains("alignment.attribute.shapeDivergence", codesOf (NameAlignment.align map None source driftedClone))
    // And it is a blocker when the DRIFTED entity is itself in the strict scope
    // (this is the path that proves `Some` enforces its members, not just `None`).
    let customerKey = (findKind source "Customer").SsKey
    Assert.Contains("alignment.attribute.shapeDivergence",
                    codesOf (NameAlignment.align map (Some (Set.singleton customerKey)) source driftedClone))

[<Fact>]
let ``align refuses alignment.identityCollision when two source entities share a name (non-injective)`` () =
    // A malformed source with two "City" entities: both name-match the clone's
    // single City → two source identities collapse onto one sink identity. The
    // injectivity guard refuses rather than let CatalogDiff silently drop one.
    let dupSource =
        { source with
            Modules = source.Modules |> List.map (fun m ->
                let city = m.Kinds |> List.find (fun k -> Name.value k.Name = "City")
                { m with Kinds = m.Kinds @ [ { city with SsKey = OssysOriginal (System.Guid "a9999999-0000-0000-0000-000000000000") } ] }) }
    Assert.Contains("alignment.identityCollision", codesOf (NameAlignment.align map None dupSource clone))

[<Fact>]
let ``align remaps a catalog-level sequence by name`` () =
    let srcSeq =
        Sequence.create (OssysOriginal (System.Guid "a0000050-0000-0000-0000-000000000000")) (nm "SEQ_Order") "dbo" "bigint" None None None None false SequenceCacheMode.Unspecified None
        |> Result.value
    let snkSeq = { srcSeq with SsKey = OssysOriginal (System.Guid "b0000050-0000-0000-0000-000000000000") }
    match NameAlignment.align map None { source with Sequences = [ srcSeq ] } { clone with Sequences = [ snkSeq ] } with
    | FsResult.Ok aligned -> Assert.Equal(snkSeq.SsKey, (aligned.Sequences |> List.exactlyOne).SsKey)
    | FsResult.Error es -> Assert.Fail(sprintf "%A" (es |> List.map (fun e -> e.Code)))

[<Fact>]
let ``AlignmentMode round-trips parse ∘ serialize over both cases`` () =
    for m in [ AlignmentMode.BySsKey; AlignmentMode.ByName ] do
        Assert.Equal<Result<AlignmentMode>>(Result.success m, AlignmentMode.parse (AlignmentMode.serialize m))

/// A two-module catalog: mapped "Sales" (Customer, FK RegionId → Region) + an
/// UN-mapped "Geo" (Region). Prefix distinguishes source ("d") from clone ("e").
let private buildEscape (p: string) : Catalog =
    let k (n: int) : SsKey = OssysOriginal (System.Guid (sprintf "%s%07d-0000-0000-0000-000000000000" p n))
    let pk (n: int) (col: string) : Attribute =
        { Attribute.create (k n) (nm col) Integer with
            Column = ColumnRealization.create (col.ToUpperInvariant()) false |> Result.value
            IsPrimaryKey = true; IsMandatory = true }
    let fk (n: int) (col: string) : Attribute =
        { Attribute.create (k n) (nm col) Integer with
            Column = ColumnRealization.create (col.ToUpperInvariant()) true |> Result.value }
    let region = Kind.create (k 5) (nm "Region") (tid "OSUSR_G_REGION") [ pk 50 "Id" ]
    let custRegion = fk 61 "RegionId"
    let customer =
        { Kind.create (k 6) (nm "Customer") (tid "OSUSR_S_CUSTOMER") [ pk 60 "Id"; custRegion ] with
            References = [ Reference.create (k 62) (nm "FK_Customer_Region") custRegion.SsKey region.SsKey ] }
    mkCatalog [ mkModule (k 500) (nm "Sales") [ customer ]; mkModule (k 501) (nm "Geo") [ region ] ]

[<Fact>]
let ``align leaves an out-of-contract FK target unmapped — the escape is the T0.3 gate's, not a rewrite`` () =
    // Only "Sales" is mapped; Customer's FK into the un-mapped "Geo" module keeps
    // its SOURCE Region identity so the subset-FK / T0.3 gate owns the escape.
    let escapeMap = Map.ofList [ "Sales", "Sales" ]
    match NameAlignment.align escapeMap None (buildEscape "d") (buildEscape "e") with
    | FsResult.Ok aligned ->
        let fk = (findKind aligned "Customer").References |> List.exactlyOne
        Assert.Equal((findKind (buildEscape "d") "Region").SsKey, fk.TargetKind)
    | FsResult.Error es -> Assert.Fail(sprintf "%A" (es |> List.map (fun e -> e.Code)))

[<Fact>]
let ``align is deterministic — identical inputs yield identical output`` () =
    let a = NameAlignment.align map None source clone
    let b = NameAlignment.align map None source clone
    Assert.Equal<Result<Catalog>>(a, b)

[<Fact>]
let ``alignForMode BySsKey is identity — the source rides through unchanged`` () =
    match NameAlignment.alignForMode AlignmentMode.BySsKey Map.empty [] source clone with
    | FsResult.Ok c -> Assert.Equal<Catalog>(source, c)
    | FsResult.Error es -> Assert.Fail(sprintf "%A" es)

[<Fact>]
let ``alignForMode ByName runs the pass — the aligned pair diffs to zero`` () =
    match NameAlignment.alignForMode AlignmentMode.ByName map [] source clone with
    | FsResult.Ok aligned -> Assert.True(CatalogDiff.between aligned clone |> CatalogDiff.isEmpty)
    | FsResult.Error es -> Assert.Fail(sprintf "%A" (es |> List.map (fun e -> e.Code)))

module Twin.Tests.CrossoverTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Twin.Core

// ---------------------------------------------------------------------------
// The crossover merge (PROVING_SURFACE_DESIGN §5.2): extremes survive, an
// average never replaces one, provenance rides the report. The properties
// here are the merge's laws; the examples pin the decisions — including the
// null-rate divergence from the kernel's Profile.merge (its independent
// MAX(NullCount)/MAX(RowCount) understates the worst rate).
// ---------------------------------------------------------------------------

let private ok (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> failwithf "expected success, got: %A" (es |> List.map (fun e -> e.Code))

let private codes (r: Result<'a>) : string list =
    match r with Ok _ -> [] | Error es -> es |> List.map (fun e -> e.Code)

let private col (name: string) (rows: int64) (nulls: int64) : ColumnEvidence =
    { Column = name; RowCount = rows; NullCount = nulls; MaxLength = None
      DistinctCount = None; Truncated = false; HasDuplicates = false
      Frequencies = []; Numeric = None; Text = None }

let private table (name: string) (columns: ColumnEvidence list) : TableEvidence =
    { Table = name
      RowCount = columns |> List.map (fun c -> c.RowCount) |> function [] -> 0L | xs -> List.max xs
      Columns = columns }

let private pack (source: string) (tables: TableEvidence list) : EvidencePack =
    { Evidence.emptyPack RichTier with Sources = [ source ]; Tables = tables }

let private rate (nulls: int64) (rows: int64) : decimal =
    if rows = 0L then 0m else decimal nulls / decimal rows

// ---------------------------------------------------------------------------
// Properties.
// ---------------------------------------------------------------------------

[<Property>]
let ``the merged null rate is at least every input's rate`` (pairs: NonEmptyArray<byte * byte>) =
    let inputs =
        pairs.Get
        |> Array.toList
        |> List.mapi (fun i (nulls, extra) ->
            let rows = int64 nulls + int64 extra + 1L
            let nulls = int64 nulls
            pack (sprintf "s%d" i) [ table "dbo.T" [ col "C" rows nulls ] ])
    let merged, _ = ok (Crossover.merge inputs)
    let mergedCol = (List.exactlyOne merged.Tables).Columns |> List.exactlyOne
    let mergedRate = rate mergedCol.NullCount mergedCol.RowCount
    inputs
    |> List.forall (fun p ->
        let c = (List.exactlyOne p.Tables).Columns |> List.exactlyOne
        mergedRate >= rate c.NullCount c.RowCount)

[<Property>]
let ``the merged row count is the maximum input row count`` (pairs: NonEmptyArray<byte * byte>) =
    let inputs =
        pairs.Get
        |> Array.toList
        |> List.mapi (fun i (nulls, extra) ->
            let rows = int64 nulls + int64 extra + 1L
            pack (sprintf "s%d" i) [ table "dbo.T" [ col "C" rows (int64 nulls) ] ])
    let merged, _ = ok (Crossover.merge inputs)
    let mergedCol = (List.exactlyOne merged.Tables).Columns |> List.exactlyOne
    let maxRows =
        inputs |> List.map (fun p -> ((List.exactlyOne p.Tables).Columns |> List.exactlyOne).RowCount) |> List.max
    mergedCol.RowCount = maxRows && mergedCol.NullCount <= mergedCol.RowCount

[<Property>]
let ``the merged envelope contains every input envelope and stays monotone`` (edges: NonEmptyArray<int * int>) =
    let shapes =
        edges.Get
        |> Array.toList
        |> List.map (fun (a, b) ->
            let lo = decimal (min a b)
            let hi = decimal (max a b)
            let step (k: int) = lo + (hi - lo) * decimal k / 6m
            { Min = lo; P25 = step 1; P50 = step 2; P75 = step 3; P95 = step 4; P99 = step 5; Max = hi })
    let inputs =
        shapes
        |> List.mapi (fun i s ->
            pack (sprintf "s%d" i)
                [ table "dbo.T" [ { col "N" (int64 (i + 1) * 10L) 0L with Numeric = Some s } ] ])
    let merged, _ = ok (Crossover.merge inputs)
    let m = ((List.exactlyOne merged.Tables).Columns |> List.exactlyOne).Numeric |> Option.get
    let contains =
        shapes |> List.forall (fun s -> m.Min <= s.Min && m.Max >= s.Max)
    let monotone =
        m.Min <= m.P25 && m.P25 <= m.P50 && m.P50 <= m.P75
        && m.P75 <= m.P95 && m.P95 <= m.P99 && m.P99 <= m.Max
    contains && monotone

[<Property>]
let ``the merge is invariant under input permutation`` (a: byte * byte) (b: byte * byte) (c: byte * byte) =
    let mk (label: string) ((nulls, extra): byte * byte) =
        let rows = int64 nulls + int64 extra + 1L
        pack label [ table "dbo.T" [ col "C" rows (int64 nulls) ] ]
    let packs = [ mk "dev" a; mk "qa" b; mk "uat" c ]
    let forward, forwardReport = ok (Crossover.merge packs)
    let reversed, reversedReport = ok (Crossover.merge (List.rev packs))
    forward = reversed
    && Crossover.serializeReport forwardReport = Crossover.serializeReport reversedReport

[<Fact>]
let ``re-merging a merged pack changes nothing`` () =
    let inputs =
        [ pack "dev" [ table "dbo.T" [ { col "C" 100L 10L with MaxLength = Some 8 } ] ]
          pack "qa" [ table "dbo.T" [ { col "C" 40L 20L with MaxLength = Some 16 } ] ] ]
    let once, _ = ok (Crossover.merge inputs)
    let twice, _ = ok (Crossover.merge [ once ])
    Assert.Equal(once, twice)

// ---------------------------------------------------------------------------
// The decisions, pinned as examples.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the worst null rate survives at the worst volume — the Profile.merge divergence`` () =
    // Profile.merge would take MAX(NullCount)=10,000 over MAX(RowCount)=1,000,000
    // and read 1%. The crossover keeps environment a's 50% rate and scales it
    // to the merged volume.
    let a = pack "a" [ table "dbo.T" [ col "C" 100L 50L ] ]
    let b = pack "b" [ table "dbo.T" [ col "C" 1_000_000L 10_000L ] ]
    let merged, report = ok (Crossover.merge [ a; b ])
    let c = (List.exactlyOne merged.Tables).Columns |> List.exactlyOne
    Assert.Equal(1_000_000L, c.RowCount)
    Assert.Equal(500_000L, c.NullCount)
    let entry = report.Statistics |> List.find (fun s -> s.Statistic = "nullRate")
    Assert.Equal("a", entry.Winner)
    Assert.Contains(("a", "50/100"), entry.PerSource)
    Assert.Contains(("b", "10000/1000000"), entry.PerSource)

[<Fact>]
let ``every winning extreme is attributed to its environment`` () =
    let dev =
        pack "dev"
            [ table "dbo.Customer"
                [ { col "Email" 500L 0L with MaxLength = Some 40; Frequencies = [ "XSECRETDEVV", 500L ]; DistinctCount = Some 1L } ] ]
    let qa =
        pack "qa"
            [ table "dbo.Customer"
                [ { col "Email" 200L 40L with MaxLength = Some 40; HasDuplicates = true; Frequencies = [ "XSECRETQAV", 200L ]; DistinctCount = Some 1L } ] ]
    let uat =
        { pack "uat"
            [ table "dbo.Customer"
                [ { col "Email" 300L 3L with MaxLength = Some 120; Frequencies = [ "XSECRETUATV", 300L ]; DistinctCount = Some 1L } ] ] with
            Orphans = [ { ChildTable = "dbo.Order"; ChildColumn = "CustomerId"; ParentTable = "dbo.Customer"; OrphanCount = 7L } ] }
    let merged, report = ok (Crossover.merge [ dev; qa; uat ])
    let find stat = report.Statistics |> List.find (fun s -> s.Statistic = stat)
    Assert.Equal("qa", (find "nullRate").Winner)
    Assert.Equal("uat", (find "maxLength").Winner)
    Assert.Equal("qa", (find "hasDuplicates").Winner)
    Assert.Equal("(union)", (find "vocabulary").Winner)
    let c = (List.exactlyOne merged.Tables).Columns |> List.exactlyOne
    Assert.Equal(Some 120, c.MaxLength)
    Assert.True c.HasDuplicates
    // The vocabulary is the union of all three environments' values.
    Assert.Equal(3, List.length c.Frequencies)
    Assert.Equal(Some 3L, c.DistinctCount)
    // The uat-only orphan survives the merge untouched.
    Assert.Equal(7L, (List.exactlyOne merged.Orphans).OrphanCount)

[<Fact>]
let ``mixed tiers refuse by name`` () =
    let rich = pack "dev" [ table "dbo.T" [ col "C" 1L 0L ] ]
    let shape = Evidence.deriveShape (pack "qa" [ table "dbo.T" [ col "C" 1L 0L ] ])
    Assert.Contains("twin.evidence.crossover.tierMismatch", codes (Crossover.merge [ rich; shape ]))
    Assert.Contains("twin.evidence.crossover.noInputs", codes (Crossover.merge []))

[<Fact>]
let ``a duplicated fan-out edge merges to one envelope entry`` () =
    let shapeOf lo hi =
        { Min = lo; P25 = lo; P50 = (lo + hi) / 2m; P75 = hi; P95 = hi; P99 = hi; Max = hi }
    let mk label lo hi =
        { pack label [ table "dbo.Order" [ col "CustomerId" 100L 0L ] ] with
            FanOuts =
                [ { ChildTable = "dbo.Order"; ChildColumn = "CustomerId"; ParentTable = "dbo.Customer"
                    Shape = shapeOf lo hi } ] }
    let merged, _ = ok (Crossover.merge [ mk "dev" 1m 4m; mk "uat" 2m 9m ])
    let fan = List.exactlyOne merged.FanOuts
    Assert.Equal(1m, fan.Shape.Min)
    Assert.Equal(9m, fan.Shape.Max)

[<Fact>]
let ``clamp drops what the trunk does not carry and reports it as drift`` () =
    let trunk =
        { Tables = Set.ofList [ "dbo.customer"; "dbo.order" ]
          Columns = Set.ofList [ ("dbo.customer", "email"); ("dbo.order", "customerid") ] }
    let qa =
        { pack "qa"
            [ table "dbo.Customer" [ col "Email" 10L 0L; col "LegacyCode" 10L 0L ]
              table "dbo.AuditShadow" [ col "Id" 5L 0L ] ] with
            Orphans =
                [ { ChildTable = "dbo.Order"; ChildColumn = "CustomerId"; ParentTable = "dbo.Customer"; OrphanCount = 2L }
                  { ChildTable = "dbo.Order"; ChildColumn = "LegacyRef"; ParentTable = "dbo.Customer"; OrphanCount = 1L } ] }
    let clamped, drift = Crossover.clamp trunk qa
    Assert.Equal<string list>([ "dbo.Customer" ], clamped.Tables |> List.map (fun t -> t.Table))
    Assert.Equal<string list>([ "Email" ], (List.exactlyOne clamped.Tables).Columns |> List.map (fun c -> c.Column))
    Assert.Equal(1, List.length clamped.Orphans)
    let kinds = drift |> List.map (fun d -> d.Kind)
    Assert.Contains(TableNotInTrunk, kinds)
    Assert.Contains(ColumnNotInTrunk, kinds)
    Assert.Contains(EdgeNotInTrunk, kinds)
    Assert.All(drift, fun d -> Assert.Equal<string list>([ "qa" ], d.Sources))

[<Fact>]
let ``the report renders no captured literal`` () =
    let dev =
        pack "dev"
            [ table "dbo.Customer"
                [ { col "Status" 50L 5L with Frequencies = [ "XSECRETACTIVE", 40L ]; DistinctCount = Some 1L } ] ]
    let qa =
        { pack "qa"
            [ table "dbo.Customer"
                [ { col "Status" 80L 20L with Frequencies = [ "XSECRETDORMANT", 80L ]; DistinctCount = Some 1L } ] ] with
            Selectivities =
                [ { ChildTable = "dbo.Order"; ChildColumn = "CustomerId"; ParentTable = "dbo.Customer"
                    DistinctCount = 2L; Counts = [ 70L; 30L ] } ]
            Joints =
                [ { Table = "dbo.Order"; Columns = [ "CustomerId"; "RegionId" ]
                    DistinctCount = 1L; Frequencies = [ "XSECRETJ1|XSECRETJ2", 100L ] } ] }
    let _, report = ok (Crossover.merge [ dev; qa ])
    let json = Crossover.serializeReport report
    Assert.DoesNotContain("XSECRET", json)
    Assert.Contains("nullRate", json)
// -- The string-plane axis (F1) ---------------------------------------------

let private textShape (empty: int64) (trailing: int64) (collisions: int64) (p50: int option) (p90: int option) : TextShape =
    { EmptyCount = empty; TrailingSpaceCount = trailing; CaseCollisions = collisions
      LengthP50 = p50; LengthP90 = p90 }

[<Fact>]
let ``string counts merge extreme-preservingly: worst rate rescaled, collisions and quantiles by max`` () =
    let dev =
        pack "dev" [ table "dbo.T" [ { col "C" 1000L 0L with Text = Some (textShape 1L 0L 0L (Some 10) (Some 20)) } ] ]
    let qa =
        pack "qa" [ table "dbo.T" [ { col "C" 10L 0L with Text = Some (textShape 2L 3L 4L (Some 12) (Some 18)) } ] ]
    let merged, report = ok (Crossover.merge [ dev; qa ])
    let c = (List.exactlyOne merged.Tables).Columns |> List.exactlyOne
    match c.Text with
    | None -> failwith "the merged column dropped the string counts"
    | Some ts ->
        // qa's 2/10 empty rate dominates dev's 1/1000 and rescales to the
        // merged 1,000 rows with the int64 ceiling — the null-rate policy.
        Assert.Equal(200L, ts.EmptyCount)
        Assert.Equal(300L, ts.TrailingSpaceCount)
        Assert.Equal(4L, ts.CaseCollisions)
        Assert.Equal(Some 12, ts.LengthP50)
        Assert.Equal(Some 20, ts.LengthP90)
    let stats = report.Statistics |> List.map (fun s -> s.Statistic, s.Winner)
    Assert.Contains(("emptyRate", "qa"), stats)
    Assert.Contains(("trailingSpaceRate", "qa"), stats)
    Assert.Contains(("caseCollisions", "qa"), stats)

[<Fact>]
let ``a source without string counts merges against one that has them`` () =
    let dev = pack "dev" [ table "dbo.T" [ col "C" 100L 0L ] ]
    let qa = pack "qa" [ table "dbo.T" [ { col "C" 50L 0L with Text = Some (textShape 5L 0L 0L None None) } ] ]
    let merged, _ = ok (Crossover.merge [ dev; qa ])
    let c = (List.exactlyOne merged.Tables).Columns |> List.exactlyOne
    match c.Text with
    | Some ts -> Assert.Equal(10L, ts.EmptyCount)
    | None -> failwith "the lone source's string counts vanished"

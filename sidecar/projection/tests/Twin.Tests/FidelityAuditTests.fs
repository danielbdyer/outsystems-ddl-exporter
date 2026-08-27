module Twin.Tests.FidelityAuditTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Twin.Core

// ---------------------------------------------------------------------------
// The per-environment fidelity audit (PROVING_SURFACE_DESIGN §5.2, decision
// j): the template must demonstrate, per environment, that it is at least
// as blocking as that environment's captured reality. The audit is the
// merge's independent checker: a lawful merge output never fails a blocking
// verdict against any of its own inputs.
// ---------------------------------------------------------------------------

let private col (n: string) (rows: int64) (nulls: int64) : ColumnEvidence =
    { Column = n; RowCount = rows; NullCount = nulls; MaxLength = None
      DistinctCount = None; Truncated = false; HasDuplicates = false
      Frequencies = []; Numeric = None; Text = None; ConditionalNulls = None }

let private table (n: string) (columns: ColumnEvidence list) : TableEvidence =
    { Table = n
      RowCount = columns |> List.map (fun c -> c.RowCount) |> function [] -> 0L | xs -> List.max xs
      Columns = columns }

let private pack (source: string) (tables: TableEvidence list) : EvidencePack =
    { Evidence.emptyPack RichTier with Sources = [ source ]; Tables = tables }

[<Fact>]
let ``identical packs audit clean`` () =
    let p = pack "qa" [ table "dbo.Customer" [ { col "Email" 50L 5L with MaxLength = Some 30; HasDuplicates = true } ] ]
    let section = FidelityAudit.audit Set.empty p { p with Sources = [ "minted" ] }
    Assert.Equal(0, section.Failures)
    Assert.Equal(0, section.Advisories)
    Assert.Equal("qa", section.Source)

[<Fact>]
let ``a template that under-blocks an environment's null rate fails, and the environment is named`` () =
    let qa = pack "qa" [ table "dbo.Customer" [ col "Email" 40L 20L ] ]
    let minted = pack "minted" [ table "dbo.Customer" [ col "Email" 100L 10L ] ]
    let section = FidelityAudit.audit Set.empty qa minted
    Assert.Equal("qa", section.Source)
    Assert.True(section.Failures >= 1)
    let failing = section.Verdicts |> List.find (fun v -> v.Statistic = "nullRate")
    Assert.False failing.Ok
    Assert.True failing.Blocking
    Assert.Contains("10/100", failing.Detail)
    Assert.Contains("20/40", failing.Detail)

[<Fact>]
let ``an exempt coordinate demotes the verdict to advisory`` () =
    let qa =
        { pack "qa" [ table "dbo.Order" [ col "CustomerId" 10L 0L ] ] with
            Orphans = [ { ChildTable = "dbo.Order"; ChildColumn = "CustomerId"; ParentTable = "dbo.Customer"; OrphanCount = 3L } ] }
    let minted = pack "minted" [ table "dbo.Order" [ col "CustomerId" 10L 0L ] ]
    let strict = FidelityAudit.audit Set.empty qa minted
    Assert.Equal(1, strict.Failures)
    let exempt = Set.ofList [ "dbo.Order.CustomerId -> dbo.Customer" ]
    let demoted = FidelityAudit.audit exempt qa minted
    Assert.Equal(0, demoted.Failures)
    Assert.Equal(1, demoted.Advisories)

[<Fact>]
let ``a planted orphan satisfies the environment's recorded reality`` () =
    let uat =
        { pack "uat" [ table "dbo.Order" [ col "LegacyRef" 10L 0L ] ] with
            Orphans = [ { ChildTable = "dbo.Order"; ChildColumn = "LegacyRef"; ParentTable = "dbo.Customer"; OrphanCount = 2L } ] }
    let minted =
        { pack "minted" [ table "dbo.Order" [ col "LegacyRef" 10L 0L ] ] with
            Orphans = [ { ChildTable = "dbo.Order"; ChildColumn = "LegacyRef"; ParentTable = "dbo.Customer"; OrphanCount = 2L } ] }
    let section = FidelityAudit.audit Set.empty uat minted
    Assert.Equal(0, section.Failures)

[<Fact>]
let ``a missing vocabulary value is an advisory, never a failure`` () =
    let qa =
        pack "qa"
            [ table "dbo.Customer"
                [ { col "Status" 50L 0L with Frequencies = [ "XSECRETA", 30L; "XSECRETB", 20L ]; DistinctCount = Some 2L } ] ]
    let minted =
        pack "minted"
            [ table "dbo.Customer"
                [ { col "Status" 50L 0L with Frequencies = [ "XSECRETA", 50L ]; DistinctCount = Some 2L } ] ]
    let section = FidelityAudit.audit Set.empty qa minted
    Assert.Equal(0, section.Failures)
    Assert.True(section.Advisories >= 1)
    let vocab = section.Verdicts |> List.find (fun v -> v.Statistic = "vocabulary")
    Assert.Contains("missing 1", vocab.Detail)

[<Fact>]
let ``the audit report renders no captured literal`` () =
    let qa =
        pack "qa"
            [ table "dbo.Customer"
                [ { col "Status" 50L 0L with Frequencies = [ "XSECRETA", 30L ]; DistinctCount = Some 1L } ] ]
    let minted =
        pack "minted"
            [ table "dbo.Customer"
                [ { col "Status" 50L 0L with Frequencies = [ "XSECRETZ", 50L ]; DistinctCount = Some 1L } ] ]
    let json = FidelityAudit.serializeReport (FidelityAudit.auditAll Set.empty [ qa ] minted)
    Assert.DoesNotContain("XSECRET", json)
    Assert.Contains("vocabulary", json)

[<Property>]
let ``a lawful merge output never fails a blocking verdict against its own inputs`` (pairs: NonEmptyArray<byte * byte>) =
    let inputs =
        pairs.Get
        |> Array.toList
        |> List.mapi (fun i (nulls, extra) ->
            let rows = int64 nulls + int64 extra + 1L
            pack (sprintf "s%d" i) [ table "dbo.T" [ col "C" rows (int64 nulls) ] ])
    match Crossover.merge inputs with
    | Error _ -> false
    | Ok (merged, _) ->
        let report = FidelityAudit.auditAll Set.empty inputs { merged with Sources = [ "minted" ] }
        FidelityAudit.failures report = 0
// -- The string-plane axis (F1) ---------------------------------------------

let private textOf (empty: int64) (trailing: int64) (collisions: int64) : TextShape =
    { EmptyCount = empty; TrailingSpaceCount = trailing; CaseCollisions = collisions
      LengthP50 = None; LengthP90 = None }

[<Fact>]
let ``a template with no empty strings under-blocks an environment that has them`` () =
    let qa = pack "qa" [ table "dbo.Customer" [ { col "Email" 50L 0L with Text = Some (textOf 3L 0L 0L) } ] ]
    let minted = pack "minted" [ table "dbo.Customer" [ col "Email" 50L 0L ] ]
    let section = FidelityAudit.audit Set.empty qa minted
    let failing = section.Verdicts |> List.find (fun v -> v.Statistic = "emptyString")
    Assert.False failing.Ok
    Assert.True failing.Blocking
    Assert.True(section.Failures >= 1)

[<Fact>]
let ``a planted presence satisfies the blocking claim while the count stays a margin`` () =
    let qa = pack "qa" [ table "dbo.Customer" [ { col "Email" 50L 0L with Text = Some (textOf 5L 1L 2L) } ] ]
    let minted = pack "minted" [ table "dbo.Customer" [ { col "Email" 50L 0L with Text = Some (textOf 2L 1L 1L) } ] ]
    let section = FidelityAudit.audit Set.empty qa minted
    Assert.Equal(0, section.Failures)
    let margin = section.Verdicts |> List.find (fun v -> v.Statistic = "emptyStringCount")
    Assert.False margin.Ok
    Assert.False margin.Blocking

[<Fact>]
let ``length quantiles are advisory margins`` () =
    let qa =
        pack "qa" [ table "dbo.Customer" [ { col "Email" 50L 0L with Text = Some { textOf 0L 0L 0L with LengthP90 = Some 30 } } ] ]
    let minted =
        pack "minted" [ table "dbo.Customer" [ { col "Email" 50L 0L with Text = Some { textOf 0L 0L 0L with LengthP90 = Some 20 } } ] ]
    let section = FidelityAudit.audit Set.empty qa minted
    Assert.Equal(0, section.Failures)
    let margin = section.Verdicts |> List.find (fun v -> v.Statistic = "lengthP90")
    Assert.False margin.Ok
    Assert.False margin.Blocking
    Assert.Contains("minted 20", margin.Detail)
    Assert.Contains("source 30", margin.Detail)

// -- The conditional-null structure and the hot parent (F2) ------------------

let private fanOutOf (child: string) (column: string) (parent: string) (p95: decimal) (mx: decimal) : FanOutEvidence =
    { ChildTable = child; ChildColumn = column; ParentTable = parent
      Shape = { Min = 1m; P25 = 1m; P50 = 1m; P75 = p95; P95 = p95; P99 = mx; Max = mx } }

[<Fact>]
let ``a template without the recorded maximum fan-out fails that edge, blocking`` () =
    let uat = { pack "uat" [] with FanOuts = [ fanOutOf "dbo.Order" "CustomerId" "dbo.Customer" 2m 5m ] }
    let minted = pack "minted" []
    let section = FidelityAudit.audit Set.empty uat minted
    Assert.Equal(1, section.Failures)
    let v = section.Verdicts |> List.find (fun v -> v.Statistic = "fanOutMax")
    Assert.True v.Blocking
    Assert.False v.Ok
    Assert.Contains("minted -", v.Detail)

[<Fact>]
let ``a carried maximum passes while a thinner 95th percentile stays an advisory`` () =
    let uat = { pack "uat" [] with FanOuts = [ fanOutOf "dbo.Order" "CustomerId" "dbo.Customer" 3m 5m ] }
    let minted = { pack "minted" [] with FanOuts = [ fanOutOf "dbo.Order" "CustomerId" "dbo.Customer" 2m 5m ] }
    let section = FidelityAudit.audit Set.empty uat minted
    Assert.Equal(0, section.Failures)
    Assert.Equal(1, section.Advisories)
    let p95 = section.Verdicts |> List.find (fun v -> v.Statistic = "fanOutP95")
    Assert.False p95.Blocking
    Assert.False p95.Ok

[<Fact>]
let ``a maximum under two is no claim on the template`` () =
    let uat = { pack "uat" [] with FanOuts = [ fanOutOf "dbo.Order" "CustomerId" "dbo.Customer" 1m 1m ] }
    let section = FidelityAudit.audit Set.empty uat (pack "minted" [])
    Assert.Equal(0, section.Failures)
    Assert.Empty(section.Verdicts |> List.filter (fun v -> v.Statistic = "fanOutMax"))

[<Fact>]
let ``a conditional structure the mint's discovery missed is an advisory naming only columns`` () =
    let qa =
        pack "qa"
            [ table "dbo.Customer"
                [ { col "Rating" 20L 6L with
                      ConditionalNulls = Some { Partner = "Name"; Rates = [ "XSECRETVALUE", 6L, 12L ] } } ] ]
    let minted = pack "minted" [ table "dbo.Customer" [ col "Rating" 20L 6L ] ]
    let section = FidelityAudit.audit Set.empty qa minted
    Assert.Equal(0, section.Failures)
    let v = section.Verdicts |> List.find (fun v -> v.Statistic = "conditionalNulls")
    Assert.False v.Blocking
    Assert.False v.Ok
    Assert.DoesNotContain("XSECRET", v.Detail)
    Assert.Contains("Name", v.Detail)

[<Fact>]
let ``a surviving conditional structure with the same partner audits clean`` () =
    let cn = Some { Partner = "Name"; Rates = [ "V", 6L, 12L ] }
    let qa = pack "qa" [ table "dbo.Customer" [ { col "Rating" 20L 6L with ConditionalNulls = cn } ] ]
    let minted = pack "minted" [ table "dbo.Customer" [ { col "Rating" 20L 6L with ConditionalNulls = cn } ] ]
    let section = FidelityAudit.audit Set.empty qa minted
    Assert.Equal(0, section.Failures)
    Assert.Equal(0, section.Advisories)

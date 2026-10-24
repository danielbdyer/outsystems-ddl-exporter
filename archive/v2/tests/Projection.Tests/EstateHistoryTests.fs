module Projection.Tests.EstateHistoryTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The estate's recorded readings (`EstateHistory` — wave A7, the burndown's
// memory; DECISIONS 2026-07-15 entry 4 layout). The laws under test:
//   - FIRST-SEEN CARRIES: a finding's age belongs to the finding — a key
//     present in the previous reading keeps its first-seen instant; a new
//     key is as old as this run.
//   - THE STREAK: consecutive unified readings count up; one diverged
//     reading resets to zero.
//   - MOVEMENT: closed / opened / remaining diff by FindingKey; the oldest
//     open finding's age reads from the carried first-seen.
//   - ROUND-TRIP + FAIL-CLOSED: load after save returns the reading; the
//     per-run record and latest.json carry the same bytes; a torn record
//     reads as no baseline, never a half-truth.
// ---------------------------------------------------------------------------

let private t0 = DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero)
let private t1 = DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero)

let private agreed : Estate.TargetOperand = Estate.TargetOperand.AgreedEnv "cloud-dev"

let private operand (label: string) (c: Catalog) (p: Profile option) : Compare.Operand =
    { Label = label; Catalog = c; Profile = p }

let private nullEvidence (attrKey: SsKey) (rowCount: int64) (nullCount: int64) : ColumnProfile =
    { AttributeKey = attrKey
      RowCount = rowCount
      NullCount = nullCount
      MaxObservedLength = None
      NullCountProbeStatus = ProbeStatus.observed rowCount }

let private orphanEvidence (refKey: SsKey) (orphans: int64) : ForeignKeyReality =
    { ReferenceKey = refKey
      HasOrphan = orphans > 0L
      OrphanCount = orphans
      IsNoCheck = false
      ProbeStatus = ProbeStatus.observed 1000L }

/// A report with two data findings (NOT NULL on Customer.Name, orphans on
/// Order.CustomerId) — the movement fixtures' "dirty" reading.
let private dirtyReport () : Estate.EstateReport =
    let dirty =
        { Profile.empty with
            Columns = [ nullEvidence customerNameKey 5_000L 4_120L ]
            ForeignKeys = [ orphanEvidence orderRefToCustomer 20L ] }
    Estate.compute agreed sampleCatalog
        [ "cloud-uat", operand "cloud-uat" sampleCatalog (Some dirty) ]

/// A report with ONE of the two findings repaired (the orphans remain) —
/// the movement fixtures' "half-repaired" reading.
let private halfRepairedReport () : Estate.EstateReport =
    let half = { Profile.empty with ForeignKeys = [ orphanEvidence orderRefToCustomer 20L ] }
    Estate.compute agreed sampleCatalog
        [ "cloud-uat", operand "cloud-uat" sampleCatalog (Some half) ]

/// A clean (unified) reading.
let private unifiedReport () : Estate.EstateReport =
    Estate.compute agreed sampleCatalog
        [ "cloud-uat", operand "cloud-uat" sampleCatalog None ]

let private withTempStore (f: string -> unit) : unit =
    let root = Path.Combine(Path.GetTempPath(), "estate-history-" + Guid.NewGuid().ToString "N")
    try f root
    finally if Directory.Exists root then Directory.Delete(root, true)

// -- first-seen carry + the streak ---------------------------------------------

[<Fact>]
let ``recordOf: a finding's first-seen instant carries across readings; a new finding is as old as this run`` () =
    let first = EstateHistory.recordOf t0 "run-a" None (halfRepairedReport ())
    for f in first.Findings do
        Assert.Equal(t0, f.FirstSeenAtUtc)
    let second = EstateHistory.recordOf t1 "run-b" (Some first) (dirtyReport ())
    let orphanLine = second.Findings |> List.find (fun f -> f.Key.StartsWith "data.orphans:")
    let notNullLine = second.Findings |> List.find (fun f -> f.Key.StartsWith "data.notNull:")
    Assert.Equal(t0, orphanLine.FirstSeenAtUtc)
    Assert.Equal(t1, notNullLine.FirstSeenAtUtc)

[<Fact>]
let ``recordOf: the streak counts consecutive unified readings and a diverged reading resets it`` () =
    let one = EstateHistory.recordOf t0 "run-a" None (unifiedReport ())
    Assert.Equal(1, one.Streak)
    let two = EstateHistory.recordOf t0 "run-b" (Some one) (unifiedReport ())
    Assert.Equal(2, two.Streak)
    let broken = EstateHistory.recordOf t1 "run-c" (Some two) (dirtyReport ())
    Assert.Equal(0, broken.Streak)
    let again = EstateHistory.recordOf t1 "run-d" (Some broken) (unifiedReport ())
    Assert.Equal(1, again.Streak)

// -- the movement ---------------------------------------------------------------

[<Fact>]
let ``burndownOf: closed, opened, and remaining diff by FindingKey; the oldest open finding's age reads from the carried first-seen`` () =
    // Baseline: both findings, recorded five days before this run. Current:
    // the NOT NULL is repaired (closed), the orphans remain — the oldest
    // open finding is five days old.
    let baseline = EstateHistory.recordOf t0 "run-a" None (dirtyReport ())
    let movement = EstateHistory.burndownOf t1 baseline (halfRepairedReport ())
    Assert.Equal("run-a", movement.SinceRunId)
    Assert.Equal(5, movement.SinceAgeDays)
    Assert.Equal(1, movement.Closed)
    Assert.Equal(0, movement.Opened)
    Assert.Equal(1, movement.Remaining)
    Assert.Equal(Some 5, movement.OldestDays)

[<Fact>]
let ``burndownOf: a finding the baseline never saw counts as opened, aged zero`` () =
    let baseline = EstateHistory.recordOf t1 "run-a" None (halfRepairedReport ())
    let movement = EstateHistory.burndownOf t1 baseline (dirtyReport ())
    Assert.Equal(0, movement.Closed)
    Assert.Equal(1, movement.Opened)
    Assert.Equal(1, movement.Remaining)

[<Fact>]
let ``burndownOf: a unified current reading closes everything and carries no oldest age`` () =
    let baseline = EstateHistory.recordOf t0 "run-a" None (dirtyReport ())
    let movement = EstateHistory.burndownOf t1 baseline (unifiedReport ())
    Assert.Equal(2, movement.Closed)
    Assert.Equal(0, movement.Opened)
    Assert.Equal(0, movement.Remaining)
    Assert.Equal(None, movement.OldestDays)

// -- round-trip + fail-closed ----------------------------------------------------

[<Fact>]
let ``history: load after save returns the reading — by run id and as latest, one set of bytes`` () =
    withTempStore (fun root ->
        let record = EstateHistory.recordOf t0 "run-a" None (dirtyReport ())
        match EstateHistory.save root record with
        | Ok () -> ()
        | Error es -> failwithf "save failed: %A" es
        Assert.Equal(Some record, EstateHistory.loadLatest root)
        Assert.Equal(Some record, EstateHistory.loadRun root "run-a")
        Assert.Equal(
            File.ReadAllText(EstateHistory.recordPath root "run-a"),
            File.ReadAllText(EstateHistory.latestPath root)))

[<Fact>]
let ``history: a second save moves latest while the named record stays reachable (the --since door)`` () =
    withTempStore (fun root ->
        let first = EstateHistory.recordOf t0 "run-a" None (dirtyReport ())
        let second = EstateHistory.recordOf t1 "run-b" (Some first) (halfRepairedReport ())
        (match EstateHistory.save root first with Ok () -> () | Error es -> failwithf "%A" es)
        (match EstateHistory.save root second with Ok () -> () | Error es -> failwithf "%A" es)
        Assert.Equal(Some second, EstateHistory.loadLatest root)
        Assert.Equal(Some first, EstateHistory.loadRun root "run-a"))

[<Fact>]
let ``history: an absent or torn record reads as no baseline — fail-closed, never a half-truth`` () =
    withTempStore (fun root ->
        Assert.Equal(None, EstateHistory.loadLatest root)
        Assert.Equal(None, EstateHistory.loadRun root "never-recorded")
        Directory.CreateDirectory(Path.Combine(root, "estate")) |> ignore
        File.WriteAllText(EstateHistory.latestPath root, "{ \"runId\": \"x\", \"findings\": [ { \"key\": 42 } ] }")
        Assert.Equal(None, EstateHistory.loadLatest root))

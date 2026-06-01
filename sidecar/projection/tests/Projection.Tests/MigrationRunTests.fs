module Projection.Tests.MigrationRunTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err -> Assert.Fail(sprintf "%A" err); Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private nm (s: string) : Name = Name.create s |> mustResultOk
let private ver (o: int) (l: string) = Version.create o l |> mustResultOk
let private tl (n: string) = Timeline.create n |> mustResultOk
let private at (iso: string) = DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture)
let private key (n: int) : SsKey =
    SsKey.ossysOriginal (System.Guid(n, 0s, 0s, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy))

let private renamedTarget : Catalog =
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ { customer with Name = nm "Patron" }; order; country ] } ]

let private reshapedTarget : Catalog =
    let c' = { customer with Attributes = customer.Attributes |> List.mapi (fun i a -> if i = 0 then { a with Column = { a.Column with IsNullable = not a.Column.IsNullable } } else a) }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ c'; order; country ] } ]

let private nonShapeTarget : Catalog =
    let c' = { customer with Attributes = customer.Attributes |> List.mapi (fun i a -> if i = 0 then { a with IsIdentity = not a.IsIdentity } else a) }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ c'; order; country ] } ]

let private extraKind : Kind =
    Kind.create (key 9001) (nm "Extra") (TableId.create "dbo" "Extra" |> mustResultOk)
        [ Attribute.create (key 9002) (nm "Id") PrimitiveType.Integer ]

let private withExtraKind (c: Catalog) : Catalog =
    let m0 = List.head c.Modules
    Catalog.create ({ m0 with Kinds = m0.Kinds @ [ extraKind ] } :: List.tail c.Modules) c.Sequences |> mustResultOk

let private isAlter = function Statement.AlterTableAlterColumn _ -> true | _ -> false

let private withTempFile (f: string -> unit) : unit =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "migrate-%s.json" (Guid.NewGuid().ToString "N"))
    try f path
    finally if System.IO.File.Exists path then System.IO.File.Delete path

// ===========================================================================
// 6.D.1 — the composition: one preview produces the schema differential +
// the RefactorLog, minimum-viable, from the displacement
// ===========================================================================

[<Fact>]
let ``6.D.1: a reshape previews an ALTER differential (not a CREATE), one statement`` () =
    let artifacts = MigrationRun.preview false sampleCatalog reshapedTarget |> mustOk
    Assert.Equal(1, artifacts.SchemaStatements |> List.filter isAlter |> List.length)
    Assert.True(artifacts.SchemaStatements |> List.forall isAlter)   // no CREATE TABLE — minimum-viable
    Assert.Empty(artifacts.RefactorLog)

[<Fact>]
let ``6.D.1: a rename previews a RefactorLog entry (data-preserving), no ALTER`` () =
    let artifacts = MigrationRun.preview false sampleCatalog renamedTarget |> mustOk
    Assert.Empty(artifacts.SchemaStatements)
    Assert.Equal(1, List.length artifacts.RefactorLog)
    Assert.Equal("Patron", (List.exactlyOne artifacts.RefactorLog).NewName)

[<Fact>]
let ``6.D.1: an idempotent migrate previews empty artifacts (CDC-silent, zero touches)`` () =
    let artifacts = MigrationRun.preview false sampleCatalog sampleCatalog |> mustOk
    Assert.Empty(artifacts.SchemaStatements)
    Assert.Empty(artifacts.RefactorLog)
    Assert.True(Migration.isIdempotent artifacts.Plan)

// ===========================================================================
// Fail-loud — refuse before any write
// ===========================================================================

[<Fact>]
let ``6.D.1: a destructive drop refuses (RefusedByViolations) before any write`` () =
    match MigrationRun.preview false (withExtraKind sampleCatalog) sampleCatalog with
    | FsResult.Error (RefusedByViolations [ WouldDropKind _ ]) -> ()
    | other -> Assert.Fail(sprintf "expected RefusedByViolations, got %A" other)

[<Fact>]
let ``6.D.1: allowDrops lets the drop through`` () =
    let artifacts = MigrationRun.preview true (withExtraKind sampleCatalog) sampleCatalog |> mustOk
    Assert.Equal(1, artifacts.Plan.Preview.Channels.RemovedKinds)

[<Fact>]
let ``6.D.1: a non-shape facet change refuses (RefusedBySchemaErrors)`` () =
    match MigrationRun.preview false sampleCatalog nonShapeTarget with
    | FsResult.Error (RefusedBySchemaErrors errs) -> Assert.NotEmpty(errs)
    | other -> Assert.Fail(sprintf "expected RefusedBySchemaErrors, got %A" other)

// ===========================================================================
// The durable provenance loop — migrate records its episode; the FTC over the
// recorded chain reproduces B (the round-trip canary closes through 6.H)
// ===========================================================================

[<Fact>]
let ``6.D.1: record opens a timeline at genesis on the first migration`` () =
    withTempFile (fun path ->
        let artifacts = MigrationRun.preview true sampleCatalog renamedTarget |> mustOk
        let coord = EpisodeCoordinate.create (ver 0 "1.0.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
        let chain = MigrationRun.record path (tl "dev") coord (Some "reflog#1") (DataObservation.create 0 None) artifacts |> mustOk
        Assert.Equal(1, EpisodicLifecycle.episodes chain |> List.length)
        Assert.Equal<Catalog>(renamedTarget, (EpisodicLifecycle.head chain).Schema))

[<Fact>]
let ``6.D.1: the full A->B loop — migrate, record, then reconstruct reproduces B (durable round-trip)`` () =
    withTempFile (fun path ->
        // Episode 0: genesis at sampleCatalog (no migration — the starting state).
        let genesisCoord = EpisodeCoordinate.create (ver 0 "1.0.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
        LifecycleStore.save path (EpisodicLifecycle.genesis (tl "dev") (Episode.ofSchema genesisCoord sampleCatalog))
        |> (function FsResult.Ok () -> () | FsResult.Error e -> Assert.Fail(sprintf "%A" e))
        // Episode 1: migrate sampleCatalog → reshapedTarget, recorded.
        let artifacts = MigrationRun.preview false sampleCatalog reshapedTarget |> mustOk
        let coord = EpisodeCoordinate.create (ver 1 "1.1.0") Environment.Dev (at "2026-06-08T09:00:00+00:00")
        let chain = MigrationRun.record path (tl "dev") coord (Some "reflog#1") (DataObservation.create 12 (Some "lsn:0x0C")) artifacts |> mustOk
        // The FTC over the recorded chain reproduces B (genesis ⊕ δ = target).
        let reconstructed = EpisodicLifecycle.reconstructLatestSchema chain |> mustOk
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between reshapedTarget reconstructed |> mustOk))
        // And it survives a reload from disk — durable provenance.
        let reloaded = LifecycleStore.load path |> mustOk
        let reReconstructed = EpisodicLifecycle.reconstructLatestSchema reloaded |> mustOk
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between reshapedTarget reReconstructed |> mustOk)))

// ===========================================================================
// The live-execute rename channel — sp_rename for physical table renames
// ===========================================================================

let private physicalRenameTarget : Catalog =
    let c' = { customer with Name = nm "Patron"; Physical = { customer.Physical with Table = "PATRON_TBL" } }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ c'; order; country ] } ]

[<Fact>]
let ``live: renameStatements emits sp_rename + a logical-name re-bind for a physical table rename`` () =
    let diff = CatalogDiff.between sampleCatalog physicalRenameTarget |> mustOk
    let stmts = MigrationRun.renameStatements diff
    Assert.Equal(2, List.length stmts)
    // (1) the physical rename; (2) the V2.LogicalName re-bind to the new name.
    Assert.Contains("sp_rename", stmts.[0])
    Assert.Contains("PATRON_TBL", stmts.[0])
    Assert.Contains("sp_updateextendedproperty", stmts.[1])
    Assert.Contains("V2.LogicalName", stmts.[1])
    Assert.Contains("Patron", stmts.[1])

[<Fact>]
let ``live: a logical-only rename re-binds the logical name without an sp_rename`` () =
    // renamedTarget changes the logical Name but keeps customer's Physical.Table:
    // the physical object stays, but its V2.LogicalName binding must still update.
    let diff = CatalogDiff.between sampleCatalog renamedTarget |> mustOk
    let one = List.exactlyOne (MigrationRun.renameStatements diff)
    Assert.DoesNotContain("sp_rename", one)   // no physical rename
    Assert.Contains("sp_updateextendedproperty", one)
    Assert.Contains("Patron", one)

[<Fact>]
let ``6.D.1: record refuses a non-advancing version (NonMonotonic)`` () =
    withTempFile (fun path ->
        let artifacts = MigrationRun.preview true sampleCatalog renamedTarget |> mustOk
        let coord0 = EpisodeCoordinate.create (ver 5 "1.5.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
        MigrationRun.record path (tl "dev") coord0 None (DataObservation.create 0 None) artifacts |> mustOk |> ignore
        // A second record at a NON-advancing ordinal must refuse.
        let coord1 = EpisodeCoordinate.create (ver 5 "1.5.1") Environment.Dev (at "2026-06-02T09:00:00+00:00")
        match MigrationRun.record path (tl "dev") coord1 None (DataObservation.create 0 None) artifacts with
        | FsResult.Error (NonMonotonic _) -> ()
        | other -> Assert.Fail(sprintf "expected NonMonotonic, got %A" other))

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
    let artifacts = MigrationRun.preview DeclareNone sampleCatalog reshapedTarget |> mustOk
    Assert.Equal(1, artifacts.SchemaStatements |> List.filter isAlter |> List.length)
    Assert.True(artifacts.SchemaStatements |> List.forall isAlter)   // no CREATE TABLE — minimum-viable
    Assert.Empty(artifacts.RefactorLog)

[<Fact>]
let ``6.D.1: a rename previews a RefactorLog entry (data-preserving), no ALTER`` () =
    let artifacts = MigrationRun.preview DeclareNone sampleCatalog renamedTarget |> mustOk
    Assert.Empty(artifacts.SchemaStatements)
    Assert.Equal(1, List.length artifacts.RefactorLog)
    Assert.Equal("Patron", (List.exactlyOne artifacts.RefactorLog).NewName)

[<Fact>]
let ``6.D.1: an idempotent migrate previews empty artifacts (CDC-silent, zero touches)`` () =
    let artifacts = MigrationRun.preview DeclareNone sampleCatalog sampleCatalog |> mustOk
    Assert.Empty(artifacts.SchemaStatements)
    Assert.Empty(artifacts.RefactorLog)
    Assert.True(Migration.isIdempotent artifacts.Plan)

// ===========================================================================
// Fail-loud — refuse before any write
// ===========================================================================

[<Fact>]
let ``6.D.1: a destructive drop refuses (RefusedByViolations) before any write`` () =
    match MigrationRun.preview DeclareNone (withExtraKind sampleCatalog) sampleCatalog with
    | FsResult.Error (RefusedByViolations [ SchemaLoss.DropKind _ ]) -> ()
    | other -> Assert.Fail(sprintf "expected RefusedByViolations, got %A" other)

[<Fact>]
let ``6.D.1: allowDrops lets the drop through`` () =
    let artifacts = MigrationRun.preview DeclareAll (withExtraKind sampleCatalog) sampleCatalog |> mustOk
    Assert.Equal(1, artifacts.Plan.Preview.Channels.RemovedKinds)

[<Fact>]
let ``6.D.1: a non-shape facet change refuses (RefusedBySchemaErrors)`` () =
    match MigrationRun.preview DeclareNone sampleCatalog nonShapeTarget with
    | FsResult.Error (RefusedBySchemaErrors errs) -> Assert.NotEmpty(errs)
    | other -> Assert.Fail(sprintf "expected RefusedBySchemaErrors, got %A" other)

// ===========================================================================
// The durable provenance loop — migrate records its episode; the FTC over the
// recorded chain reproduces B (the round-trip canary closes through 6.H)
// ===========================================================================

[<Fact>]
let ``6.D.1: record opens a timeline at genesis on the first migration`` () =
    withTempFile (fun path ->
        let artifacts = MigrationRun.preview DeclareAll sampleCatalog renamedTarget |> mustOk
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
        let artifacts = MigrationRun.preview DeclareNone sampleCatalog reshapedTarget |> mustOk
        let coord = EpisodeCoordinate.create (ver 1 "1.1.0") Environment.Dev (at "2026-06-08T09:00:00+00:00")
        let chain = MigrationRun.record path (tl "dev") coord (Some "reflog#1") (DataObservation.create 12 (Some "lsn:0x0C")) artifacts |> mustOk
        // The FTC over the recorded chain reproduces B (genesis ⊕ δ = target).
        let reconstructed = EpisodicLifecycle.reconstructLatestSchema chain |> mustOk
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between reshapedTarget reconstructed))
        // And it survives a reload from disk — durable provenance.
        let reloaded = LifecycleStore.load path |> mustOk
        let reReconstructed = EpisodicLifecycle.reconstructLatestSchema reloaded |> mustOk
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between reshapedTarget reReconstructed)))

// ===========================================================================
// The snapshot⊖snapshot loop — previewFromStore sources state A from the
// durable LifecycleStore (closes the latent emission→snapshot→diff calculus)
// ===========================================================================

[<Fact>]
let ``6.H: previewFromStore on a missing store is genesis — B is all Add, no losses`` () =
    withTempFile (fun path ->
        // No store at `path` → genesis: A = ∅, so every kind is Added, nothing
        // dropped — safe even under DeclareNone (the safe default).
        let artifacts = MigrationRun.previewFromStore path DeclareNone sampleCatalog |> mustOk
        Assert.True(Migration.isSafe artifacts.Plan)
        Assert.Equal(0, artifacts.Plan.Preview.Channels.RemovedKinds)
        Assert.True(artifacts.Plan.Preview.Channels.AddedKinds > 0))

[<Fact>]
let ``6.H: previewFromStore diffs B against the reconstructed prior snapshot (same δ as the two-model preview)`` () =
    withTempFile (fun path ->
        // Prior emission persisted: genesis at sampleCatalog (state A on disk).
        let coord = EpisodeCoordinate.create (ver 0 "1.0.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
        LifecycleStore.save path (EpisodicLifecycle.genesis (tl "dev") (Episode.ofSchema coord sampleCatalog))
        |> (function FsResult.Ok () -> () | FsResult.Error e -> Assert.Fail(sprintf "%A" e))
        // Preview the evolved model against the STORED prior — no hand-authored A.
        let fromStore = MigrationRun.previewFromStore path DeclareAll renamedTarget |> mustOk
        // It equals the two-model preview against the same A (the stored prior
        // reconstructs to sampleCatalog): the loop is faithful.
        let direct = MigrationRun.preview DeclareAll sampleCatalog renamedTarget |> mustOk
        Assert.Equal(direct.Plan.Preview.Norm, fromStore.Plan.Preview.Norm)
        Assert.Equal(1, fromStore.Plan.Preview.Channels.RenamedKinds))

[<Fact>]
let ``D4: previewFromStoreForcing true forces genesis even when the store exists`` () =
    withTempFile (fun path ->
        // A populated store whose reconstructed prior IS the target B: a
        // non-forced preview against it is the identity (zero displacement).
        let coord = EpisodeCoordinate.create (ver 0 "1.0.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
        LifecycleStore.save path (EpisodicLifecycle.genesis (tl "dev") (Episode.ofSchema coord sampleCatalog))
        |> (function FsResult.Ok () -> () | FsResult.Error e -> Assert.Fail(sprintf "%A" e))
        // Non-forced: A reconstructs to sampleCatalog (= B), so nothing is Added.
        let noForce = MigrationRun.previewFromStoreForcing false path DeclareNone sampleCatalog |> mustOk
        Assert.Equal(0, noForce.Plan.Preview.Channels.AddedKinds)
        // Forced (`--from empty`): A = ∅ regardless of the store, so EVERY kind is
        // Added and nothing is dropped — the from-scratch shape on a live store.
        let forced = MigrationRun.previewFromStoreForcing true path DeclareNone sampleCatalog |> mustOk
        Assert.True(Migration.isSafe forced.Plan)
        Assert.Equal(0, forced.Plan.Preview.Channels.RemovedKinds)
        Assert.True(forced.Plan.Preview.Channels.AddedKinds > 0))

// ===========================================================================
// The live-execute rename channel — sp_rename for physical table renames
// ===========================================================================

let private physicalRenameTarget : Catalog =
    let c' = { customer with Name = nm "Patron"; Physical = mkTableId (TableId.schemaText customer.Physical) "PATRON_TBL" }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ c'; order; country ] } ]

[<Fact>]
let ``live: renameStatements emits sp_rename + a logical-name re-bind for a physical table rename`` () =
    let diff = CatalogDiff.between sampleCatalog physicalRenameTarget
    let stmts = MigrationRun.renameStatements diff
    Assert.Equal(2, List.length stmts)
    // (1) the physical rename; (2) the V2.LogicalName re-bind to the new name.
    Assert.Contains("sp_rename", stmts.[0])
    Assert.Contains("PATRON_TBL", stmts.[0])
    Assert.Contains("sp_updateextendedproperty", stmts.[1])
    Assert.Contains("Projection.LogicalName", stmts.[1])
    Assert.Contains("Patron", stmts.[1])

let private columnRenameTarget : Catalog =
    let c' =
        { customer with
            Attributes =
                customer.Attributes
                |> List.mapi (fun i a ->
                    if i = 0 then { a with Name = nm "RenamedCol"; Column = { a.Column with ColumnName = ColumnName.create "RENAMED_COL" |> Result.value } } else a) }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ c'; order; country ] } ]

[<Fact>]
let ``live: a column rename emits sp_rename COLUMN + a column-level logical re-bind`` () =
    let diff = CatalogDiff.between sampleCatalog columnRenameTarget
    let stmts = MigrationRun.renameStatements diff
    Assert.Equal(2, List.length stmts)
    // (1) the physical column rename; (2) the column-level V2.LogicalName re-bind.
    Assert.Contains("sp_rename", stmts.[0])
    Assert.Contains("'COLUMN'", stmts.[0])
    Assert.Contains("RENAMED_COL", stmts.[0])
    Assert.Contains("@level2type = N'COLUMN'", stmts.[1])
    Assert.Contains("RenamedCol", stmts.[1])

[<Fact>]
let ``live: a logical-only rename re-binds the logical name without an sp_rename`` () =
    // renamedTarget changes the logical Name but keeps customer's Physical.Table:
    // the physical object stays, but its V2.LogicalName binding must still update.
    let diff = CatalogDiff.between sampleCatalog renamedTarget
    let one = List.exactlyOne (MigrationRun.renameStatements diff)
    Assert.DoesNotContain("sp_rename", one)   // no physical rename
    Assert.Contains("sp_updateextendedproperty", one)
    Assert.Contains("Patron", one)

[<Fact>]
let ``6.D.1: record refuses a non-advancing version (NonMonotonic)`` () =
    withTempFile (fun path ->
        let artifacts = MigrationRun.preview DeclareAll sampleCatalog renamedTarget |> mustOk
        let coord0 = EpisodeCoordinate.create (ver 5 "1.5.0") Environment.Dev (at "2026-06-01T09:00:00+00:00")
        MigrationRun.record path (tl "dev") coord0 None (DataObservation.create 0 None) artifacts |> mustOk |> ignore
        // A second record at a NON-advancing ordinal must refuse.
        let coord1 = EpisodeCoordinate.create (ver 5 "1.5.1") Environment.Dev (at "2026-06-02T09:00:00+00:00")
        match MigrationRun.record path (tl "dev") coord1 None (DataObservation.create 0 None) artifacts with
        | FsResult.Error (NonMonotonic _) -> ()
        | other -> Assert.Fail(sprintf "expected NonMonotonic, got %A" other))

// ===========================================================================
// AC-P8 (P8.4) — the CLI's composed execute→record persists the episode. The
// record-leg seam (`recordVerified`) is what `runMigrateExecute` calls after a
// verified execute; here we exercise it directly (DB-free, since it takes the
// already-computed `MigrationOutcome`) and assert the store is durably written
// and reloads to B. This DISCRIMINATES: an execute that never records leaves
// the store absent, so `reconstructLatestSchema` cannot reproduce B.
// ===========================================================================

let private verifiedOutcome (source: Catalog) (target: Catalog) : MigrationOutcome =
    // DeclareAll: this helper records provenance for an *already-decided* verified
    // migration, so it declares every loss — a transition may narrow a column
    // (AC-G8 now refuses an undeclared narrowing), which is a valid declared
    // migration to record. The AC-P8 tests assert ordinal/FTC behavior, not the
    // loss-declaration gate (that is AC-G8/S11's job in SchemaMigrationEmitterTests).
    let artifacts = MigrationRun.preview DeclareAll source target |> mustOk
    // A Verified outcome: B' (Reconstructed) is the target B, so the
    // PhysicalSchema diff is empty (the execute round-trip succeeded).
    let sdiff = PhysicalSchema.diff (PhysicalSchema.ofCatalog target) (PhysicalSchema.ofCatalog target)
    { Artifacts = artifacts
      Reconstructed = target
      SchemaDiff = sdiff
      Verified = PhysicalSchema.isSchemaEqual sdiff }

[<Fact>]
let ``AC-P8: recordVerified persists a verified execute; the store reloads and reconstructs B`` () =
    withTempFile (fun path ->
        // The composed execute→record leg: a verified outcome is recorded, the
        // store is written to disk, and the FTC over the reloaded chain
        // reproduces B (the durable provenance the CLI's --lifecycle-store gives).
        let outcome = verifiedOutcome sampleCatalog reshapedTarget
        let chain =
            MigrationRun.recordVerified path (tl "dev") Environment.Dev
                (at "2026-06-08T09:00:00+00:00") (Some "reflog#1") (DataObservation.create 0 None) outcome
            |> mustOk
        Assert.Equal(1, EpisodicLifecycle.episodes chain |> List.length)
        // The file exists on disk — durable, not just in-memory.
        Assert.True(System.IO.File.Exists path)
        // Reload from disk and reconstruct: B' reproduces B (T16 through 6.H).
        let reloaded = LifecycleStore.load path |> mustOk
        let reReconstructed = EpisodicLifecycle.reconstructLatestSchema reloaded |> mustOk
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between reshapedTarget reReconstructed)))

[<Fact>]
let ``AC-P8: a second recordVerified appends at the next monotonic ordinal (timeline advances)`` () =
    withTempFile (fun path ->
        // First episode: genesis at sampleCatalog → reshapedTarget.
        MigrationRun.recordVerified path (tl "dev") Environment.Dev
            (at "2026-06-08T09:00:00+00:00") None (DataObservation.create 0 None)
            (verifiedOutcome sampleCatalog reshapedTarget)
        |> mustOk |> ignore
        // Second episode: reshapedTarget → renamedTarget. nextCoordinate derives
        // ordinal 1 from the store's head (ordinal 0); the append is monotone.
        let chain =
            MigrationRun.recordVerified path (tl "dev") Environment.Dev
                (at "2026-06-15T09:00:00+00:00") None (DataObservation.create 0 None)
                (verifiedOutcome reshapedTarget renamedTarget)
            |> mustOk
        Assert.Equal(2, EpisodicLifecycle.episodes chain |> List.length)
        Assert.Equal(1, Version.ordinal (Episode.version (EpisodicLifecycle.latest chain))))

[<Fact>]
let ``AC-P8: recordVerified refuses an UNVERIFIED outcome (the timeline only carries B'=B episodes)`` () =
    withTempFile (fun path ->
        let outcome = { verifiedOutcome sampleCatalog reshapedTarget with Verified = false }
        match MigrationRun.recordVerified path (tl "dev") Environment.Dev
                (at "2026-06-08T09:00:00+00:00") None (DataObservation.create 0 None) outcome with
        | FsResult.Error (NonMonotonic _) -> ()
        | other -> Assert.Fail(sprintf "expected refusal of an unverified outcome, got %A" other)
        // And nothing was written — an unverified run leaves no provenance.
        Assert.False(System.IO.File.Exists path))

// ---------------------------------------------------------------------------
// 6.A.7 — the migrate preview surfaces a Synthesized-key rename as a Warning
// (identity.synthesizedRenameUnstable) instead of silently re-keying it. A
// non-V2 (name-derived) source cannot thread identity across a rename; the
// drop + add is allowed under --allow-drops but the operator is warned.
// ---------------------------------------------------------------------------

[<Fact>]
let ``6.A.7: migrate preview surfaces identity.synthesizedRenameUnstable for a Synthesized-key rename`` () =
    let synthK (parts: string list) : SsKey = SsKey.synthesizedComposite "READSIDE_KIND" parts |> mustResultOk
    let attrK (parts: string list) : SsKey = SsKey.synthesizedComposite "READSIDE_ATTR" parts |> mustResultOk
    let at7 (k: SsKey) (col: string) (isPk: bool) : Attribute =
        { Attribute.create k (nm col) PrimitiveType.Integer with
            Column = ColumnRealization.create (col) (not isPk) |> Result.value; IsPrimaryKey = isPk; IsMandatory = isPk }
    let kindOf7 (kKey: SsKey) (table: string) : Kind =
        Kind.create kKey (nm table) (TableId.create "dbo" table |> mustResultOk)
            [ at7 (attrK [table; "ID"]) "ID" true; at7 (attrK [table; "BODY"]) "BODY" false ]
    let catOf7 (k: Kind) : Catalog =
        IRBuilders.mkCatalog [ IRBuilders.mkModule (synthK ["MOD"]) (nm "M") [ k ] ]
    let oldCat = catOf7 (kindOf7 (synthK ["dbo.T_OLD"]) "T_OLD")
    let newCat = catOf7 (kindOf7 (synthK ["dbo.T_NEW"]) "T_NEW")
    // allowDrops = true so the drop + add proceeds; the warning still surfaces.
    let artifacts = MigrationRun.preview DeclareAll oldCat newCat |> mustOk
    Assert.Contains(
        artifacts.SchemaDiagnostics,
        fun (e: DiagnosticEntry) -> e.Code = "identity.synthesizedRenameUnstable" && e.Severity = DiagnosticSeverity.Warning)

// ---------------------------------------------------------------------------
// 6.A.13 — engine-level CDC-silence: an UNCHANGED schema emits zero DDL. The
// empty CatalogDiff produces no ALTER statements AND no rename statements, so
// `execute` issues nothing against the DB — an idempotent redeploy churns no
// CDC. (This is V2's engine-level idempotence, not DacFx tool-level.)
// ---------------------------------------------------------------------------

[<Fact>]
let ``6.A.13: redeploying an unchanged schema emits zero DDL (engine-level CDC-silence)`` () =
    let artifacts = MigrationRun.preview DeclareNone sampleCatalog sampleCatalog |> mustOk
    Assert.Empty(artifacts.SchemaStatements)
    Assert.Empty(MigrationRun.renameStatements artifacts.Plan.Diff)
    Assert.True(Migration.isIdempotent artifacts.Plan)

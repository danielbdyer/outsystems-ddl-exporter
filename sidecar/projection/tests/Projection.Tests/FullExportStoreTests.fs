[<Xunit.Collection("Global-MutableState")>]
module Projection.Tests.FullExportStoreTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
// G3 — RefactorLogEntry / RefactorElementType (the bundle's `.refactorlog`
// operations the store-wire witnesses assert on).
open Projection.Targets.SSDT

/// Track W1-B (seam T2) — the **diff-vs-prior store leg** for `full-export`,
/// advancing protein cells X1 (P-1/P-2 load: diff-vs-prior + record) and X3
/// (SSIS publication bundle). PURE tests (no Docker / no SQL): they drive the
/// real `FullExportRun.executeWithStore` (the same Pipeline orchestration the
/// CLI's `runFullExport` consumes) against temp model + config files and a
/// temp `LifecycleStore` path, asserting on the returned `FullExportStoreLeg`
/// and the durable chain.
///
/// The **discriminating witness** (§7 HOLLOW register `no-diff-vs-prior`): if
/// the store is NOT read — every run treated as genesis — then a second
/// identical run re-emits a full genesis displacement, and the
/// "empty second displacement" assertion below fails. That is the test that
/// distinguishes a real diff-vs-prior from the pre-W1-B genesis-only behavior.

// ---------------------------------------------------------------------------
// Fixtures — a minimal V1 model, and an incremental variant (one added column)
// ---------------------------------------------------------------------------

/// One-entity, one-attribute model.
let private v1ModelOneColumn : string =
    """{
  "exportedAtUtc": "2026-06-03T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "User",
          "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id",
              "physicalName": "ID",
              "originalName": null,
              "dataType": "rtIdentifier",
              "length": null, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [], "indexes": [], "triggers": []
        }
      ]
    }
  ]
}"""

/// The same model with one added column (`Email`). The diff-vs-prior of the
/// one-column model → this model is exactly one added attribute.
let private v1ModelTwoColumns : string =
    """{
  "exportedAtUtc": "2026-06-03T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "User",
          "physicalName": "OSUSR_APPCORE_USER",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            {
              "name": "Id",
              "physicalName": "ID",
              "originalName": null,
              "dataType": "rtIdentifier",
              "length": null, "precision": null, "scale": null, "default": null,
              "isMandatory": true, "isIdentifier": true, "isAutoNumber": true,
              "isActive": true, "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            },
            {
              "name": "Email",
              "physicalName": "EMAIL",
              "originalName": null,
              "dataType": "rtText",
              "length": 200, "precision": null, "scale": null, "default": null,
              "isMandatory": false, "isIdentifier": false, "isAutoNumber": false,
              "isActive": true, "isReference": 0,
              "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null,
              "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0,
              "external_dbType": null, "physical_isPresentButInactive": 0
            }
          ],
          "relationships": [], "indexes": [], "triggers": []
        }
      ]
    }
  ]
}"""

let private writeTempJson (content: string) : string =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fes-model-%s.json" (Guid.NewGuid().ToString "N"))
    File.WriteAllText(path, content)
    path

let private writeTempConfig (modelPath: string) (outputDir: string) : string =
    let json =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" } }"""
            (modelPath.Replace("\\", "\\\\"))
            (outputDir.Replace("\\", "\\\\"))
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fes-config-%s.json" (Guid.NewGuid().ToString "N"))
    File.WriteAllText(path, json)
    path

let private tempOutputDir () : string =
    Path.Combine(Path.GetTempPath(), sprintf "fes-out-%s" (Guid.NewGuid().ToString "N"))

let private tempStorePath () : string =
    Path.Combine(Path.GetTempPath(), sprintf "fes-store-%s.json" (Guid.NewGuid().ToString "N"))

let private safeRm (dir: string) : unit =
    if Directory.Exists dir then
        try Directory.Delete(dir, recursive = true) with _ -> ()

let private safeDel (file: string) : unit =
    if File.Exists file then (try File.Delete file with _ -> ())

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private mustEmitOk (r: Microsoft.FSharp.Core.Result<'a, EmitError>) : 'a =
    match r with
    | Microsoft.FSharp.Core.Result.Ok v -> v
    | Microsoft.FSharp.Core.Result.Error e -> Assert.Fail(sprintf "%A" e); Unchecked.defaultof<'a>

let private mustStoreOk (r: Microsoft.FSharp.Core.Result<'a, LifecycleStoreError>) : 'a =
    match r with
    | Microsoft.FSharp.Core.Result.Ok v -> v
    | Microsoft.FSharp.Core.Result.Error e -> Assert.Fail(sprintf "%A" e); Unchecked.defaultof<'a>

let private tl : Timeline = Timeline.create "appcore" |> mustOk

/// Run a full-export with the store leg against `storePath`, asserting the run
/// succeeded; returns the store leg (which must be `Some` when a store path is
/// supplied). `at` advances per call so the recorded episodes are monotone.
let private runWithStore (configPath: string) (storePath: string) (atDay: int) : Compose.FullExportStoreLeg =
    let at = DateTimeOffset(2026, 6, atDay, 9, 0, 0, TimeSpan.Zero)
    let outcome, leg =
        FullExportRun.executeWithStore
            configPath None LogSink.Verbosity.Quiet Set.empty
            (Some storePath) tl Environment.Dev at
    match outcome with
    | FullExportRun.RunOutcome.Succeeded _ -> ()
    | other -> Assert.Fail(sprintf "expected Succeeded, got %A" other)
    match leg with
    | Some l -> l
    | None -> Assert.Fail "store path supplied but no FullExportStoreLeg returned"; Unchecked.defaultof<_>

let private episodeCount (storePath: string) : int =
    LifecycleStore.load storePath |> mustStoreOk |> EpisodicLifecycle.episodes |> List.length

// ===========================================================================
// X1.2 — diff-vs-prior discriminates genesis-vs-incremental (the witness)
// ===========================================================================

[<Fact>]
let ``X1.2: second identical full-export against the same store has an EMPTY displacement (diff-vs-prior, not genesis)`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let out1 = tempOutputDir ()
    let out2 = tempOutputDir ()
    let cfg1 = writeTempConfig modelPath out1
    let cfg2 = writeTempConfig modelPath out2
    let store = tempStorePath ()
    try
        // Run 1 — genesis: every kind is Add, so the displacement is non-empty.
        let leg1 = runWithStore cfg1 store 1
        Assert.False(CatalogDiff.isEmpty leg1.Displacement, "genesis displacement must be non-empty")
        Assert.Equal(1, episodeCount store)

        // Run 2 — IDENTICAL model, same store. The displacement of B ⊖ A where
        // A = the prior emission and B = the same model is EMPTY (zero net
        // change). THIS is the discriminator: a genesis-only (store-not-read)
        // implementation would re-emit the full genesis displacement here.
        let leg2 = runWithStore cfg2 store 2
        Assert.True(
            CatalogDiff.isEmpty leg2.Displacement,
            sprintf "second identical run must have an EMPTY displacement (diff-vs-prior); got norm=%d" leg2.Manifest.SchemaNorm)
        Assert.Equal(0, leg2.Manifest.SchemaNorm)

        // The store gains exactly ONE additional episode (no genesis re-open).
        Assert.Equal(2, episodeCount store)
    finally
        safeRm out1; safeRm out2; safeDel cfg1; safeDel cfg2; safeDel store; safeDel modelPath

// ===========================================================================
// X1.6 / X3.7 — incremental change is captured in the ChangeManifest
// ===========================================================================

[<Fact>]
let ``X1.6: adding a column makes the second displacement name exactly that one added attribute`` () =
    let model1 = writeTempJson v1ModelOneColumn
    let model2 = writeTempJson v1ModelTwoColumns
    let out1 = tempOutputDir ()
    let out2 = tempOutputDir ()
    let cfg1 = writeTempConfig model1 out1
    let cfg2 = writeTempConfig model2 out2
    let store = tempStorePath ()
    try
        let _leg1 = runWithStore cfg1 store 1
        let leg2 = runWithStore cfg2 store 2

        // The displacement names exactly one added attribute (the new Email
        // column) and nothing else moved.
        let channels = leg2.Manifest.Channels
        Assert.Equal(1, channels.AddedAttributes)
        Assert.Equal(0, channels.RemovedAttributes)
        Assert.Equal(0, channels.RenamedAttributes)
        Assert.Equal(0, channels.ChangedAttributes)
        Assert.Equal(0, channels.AddedKinds)
        Assert.Equal(0, channels.RemovedKinds)
        Assert.Equal(0, channels.RenamedKinds)
        Assert.Equal(1, leg2.Manifest.SchemaNorm)
        Assert.False(CatalogDiff.isEmpty leg2.Displacement, "an added column is a real displacement")
        Assert.Equal(2, episodeCount store)
    finally
        safeRm out1; safeRm out2; safeDel cfg1; safeDel cfg2; safeDel store; safeDel model1; safeDel model2

// ===========================================================================
// X3.7 — rename accumulates in the cumulative refactorlog (dedup by OperationKey)
// ===========================================================================

[<Fact>]
let ``X3.7: an idempotent re-export does not grow the accumulated refactorlog (dedup by OperationKey)`` () =
    // Two identical runs: neither performs a rename, so the accumulated
    // refactorlog stays empty across both — the cumulative document never
    // double-counts an operation it already recorded.
    let modelPath = writeTempJson v1ModelOneColumn
    let out1 = tempOutputDir ()
    let out2 = tempOutputDir ()
    let cfg1 = writeTempConfig modelPath out1
    let cfg2 = writeTempConfig modelPath out2
    let store = tempStorePath ()
    try
        let leg1 = runWithStore cfg1 store 1
        let leg2 = runWithStore cfg2 store 2
        // No renames in either model, so the cumulative log is empty both times.
        Assert.Empty(leg1.AccumulatedRefactorLog)
        Assert.Empty(leg2.AccumulatedRefactorLog)
    finally
        safeRm out1; safeRm out2; safeDel cfg1; safeDel cfg2; safeDel store; safeDel modelPath

// ===========================================================================
// Genesis path stays byte-identical: no store ⇒ no leg, no store file
// ===========================================================================

[<Fact>]
let ``no --lifecycle-store ⇒ no store leg and no episode file (byte-identical genesis)`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    try
        let at = DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
        let outcome, leg =
            FullExportRun.executeWithStore
                cfg None LogSink.Verbosity.Quiet Set.empty
                None tl Environment.Dev at
        match outcome with
        | FullExportRun.RunOutcome.Succeeded _ -> ()
        | other -> Assert.Fail(sprintf "expected Succeeded, got %A" other)
        Assert.True(leg.IsNone, "no store path ⇒ no FullExportStoreLeg")
        // The genesis emission still landed.
        Assert.True(File.Exists(Path.Combine(outDir, Compose.ArtifactPath.json)), "projection.json must land")
    finally
        safeRm outDir; safeDel cfg; safeDel modelPath

// ===========================================================================
// X3.T1 / X3.T2 — the publication bundle reconstructs the latest schema, and
// an intermediate prior state, from genesis (the SSIS consumer FTC)
// ===========================================================================

[<Fact>]
let ``X3.T1: the recorded chain reconstructs the latest schema from genesis (fresh consumer FTC)`` () =
    // Two episodes: genesis (one column), then incremental (two columns). A
    // fresh consumer loading ONLY the store reconstructs the latest schema
    // from genesis + the per-edge deltas (reconstructLatestSchema).
    let model1 = writeTempJson v1ModelOneColumn
    let model2 = writeTempJson v1ModelTwoColumns
    let out1 = tempOutputDir ()
    let out2 = tempOutputDir ()
    let cfg1 = writeTempConfig model1 out1
    let cfg2 = writeTempConfig model2 out2
    let store = tempStorePath ()
    try
        let _leg1 = runWithStore cfg1 store 1
        let leg2 = runWithStore cfg2 store 2

        // A FRESH consumer: reload the bundle's recorded chain from disk and
        // reconstruct the latest schema from genesis.
        let reloaded = LifecycleStore.load store |> mustStoreOk
        Assert.Equal(2, EpisodicLifecycle.episodes reloaded |> List.length)
        let reconstructed = EpisodicLifecycle.reconstructLatestSchema reloaded |> mustEmitOk
        // The reconstructed latest schema equals the recorded latest episode's
        // schema (the FTC round-trip) — and it is the two-column model (the
        // chain integrated the incremental displacement, not a re-genesis).
        let latest = EpisodicLifecycle.latest reloaded |> Episode.schema
        // The net displacement reconstructed → stored-latest must be empty (the
        // reconstruction reproduces the stored latest).
        let netToReconstructed = CatalogDiff.between reconstructed latest
        Assert.True(CatalogDiff.isEmpty netToReconstructed, "reconstructLatestSchema must reproduce the stored latest schema")

        // The intermediate prior state is also reconstructible: the second
        // leg's displacement carried the genesis (one-column) schema forward by
        // exactly the added column.
        Assert.Equal(1, leg2.Manifest.Channels.AddedAttributes)
    finally
        safeRm out1; safeRm out2; safeDel cfg1; safeDel cfg2; safeDel store; safeDel model1; safeDel model2

[<Fact>]
let ``X3.T2 (adversarial): the accumulated refactorlog is a distinct bundle member a rename-edge reconstruction depends on`` () =
    // Reconstruction of an intermediate state across a rename edge depends on
    // the accumulated refactorlog (DacFx applies sp_rename, not DROP+ADD). The
    // witness: the store leg surfaces the accumulated log as a first-class
    // bundle member (not derivable from the CREATE files alone), so a bundle
    // that omitted it would be missing the only evidence that preserves data
    // across a rename. Here no rename occurs, so the log is empty — but its
    // presence as a distinct member is what a fresh consumer needs.
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    let store = tempStorePath ()
    try
        let leg = runWithStore cfg store 1
        // The accumulated refactorlog is a distinct bundle member (here empty,
        // because no rename occurred) — its presence in the leg is what a fresh
        // consumer needs; a bundle omitting it cannot reconstruct rename edges.
        Assert.NotNull(box leg.AccumulatedRefactorLog)
        Assert.Empty(leg.AccumulatedRefactorLog)
        // And the CREATE bundle landed independently (the schema-state plane).
        Assert.True(File.Exists(Path.Combine(outDir, Compose.ArtifactPath.json)))
    finally
        safeRm outDir; safeDel cfg; safeDel store; safeDel modelPath

// ===========================================================================
// NM-33 — `Episode.withProvenance` is wired at the episode-record site, so the
// recorded episode (and the ChangeManifest of its edge) carries the run's
// §5.5 applied-transforms overlay enumeration instead of the dead `[]` default.
// ===========================================================================

// ===========================================================================
// G3 (DECISIONS 2026-07-16) — the accumulated `.refactorlog` is a BUNDLE
// artifact. A store-threaded run writes `ProjectionCatalog.refactorlog`
// (deployed vocabulary, rendered at the episode's `At`) inside the atomic
// bundle and reports it; a store-less run writes nothing (byte-identical
// genesis). With `emission.sqlproj: true` the project carries the matching
// `RefactorLog` item exactly when the file exists.
// ===========================================================================

let private writeTempConfigSqlproj (modelPath: string) (outputDir: string) : string =
    let json =
        sprintf
            """{ "model": { "path": "%s" }, "output": { "dir": "%s" }, "emission": { "sqlproj": true } }"""
            (modelPath.Replace("\\", "\\\\"))
            (outputDir.Replace("\\", "\\\\"))
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fes-config-%s.json" (Guid.NewGuid().ToString "N"))
    File.WriteAllText(path, json)
    path

/// `runWithStore` returning the report too (the G3 witnesses assert on
/// `report.Paths`).
let private runWithStoreReport (configPath: string) (storePath: string) (atDay: int) : Compose.RunReport * Compose.FullExportStoreLeg =
    let at = DateTimeOffset(2026, 6, atDay, 9, 0, 0, TimeSpan.Zero)
    let outcome, leg =
        FullExportRun.executeWithStore
            configPath None LogSink.Verbosity.Quiet Set.empty
            (Some storePath) tl Environment.Dev at
    match outcome, leg with
    | FullExportRun.RunOutcome.Succeeded (report, _), Some l -> report, l
    | other, _ -> Assert.Fail(sprintf "expected Succeeded with a store leg, got %A" other); Unchecked.defaultof<_>

[<Fact>]
let ``G3: a store-threaded run writes ProjectionCatalog.refactorlog inside the bundle and reports it`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    let store = tempStorePath ()
    try
        let report, leg = runWithStoreReport cfg store 1
        let refactorPath = Path.Combine(outDir, Compose.ArtifactPath.refactorLog)
        Assert.True(File.Exists refactorPath, "the store-threaded bundle carries ProjectionCatalog.refactorlog")
        Assert.Contains(refactorPath, report.Paths)
        // Genesis: no prior, no renames — the truthful EMPTY document (the
        // presence contract is store-threaded ⟺ file exists, not non-empty).
        let xml = File.ReadAllText refactorPath
        Assert.Contains("<Operations", xml)
        Assert.DoesNotContain("<Operation ", xml)
        Assert.Empty(leg.AccumulatedRefactorLog)
    finally
        safeRm outDir; safeDel cfg; safeDel store; safeDel modelPath

[<Fact>]
let ``G3: a store-less run writes NO refactorlog (byte-identical genesis bundle)`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    try
        let at = DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
        let outcome, _ =
            FullExportRun.executeWithStore
                cfg None LogSink.Verbosity.Quiet Set.empty
                None tl Environment.Dev at
        match outcome with
        | FullExportRun.RunOutcome.Succeeded (report, _) ->
            let refactorPath = Path.Combine(outDir, Compose.ArtifactPath.refactorLog)
            Assert.False(File.Exists refactorPath, "a store-less bundle carries no refactorlog")
            Assert.DoesNotContain(refactorPath, report.Paths)
        | other -> Assert.Fail(sprintf "expected Succeeded, got %A" other)
    finally
        safeRm outDir; safeDel cfg; safeDel modelPath

[<Fact>]
let ``G3: with emission.sqlproj the store-threaded project carries the RefactorLog item; store-less carries none`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outWith = tempOutputDir ()
    let outWithout = tempOutputDir ()
    let cfgWith = writeTempConfigSqlproj modelPath outWith
    let cfgWithout = writeTempConfigSqlproj modelPath outWithout
    let store = tempStorePath ()
    try
        let _ = runWithStoreReport cfgWith store 1
        let sqlprojWith = File.ReadAllText(Path.Combine(outWith, Compose.ArtifactPath.sqlproj))
        Assert.Contains("RefactorLog", sqlprojWith)
        Assert.Contains(Compose.ArtifactPath.refactorLog, sqlprojWith)
        let at = DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero)
        let outcome, _ =
            FullExportRun.executeWithStore
                cfgWithout None LogSink.Verbosity.Quiet Set.empty
                None tl Environment.Dev at
        match outcome with
        | FullExportRun.RunOutcome.Succeeded _ ->
            let sqlprojWithout = File.ReadAllText(Path.Combine(outWithout, Compose.ArtifactPath.sqlproj))
            Assert.DoesNotContain("RefactorLog", sqlprojWithout)
        | other -> Assert.Fail(sprintf "expected Succeeded, got %A" other)
    finally
        safeRm outWith; safeRm outWithout; safeDel cfgWith; safeDel cfgWithout; safeDel store; safeDel modelPath

/// The PRIOR emission's schema, hand-authored with the SAME synthesized
/// SsKeys the JSON reader derives for `v1ModelOneColumn` (module `AppCore`,
/// entity `User`) but an OLDER logical name (`OldUser`). The store is the
/// identity carrier (CatalogCodec persists SsKeys), so seeding it with this
/// genesis simulates exactly what an identity-stable source produces across
/// a rename — file-sourced models alone cannot thread identity across a
/// rename (name-derived synthesized keys; the 6.A.7 limitation).
let private priorSchemaOldUser () : Catalog =
    let kindKey = SsKey.synthesizedComposite "OS_KIND" [ "AppCore"; "User" ] |> mustOk
    let attrKey = SsKey.synthesizedComposite "OS_ATTR" [ "AppCore"; "User"; "Id" ] |> mustOk
    let moduleKey = SsKey.synthesized "OS_MOD" "AppCore" |> mustOk
    let nameOf (s: string) = Name.create s |> mustOk
    let idAttr =
        { Attribute.create attrKey (nameOf "Id") Integer with
            Column = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true
            IsMandatory = true
            IsIdentity = true }
    let kind =
        Kind.create kindKey (nameOf "OldUser")
            (TableId.create "dbo" "OSUSR_APPCORE_USER" |> mustOk)
            [ idAttr ]
    { Modules =
        [ { SsKey = moduleKey; Name = nameOf "AppCore"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]
      Sequences = [] }

[<Fact>]
let ``G3: a rename across episodes lands in the bundle's refactorlog in DEPLOYED vocabulary, agreeing with the leg`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    let store = tempStorePath ()
    try
        // Seed the prior: one genesis episode whose kind is the SAME identity
        // (SsKey) under the OLD logical name `OldUser`.
        let priorAt = DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
        let coordinate =
            EpisodeCoordinate.create (Version.create 0 "v0" |> mustOk) Environment.Dev priorAt
        let genesis =
            Episode.create coordinate (priorSchemaOldUser ()) Profile.empty None DataObservation.empty
        LifecycleStore.save store (EpisodicLifecycle.genesis tl genesis) |> mustStoreOk
        // Run the full-export: the model names the kind `User` — same SsKey,
        // new logical name — a RENAME the deployed estate must sp_rename.
        let report, leg = runWithStoreReport cfg store 2
        // The leg's accumulated log carries exactly the table rename.
        Assert.Equal(1, List.length leg.AccumulatedRefactorLog)
        let entry = List.head leg.AccumulatedRefactorLog
        Assert.Equal(SqlTable, entry.ElementType)
        Assert.Equal("[dbo].[OldUser]", entry.ElementName)
        Assert.Equal("User", entry.NewName)
        // The bundle's document exists, is reported, and speaks the SAME
        // operations (file ⇔ leg agreement — the hydrated-plane bundle
        // derivation and the read-plane episode derivation agree).
        let refactorPath = Path.Combine(outDir, Compose.ArtifactPath.refactorLog)
        Assert.True(File.Exists refactorPath)
        Assert.Contains(refactorPath, report.Paths)
        let xml = File.ReadAllText refactorPath
        Assert.Contains(sprintf "Key=\"%s\"" (entry.OperationKey.ToString "D"), xml)
        Assert.Contains("Value=\"[dbo].[OldUser]\"", xml)
        Assert.Contains("Value=\"User\"", xml)
        // And the recorded chain now carries two episodes (the record phase
        // still ran, after the bundle).
        Assert.Equal(2, episodeCount store)
    finally
        safeRm outDir; safeDel cfg; safeDel store; safeDel modelPath

[<Fact>]
let ``G3: the accumulated document is CUMULATIVE — a later no-change run still carries the rename`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let out2 = tempOutputDir ()
    let out3 = tempOutputDir ()
    let cfg2 = writeTempConfig modelPath out2
    let cfg3 = writeTempConfig modelPath out3
    let store = tempStorePath ()
    try
        let priorAt = DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
        let coordinate =
            EpisodeCoordinate.create (Version.create 0 "v0" |> mustOk) Environment.Dev priorAt
        let genesis =
            Episode.create coordinate (priorSchemaOldUser ()) Profile.empty None DataObservation.empty
        LifecycleStore.save store (EpisodicLifecycle.genesis tl genesis) |> mustStoreOk
        let _, leg2 = runWithStoreReport cfg2 store 2
        let _, leg3 = runWithStoreReport cfg3 store 3
        // Run 3's displacement is empty (same model), but the ACCUMULATED
        // document still carries the OldUser→User operation — the cumulative
        // contract (AC-P6): DacFx sp_renames any source older than latest.
        Assert.Equal(1, List.length leg3.AccumulatedRefactorLog)
        Assert.Equal(
            (List.head leg2.AccumulatedRefactorLog).OperationKey,
            (List.head leg3.AccumulatedRefactorLog).OperationKey)
        let xml3 = File.ReadAllText(Path.Combine(out3, Compose.ArtifactPath.refactorLog))
        Assert.Contains("Value=\"[dbo].[OldUser]\"", xml3)
    finally
        safeRm out2; safeRm out3; safeDel cfg2; safeDel cfg3; safeDel store; safeDel modelPath

[<Fact>]
let ``NM-33: the recorded episode carries the run's AppliedTransforms (withProvenance is wired, not the dead [] default)`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    let store = tempStorePath ()
    try
        // A genesis full-export against a store. Before NM-33 the recorded
        // episode defaulted to `AppliedTransforms = []` (withProvenance had zero
        // production callers), so the ChangeManifest's AppliedTransforms — and
        // the durable episode's — were ALWAYS empty. With the wiring live, the
        // composed run's per-artifact overlay enumeration (at minimum the
        // skeleton `None` rows) threads onto the episode.
        let leg = runWithStore cfg store 1

        // The change manifest of the genesis edge now carries the overlay
        // enumeration — non-empty for any run that emitted artifacts.
        Assert.NotEmpty(leg.Manifest.AppliedTransforms)

        // And it SURVIVES the store round-trip onto the durable episode (the
        // NM-34 durability the next sub-step guarantees; asserted here so the
        // provenance is proven live end-to-end, not just on the in-memory edge).
        let recorded =
            LifecycleStore.load store
            |> mustStoreOk
            |> EpisodicLifecycle.latest
        Assert.NotEmpty(recorded.AppliedTransforms)
        Assert.Equal<(SsKey * OverlayAxis option) list>(
            leg.Manifest.AppliedTransforms, recorded.AppliedTransforms)
    finally
        safeRm outDir; safeDel cfg; safeDel store; safeDel modelPath

// ===========================================================================
// The publish spine (2026-07-02) — the store leg is a DECLARED stage, so the
// live board covers the whole run (before this, the board hit its done-frame
// while the store leg was still working). Wire assertions ride the same
// harness; the board law is R1e — the stored stream reconstructs the same
// terminal board the live subscriber built.
// ===========================================================================

let private captureEnvelopes (body: unit -> unit) : string list =
    let sw = new StringWriter()
    LogSink.withWriter sw (fun () -> body ())
    sw.ToString().Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList

[<Fact>]
let ``publish spine: a store-bearing run brackets the store leg on the wire, after the emission arc`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    let store = tempStorePath ()
    try
        let at = DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
        let mutable outcome = FullExportRun.RunOutcome.Aborted (exn "unset")
        let lines =
            captureEnvelopes (fun () ->
                let o, _ =
                    FullExportRun.executeWithStore
                        cfg None LogSink.Verbosity.Quiet Set.empty (Some store) tl Environment.Dev at
                outcome <- o)
        (match outcome with
         | FullExportRun.RunOutcome.Succeeded _ -> ()
         | other -> Assert.Fail(sprintf "expected Succeeded, got %A" other))
        let idxOf (needle: string) =
            match lines |> List.tryFindIndex (fun l -> l.Contains needle) with
            | Some i -> i
            | None -> Assert.Fail(sprintf "no envelope line contains %s" needle); -1
        // the store leg opens AFTER the emission arc closed (a post-root stage)
        Assert.True(idxOf "\"store.started\"" > idxOf "\"emit.started\"")
        // and it CLOSES — the summary.stageCompleted for the store stage rides
        Assert.True(lines |> List.exists (fun l -> l.Contains "summary.stageCompleted" && l.Contains "\"store\""))

        // R1e — the stored stream, seeded from the store-bearing publish spine,
        // reconstructs a TERMINAL board: every seeded line (extract, profile,
        // emit, store) closed. This is the exact projection the live board folds.
        let board =
            Projection.Cli.Watch.boardOfStored
                (Projection.Cli.Watch.seededOf (Spines.publishWith true false))
                lines
        Assert.True(Projection.Cli.Watch.isTerminal board, "the store-bearing spine must close every seeded line")
    finally
        safeRm outDir; safeDel cfg; safeDel store; safeDel modelPath

[<Fact>]
let ``publish spine: a store-less run emits NO store stage events — the bare pipeline spine stays byte-identical`` () =
    let modelPath = writeTempJson v1ModelOneColumn
    let outDir = tempOutputDir ()
    let cfg = writeTempConfig modelPath outDir
    try
        let at = DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
        let lines =
            captureEnvelopes (fun () ->
                FullExportRun.executeWithStore
                    cfg None LogSink.Verbosity.Quiet Set.empty None tl Environment.Dev at
                |> ignore)
        Assert.False(lines |> List.exists (fun l -> l.Contains "store.started"))
        // the bare spine still reaches terminal off the same stream
        let board =
            Projection.Cli.Watch.boardOfStored
                (Projection.Cli.Watch.seededOf (Spines.publishWith false false))
                lines
        Assert.True(Projection.Cli.Watch.isTerminal board)
    finally
        safeRm outDir; safeDel cfg; safeDel modelPath

[<Fact>]
let ``publish spine: publishWith declares the dispatch-selected arcs — bare, store, load, both`` () =
    let keys (spine: RunSpine) = RunSpine.keys spine
    Assert.Equal<string list>([ "extract"; "profile"; "emit" ], keys (Spines.publishWith false false))
    Assert.Equal<string list>([ "extract"; "profile"; "emit"; "store" ], keys (Spines.publishWith true false))
    Assert.Equal<string list>([ "extract"; "profile"; "emit"; "seed-load" ], keys (Spines.publishWith false true))
    Assert.Equal<string list>([ "extract"; "profile"; "emit"; "store"; "seed-load" ], keys (Spines.publishWith true true))
    // the umbrella root stays the pipeline (never a watched line)
    Assert.Equal(Some "pipeline", RunSpine.rootKey (Spines.publishWith true true))

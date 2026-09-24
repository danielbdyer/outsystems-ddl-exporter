namespace Projection.Tests

// Coverage for live source-environment profile acquisition on the
// operator path (`Compose.runWithConfig`). Before this slice the
// pipeline hardcoded `Profile.empty` at two seams — the run-level
// profile AND the manifest-emitter call inside `projectFromChainWithState`
// — so `ManifestEmitter.Manifest.ColumnProfiles` was always empty.
//
// Three surfaces are exercised:
//   1. Pure manifest threading (no Docker): a `Profile` carrying numeric
//      moments flows through `Compose.projectWithState` into
//      `Outputs.Manifest.ColumnProfiles`. `Profile.empty` yields empty.
//   2. `runWithConfig` base case + named failure (no Docker): the default
//      provider carries `Profile.empty`; `provider = "live"` without an
//      out-of-band connection is a named diagnostic, not a silent empty.
//   3. Live end-to-end (Docker): `runWithConfig` with `provider = "live"`
//      against an accessible source database populates
//      `RunReport.Manifest.ColumnProfiles` from real probed evidence.
//
// The model fixtures use the realistic `ossys_EntityAttr.Type` runtime
// form (`rtIdentifier` / `rtInteger`) the OSSYS reader normalizes — the
// type comes back prefixed from the source, and the live path must
// survive the same normalization the snapshot path does.

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

module private ProfileWiringFixtures =

    let value (r: Result<'a>) : 'a = Result.value r

    let private utcNow : DateTimeOffset =
        DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

    let succeededProbe (sampleSize: int64) : ProbeStatus =
        ProbeStatus.create utcNow sampleSize Succeeded |> value

    /// A numeric distribution carrying `StatisticalMoments`, keyed to a
    /// real attribute of `Fixtures.sampleCatalog` so the manifest's
    /// `tryFindNumeric` join resolves.
    let numericProfileFor (attributeKey: SsKey) (mean: decimal) (stdDev: decimal) : Profile =
        let dist =
            NumericDistribution.create attributeKey 0M 25M 50M 75M 95M 99M 100M 100L (succeededProbe 100L)
            |> value
        let moments = StatisticalMoments.create mean stdDev |> value
        let enriched = NumericDistribution.withMoments moments dist |> value
        { Profile.empty with Distributions = [ AttributeDistribution.Numeric enriched ] }

    // -- runWithConfig harness (mirrors FullExportCliTests' temp-file shape) --

    /// A V1 `osm_model.json` with one non-static entity carrying an
    /// identifier PK + one `rtInteger` metric column. The runtime-type
    /// prefix (`rt`) is the realistic form `ossys_EntityAttr.Type` returns;
    /// `CatalogReader.normalizeAttributeType` strips it.
    let metricModelJson : string =
        """{
  "exportedAtUtc": "2026-05-24T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "Metrics",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "Metric",
          "physicalName": "OSUSR_PROF_METRIC",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null, "dataType": "rtIdentifier", "length": null, "precision": null, "scale": null, "default": null, "isMandatory": true, "isIdentifier": true, "isAutoNumber": true, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 },
            { "name": "Score", "physicalName": "SCORE", "originalName": null, "dataType": "rtInteger", "length": null, "precision": null, "scale": null, "default": null, "isMandatory": false, "isIdentifier": false, "isAutoNumber": false, "isActive": true, "isReference": 0, "refEntityId": null, "refEntity_name": null, "refEntity_physicalName": null, "reference_deleteRuleCode": null, "reference_hasDbConstraint": 0, "external_dbType": null, "physical_isPresentButInactive": 0 }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": []
        }
      ]
    }
  ]
}"""

    /// CREATE TABLE matching the parsed catalog's physical coordinates
    /// (`dbo.OSUSR_PROF_METRIC`, columns `ID` / `SCORE`).
    let createTableSql : string =
        "CREATE TABLE [dbo].[OSUSR_PROF_METRIC] (" +
        "[ID] BIGINT NOT NULL PRIMARY KEY, " +
        "[SCORE] INT NULL);"

    /// Ten rows of numeric `SCORE` evidence — comfortably above the
    /// `NumericDistribution` sample-size floor (5) so the profiler's
    /// AVG / STDEVP moments survive the smart constructor.
    let seedSql : string =
        "INSERT INTO [dbo].[OSUSR_PROF_METRIC] ([ID], [SCORE]) VALUES " +
        "(1,10),(2,20),(3,30),(4,40),(5,50),(6,60),(7,70),(8,80),(9,90),(10,100);"

    let writeTempJson (content: string) : string =
        let path =
            Path.Combine(
                Path.GetTempPath(),
                sprintf "projection-profilewiring-%s.json" (Guid.NewGuid().ToString("N")))
        File.WriteAllText(path, content)
        path

    let tempOutputDir () : string =
        Path.Combine(
            Path.GetTempPath(),
            sprintf "projection-profilewiring-out-%s" (Guid.NewGuid().ToString("N")))

    let writeConfig (modelPath: string) (outputDir: string) (provider: string) : string =
        let json =
            sprintf
                """{ "model": { "path": "%s" }, "output": { "dir": "%s" }, "profiler": { "provider": "%s" } }"""
                (modelPath.Replace("\\", "\\\\"))
                (outputDir.Replace("\\", "\\\\"))
                provider
        writeTempJson json

    let cleanup (modelPath: string) (cfgPath: string) (outDir: string) : unit =
        try File.Delete modelPath with _ -> ()
        try File.Delete cfgPath with _ -> ()
        try if Directory.Exists outDir then Directory.Delete(outDir, true) with _ -> ()

    let runConfigSync (cfg: Config.Config) : Result<Compose.RunReport> =
        (Compose.runWithConfig cfg).GetAwaiter().GetResult()


// ---------------------------------------------------------------------------
// Pure manifest-threading + runWithConfig base/failure cases (no Docker).
// ---------------------------------------------------------------------------

module ProfileManifestThreadingTests =

    open ProfileWiringFixtures

    [<Fact>]
    let ``projectWithState threads a moment-bearing Profile into Manifest.ColumnProfiles`` () =
        let profile = numericProfileFor Fixtures.customerTenantKey 50M 12M
        let outputs, _ =
            Compose.projectWithState
                Policy.empty profile
                EmissionFolders.empty TransformGroups.empty Fixtures.sampleCatalog
        Assert.NotEmpty(outputs.Manifest.ColumnProfiles)
        Assert.Contains(
            outputs.Manifest.ColumnProfiles,
            fun (cp: ManifestEmitter.ColumnProfileSummary) -> cp.Mean = 50M && cp.StdDev = 12M)

    [<Fact>]
    let ``projectWithState with Profile.empty yields empty Manifest.ColumnProfiles (base case)`` () =
        let outputs, _ =
            Compose.projectWithState
                Policy.empty Profile.empty
                EmissionFolders.empty TransformGroups.empty Fixtures.sampleCatalog
        Assert.Empty(outputs.Manifest.ColumnProfiles)

    [<Fact>]
    let ``runWithConfig default provider carries Profile.empty (empty ColumnProfiles, no connection needed)`` () =
        let modelPath = writeTempJson metricModelJson
        let outDir = tempOutputDir ()
        let cfgPath = writeConfig modelPath outDir "fixture"
        try
            match Config.fromFile cfgPath with
            | Error es -> failwithf "config parse failed: %A" es
            | Ok cfg ->
                match runConfigSync cfg with
                | Error es -> failwithf "runWithConfig failed: %A" es
                | Ok report -> Assert.Empty(report.Manifest.ColumnProfiles)
        finally
            cleanup modelPath cfgPath outDir

    [<Fact>]
    let ``NM-34b: runWithConfig surfaces the READ source Catalog on RunReport.ReadCatalog`` () =
        // The read model is surfaced so the run boundary can hash its canonical
        // form into the live-path input digest. The fixture model has modules;
        // ReadCatalog must carry them (the model the run actually read).
        let modelPath = writeTempJson metricModelJson
        let outDir = tempOutputDir ()
        let cfgPath = writeConfig modelPath outDir "fixture"
        try
            match Config.fromFile cfgPath with
            | Error es -> failwithf "config parse failed: %A" es
            | Ok cfg ->
                match runConfigSync cfg with
                | Error es -> failwithf "runWithConfig failed: %A" es
                | Ok report ->
                    Assert.NotEmpty(report.ReadCatalog.Modules)
                    // The canonical serialization is non-empty and deterministic
                    // — the live-path model-digest input is well-formed.
                    let canonical = Projection.Targets.Json.CatalogCodec.serialize report.ReadCatalog
                    Assert.NotEqual<string>("", canonical)
        finally
            cleanup modelPath cfgPath outDir


// ---------------------------------------------------------------------------
// Env-var-sensitive cases: serialized in the Docker-SqlServer collection so
// they never race Deploy's warm-container read of the same env var. The
// failure case needs no Docker (it fails before opening a connection); it
// lives here only for env-var serialization.
// ---------------------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type RunWithConfigConnectionFailureTests() =

    [<Fact>]
    member _.``runWithConfig live provider without an out-of-band connection fails with a named diagnostic`` () =
        let modelPath = ProfileWiringFixtures.writeTempJson ProfileWiringFixtures.metricModelJson
        let outDir = ProfileWiringFixtures.tempOutputDir ()
        let cfgPath = ProfileWiringFixtures.writeConfig modelPath outDir "live"
        let prior = Environment.GetEnvironmentVariable Config.SourceConnectionStringEnvVar
        Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, null)
        try
            match Config.fromFile cfgPath with
            | Error es -> failwithf "config parse failed: %A" es
            | Ok cfg ->
                match ProfileWiringFixtures.runConfigSync cfg with
                | Ok _ -> failwith "expected a named failure when the live connection is absent"
                | Error es ->
                    Assert.Contains(es, fun (e: ValidationError) -> e.Code = "pipeline.profiler.connectionMissing")
        finally
            Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, prior)
            ProfileWiringFixtures.cleanup modelPath cfgPath outDir


[<Xunit.Collection("Docker-SqlServer")>]
type RunWithConfigLiveProfileTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``runWithConfig live provider populates Manifest.ColumnProfiles from the source database`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP runwithconfig-live-profile: Docker daemon not reachable."
        else
            (fixture.WithEphemeralDatabase "RunCfgLive" (fun cnn perDbConn ->
                task {
                    do! Deploy.executeBatch cnn ProfileWiringFixtures.createTableSql
                    do! Deploy.executeBatch cnn ProfileWiringFixtures.seedSql
                    let modelPath = ProfileWiringFixtures.writeTempJson ProfileWiringFixtures.metricModelJson
                    let outDir = ProfileWiringFixtures.tempOutputDir ()
                    let cfgPath = ProfileWiringFixtures.writeConfig modelPath outDir "live"
                    let prior = Environment.GetEnvironmentVariable Config.SourceConnectionStringEnvVar
                    Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, perDbConn)
                    try
                        match Config.fromFile cfgPath with
                        | Error es -> return failwithf "config parse failed: %A" es
                        | Ok cfg ->
                            let! report = Compose.runWithConfig cfg
                            match report with
                            | Error es -> return failwithf "runWithConfig failed: %A" es
                            | Ok r ->
                                Assert.NotEmpty(r.Manifest.ColumnProfiles)
                                // The profile was probed against the *physical*
                                // source (`OSUSR_PROF_METRIC` / `SCORE`); the
                                // manifest reports the *logical* emitted names
                                // (`Metric` / `Score`). The join holds across the
                                // naming morphism because `tryFindNumeric` keys on
                                // rename-invariant `SsKey`. Mean of 10..100 = 55.
                                Assert.Contains(
                                    r.Manifest.ColumnProfiles,
                                    fun (cp: ManifestEmitter.ColumnProfileSummary) ->
                                        cp.Column = "Score" && cp.Mean = 55M)
                    finally
                        Environment.SetEnvironmentVariable(Config.SourceConnectionStringEnvVar, prior)
                        ProfileWiringFixtures.cleanup modelPath cfgPath outDir
                })).GetAwaiter().GetResult()

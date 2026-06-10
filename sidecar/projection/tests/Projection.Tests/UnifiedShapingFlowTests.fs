[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.UnifiedShapingFlowTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.IRBuilders

/// THE_CONFIG_CONTROL_PLANE §6 (S3) — the discriminating slice for the
/// flow→shaping wiring. The payoff is that a daily `projection <flow>`
/// emission applies the unified config's shaping (`policy` / `overrides`
/// / `model.modules`). The empty-default config MASKS the riskiest seam
/// (the module filter), so this suite tests with a **non-empty
/// `model.modules`** scope and asserts it takes effect on BOTH the bundle
/// emission path (`Compose.projectWithConfig`) and the live/docker
/// emission path (`Deploy.runWithReadback` over the same overlay output).
/// If the seam were wrong (the module filter on only one path), one path
/// would be scoped and the other would not.
///
/// The dispatch (`Program.needCatalog`) routes the resolved catalog
/// through `Compose.applyModuleFilter` — the SINGLE shared seam — before
/// either emission entry runs, so the tests mirror that order: filter
/// first, then project / deploy.
///
/// **Emitted name = logical name.** `LogicalTableEmission` (an enabled
/// chain pass) substitutes `Kind.Physical.Table ← Name.value k.Name`, so
/// the emitted CREATE TABLE / readback names the LOGICAL entity. The
/// fixtures below give each module a distinctive logical name so the
/// scope probe is unambiguous on both the bundle and the deployed schema.

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun (e: ValidationError) -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkSsKey label = SsKey.synthesized "OS_TEST" label |> mustOk
let private mkName s = Name.create s |> mustOk

/// A PK-bearing, deploy-valid Kind whose LOGICAL name is `logicalEntity`
/// (the name the emitter renders — LogicalTableEmission). The physical
/// table seed is kept aligned for readability.
let private mkKind (logicalEntity: string) : Kind =
    let pk =
        { Attribute.create (mkSsKey (sprintf "ATTR_%s_ID" logicalEntity)) (mkName "Id") PrimitiveType.Integer with
            Column       = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true }
    Kind.create
        (mkSsKey (sprintf "KIND_%s" logicalEntity))
        (mkName logicalEntity)
        (TableId.create "dbo" (sprintf "OSUSR_%s" (logicalEntity.ToUpperInvariant())) |> mustOk)
        [ pk ]

let private mkActiveModule name kinds =
    { mkModule (mkSsKey (sprintf "MOD_%s" name)) (mkName name) kinds with IsActive = true }

/// Two modules — Sales (entity SalesCustomer) and Ops (entity OpsOrder).
/// A `model.modules` scope to a subset must keep the scoped module's
/// table and drop the other module's table.
let private twoModuleCatalog : Catalog =
    mkCatalog
        [ mkActiveModule "Sales" [ mkKind "SalesCustomer" ]
          mkActiveModule "Ops"   [ mkKind "OpsOrder" ] ]

/// A `Config.Config` shaping (the `cfg.Shaping` view) scoping `model.modules`
/// to just the named modules. Everything else is the lenient default.
let private shapingScopedTo (modules: string list) : Config.Config =
    { Config.defaultConfig with
        Model =
            { Config.defaultConfig.Model with
                Modules = modules |> List.map Config.ModuleSelector.Whole } }

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then
        true
    else
        printfn "SKIP %s: Docker daemon not reachable." label
        false

/// The scoped entities present in the emitted bundle SQL (case-insensitive,
/// matched on the logical entity names the emitter renders).
let private bundleEntities (outputs: Compose.Outputs) : Set<string> =
    let sql = (Compose.aggregateSsdt outputs.SsdtBundle).ToUpperInvariant()
    [ "SALESCUSTOMER"; "OPSORDER" ]
    |> List.filter (fun t -> sql.Contains(sprintf "[DBO].[%s]" t))
    |> Set.ofList

let private deployedEntities (deployed: Catalog) : Set<string> =
    Catalog.allKinds deployed
    |> List.map (fun k -> (TableId.tableText k.Physical).ToUpperInvariant())
    |> Set.ofList

// ---------------------------------------------------------------------------
// THE MANDATORY DISCRIMINATING TEST — non-empty model.modules on BOTH paths.
// ---------------------------------------------------------------------------

[<Fact>]
let ``S3: a model.modules scope narrows the BUNDLE emission to the scoped module's tables only`` () =
    let shaping = shapingScopedTo [ "Sales" ]
    // The shared seam (mirrors Program.needCatalog) — filter THEN project.
    let filtered = Compose.applyModuleFilter shaping twoModuleCatalog |> mustOk
    let outputs = Compose.projectWithConfig shaping filtered |> mustOk
    let entities = bundleEntities outputs
    Assert.Contains("SALESCUSTOMER", entities)
    Assert.DoesNotContain("OPSORDER", entities)

[<Fact>]
let ``S3: a model.modules scope narrows the DOCKER/live emission to the scoped module's tables only`` () =
    if skipIfNoDocker "model-modules-docker" then
        let shaping = shapingScopedTo [ "Sales" ]
        // Same shared seam, same overlay entry the DeployDocker arm uses
        // (Deploy.runFromCatalogWith routes through Compose.projectWithConfig).
        let filtered = Compose.applyModuleFilter shaping twoModuleCatalog |> mustOk
        let outputs = Compose.projectWithConfig shaping filtered |> mustOk
        let sql = Compose.aggregateSsdt outputs.SsdtBundle
        let result = (Deploy.runWithReadback sql).GetAwaiter().GetResult()
        Assert.True(result.Report.Ok, sprintf "deploy failed: %A" result.Report.Errors)
        match result.Reconstructed with
        | None -> Assert.Fail "expected a reconstructed catalog from readback"
        | Some deployed ->
            let tables = deployedEntities deployed
            Assert.Contains("SALESCUSTOMER", tables)
            Assert.DoesNotContain("OPSORDER", tables)

[<Fact>]
let ``S3: the bundle and docker paths agree under the same model.modules scope (the seam is shared)`` () =
    if skipIfNoDocker "model-modules-agreement" then
        let shaping = shapingScopedTo [ "Sales" ]
        let filtered = Compose.applyModuleFilter shaping twoModuleCatalog |> mustOk
        let outputs = Compose.projectWithConfig shaping filtered |> mustOk
        let bundleTables = bundleEntities outputs
        let sql = Compose.aggregateSsdt outputs.SsdtBundle
        let result = (Deploy.runWithReadback sql).GetAwaiter().GetResult()
        Assert.True(result.Report.Ok, sprintf "deploy failed: %A" result.Report.Errors)
        let deployedTables =
            match result.Reconstructed with
            | Some deployed -> deployedEntities deployed
            | None -> Set.empty
        // Both paths see exactly the scoped module's table — neither path
        // leaks the unscoped Ops table; the module filter is one seam.
        Assert.Equal<Set<string>>(bundleTables, deployedTables)

// ---------------------------------------------------------------------------
// The overlay-fires witness — an overrides.emissionFolders override produces
// a bundle artifact that DIFFERS from the skeleton (the overlay fired, not
// Policy.empty / the default-empty overrides).
// ---------------------------------------------------------------------------

[<Fact>]
let ``S3: an emissionFolders override makes the bundle artifact DIFFER from the skeleton`` () =
    let single = mkCatalog [ mkActiveModule "Sales" [ mkKind "SalesCustomer" ] ]
    // Remap Sales::SalesCustomer's SSDT file from the default
    // `Modules/Sales/` directory to an operator-named folder.
    let shaping =
        { Config.defaultConfig with
            Overrides =
                { Config.defaultConfig.Overrides with
                    EmissionFolders =
                        [ { Ref = { Module = "Sales"; Entity = "SalesCustomer" }
                            Folder = "Reference/Static" } ] } }
    let skeletonKeys =
        (Compose.projectWithConfig Config.defaultConfig single |> mustOk).SsdtBundle
        |> Map.toList |> List.map fst |> Set.ofList
    let shapedKeys =
        (Compose.projectWithConfig shaping single |> mustOk).SsdtBundle
        |> Map.toList |> List.map fst |> Set.ofList
    // The overlay fired: the bundle's file layout differs, and the operator
    // folder appears in the shaped bundle but not the skeleton.
    Assert.NotEqual<Set<string>>(skeletonKeys, shapedKeys)
    Assert.Contains(shapedKeys, fun (k: string) -> k.Contains "Reference/Static")
    Assert.DoesNotContain(skeletonKeys, fun (k: string) -> k.Contains "Reference/Static")

[<Fact>]
let ``S3: the empty-default shaping is byte-identical to the un-shaped project (empty-default invariant)`` () =
    let single = mkCatalog [ mkActiveModule "Sales" [ mkKind "SalesCustomer" ] ]
    // applyModuleFilter under the default config is the identity ...
    let filtered = Compose.applyModuleFilter Config.defaultConfig single |> mustOk
    Assert.Equal<Module list>(single.Modules, filtered.Modules)
    // ... and projectWithConfig under the default config equals the prior
    // `project EmissionPolicy.empty` body byte-for-byte.
    let shaped = Compose.projectWithConfig Config.defaultConfig filtered |> mustOk
    let baseline = Compose.project EmissionPolicy.empty single
    Assert.Equal<Map<string, string>>(baseline.SsdtBundle, shaped.SsdtBundle)

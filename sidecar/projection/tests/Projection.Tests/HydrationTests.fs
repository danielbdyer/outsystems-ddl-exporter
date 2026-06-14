module Projection.Tests.HydrationTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests

// WP6 step 4 (DECISIONS 2026-06-13) — the read-only hydration step. The live
// OSSYS stream cannot be exercised in this environment (no OSSYS source), so
// these witnesses cover the parts that don't need a connection: the PURE
// graft, the config-derived named-skip diagnostic, and the no-connection
// branches of `hydrateCatalog` (data-off and file-sourced ⇒ identity).

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun (e: ValidationError) -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_HYD" parts |> mustOk

let private mkName (s: string) : Name = Name.create s |> mustOk

let private idAttr (kindName: string) : Attribute =
    { Attribute.create (mkKey [kindName; "Id"]) (mkName "Id") Integer with
        Column = ColumnRealization.create "ID" false |> Result.value
        IsPrimaryKey = true
        IsMandatory = true }

/// A static-entity kind: the `Static` marker present, populations as given
/// (empty mirrors the OSSYS forward read's `Static []`).
let private staticKind (name: string) (rows: StaticRow list) : Kind =
    { Kind.create (mkKey [name]) (mkName name)
        (Fixtures.mkTableId "dbo" (sprintf "OSUSR_HYD_%s" (name.ToUpperInvariant())))
        [ idAttr name ]
      with Modality = [ Static rows ] }

/// A non-static kind (no `Static` marker) — hydration must never touch it.
let private plainKind (name: string) : Kind =
    Kind.create (mkKey [name]) (mkName name)
        (Fixtures.mkTableId "dbo" (sprintf "OSUSR_HYD_%s" (name.ToUpperInvariant())))
        [ idAttr name ]

let private mkCatalog (kinds: Kind list) : Catalog =
    IRBuilders.mkCatalog [ IRBuilders.mkModule (mkKey ["M"]) (mkName "M") kinds ]

// ---------------------------------------------------------------------------
// The pure graft.
// ---------------------------------------------------------------------------

[<Fact>]
let ``WP6 step 4: graftStaticPopulations fills a Static marker by SsKey`` () =
    let stat = staticKind "Country" []
    let catalog = mkCatalog [ stat ]
    let row : StaticRow = { Identifier = mkKey ["Country"; "Row"; "1"]; Values = Map.ofList [ mkName "Id", "1" ] }
    let hydrated = Hydration.graftStaticPopulations (Map.ofList [ stat.SsKey, [ row ] ]) catalog
    let hStat = Catalog.allKinds hydrated |> List.find (fun k -> k.SsKey = stat.SsKey)
    Assert.Equal (1, List.length (Kind.staticPopulations hStat))

[<Fact>]
let ``WP6 step 4: graftStaticPopulations never adds a Static marker to a non-static kind`` () =
    let plain = plainKind "Order"
    let catalog = mkCatalog [ plain ]
    let row : StaticRow = { Identifier = mkKey ["Order"; "Row"; "1"]; Values = Map.ofList [ mkName "Id", "1" ] }
    // Even though the map carries rows for the plain kind, it has no Static
    // marker, so the graft leaves it untouched (no marker minted).
    let hydrated = Hydration.graftStaticPopulations (Map.ofList [ plain.SsKey, [ row ] ]) catalog
    let hPlain = Catalog.allKinds hydrated |> List.find (fun k -> k.SsKey = plain.SsKey)
    Assert.False (hPlain.Modality |> List.exists (function Static _ -> true | _ -> false))

[<Fact>]
let ``WP6 step 4: graftStaticPopulations preserves kind order and other kinds`` () =
    let stat = staticKind "Country" []
    let plain = plainKind "Order"
    let catalog = mkCatalog [ stat; plain ]
    let row : StaticRow = { Identifier = mkKey ["Country"; "Row"; "1"]; Values = Map.ofList [ mkName "Id", "1" ] }
    let hydrated = Hydration.graftStaticPopulations (Map.ofList [ stat.SsKey, [ row ] ]) catalog
    let keys = Catalog.allKinds hydrated |> List.map (fun k -> k.SsKey)
    Assert.Equal<SsKey list> ([ stat.SsKey; plain.SsKey ], keys)

// ---------------------------------------------------------------------------
// The config-derived named-skip diagnostic.
// ---------------------------------------------------------------------------

let private withModel (ossys: string option) (path: string option) (cfg: Config.Config) : Config.Config =
    { cfg with Model = { cfg.Model with Ossys = ossys; Path = path } }

let private dataOff (cfg: Config.Config) : Config.Config =
    { cfg with
        Emission =
            { cfg.Emission with StaticSeeds = false; MigrationDependencies = false; Bootstrap = false } }

[<Fact>]
let ``WP6 step 4: diagnostics names the skip for a model.path source (no live OSSYS) with data on`` () =
    let cfg = Config.defaultConfig |> withModel None (Some "model.json")
    let ds = Hydration.diagnostics cfg
    Assert.Equal (1, List.length ds)
    Assert.Equal<string> ("data.hydration.skippedNoLiveSource", ds.Head.Code)
    Assert.Equal<DiagnosticSeverity> (DiagnosticSeverity.Warning, ds.Head.Severity)

[<Fact>]
let ``WP6 step 4: diagnostics is silent for an OSSYS source via an env: ref`` () =
    let cfg = Config.defaultConfig |> withModel (Some "env:OSSYS") None
    Assert.Empty (Hydration.diagnostics cfg)

[<Fact>]
let ``WP6 step 4: diagnostics is silent for an OSSYS source via a file: ref (the predominant form)`` () =
    // The skip keys on the PRESENCE of model.ossys, not its ref form — a
    // file: ossys ref hydrates exactly like an env: one (not deprecated).
    let cfg = Config.defaultConfig |> withModel (Some "file:/etc/ossys.conn") None
    Assert.Empty (Hydration.diagnostics cfg)

[<Fact>]
let ``WP6 step 4: diagnostics is silent when data emission is off`` () =
    let cfg = Config.defaultConfig |> withModel None (Some "model.json") |> dataOff
    Assert.Empty (Hydration.diagnostics cfg)

[<Fact>]
let ``NM-48: diagnostics names the skip for the both-absent model source (no path, no ossys) with data on`` () =
    // The both-absent case (Ossys = None ∧ Path = None, data on) is refused
    // upstream (pipeline.config.modelNoSource), so it is unreachable in the
    // normal pipeline — but the pure `diagnostics` must NOT fall to a silent
    // `[]` relying on that distant guard. It names the skip explicitly.
    let cfg = Config.defaultConfig |> withModel None None
    let ds = Hydration.diagnostics cfg
    Assert.Equal (1, List.length ds)
    Assert.Equal<string> ("data.hydration.skippedNoModelSource", ds.Head.Code)
    Assert.Equal<DiagnosticSeverity> (DiagnosticSeverity.Warning, ds.Head.Severity)

// ---------------------------------------------------------------------------
// hydrateCatalog — the no-connection branches (identity).
// ---------------------------------------------------------------------------

let private runHydrate (cfg: Config.Config) (catalog: Catalog) : Catalog =
    (Hydration.hydrateCatalog cfg catalog).GetAwaiter().GetResult() |> mustOk

[<Fact>]
let ``WP6 step 4: hydrateCatalog is the identity when data emission is off`` () =
    let cfg = Config.defaultConfig |> withModel (Some "env:OSSYS") None |> dataOff
    let catalog = mkCatalog [ staticKind "Country" [] ]
    Assert.Equal<Catalog> (catalog, runHydrate cfg catalog)

[<Fact>]
let ``WP6 step 4: hydrateCatalog is the identity for a model.path source (no live OSSYS; the skip is the diagnostic)`` () =
    let cfg = Config.defaultConfig |> withModel None (Some "model.json")
    let catalog = mkCatalog [ staticKind "Country" [] ]
    Assert.Equal<Catalog> (catalog, runHydrate cfg catalog)

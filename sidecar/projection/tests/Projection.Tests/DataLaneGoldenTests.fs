module Projection.Tests.DataLaneGoldenTests

// ---------------------------------------------------------------------
// DATA-LANE GOLDEN comparator — operator-review enablement (2026-06-14).
//
// The master golden (GoldenEmissionTests) emits the FUSED `Data/seed.sql`
// only — its single static lane means the production ≥2-lane rule omits the
// per-lane files. This harness lets an operator review the PER-LANE data
// SQL byte-for-byte, just like the SSDT goldens: it composes a scenario with
// TWO non-empty data lanes (StaticSeeds + MigrationData) through the SAME
// production composer (`DataEmissionComposer.composeRenderedBundleFull`) and
// pins each lane's bytes under tests/Projection.Tests/Golden/data-lanes/.
//
// Why no Bootstrap lane: `BootstrapEmitter` renders from an empty row source
// in every non-hydrated path (its rows are grafted only by the live Pipeline
// hydration step, which reads a real SQL source — Docker-gated). An empty lane
// is dropped by `RenderedDataBundle.perLaneFiles`, so `Data/Bootstrap.sql` is
// absent BY CONSTRUCTION here. Bootstrap shares the StaticSeeds MERGE renderer
// (WP6 step 2), so `Data/StaticSeeds.sql` IS its render shape; a populated
// Bootstrap golden would need a Docker hydration scenario (see DECISIONS).
//
// Blessing protocol (mirrors GoldenEmissionTests / THE_GOLDEN_EMISSION.md §2):
//   GOLDEN_RECORD=1 rewrites the corpus instead of asserting. A re-record
//   lands only with a DECISIONS note naming why.
// ---------------------------------------------------------------------

open System.IO
open Xunit
open Projection.Core
open Projection.Targets.Data

let private goldenRoot : string =
    Path.Combine(__SOURCE_DIRECTORY__, "Golden")

let private scenarioDir : string =
    Path.Combine(goldenRoot, "data-lanes")

let private recording : bool =
    System.Environment.GetEnvironmentVariable "GOLDEN_RECORD" = "1"

// `composeRenderedBundleFull` returns the two-arity `Result<_, EmitError>`;
// qualify the case access (the same alias pattern CatalogDiffTests uses to
// avoid the `DiagnosticSeverity.Error` shadowing once Projection.Core is open).
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOkEmit (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> failwithf "expected Ok, got %A" e

// The static lane is GoldenCatalog's authored static kinds (Country / Region /
// ScopedLookup / Tier). The migration lane is a context over a NON-static kind
// (Assignment, Modality = []) so the two lanes are disjoint (the composer's
// OverlappingEmitterCoverage partition law holds).
let private catalog : Catalog = GoldenCatalog.catalog

let private migrationKind : Kind =
    Catalog.allKinds catalog
    |> List.find (fun k -> Name.value k.Name = "Assignment")

/// One migration row over the migration kind's own attributes — numeric cells
/// for integer columns, a text cell otherwise, keyed by LOGICAL name (the
/// `MigrationDependencyRow.Values` shape the emitter resolves against the
/// catalog). Derived from the live kind so it cannot drift from the fixture.
let private migRow (n: int) : MigrationDependencyRow =
    let cell (a: Attribute) : Name * string =
        a.Name,
        match a.Type with
        | Integer -> string n
        | _       -> sprintf "Role%d" n
    { KindKey    = migrationKind.SsKey
      Identifier = SsKey.synthesized "Assignment" (sprintf "MigRow%d" n) |> Result.value
      Values     = migrationKind.Attributes |> List.map cell |> Map.ofList }

let private bundle : DataEmissionComposer.RenderedDataBundle =
    let ctx : MigrationDependencyContext = { Rows = [ migRow 1; migRow 2 ] }
    DataEmissionComposer.composeRenderedBundleFull
        Policy.empty catalog Profile.empty ctx UserRemapContext.empty
    |> mustOkEmit

/// The reviewable data files: the non-empty per-lane renderings plus the fused
/// deploy artifact, exactly as the pipeline would publish them at ≥2 lanes.
let private dataFiles : Map<string, string> =
    DataEmissionComposer.RenderedDataBundle.perLaneFiles bundle
    |> Map.add "Data/seed.sql" bundle.Fused

let private listGoldenFiles (dir: string) : Map<string, string> =
    if not (Directory.Exists dir) then Map.empty
    else
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
        |> Seq.map (fun f ->
            let rel = Path.GetRelativePath(dir, f).Replace('\\', '/')
            rel, File.ReadAllText f)
        |> Map.ofSeq

let private record (dir: string) (files: Map<string, string>) : unit =
    if Directory.Exists dir then Directory.Delete(dir, true)
    Directory.CreateDirectory dir |> ignore
    for KeyValue (rel, body) in files do
        let path = Path.Combine(dir, rel)
        match Path.GetDirectoryName path with
        | null | "" -> ()
        | d -> Directory.CreateDirectory d |> ignore
        File.WriteAllText(path, body)

[<Fact>]
let ``data-lane golden: the per-lane static + migration data files match the blessed corpus`` () =
    // Negative invariants (THE_GOLDEN_EMISSION.md §4): LF-only, newline-terminated.
    for KeyValue (rel, body) in dataFiles do
        Assert.True(body.EndsWith "\n", sprintf "%s does not end with a newline" rel)
        Assert.DoesNotContain("\r\n", body)
    if recording then record scenarioDir dataFiles
    else
        let expected = listGoldenFiles scenarioDir
        Assert.False(Map.isEmpty expected,
            "no data-lane goldens recorded — run with GOLDEN_RECORD=1 first")
        let expectedKeys = expected |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let actualKeys = dataFiles |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        Assert.Equal<Set<string>>(expectedKeys, actualKeys)
        for KeyValue (rel, body) in expected do
            Assert.Equal(body, Map.find rel dataFiles)

[<Fact>]
let ``data-lane golden: exactly the static + migration lanes are present (bootstrap is empty by construction)`` () =
    // The reviewable set is the two populatable lanes plus the fused seed.
    // Bootstrap is absent because its rows arrive only via live hydration.
    let keys = dataFiles |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    Assert.Contains("Data/StaticSeeds.sql", keys)
    Assert.Contains("Data/MigrationData.sql", keys)
    Assert.Contains("Data/seed.sql", keys)
    Assert.DoesNotContain("Data/Bootstrap.sql", keys)

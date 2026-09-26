module Projection.Tests.DataLaneGoldenTests

// ---------------------------------------------------------------------
// DATA-LANE GOLDEN comparator — operator-review enablement (2026-06-14).
//
// The master golden (GoldenEmissionTests) emits the FUSED `Data/seed.sql`
// only — its single static lane means the production ≥2-lane rule omits the
// per-lane files. This harness lets an operator review the PER-LANE data
// SQL byte-for-byte, just like the SSDT goldens: it composes a scenario with
// all THREE non-empty data lanes (StaticSeeds + MigrationData + Bootstrap)
// through the SAME production composer
// (`DataEmissionComposer.composeRenderedBundleWithBootstrap`) and pins each
// lane's bytes under tests/Projection.Tests/Golden/data-lanes/.
//
// The Bootstrap lane (added 2026-06-14, Bootstrap-always): in production its
// rows arrive from the live Pipeline hydration step (Docker-gated); here they
// are supplied IN-MEMORY (the same `Map<SsKey, StaticRow list>` seam hydration
// fills), keyed on a non-static, non-migration kind (Customer) so the three
// lanes stay DISJOINT and the composer's `OverlappingEmitterCoverage`
// partition law holds. Bootstrap shares the StaticSeeds MERGE renderer (A40),
// so `Data/Bootstrap.sql` reads like a static-seed MERGE; the live-hydrated
// shape is witnessed end-to-end by the Docker golden.
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
      Values     = migrationKind.Attributes |> List.map cell |> StaticRow.presentValues }

// The bootstrap lane is a NON-static, NON-migration kind (Customer) so the
// three lanes are disjoint (the partition law holds). Its rows are supplied in
// memory — the same `Map<SsKey, StaticRow list>` shape the live hydration step
// fills (Bootstrap-always, 2026-06-14). Derived from the live kind so they
// cannot drift from the fixture.
let private bootstrapKind : Kind =
    Catalog.allKinds catalog
    |> List.find (fun k -> Name.value k.Name = "Customer")

let private bootRow (n: int) : StaticRow =
    let cell (a: Attribute) : Name * string =
        a.Name,
        match a.Type with
        | Integer -> string n
        | _       -> sprintf "Customer%d" n
    { Identifier = SsKey.synthesized "Customer" (sprintf "BootRow%d" n) |> Result.value
      Values     = bootstrapKind.Attributes |> List.map cell |> StaticRow.presentValues }

let private bootstrapRows : Map<SsKey, StaticRow list> =
    Map.ofList [ bootstrapKind.SsKey, [ bootRow 1; bootRow 2 ] ]

let private bundle : DataEmissionComposer.RenderedDataBundle =
    let ctx : MigrationDependencyContext = { Rows = [ migRow 1; migRow 2 ] }
    DataEmissionComposer.composeRenderedBundleWithBootstrap
        Policy.empty catalog Profile.empty ctx bootstrapRows UserRemapContext.empty
    |> mustOkEmit

/// The reviewable data files: the non-empty per-lane renderings, exactly as the
/// pipeline now publishes them (DECISIONS 2026-06-14 — the fused `Data/seed.sql`
/// file is no longer emitted; the fused composition stays in-memory for the
/// leveled deploy's cross-lane ordering).
let private dataFiles : Map<string, string> =
    DataEmissionComposer.RenderedDataBundle.perLaneFiles bundle

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
let ``data-lane golden: all three lanes are present (static + migration + bootstrap; no fused seed.sql)`` () =
    // The reviewable set is the per-lane files only. The fused Data/seed.sql is
    // no longer emitted (operator decision). All three lanes carry content: the
    // Bootstrap lane is populated from an in-memory row source (the live
    // hydration shape) on a disjoint kind (Bootstrap-always, 2026-06-14).
    let keys = dataFiles |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    Assert.Contains("Data/StaticSeeds.sql", keys)
    Assert.Contains("Data/MigrationData.sql", keys)
    Assert.Contains("Data/Bootstrap.sql", keys)
    Assert.DoesNotContain("Data/seed.sql", keys)
    // The bootstrap lane is a real MERGE (the renderer is shared with StaticSeeds).
    Assert.Contains("MERGE", Map.find "Data/Bootstrap.sql" dataFiles)

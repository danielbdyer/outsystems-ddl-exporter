[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.IndexRoundtripTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// E1 (debrief G3) — non-PK index structure is reflected in PhysicalSchema and
// survives the emit → deploy → ReadSide round-trip. Retires the prior
// `Tolerance.IndexesUnreflected` (index *structure* is now compared; index
// *options* remain the narrower `IndexOptionsUnreflected` residual).
//
// The wide canary deploys the source DDL, reads it back, runs V2's emitter,
// deploys + reads back again, and diffs on `PhysicalSchema` — which now carries
// the `Indexes` axis. An empty diff proves V2 emitted every index faithfully;
// the reflection assertion proves the axis is non-vacuously populated (a
// generator that dropped indexes entirely would pass an empty diff trivially).
// ---------------------------------------------------------------------------

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then true
    else
        printfn "SKIP %s: Docker daemon not reachable." label
        false

/// One table with a PK, a UNIQUE single-column index, and a non-unique
/// two-column index — all INT columns so the column axis round-trips cleanly
/// (V2 emits Text as NVARCHAR(MAX); ints avoid the known length tolerance).
let private widgetDdl : string =
    "CREATE TABLE [dbo].[OSUSR_E1_WIDGET] ( \
       [ID] INT NOT NULL IDENTITY(1,1) PRIMARY KEY, \
       [CODE] INT NOT NULL, \
       [REGION] INT NOT NULL \
     ); \
     CREATE UNIQUE INDEX [UX_WIDGET_CODE] ON [dbo].[OSUSR_E1_WIDGET] ([CODE]); \
     CREATE INDEX [IX_WIDGET_REGION_CODE] ON [dbo].[OSUSR_E1_WIDGET] ([REGION], [CODE]);"

[<Fact>]
let ``E1: a UNIQUE/filtered index survives emit/deploy/ReadSide and is reflected in PhysicalSchema`` () =
    if not (skipIfNoDocker "e1-index-roundtrip") then () else
    let report =
        match (Deploy.runWideCanary widgetDdl SsdtDdlEmitter.statements).GetAwaiter().GetResult() with
        | Ok r -> r
        | Error es -> failwithf "wide canary failed: %A" es

    Assert.True(report.SourceReport.Ok, sprintf "source deploy: %A" report.SourceReport.Errors)
    Assert.True(report.TargetReport.Ok, sprintf "target deploy: %A" report.TargetReport.Errors)

    // Reflection (discriminating): the source reconstruction actually carries
    // the two non-PK indexes, by name + uniqueness + ordered key columns — so
    // the empty diff below is not vacuous.
    let sourceIndexes = (PhysicalSchema.ofCatalog report.Source).Indexes
    let byName n = sourceIndexes |> Set.filter (fun (i: PhysicalIndex) -> i.Name = n) |> Set.toList
    match byName "UX_WIDGET_CODE" with
    | [ ux ] ->
        Assert.True(ux.IsUnique)
        Assert.Equal("[CODE:ASC]", ux.KeyColumns)
    | other -> failwithf "expected exactly one UX_WIDGET_CODE index, got %A" other
    match byName "IX_WIDGET_REGION_CODE" with
    | [ ix ] ->
        Assert.False(ix.IsUnique)
        Assert.Equal("[REGION:ASC][CODE:ASC]", ix.KeyColumns)
    | other -> failwithf "expected exactly one IX_WIDGET_REGION_CODE index, got %A" other

    // Survives emit/deploy/ReadSide: the index axis (and every other) round-trips.
    Assert.True(
        List.isEmpty report.Diff.MissingIndexes && List.isEmpty report.Diff.ExtraIndexes,
        sprintf "index round-trip diff non-empty:\n%s" (PhysicalSchema.renderDiff report.Diff))
    Assert.True(
        PhysicalSchema.isEqual report.Diff,
        sprintf "wide-canary diff non-empty:\n%s" (PhysicalSchema.renderDiff report.Diff))

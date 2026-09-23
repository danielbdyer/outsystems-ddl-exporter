[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.IndexRoundtripTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// E1 (debrief G3) — non-PK index structure is reflected in PhysicalSchema and
// survives the emit → deploy → ReadSide round-trip. Retired the prior
// `Tolerance.IndexesUnreflected` (structure), and schema-L3.1 closed the
// narrower `IndexOptionsUnreflected` too: the OPTION surface (filter /
// INCLUDE / storage flags) is now recovered, compared, and witnessed here
// by the two-arm closure (agreement + falsifiability) below.
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

/// One table with a PK, a UNIQUE single-column index (carrying
/// IGNORE_DUP_KEY), a non-unique two-column index, and — schema-L3.1, the
/// `IndexOptionsUnreflected` closure witness — a filtered covering index
/// with INCLUDE + FILLFACTOR + PAD_INDEX, so the OPTION surface round-trips
/// non-vacuously. All INT columns so the column axis round-trips cleanly
/// (V2 emits Text as NVARCHAR(MAX); ints avoid the known length tolerance).
let private widgetDdl : string =
    "CREATE TABLE [dbo].[OSUSR_E1_WIDGET] ( \
       [ID] INT NOT NULL IDENTITY(1,1) PRIMARY KEY, \
       [CODE] INT NOT NULL, \
       [REGION] INT NOT NULL \
     ); \
     CREATE UNIQUE INDEX [UX_WIDGET_CODE] ON [dbo].[OSUSR_E1_WIDGET] ([CODE]) WITH (IGNORE_DUP_KEY = ON); \
     CREATE INDEX [IX_WIDGET_REGION_CODE] ON [dbo].[OSUSR_E1_WIDGET] ([REGION], [CODE]); \
     CREATE UNIQUE INDEX [UX_WIDGET_REGION] ON [dbo].[OSUSR_E1_WIDGET] ([REGION]) \
       INCLUDE ([CODE]) WHERE [REGION] > 0 WITH (FILLFACTOR = 80, PAD_INDEX = ON);"

[<Fact>]
let ``E1: a UNIQUE/filtered index survives emit/deploy/ReadSide and is reflected in PhysicalSchema`` () =
    if not (skipIfNoDocker "e1-index-roundtrip") then () else
    let report =
        match (Deploy.runWideCanary widgetDdl SsdtDdlEmitter.statements).GetAwaiter().GetResult() with
        | Ok r -> r
        | Error es -> failwithf "wide canary failed: %A" es

    Assert.True(report.SourceReport.Ok, sprintf "source deploy: %A" report.SourceReport.Errors)
    Assert.True(report.TargetReport.Ok, sprintf "target deploy: %A" report.TargetReport.Errors)

    // Reflection (discriminating), at two grains:
    //   - IR grain: ReadSide reflects the DEPLOYED index names verbatim
    //     (`Index.Name` is the deployed identity of a read-back catalog);
    //   - projection grain: `PhysicalSchema.ofCatalog` is the EMISSION
    //     expectation (the adjunction law: `ofCatalog c ≡
    //     ofStatementStream (statements c)`), so it carries the emitted
    //     logical names (`IndexNaming`) — for this legacy-named deploy the
    //     derivation renames, which is exactly what a V2 re-publish would do.
    let sourceKind = Catalog.allKinds report.Source |> List.head
    let irNames = sourceKind.Indexes |> List.map (fun i -> Name.value i.Name) |> Set.ofList
    Assert.True(Set.contains "UX_WIDGET_CODE" irNames, sprintf "ReadSide lost the deployed unique index name; got %A" irNames)
    Assert.True(Set.contains "IX_WIDGET_REGION_CODE" irNames, sprintf "ReadSide lost the deployed index name; got %A" irNames)

    let sourceIndexes = (PhysicalSchema.ofCatalog report.Source).Indexes
    let byName n = sourceIndexes |> Set.filter (fun (i: PhysicalIndex) -> i.Name = n) |> Set.toList
    match byName "UIX_OSUSR_E1_WIDGET_CODE" with
    | [ ux ] ->
        Assert.True(ux.IsUnique)
        Assert.Equal("[CODE:ASC]", ux.KeyColumns)
    | other -> failwithf "expected exactly one UIX_OSUSR_E1_WIDGET_CODE index, got %A" other
    match byName "IX_OSUSR_E1_WIDGET_REGION_CODE" with
    | [ ix ] ->
        Assert.False(ix.IsUnique)
        Assert.Equal("[REGION:ASC][CODE:ASC]", ix.KeyColumns)
    | other -> failwithf "expected exactly one IX_OSUSR_E1_WIDGET_REGION_CODE index, got %A" other

    // schema-L3.1, AGREEMENT arm (non-vacuous): the OPTION surface is
    // recovered — the filtered covering index carries its filter, INCLUDE,
    // FILLFACTOR, and PAD_INDEX through readback and projection, and the
    // IGNORE_DUP_KEY unique index carries its flag. A recovery that silently
    // defaulted any of these would fail here before the diff could pass
    // vacuously.
    match byName "UIX_OSUSR_E1_WIDGET_REGION" with
    | [ fx ] ->
        Assert.True(fx.IsUnique)
        Assert.Equal("region>0", fx.Filter)
        Assert.Equal("[CODE]", fx.IncludedColumns)
        Assert.Equal(Some 80, fx.FillFactor)
        Assert.True(fx.IsPadded, "PAD_INDEX did not survive readback")
    | other -> failwithf "expected exactly one UIX_OSUSR_E1_WIDGET_REGION index, got %A" other
    match byName "UIX_OSUSR_E1_WIDGET_CODE" with
    | [ ux ] -> Assert.True(ux.IgnoreDuplicateKey, "IGNORE_DUP_KEY did not survive readback")
    | other -> failwithf "expected exactly one UIX_OSUSR_E1_WIDGET_CODE index, got %A" other

    // Survives emit/deploy/ReadSide: the index axis (and every other) round-trips —
    // and since schema-L3.1 the index-axis comparison INCLUDES the options.
    Assert.True(
        List.isEmpty report.Diff.MissingIndexes && List.isEmpty report.Diff.ExtraIndexes,
        sprintf "index round-trip diff non-empty:\n%s" (PhysicalSchema.renderDiff report.Diff))
    Assert.True(
        PhysicalSchema.isEqual report.Diff,
        sprintf "wide-canary diff non-empty:\n%s" (PhysicalSchema.renderDiff report.Diff))

    // schema-L3.1, FALSIFIABILITY arm (the M1 two-arm pattern): strip the
    // options from the read-back catalog and the projection DIVERGES against
    // the faithful one — proving the comparator now SEES option drift, the
    // exact blindness the retired `IndexOptionsUnreflected` tolerance named
    // ("symmetric-but-lost on both halves"). Before the widening these two
    // projections were EQUAL.
    let blind =
        report.Source
        |> Catalog.mapKinds (fun k ->
            { k with
                Indexes =
                    k.Indexes
                    |> List.map (fun idx ->
                        { idx with
                            Filter = None
                            IncludedColumns = []
                            FillFactor = None
                            IsPadded = false
                            AllowRowLocks = true
                            AllowPageLocks = true
                            NoRecomputeStatistics = false
                            IgnoreDuplicateKey = false
                            IsDisabled = false
                            DataCompression = None
                            DataSpace = None }) })
    let blindDiff = PhysicalSchema.diff (PhysicalSchema.ofCatalog blind) (PhysicalSchema.ofCatalog report.Source)
    Assert.False(
        List.isEmpty blindDiff.MissingIndexes && List.isEmpty blindDiff.ExtraIndexes,
        "an option-stripped projection should DIVERGE on the index axis — the widened comparator failed to see option drift")

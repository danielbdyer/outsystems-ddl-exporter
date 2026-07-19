module Projection.Tests.DataSeedFormatterTests

open Xunit
open Projection.Targets.Data

// DataSeedFormatter (2026-07-19) — the V1-parity data-lane post-processor
// (`emission.renderDataElegant`). These tests pin the two surfaces: the
// per-statement `reflowMergeValues` (inline `USING (VALUES …)` → one row per
// line, paren/quote/bracket-aware) and the lane assembly `renderLane` (compact
// pass-through when Disabled; banner + NOCOUNT + per-module/entity headers +
// reflowed MERGEs, Phase-1-before-Phase-2, when Enabled).

let private block m e n p1 p2 : DataSeedFormatter.SeedBlock =
    { Module = m; Entity = e; RowCount = n; Phase1 = p1; Phase2 = p2 }

[<Fact>]
let ``reflowMergeValues breaks an inline USING (VALUES …) into one row per line`` () =
    let input =
        "MERGE INTO [dbo].[Country]\n AS [Target]\n"
        + "USING (VALUES (1, N'US'), (2, N'CA')) AS [Source]([Id], [Code]) ON [Target].[Id] = [Source].[Id]\nWHEN ...;\nGO\n"
    let out = DataSeedFormatter.reflowMergeValues input
    Assert.Contains("USING\n(\n    VALUES\n        (1, N'US'),\n        (2, N'CA')\n) AS [Source]([Id], [Code]) ON", out)

[<Fact>]
let ``reflowMergeValues is a no-op when there is no inline USING (VALUES …)`` () =
    // A staged `#temp` MERGE and a Phase-2 UPDATE carry no inline VALUES.
    let staged = "MERGE INTO [dbo].[Big]\n AS [Target]\nUSING [#seed_Big] AS [Source] ON x = y;\nGO\n"
    Assert.Equal<string>(staged, DataSeedFormatter.reflowMergeValues staged)
    let update = "UPDATE  [dbo].[RegionA]\n    SET [PartnerId] = 1\nWHERE   [Id] = 1;\nGO\n"
    Assert.Equal<string>(update, DataSeedFormatter.reflowMergeValues update)

[<Fact>]
let ``reflowMergeValues keeps commas / parens / quotes inside a value intact`` () =
    // Embedded comma, parens, and a `''` escape must never split a tuple.
    let input = "USING (VALUES (1, N'a, (b)'), (2, N'it''s')) AS [Source]([Id], [Txt]) ON x = y"
    let out = DataSeedFormatter.reflowMergeValues input
    Assert.Contains("        (1, N'a, (b)'),\n", out)
    Assert.Contains("        (2, N'it''s')\n", out)

[<Fact>]
let ``renderLane Disabled reproduces the compact concatenation (all Phase-1 then all Phase-2)`` () =
    let blocks =
        [ block "M" "A" 1 "MERGE_A;\nGO\n" "UPDATE_A;\nGO\n"
          block "M" "B" 1 "MERGE_B;\nGO\n" "" ]
    let out = DataSeedFormatter.renderLane DataSeedFormatter.Disabled "Static Seeds" blocks
    Assert.Equal<string>("MERGE_A;\nGO\nMERGE_B;\nGO\nUPDATE_A;\nGO\n", out)

[<Fact>]
let ``renderLane Enabled emits banner + NOCOUNT + per-module + per-entity headers`` () =
    let blocks =
        [ block "AppCore" "Country" 3 "USING (VALUES (1, N'US')) AS [Source]([Id], [Code]) ON x = y;\nGO\n" "" ]
    let out = DataSeedFormatter.renderLane DataSeedFormatter.Enabled "Static Seeds" blocks
    Assert.Contains("SET NOCOUNT ON;\nGO\n", out)
    Assert.Contains("-- Static Seeds: AppCore", out)
    Assert.Contains("-- Country (3 rows)\n", out)
    Assert.Contains("    VALUES\n        (1, N'US')\n", out)

[<Fact>]
let ``renderLane Enabled pluralizes the row count (1 row vs N rows)`` () =
    let one  = DataSeedFormatter.renderLane DataSeedFormatter.Enabled "Static Seeds" [ block "M" "Solo" 1 "MERGE;\nGO\n" "" ]
    Assert.Contains("-- Solo (1 row)\n", one)
    let many = DataSeedFormatter.renderLane DataSeedFormatter.Enabled "Static Seeds" [ block "M" "Many" 5 "MERGE;\nGO\n" "" ]
    Assert.Contains("-- Many (5 rows)\n", many)

[<Fact>]
let ``renderLane Enabled renders an all-empty lane as the empty string`` () =
    // No populated block ⇒ "" so the pipeline's IsNullOrWhiteSpace filter drops
    // the lane file (exactly as the compact path did).
    let out = DataSeedFormatter.renderLane DataSeedFormatter.Enabled "Static Seeds" [ block "M" "Empty" 0 "" "" ]
    Assert.Equal<string>("", out)

[<Fact>]
let ``renderLane Enabled keeps all Phase-1 before any Phase-2 (multi-kind cycle ordering)`` () =
    let blocks =
        [ block "M" "A" 1 "MERGE_A;\nGO\n" "UPDATE_A;\nGO\n"
          block "M" "B" 1 "MERGE_B;\nGO\n" "UPDATE_B;\nGO\n" ]
    let out = DataSeedFormatter.renderLane DataSeedFormatter.Enabled "Static Seeds" blocks
    let lastMerge   = max (out.IndexOf "MERGE_A") (out.IndexOf "MERGE_B")
    let firstUpdate = min (out.IndexOf "UPDATE_A") (out.IndexOf "UPDATE_B")
    Assert.True(lastMerge < firstUpdate, "all Phase-1 MERGEs must precede any Phase-2 UPDATE")

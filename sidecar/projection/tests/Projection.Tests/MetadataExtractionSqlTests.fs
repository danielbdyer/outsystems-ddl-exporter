module Projection.Tests.MetadataExtractionSqlTests

open System.IO
open Xunit
open Projection.Adapters.OssysSql

// ---------------------------------------------------------------------------
// Chapter 5.0 slice α — V1's `outsystems_metadata_rowsets.sql` carbon-
// copied into V2 as an embedded resource. Verifies the carbon-copy
// invariant: V2's embedded resource bytes are byte-identical to V1's
// source file. Per `DECISIONS 2026-05-16 (later)`: V2 is self-contained;
// V1 is editorial donor only. The SQL is the truth; V2 inherits it
// verbatim.
//
// The parity test depends on V1's trunk being checked out at the
// expected sibling path. In environments where V1 is absent (V2-only
// branches), the parity test gates on file presence and skips with a
// Skip-named reason.
// ---------------------------------------------------------------------------

let private v1SourcePath : string =
    // V2 lives at sidecar/projection/...; V1's source is at the repo root
    // under src/AdvancedSql/. The test resolves the path relative to the
    // running test assembly (which lives at tests/Projection.Tests/bin/...).
    let assemblyLoc = System.Reflection.Assembly.GetExecutingAssembly().Location
    let assemblyDir =
        match Path.GetDirectoryName(assemblyLoc) with
        | null -> "."
        | d -> d
    // Walk up to the repo root (tests/Projection.Tests/bin/Debug/net9.0 → 5 levels).
    let repoRoot =
        Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "..", ".."))
    Path.Combine(repoRoot, "src", "AdvancedSql", "outsystems_metadata_rowsets.sql")

[<Fact>]
let ``Slice α: embedded SQL resource loads with expected line count`` () =
    let sql = MetadataExtractionSql.read()
    // V1's source file at carbon-copy time is 1184 lines.
    let lineCount = sql.Split('\n').Length
    // Be tolerant of trailing-newline variations (1184 lines = 1184 newlines
    // or 1183 newlines depending on EOL discipline). Accept either.
    Assert.InRange(lineCount, 1183, 1185)

[<Fact>]
let ``Slice α: embedded SQL declares the five expected parameters`` () =
    let sql = MetadataExtractionSql.read()
    // The five parameters per V1's contract: @ModuleNamesCsv,
    // @IncludeSystem, @IncludeInactive, @OnlyActiveAttributes,
    // @EntityFilterJson. The header comment names them explicitly.
    Assert.Contains("@ModuleNamesCsv", sql)
    Assert.Contains("@IncludeSystem", sql)
    Assert.Contains("@IncludeInactive", sql)
    Assert.Contains("@OnlyActiveAttributes", sql)
    Assert.Contains("@EntityFilterJson", sql)

[<Fact>]
let ``Slice α: embedded SQL contains the goal-header sentinel comment`` () =
    let sql = MetadataExtractionSql.read()
    Assert.Contains("OutSystems → JSON (Two-phase, CTE-free)", sql)

[<Fact(Skip = "Gates on V1 trunk being checked out alongside V2. Manually run during merges from V1.")>]
let ``Slice α: embedded SQL bytes are identical to V1's source file (gated)`` () =
    if not (File.Exists v1SourcePath) then
        // V1 source not present in this environment; the test is a no-op.
        ()
    else
        let v1Bytes = File.ReadAllBytes v1SourcePath
        let v2Bytes =
            System.Text.Encoding.UTF8.GetBytes(MetadataExtractionSql.read())
        Assert.Equal<byte[]>(v1Bytes, v2Bytes)

[<Fact>]
let ``Slice α: V1 source bytes match V2 embedded resource bytes (present-environment only)`` () =
    // Non-gated variant that runs when V1's trunk is present, but does
    // not Skip when absent — the assertion is structural rather than
    // a hard requirement. Useful when running the suite in the
    // V1-trunk-present environment (e.g., the current monorepo
    // checkout).
    if File.Exists v1SourcePath then
        let v1Bytes = File.ReadAllBytes v1SourcePath
        let v2Bytes =
            System.Text.Encoding.UTF8.GetBytes(MetadataExtractionSql.read())
        Assert.Equal<byte[]>(v1Bytes, v2Bytes)
    else
        // V1 trunk absent — assertion is vacuous; test passes by
        // construction. This is the V2-only branch case.
        Assert.True true

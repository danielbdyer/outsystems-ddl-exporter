module Projection.Tests.BuildCreateIndexDiagnosticsTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Chapter 4.7 slice β — Diagnostics-aware buildCreateIndex.
//
// buildCreateIndex is the canonical Diagnostics-bearing emitter contract.
// It returns Diagnostics<CreateIndexStatement>. Callers that don't surface
// diagnostics drop them explicitly via `.Value` at the call site
// (V2-no-back-compat — no legacy silent-skip wrapper).
//
// Future emit-time Diagnostics consumers (CHECK constraint parse
// validation; Module.ExtendedProperties; etc.) follow the same pattern.
// ---------------------------------------------------------------------------

let private mkTableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

let private plainIdx : IndexDef =
    {
        Name = "IX_Plain"
        Table = mkTableId "dbo" "T"
        Columns = [ { Name = "Id"; Direction = IndexDefColumnDirection.Ascending } ]
        IsUnique = false
        Filter = None
        IncludedColumns = []; FillFactor = None; IsPadded = false; AllowRowLocks = true; AllowPageLocks = true; NoRecomputeStatistics = false }

let private validFilterIdx : IndexDef =
    { plainIdx with
        Name = "IX_Active"
        Filter = Some "[IsActive] = 1" }

let private malformedFilterIdx : IndexDef =
    { plainIdx with
        Name = "IX_Bad"
        Filter = Some "NOT A VALID FILTER (((" }

[<Fact>]
let ``buildCreateIndex: no filter yields empty diagnostics`` () =
    let result = ScriptDomBuild.buildCreateIndex plainIdx
    Assert.Empty result.Entries
    Assert.Null result.Value.FilterPredicate

[<Fact>]
let ``buildCreateIndex: valid filter yields empty diagnostics + FilterPredicate set`` () =
    let result = ScriptDomBuild.buildCreateIndex validFilterIdx
    Assert.Empty result.Entries
    Assert.NotNull result.Value.FilterPredicate

[<Fact>]
let ``buildCreateIndex: malformed filter yields one Warning diagnostic + FilterPredicate null`` () =
    let result = ScriptDomBuild.buildCreateIndex malformedFilterIdx
    Assert.Single result.Entries |> ignore
    let entry = result.Entries |> List.head
    Assert.Equal (DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal ("emit.ssdt.index.filterParseFailure", entry.Code)
    Assert.Null result.Value.FilterPredicate

[<Fact>]
let ``buildCreateIndex .Value: callers that drop diagnostics get the raw statement`` () =
    // Pattern callers use when they explicitly don't surface diagnostics
    // (e.g., the Statement-DU dispatcher in ScriptDomBuild.buildStatement).
    let stmt = (ScriptDomBuild.buildCreateIndex malformedFilterIdx).Value
    Assert.Null stmt.FilterPredicate

[<Fact>]
let ``buildCreateIndex: composability — Diagnostics.bind chains downstream emit decisions`` () =
    // Verify the writer composes per Diagnostics.bind contract.
    let countFilteredIndexes (idxs: IndexDef list) : Diagnostics<int> =
        idxs
        |> List.fold
            (fun acc idx ->
                Diagnostics.bind
                    (fun n ->
                        ScriptDomBuild.buildCreateIndex idx
                        |> Diagnostics.map (fun stmt ->
                            if isNull stmt.FilterPredicate then n else n + 1))
                    acc)
            (Diagnostics.ofValue 0)
    let result = countFilteredIndexes [ plainIdx; validFilterIdx; malformedFilterIdx ]
    Assert.Equal (1, result.Value)  // only validFilterIdx ends up with a FilterPredicate
    Assert.Single result.Entries |> ignore  // malformed contributed one Warning entry

[<Fact>]
let ``buildCreateIndex: T1 determinism — same input yields same Diagnostics shape`` () =
    let r1 = ScriptDomBuild.buildCreateIndex malformedFilterIdx
    let r2 = ScriptDomBuild.buildCreateIndex malformedFilterIdx
    Assert.Equal (r1.Entries.Length, r2.Entries.Length)

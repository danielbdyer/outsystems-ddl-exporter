module Projection.Tests.BuildCreateIndexDiagnosticsTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Chapter 4.7 slice β — Diagnostics-aware buildCreateIndex.
//
// buildCreateIndexWithDiagnostics is the canonical Diagnostics-bearing
// emitter contract; buildCreateIndex (silent-skip) delegates via .Value.
// Future emit-time Diagnostics consumers (CHECK constraint parse
// validation; Module.ExtendedProperties; etc.) follow the same pattern.
// ---------------------------------------------------------------------------

let private mkTableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

let private plainIdx : IndexDef =
    {
        Name = "IX_Plain"
        Table = mkTableId "dbo" "T"
        Columns = [ "Id" ]
        IsUnique = false
        Filter = None
        IncludedColumns = []
    }

let private validFilterIdx : IndexDef =
    { plainIdx with
        Name = "IX_Active"
        Filter = Some "[IsActive] = 1" }

let private malformedFilterIdx : IndexDef =
    { plainIdx with
        Name = "IX_Bad"
        Filter = Some "NOT A VALID FILTER (((" }

[<Fact>]
let ``buildCreateIndexWithDiagnostics: no filter yields empty diagnostics`` () =
    let result = ScriptDomBuild.buildCreateIndexWithDiagnostics plainIdx
    Assert.Empty result.Entries
    Assert.Null result.Value.FilterPredicate

[<Fact>]
let ``buildCreateIndexWithDiagnostics: valid filter yields empty diagnostics + FilterPredicate set`` () =
    let result = ScriptDomBuild.buildCreateIndexWithDiagnostics validFilterIdx
    Assert.Empty result.Entries
    Assert.NotNull result.Value.FilterPredicate

[<Fact>]
let ``buildCreateIndexWithDiagnostics: malformed filter yields one Warning diagnostic + FilterPredicate null`` () =
    let result = ScriptDomBuild.buildCreateIndexWithDiagnostics malformedFilterIdx
    Assert.Single result.Entries |> ignore
    let entry = result.Entries |> List.head
    Assert.Equal (DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal ("emit.ssdt.index.filterParseFailure", entry.Code)
    Assert.Null result.Value.FilterPredicate

[<Fact>]
let ``buildCreateIndex (legacy silent-skip) delegates to WithDiagnostics + drops entries`` () =
    // Backward-compatibility witness: existing callers see no behavior change.
    let raw = ScriptDomBuild.buildCreateIndex validFilterIdx
    let withDiag = ScriptDomBuild.buildCreateIndexWithDiagnostics validFilterIdx
    // Same FilterPredicate non-null status.
    Assert.Equal (isNull raw.FilterPredicate, isNull withDiag.Value.FilterPredicate)

[<Fact>]
let ``buildCreateIndex (legacy silent-skip): malformed filter omits WHERE clause without surfacing the failure`` () =
    let stmt = ScriptDomBuild.buildCreateIndex malformedFilterIdx
    Assert.Null stmt.FilterPredicate
    // The silent-skip path provides no Diagnostic surface; consumers
    // wanting the failure visible should use buildCreateIndexWithDiagnostics.

[<Fact>]
let ``buildCreateIndexWithDiagnostics: composability — Diagnostics.bind chains downstream emit decisions`` () =
    // Verify the writer composes per Diagnostics.bind contract.
    let countFilteredIndexes (idxs: IndexDef list) : Diagnostics<int> =
        idxs
        |> List.fold
            (fun acc idx ->
                Diagnostics.bind
                    (fun n ->
                        ScriptDomBuild.buildCreateIndexWithDiagnostics idx
                        |> Diagnostics.map (fun stmt ->
                            if isNull stmt.FilterPredicate then n else n + 1))
                    acc)
            (Diagnostics.ofValue 0)
    let result = countFilteredIndexes [ plainIdx; validFilterIdx; malformedFilterIdx ]
    Assert.Equal (1, result.Value)  // only validFilterIdx ends up with a FilterPredicate
    Assert.Single result.Entries |> ignore  // malformed contributed one Warning entry

[<Fact>]
let ``buildCreateIndexWithDiagnostics: T1 determinism — same input yields same Diagnostics shape`` () =
    let r1 = ScriptDomBuild.buildCreateIndexWithDiagnostics malformedFilterIdx
    let r2 = ScriptDomBuild.buildCreateIndexWithDiagnostics malformedFilterIdx
    Assert.Equal (r1.Entries.Length, r2.Entries.Length)

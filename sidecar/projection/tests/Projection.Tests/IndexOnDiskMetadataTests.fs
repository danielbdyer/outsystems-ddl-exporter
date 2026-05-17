module Projection.Tests.IndexOnDiskMetadataTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT

// ---------------------------------------------------------------------------
// Chapter 4.8 slice β — on-disk Index metadata fields (FillFactor /
// IsPadded / AllowRowLocks / AllowPageLocks / NoRecomputeStatistics).
//
// V1 reference: Osm.Domain.Model.IndexOnDiskMetadata. V2 lifts to
// Index record + ScriptDom IndexOptions emission. WITH (...) clause
// omitted when all options at default; per-option non-default emission
// otherwise.
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
        FillFactor = None
        IsPadded = false
        AllowRowLocks = true
        AllowPageLocks = true
        NoRecomputeStatistics = false
    }

[<Fact>]
let ``Defaults: no WITH clause when all on-disk options at default`` () =
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex plainIdx })
    Assert.DoesNotContain ("FILLFACTOR", sql)
    Assert.DoesNotContain ("PAD_INDEX", sql)
    Assert.DoesNotContain ("ALLOW_ROW_LOCKS = OFF", sql)
    Assert.DoesNotContain ("ALLOW_PAGE_LOCKS = OFF", sql)
    Assert.DoesNotContain ("STATISTICS_NORECOMPUTE", sql)

[<Fact>]
let ``FillFactor: emits FILLFACTOR = n when Some`` () =
    let idx = { plainIdx with FillFactor = Some 80 }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    Assert.Contains ("FILLFACTOR", sql)
    Assert.Contains ("80", sql)

[<Fact>]
let ``IsPadded: emits PAD_INDEX = ON when true`` () =
    let idx = { plainIdx with IsPadded = true }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    Assert.Contains ("PAD_INDEX", sql)

[<Fact>]
let ``AllowRowLocks = false: emits ALLOW_ROW_LOCKS = OFF`` () =
    let idx = { plainIdx with AllowRowLocks = false }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    Assert.Contains ("ALLOW_ROW_LOCKS", sql)
    Assert.Contains ("OFF", sql)

[<Fact>]
let ``AllowPageLocks = false: emits ALLOW_PAGE_LOCKS = OFF`` () =
    let idx = { plainIdx with AllowPageLocks = false }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    Assert.Contains ("ALLOW_PAGE_LOCKS", sql)
    Assert.Contains ("OFF", sql)

[<Fact>]
let ``NoRecomputeStatistics: emits STATISTICS_NORECOMPUTE = ON when true`` () =
    let idx = { plainIdx with NoRecomputeStatistics = true }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    Assert.Contains ("STATISTICS_NORECOMPUTE", sql)

[<Fact>]
let ``Combined: WITH clause emits multiple options when several non-default`` () =
    let idx =
        { plainIdx with
            FillFactor = Some 75
            IsPadded = true
            AllowRowLocks = false }
    let sql = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    Assert.Contains ("FILLFACTOR", sql)
    Assert.Contains ("75", sql)
    Assert.Contains ("PAD_INDEX", sql)
    Assert.Contains ("ALLOW_ROW_LOCKS", sql)

[<Fact>]
let ``T1: determinism — same on-disk metadata yields same SQL`` () =
    let idx = { plainIdx with FillFactor = Some 80; IsPadded = true }
    let s1 = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    let s2 = ScriptDomGenerate.toText (seq { Statement.CreateIndex idx })
    Assert.Equal<string> (s1, s2)

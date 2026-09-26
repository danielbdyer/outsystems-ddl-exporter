module Projection.Tests.DeployFeasibilityTests

open System.Threading.Tasks
open Xunit
open Projection.Pipeline

/// The deploy-feasibility gate's DECISION LOGIC over an injected executor (no
/// server). The laws:
///   * ordering-blind: a batch that fails only because its dependency had not
///     applied yet APPLIES on a later round — the fixed point retries until no
///     progress, so file order can never manufacture a false rejection;
///   * a GENUINE rejection survives to the fixed point and is reported with
///     the server's error, its file, and its in-file ordinal;
///   * batches split on the sqlcmd GO rule; blank batches are dropped;
///   * green ⇔ zero findings.
/// The live half (`applyBundle` over a real disposable database — the
/// FK-to-non-unique-column rejection the manual probe witnessed) belongs to
/// the Docker-gated Integration assembly.

let private run (t: Task<'a>) : 'a = t.GetAwaiter().GetResult()

/// A fake server: `ok` names the batches that always apply; `needs` maps a
/// batch to the batch that must have applied FIRST (the dependency shape);
/// everything else is a genuine rejection with the given error.
let private fakeExec
    (ok: string list)
    (needs: (string * string) list)
    (rejectError: string)
    : (string -> Task<string option>) =
    let appliedSet = System.Collections.Generic.HashSet<string>()
    fun sql ->
        let sqlTrim = sql.Trim()
        if List.contains sqlTrim ok then
            appliedSet.Add sqlTrim |> ignore
            Task.FromResult None
        else
            match needs |> List.tryFind (fun (b, _) -> b = sqlTrim) with
            | Some (_, dep) when appliedSet.Contains dep ->
                appliedSet.Add sqlTrim |> ignore
                Task.FromResult None
            | Some (_, dep) ->
                Task.FromResult (Some (sprintf "missing dependency %s" dep))
            | None ->
                Task.FromResult (Some rejectError)

[<Fact>]
let ``ordering-blind: a dependency-ordered batch applies on the retry round, not as a finding`` () =
    // child.sql's FK batch needs parent.sql's table — and arrives FIRST.
    let files =
        [ "child.sql", "CREATE CHILD\nGO\n"
          "parent.sql", "CREATE PARENT\nGO\n" ]
    let exec = fakeExec [ "CREATE PARENT" ] [ "CREATE CHILD", "CREATE PARENT" ] "unused"
    let report = run (DeployFeasibility.applyToFixedPoint exec files)
    Assert.True(DeployFeasibility.Report.green report)
    Assert.Equal(2, report.BatchesApplied)

[<Fact>]
let ``a genuine rejection survives the fixed point with the server's error, file, and ordinal`` () =
    let files =
        [ "ok.sql", "CREATE PARENT\nGO\n"
          "bad.sql", "CREATE PARENT2\nGO\nCREATE FK_TO_NONUNIQUE\nGO\n" ]
    let exec = fakeExec [ "CREATE PARENT"; "CREATE PARENT2" ] [] "Could not create constraint or index. See previous errors."
    let report = run (DeployFeasibility.applyToFixedPoint exec files)
    Assert.False(DeployFeasibility.Report.green report)
    Assert.Equal(2, report.BatchesApplied)
    let f = List.exactlyOne report.Findings
    Assert.Equal("bad.sql", f.File)
    Assert.Equal(1, f.BatchOrdinal)
    Assert.Contains("Could not create constraint", f.Error)

[<Fact>]
let ``two interdependent failures both resolve across rounds (transitive ordering)`` () =
    // c needs b, b needs a; the file order is worst-case (c, b, a) — three rounds.
    let files = [ "z.sql", "C\nGO\nB\nGO\nA\nGO\n" ]
    let exec = fakeExec [ "A" ] [ "B", "A"; "C", "B" ] "unused"
    let report = run (DeployFeasibility.applyToFixedPoint exec files)
    Assert.True(DeployFeasibility.Report.green report)
    Assert.Equal(3, report.BatchesApplied)

[<Fact>]
let ``blank batches are dropped, never executed or counted`` () =
    let calls = System.Collections.Generic.List<string>()
    let exec (sql: string) : Task<string option> =
        calls.Add sql
        Task.FromResult None
    let report = run (DeployFeasibility.applyToFixedPoint exec [ "f.sql", "\nGO\n\nGO\nREAL\nGO\n" ])
    Assert.Equal(1, report.BatchesApplied)
    Assert.Equal(1, calls.Count)

[<Fact>]
let ``an empty bundle is trivially green`` () =
    let exec (_: string) : Task<string option> = Task.FromResult None
    let report = run (DeployFeasibility.applyToFixedPoint exec [])
    Assert.True(DeployFeasibility.Report.green report)
    Assert.Equal(0, report.BatchesApplied)

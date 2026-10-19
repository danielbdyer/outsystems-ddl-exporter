module Projection.Tests.PerfHarnessCatalogTests

open System.IO
open Xunit

// H7 (CONSTELLATION_BACKLOG plane N9): the scenario catalog is the fifth
// declare-once system (CONSTELLATION §9.8.11) — one definition site
// (`PerfHarnessScenarios.all`) projecting the gated facts and the shell
// `list` registry. This totality test pins the projection the shell
// reads: every `PERF-SCENARIO` registry comment expands to exactly the
// declared scenario names, and the declared names are unique — the
// code⇔copy shape, applied to code⇔registry-comment. Pure pool: `all`
// holds thunks, so no fixture catalog is built here.

let private sourceFile () : string =
    // The source is the fixture: walk up from the test bin dir to
    // tests/Projection.Tests/ (bin/<cfg>/<tfm> → three parents).
    let rec ascend (dir: DirectoryInfo) =
        let candidate = Path.Combine(dir.FullName, "PerfHarnessScenarios.fs")
        // 2026-07-01 assembly split: PerfHarnessScenarios.fs moved to the
        // Projection.Tests.Integration project (it depends on the container
        // fixtures). Once the walk reaches the tests/ dir, find it there.
        let integCandidate =
            Path.Combine(dir.FullName, "Projection.Tests.Integration", "PerfHarnessScenarios.fs")
        if File.Exists candidate then candidate
        elif File.Exists integCandidate then integCandidate
        else
            match dir.Parent with
            | null ->
                failwith
                    "PerfHarnessScenarios.fs not found walking up from the test bin dir — the totality test needs the source as its fixture."
            | parent -> ascend parent
    ascend (DirectoryInfo(System.AppContext.BaseDirectory))

/// Expand one registry line — `PERF-SCENARIO: <name words> <s1>|<s2> | keylabels=…`
/// — into hyphenated scenario names: prefix tokens joined by '-', one
/// name per scale alternation (the same reading `perf-harness.sh list`
/// presents and `--filter` consumes).
let private expand (line: string) : string list =
    let afterTag =
        line.Substring(line.IndexOf("PERF-SCENARIO:") + "PERF-SCENARIO:".Length).Trim()
    let body =
        match afterTag.IndexOf("| keylabels=") with
        | -1 -> afterTag
        | i -> afterTag.Substring(0, i).Trim()
    let tokens = body.Split(' ') |> Array.filter (fun t -> t <> "")
    let prefix = tokens.[.. tokens.Length - 2] |> String.concat "-"
    tokens.[tokens.Length - 1].Split('|')
    |> Array.toList
    |> List.map (fun scale -> sprintf "%s-%s" prefix scale)

let private registryLines () : string[] =
    File.ReadAllLines(sourceFile ())
    |> Array.filter (fun l -> l.TrimStart().StartsWith "// PERF-SCENARIO:")

// NB the test name deliberately says "the declared catalog", not the
// module's name: `test.sh` splits pools by substring over the FQN
// (`FullyQualifiedName!~<DockerClassFileName>`), so a display name that
// embeds a Docker-collection module name would silently fall out of the
// pure pool (this one did, until renamed — backlog risk register).
[<Fact>]
let ``H7: PERF-SCENARIO registry ⇔ the declared catalog — the declare-once totality`` () =
    let registryNames =
        registryLines ()
        |> Array.collect (expand >> List.toArray)
        |> Set.ofArray
    let declaredNames =
        PerfHarnessScenarios.all |> List.map (fun d -> d.Name) |> Set.ofList
    Assert.NotEmpty(registryNames)
    Assert.Equal<Set<string>>(declaredNames, registryNames)

[<Fact>]
let ``H7: declared scenario names are unique`` () =
    let names = PerfHarnessScenarios.all |> List.map (fun d -> d.Name)
    Assert.Equal(names.Length, (List.distinct names).Length)

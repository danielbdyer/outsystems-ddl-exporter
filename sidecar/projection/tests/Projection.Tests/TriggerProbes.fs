module Projection.Tests.TriggerProbes

// ---------------------------------------------------------------------------
// align-III.11 — the machine-evaluable deferral triggers, EVALUATED.
//
// The Active-deferrals index (DECISIONS.md) and the H-stub Skip rationales
// carry every trigger as prose; a large subclass is machine-evaluable today
// (site counts, Bench thresholds, built-surface absence), and the registry's
// own origin story is a trigger that FIRED SILENTLY (the transform registry,
// session 12). These probes make that failure mode structural for the
// measurable subset: **a fired trigger is a red test, not a hoped-for scan.**
//
// Honesty notes, per probe:
//   - A probe evaluates the MACHINE-READABLE HALF of its trigger exactly as
//     the deferral states it; any demand-pressure half ("a consumer asks…")
//     stays prose in the index — the probe never claims to cover it.
//   - A probe going RED is not an error to suppress: it is the trigger
//     firing. The fix is the deferral's cash-out entry (or a DECISIONS
//     amendment re-stating the trigger), never a probe edit alone.
// ---------------------------------------------------------------------------

open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Xunit

/// Walk up from the running test assembly to the projection root (the
/// directory containing `tests/Projection.Tests/AxiomTests.fs`) — the same
/// findUp idiom AxiomTests/M16 and MatrixLadderTests use.
let private projectionRoot : string =
    let rec findUp (dir: DirectoryInfo option) : string option =
        match dir with
        | None -> None
        | Some d ->
            let marker = Path.Combine(d.FullName, "tests", "Projection.Tests", "AxiomTests.fs")
            if File.Exists marker then Some d.FullName
            else findUp (Option.ofObj d.Parent)
    match findUp (Some (DirectoryInfo (Directory.GetCurrentDirectory()))) with
    | Some root -> root
    | None -> failwith "TriggerProbes: projection root not found above the test assembly"

let private srcFsFiles () : string list =
    Directory.GetFiles(Path.Combine(projectionRoot, "src"), "*.fs", SearchOption.AllDirectories)
    |> Array.filter (fun p -> not (p.Contains (Path.DirectorySeparatorChar.ToString() + "obj" + Path.DirectorySeparatorChar.ToString()))
                              && not (p.Contains (Path.DirectorySeparatorChar.ToString() + "bin" + Path.DirectorySeparatorChar.ToString())))
    |> Array.toList

// ---------------------------------------------------------------------------
// H-012 — active patterns for SsKey structural dispatch.
// Trigger (HORIZON / the Skip's own words): "nested-match-on-SsKey-variant
// recurs at ≥3 sites" outside the accessors. The Skip records a HAND-RUN
// audit ("zero such nested matches found", 2026-05-22) — this probe machine-
// bounds the outer condition on every run: the `DerivedFrom` variant may be
// matched (an `| DerivedFrom …` arm) in at most TWO files outside its home
// (`Identity.fs`, where the closed DU's accessors legitimately dispatch).
// ---------------------------------------------------------------------------

[<Fact>]
let ``probe H-012: the SsKey variant is matched outside Identity.fs at fewer than 3 sites (the active-pattern trigger is unfired)`` () =
    let armRe = Regex(@"\|\s*DerivedFrom\b")
    let sites =
        srcFsFiles ()
        |> List.filter (fun p -> FileInfo(p).Name <> "Identity.fs")
        |> List.filter (fun p -> armRe.IsMatch(File.ReadAllText p))
    Assert.True(
        List.length sites < 3,
        sprintf
            "H-012's trigger has FIRED: the SsKey variant is arm-matched at %d sites outside Identity.fs (%s). \
             Cash out the deferral (adopt the active pattern) or amend the trigger in DECISIONS — do not edit this probe alone."
            (List.length sites)
            (sites |> List.map (fun p -> FileInfo(p).Name) |> String.concat ", "))

// ---------------------------------------------------------------------------
// Bench-threshold probes — H-006 / H-099 (">50% of pipeline wall time at
// operator-reality canary scale") and H-011 (">10s pass-chain wall time").
// The recorded surface is `bench/baseline-canary.json` (Stats[].Label/MeanMs).
// The triggers presuppose PASS-scoped labels; the honest reading of the
// current baseline is that NO pass-scoped label exists yet (the pass chain
// runs below the labeling grain), so the triggers are machine-checked as
// unfired rather than hand-claimed: these probes parse the real baseline on
// every run, and the moment a `pass.`-scoped label lands and breaches its
// threshold, the probe reds. Re-recording the baseline re-evaluates them.
// ---------------------------------------------------------------------------

type private BenchStat = { Label: string; MeanMs: float }

let private baselineStats () : BenchStat list =
    let path = Path.Combine(projectionRoot, "bench", "baseline-canary.json")
    Assert.True(File.Exists path, sprintf "bench baseline missing at %s — the Bench-threshold triggers have lost their measurement surface" path)
    use doc = JsonDocument.Parse(File.ReadAllText path)
    doc.RootElement.GetProperty("Stats").EnumerateArray()
    |> Seq.map (fun el ->
        { Label = el.GetProperty("Label").GetString() |> Option.ofObj |> Option.defaultValue ""
          MeanMs = el.GetProperty("MeanMs").GetDouble() })
    |> List.ofSeq

let private passScoped (stats: BenchStat list) : BenchStat list =
    stats |> List.filter (fun s -> s.Label.StartsWith "pass.")

[<Fact>]
let ``probe H-006/H-099: no pass-scoped bench label exceeds 50 percent of the recorded canary wall (the parallel/remote triggers are unfired)`` () =
    let stats = baselineStats ()
    let total = stats |> List.sumBy (fun s -> s.MeanMs)
    let offenders =
        passScoped stats
        |> List.filter (fun s -> total > 0.0 && s.MeanMs / total > 0.5)
    Assert.True(
        List.isEmpty offenders,
        sprintf
            "H-006/H-099's trigger has FIRED: pass label(s) dominate the recorded canary wall: %s. \
             Cash out the deferral (parallel composition / remote execution) or amend the trigger — not this probe."
            (offenders |> List.map (fun s -> sprintf "%s (%.0f ms)" s.Label s.MeanMs) |> String.concat ", "))

[<Fact>]
let ``probe H-011: the pass-scoped bench labels sum under 10 seconds at canary scale (the incremental-computation trigger is unfired)`` () =
    let passTotal = passScoped (baselineStats ()) |> List.sumBy (fun s -> s.MeanMs)
    Assert.True(
        passTotal <= 10_000.0,
        sprintf
            "H-011's trigger has FIRED: the pass chain reads %.0f ms (> 10s) at canary scale in the recorded baseline. \
             Cash out incremental pass-graph computation or amend the trigger — not this probe."
            passTotal)

// ---------------------------------------------------------------------------
// The four unbuilt Composition primitives — `fallback` / `accumulate` /
// `wrap` / `lift` (Active-deferrals index, 2026-05-13). The trigger's
// demand-pressure half ("a second strategy returns no-decision and another
// picks up", …) is prose; the machine-evaluable half is the BUILT-SURFACE
// ABSENCE: each primitive remains undefined in `Strategies/Composition.fs`.
// Building one without the index cash-out reds this probe — the entry and
// the code cannot drift apart silently.
// ---------------------------------------------------------------------------

[<Fact>]
let ``probe: the deferred Composition primitives (fallback/accumulate/wrap/lift) remain unbuilt until their index rows cash out`` () =
    let compositionText =
        File.ReadAllText(Path.Combine(projectionRoot, "src", "Projection.Core", "Strategies", "Composition.fs"))
    let built =
        [ "fallback"; "accumulate"; "wrap"; "lift" ]
        |> List.filter (fun name -> Regex.IsMatch(compositionText, sprintf @"\blet\s+%s\b" name))
    Assert.True(
        List.isEmpty built,
        sprintf
            "Deferred Composition primitive(s) are now BUILT: %s. The Active-deferrals index row(s) must cash out \
             in the same commit (DECISIONS.md) — update the index, then this probe's expectation."
            (String.concat ", " built))

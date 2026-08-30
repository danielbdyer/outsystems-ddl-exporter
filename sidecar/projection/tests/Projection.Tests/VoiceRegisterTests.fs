module Projection.Tests.VoiceRegisterTests

// align-III.1v (the a11 voice-register audit; operator directive
// 2026-08-16): THE_VOICE.md §2.2 bans the lazy parenthesized-suffix plural
// on operator surfaces — a statement is readable aloud (§1 rule 3), so the
// real form is written with the verb agreeing. align-III.22 (a11 stage 2)
// widened the freeze from the nine stage-1 files to the FULL src tree:
// every string literal in every production file holds zero occurrences.
//
// Two consent surfaces are EXCLUDED pending an explicit operator ruling
// (the a11 ruling's carve-out: WriteSignoff/ActConsent are exemplary
// surfaces whose copy the operator signs; their register does not move
// on an agent's initiative): `Projection.Pipeline/WriteSignoff.fs`,
// `Projection.Core/ActConsent.fs`.
//
// Scan discipline: comment lines are skipped (doc-comments may NAME the
// banned form — "never \"table(s)\"" — without violating it), and only
// double-quoted string spans are inspected, so code like `sb.Append(s)`
// never trips the net. The suffix family covers the observed morphology:
// (s) / (es) / (ies) / (en).

open System.IO
open System.Text.RegularExpressions
open Xunit

let private srcRoot = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src")

/// Consent surfaces excluded from the freeze (operator ruling pending).
let private excluded : string list =
    [ Path.Combine("Projection.Pipeline", "WriteSignoff.fs")
      Path.Combine("Projection.Core", "ActConsent.fs") ]

let private stringSpans = Regex("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled)
let private lazyPlural  = Regex(@"\w\((s|es|ies|en)\)", RegexOptions.Compiled)

[<Fact>]
let ``THE_VOICE 2-2: the lazy plural is banned — every production string in src holds zero occurrences (the align-III-22 ratchet)`` () =
    let files =
        Directory.EnumerateFiles(srcRoot, "*.fs", SearchOption.AllDirectories)
        |> Seq.filter (fun p ->
            let rel = Path.GetRelativePath(srcRoot, p)
            excluded |> List.forall (fun ex -> rel <> ex))
        |> Seq.toList
    Assert.True(files.Length > 300, sprintf "the ratchet found only %d src files — the root resolution is broken" files.Length)
    let offenders =
        files
        |> List.collect (fun path ->
            let rel = Path.GetRelativePath(srcRoot, path)
            File.ReadAllLines path
            |> Array.toList
            |> List.mapi (fun i line -> i + 1, line)
            |> List.filter (fun (_, line) -> not (line.TrimStart().StartsWith "//"))
            |> List.filter (fun (_, line) ->
                stringSpans.Matches line
                |> Seq.exists (fun m -> lazyPlural.IsMatch m.Value))
            |> List.map (fun (n, _) -> sprintf "%s:%d" rel n))
    Assert.True(List.isEmpty offenders, sprintf "lazy plurals found: %s" (String.concat "; " offenders))

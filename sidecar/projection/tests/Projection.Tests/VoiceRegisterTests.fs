module Projection.Tests.VoiceRegisterTests

// align-III.1v (the a11 voice-register audit; operator directive
// 2026-08-16): THE_VOICE.md §2.2 bans the lazy parenthesized-suffix plural
// on operator surfaces — a statement is readable aloud (§1 rule 3), so the
// real form is written with the verb agreeing. This law FREEZES the
// repaired files at zero occurrences (the executable ratchet, the source-
// scan idiom). The a11 repair plan's stage 2 widens the frozen list to the
// full src tree when the transfer/emitter long tail is repaired.

open System.IO
open Xunit

let private srcRoot = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src")

/// The repaired surfaces (a11 stage 1) — held at zero.
let private frozen : string list =
    [ "Projection.Cli/Voice.fs"
      "Projection.Cli/EstateBoardView.fs"
      "Projection.Cli/TtyRenderer.fs"
      "Projection.Cli/GoBoardView.fs"
      "Projection.Cli/ReviewNavigator.fs"
      "Projection.Cli/Faces/Fidelity.fs"
      "Projection.Cli/Program.fs"
      "Projection.Core/EstateFinding.fs"
      "Projection.Pipeline/Estate.fs" ]

[<Fact>]
let ``THE_VOICE 2-2: the lazy plural is banned — every repaired surface holds zero occurrences (the align-III-1v ratchet)`` () =
    let offenders =
        frozen
        |> List.collect (fun rel ->
            let path = Path.Combine(srcRoot, rel)
            File.ReadAllLines path
            |> Array.toList
            |> List.mapi (fun i line -> sprintf "%s:%d" rel (i + 1), line)
            |> List.filter (fun (_, line) -> line.Contains "(s)")
            |> List.map fst)
    Assert.True(List.isEmpty offenders, sprintf "lazy plurals found: %s" (String.concat "; " offenders))

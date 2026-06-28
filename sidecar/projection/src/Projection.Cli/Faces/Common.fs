module Projection.Cli.Faces.Common

// Cross-face helpers shared by the run faces — the spine the per-verb Faces/*.fs
// files sit on (recon #3). Compiled before RunFaces and the face files so the
// shared `Face` combinator + `nameOf` have ONE definition instead of being
// stranded (private) in the wall.

open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

/// Resolve an `SsKey` to its `Name` via the catalog name-index, falling back to
/// the honest `rootOriginal` (a bare GUID for an `OssysOriginal` key) when the
/// key is absent. The reconciliation / integrity / load-plan narration surfaces
/// share this so a real OSSYS estate doesn't render as a wall of hex.
let nameOf (names: Map<SsKey, string>) (key: SsKey) : string =
    Map.tryFind key names |> Option.defaultValue (SsKey.rootOriginal key)

/// The shared verb spine (recon #3 — the `Face` combinator). Every face's tail is
/// `let exit = <body, or the live Watch board on --pretty>; dumpBench "<verb>"; exit`
/// — these combinators own that tail so it exists once and cannot be forgotten.
/// Shared across the per-verb files (deploy / transfer / migrate), so it lives on
/// the `Faces.Common` spine rather than private inside any one of them.
[<RequireQualifiedAccess>]
module Face =

    /// Run a verb body and emit its bench snapshot, returning its exit code — the
    /// one place `dumpBench` fires after a face's work, so a face cannot forget it.
    let run (label: string) (body: unit -> int) : int =
        let code = body ()
        dumpBench label
        code

    /// The watch-preamble alone: on `--pretty` + a real TTY — and `gateOpen` (for
    /// verbs that only watch once their execute gate is open; a dry-run writes
    /// nothing, so its load stage would never advance) — render the body through the
    /// live `Watch` board, else run it inline. For faces whose live stage sits APART
    /// from their `dumpBench` tail (the migrate legs render the execute leg under the
    /// board deep inside a `task`, then dump bench at the outer return).
    let watchInline (gateOpen: bool) (spine: RunSpine) (body: unit -> int) : int =
        if gateOpen && Watch.shouldWatch prettyMode.Value then
            Watch.renderWatch spine (Watch.resolveDwellMs ()) body
        else body ()

    /// The watch-preamble + `dumpBench` tail combined, for faces whose live stage and
    /// bench-dump are adjacent (deploy, transfer).
    let staged (label: string) (gateOpen: bool) (spine: RunSpine) (body: unit -> int) : int =
        run label (fun () -> watchInline gateOpen spine body)

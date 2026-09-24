module Projection.Tests.RunSpineTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Card S1 (CONSTELLATION_BACKLOG stage 2) — the spine types' smart ctors.
/// The contract under test: an invalid stage name or spine is
/// unrepresentable (the house derive-macro), so the `staged { }` CE (S2)
/// and the Watch pre-seed never meet a blank, dotted, duplicated, or empty
/// arc at runtime.

let private stage (n: string) : StageName =
    StageName.create n |> Result.value

[<Fact>]
let ``S1: StageName.create — non-blank, dot-free; the wire-code prefix convention is protected at construction`` () =
    // The happy path round-trips to the wire key.
    Assert.Equal("deploy", StageName.create "deploy" |> Result.value |> StageName.value)
    // Blank refuses by name.
    Assert.Equal<string list>(
        [ "stage.name.empty" ],
        StageName.create "   " |> Result.errors |> List.map (fun e -> e.Code))
    // A dotted name would collide with the envelope-code namespace
    // (`<stage>.started`); refused by name.
    Assert.Equal<string list>(
        [ "stage.name.dotted" ],
        StageName.create "deploy.schema" |> Result.errors |> List.map (fun e -> e.Code))

[<Fact>]
let ``S1: RunSpine.create — declared stages are distinct, non-empty, order-preserved`` () =
    // Order is the execution order — preserved exactly.
    let spine = RunSpine.create [ stage "emit"; stage "deploy"; stage "canary" ] |> Result.value
    Assert.Equal<string list>(
        [ "emit"; "deploy"; "canary" ],
        RunSpine.declared spine |> List.map StageName.value)
    // Empty refuses by name.
    Assert.Equal<string list>(
        [ "spine.stages.empty" ],
        RunSpine.create [] |> Result.errors |> List.map (fun e -> e.Code))
    // A duplicated stage refuses by name (declared ⇔ executed needs a
    // well-defined declaration side).
    Assert.Equal<string list>(
        [ "spine.stages.duplicateKey" ],
        RunSpine.create [ stage "emit"; stage "emit" ]
        |> Result.errors
        |> List.map (fun e -> e.Code))

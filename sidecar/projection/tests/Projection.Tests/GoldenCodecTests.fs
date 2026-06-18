module Projection.Tests.GoldenCodecTests

open Xunit
open Projection.Core
open Projection.Targets.Json

// Slice 6 — the portable golden dataset codec. The keystone law
// (`∀ ds. deserialize (serialize ds) = Ok ds`) + determinism (a row's columns
// round-trip order-independently, since a row is a `Map`).

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %s" (es |> List.map (fun e -> e.Code) |> String.concat ", "))

let private nm (s: string) : Name = Name.create s |> mustOk

let private sample : GoldenDataset =
    { Version = 1
      Entities =
        [ { Entity = "Country"; Rows = [ Map.ofList [ nm "ID", "10" ] ] }
          { Entity = "Order"
            Rows =
              [ Map.ofList [ nm "ID", "1000"; nm "USER_ID", "100" ]
                Map.ofList [ nm "ID", "1001"; nm "USER_ID", "100" ] ] } ] }

[<Fact>]
let ``golden codec round-trips: deserialize (serialize ds) = Ok ds`` () =
    let json = GoldenCodec.serialize sample
    match GoldenCodec.deserialize json with
    | Ok ds    -> Assert.Equal<GoldenDataset>(sample, ds)
    | Error es -> Assert.Fail (sprintf "expected Ok; got %A" es)

[<Fact>]
let ``golden codec serialization is deterministic and column-order-independent`` () =
    // Same row, columns inserted in the opposite order — a `Map` is
    // order-independent, so the serialized bytes must be identical.
    let reordered : GoldenDataset =
        { Version = 1
          Entities =
            [ { Entity = "Country"; Rows = [ Map.ofList [ nm "ID", "10" ] ] }
              { Entity = "Order"
                Rows =
                  [ Map.ofList [ nm "USER_ID", "100"; nm "ID", "1000" ]
                    Map.ofList [ nm "USER_ID", "100"; nm "ID", "1001" ] ] } ] }
    Assert.Equal(GoldenCodec.serialize sample, GoldenCodec.serialize reordered)

[<Fact>]
let ``golden codec refuses malformed json`` () =
    Assert.True((GoldenCodec.deserialize "{ not json").IsError)

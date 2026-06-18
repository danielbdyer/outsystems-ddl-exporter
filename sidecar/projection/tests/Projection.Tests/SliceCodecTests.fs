module Projection.Tests.SliceCodecTests

open Xunit
open Projection.Core
open Projection.Targets.Json
open Projection.Tests.Fixtures

// The versioned slice-definition codec. Keystone law
// (`∀ s. deserialize (serialize s) = Ok s`) over every Predicate /
// TraversalDirection arm, plus re-validating decode (A39 — a duplicate
// directive is REFUSED on load through `SliceSpec.create`).

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es -> invalidOp (sprintf "fixture: %s" (es |> List.map (fun e -> e.Code) |> String.concat ", "))

let private sample : SliceSpec =
    let roots =
        [ { Entity = EntityCoordinate.create "Sales" "Order"
            Predicate = Predicate.And [ Predicate.Equals (mkName "REGION", "West")
                                        Predicate.In (mkName "STATUS", [ "open"; "shipped" ]) ] }
          { Entity = EntityCoordinate.ofEntity "Invoice"
            Predicate = Predicate.Raw "[YEAR] >= 2025" } ]
    let directives =
        [ { From = EntityCoordinate.ofEntity "Order"; Relationship = "UserRef"; Direction = TraversalDirection.Stop }
          { From = EntityCoordinate.ofEntity "Order"; Relationship = "LineItems"; Direction = TraversalDirection.Down 2 }
          { From = EntityCoordinate.ofEntity "Invoice"; Relationship = "CustRef"; Direction = TraversalDirection.Up } ]
    SliceSpec.create 1 roots directives |> mustOk

[<Fact>]
let ``slice codec round-trips: deserialize (serialize s) = Ok s`` () =
    let json = SliceCodec.serialize sample
    match SliceCodec.deserialize json with
    | Ok s     -> Assert.Equal<SliceSpec>(sample, s)
    | Error es -> Assert.Fail (sprintf "expected Ok; got %A" es)

[<Fact>]
let ``slice codec serialization is deterministic`` () =
    Assert.Equal(SliceCodec.serialize sample, SliceCodec.serialize sample)

[<Fact>]
let ``slice codec decode re-validates: a duplicate directive is refused`` () =
    // Hand-author a JSON with two directives for the same (entity, relationship)
    // edge — `SliceSpec.create` must refuse it on decode (A39).
    let json =
        """{ "version": 1,
             "roots": [ { "entity": { "module": "", "entity": "Order" }, "predicate": { "op": "all" } } ],
             "directives": [
               { "from": { "module": "", "entity": "Order" }, "relationship": "UserRef", "direction": { "kind": "up" } },
               { "from": { "module": "", "entity": "Order" }, "relationship": "UserRef", "direction": { "kind": "stop" } } ] }"""
    match SliceCodec.deserialize json with
    | Ok _     -> Assert.Fail "expected a duplicate-directive refusal on decode"
    | Error es -> Assert.Equal("slice.directive.duplicate", (List.head es).Code)

[<Fact>]
let ``slice codec refuses an empty-roots artifact`` () =
    match SliceCodec.deserialize """{ "version": 1, "roots": [], "directives": [] }""" with
    | Ok _     -> Assert.Fail "expected an empty-roots refusal on decode"
    | Error es -> Assert.Equal("slice.roots.empty", (List.head es).Code)

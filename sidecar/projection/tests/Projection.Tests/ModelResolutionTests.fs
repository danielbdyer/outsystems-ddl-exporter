module Projection.Tests.ModelResolutionTests

open Xunit
open Projection.Core
open Projection.Pipeline

// V1_INPUT_DEPRECATION.md §3 — the model-source primary/fallback policy.
// Live OSSYS is primary when configured; the osm_model.json file is the
// optional fallback; neither is a named refusal. Pure selection law.

[<Fact>]
let ``primary: live OSSYS wins when configured`` () =
    match ModelResolution.chooseOrigin (Some "env:OSSYS_CONN") (Some "model.json") with
    | Ok (ModelResolution.LiveOssys "env:OSSYS_CONN") -> ()
    | other -> Assert.Fail(sprintf "expected LiveOssys primary, got %A" other)

[<Fact>]
let ``primary: live OSSYS is used even without a file fallback`` () =
    match ModelResolution.chooseOrigin (Some "env:OSSYS_CONN") None with
    | Ok (ModelResolution.LiveOssys "env:OSSYS_CONN") -> ()
    | other -> Assert.Fail(sprintf "expected LiveOssys, got %A" other)

[<Fact>]
let ``fallback: the model file is used when no live OSSYS is configured`` () =
    match ModelResolution.chooseOrigin None (Some "model.json") with
    | Ok (ModelResolution.ModelFile "model.json") -> ()
    | other -> Assert.Fail(sprintf "expected ModelFile fallback, got %A" other)

[<Fact>]
let ``neither source configured is a named refusal`` () =
    match ModelResolution.chooseOrigin None None with
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "model.noSource")
    | Ok o -> Assert.Fail(sprintf "expected refusal, got %A" o)

// The data-sink chapter, S7 — the full selection (`chooseOriginWith`): the
// sink env is the LAST online fallback (no existing resolution changes
// shape), and --offline pins away from the wire (sink preferred, file
// allowed, live forbidden; nothing offline-true configured is its own named
// refusal).

[<Fact>]
let ``online: the sink env serves only when nothing live or authored is configured`` () =
    match ModelResolution.chooseOriginWith false (Some ("uat", None)) (Some "env:OSSYS_CONN") (Some "model.json") with
    | Ok (ModelResolution.LiveOssys _) -> ()
    | other -> Assert.Fail(sprintf "expected live to keep primacy, got %A" other)
    match ModelResolution.chooseOriginWith false (Some ("uat", None)) None (Some "model.json") with
    | Ok (ModelResolution.ModelFile _) -> ()
    | other -> Assert.Fail(sprintf "expected the file to keep its fallback slot, got %A" other)
    match ModelResolution.chooseOriginWith false (Some ("uat", Some 3)) None None with
    | Ok (ModelResolution.SinkWitness ("uat", Some 3)) -> ()
    | other -> Assert.Fail(sprintf "expected the sink as last fallback, got %A" other)

[<Fact>]
let ``offline: the sink is preferred, the file serves, live never resolves`` () =
    match ModelResolution.chooseOriginWith true (Some ("uat", None)) (Some "env:OSSYS_CONN") (Some "model.json") with
    | Ok (ModelResolution.SinkWitness ("uat", None)) -> ()
    | other -> Assert.Fail(sprintf "expected the sink under --offline, got %A" other)
    match ModelResolution.chooseOriginWith true None (Some "env:OSSYS_CONN") (Some "model.json") with
    | Ok (ModelResolution.ModelFile "model.json") -> ()
    | other -> Assert.Fail(sprintf "expected the file (already offline-true), got %A" other)

[<Fact>]
let ``offline with nothing offline-true configured is its own named refusal`` () =
    match ModelResolution.chooseOriginWith true None (Some "env:OSSYS_CONN") None with
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "model.offline.noSource")
    | Ok o -> Assert.Fail(sprintf "expected the offline refusal, got %A" o)

[<Fact>]
let ``the two-source form is the full form under (offline=false, no sink) — one selection law`` () =
    match ModelResolution.chooseOrigin (Some "c") (Some "f"),
          ModelResolution.chooseOriginWith false None (Some "c") (Some "f") with
    | Ok a, Ok b -> Assert.Equal(a, b)
    | a, b -> Assert.Fail(sprintf "expected agreement, got %A vs %A" a b)

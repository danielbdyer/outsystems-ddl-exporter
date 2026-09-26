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

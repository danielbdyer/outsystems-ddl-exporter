module Projection.Tests.SinkBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline

// The `sink` config section's binder (the data-sink chapter, S8; A44 is
// enforced per-binder): the closed off|auto|pinned vocabulary parses in both
// directions (everything the schema advertises parses; everything else is a
// NAMED refusal listing the known set), per-environment refinements bind and
// resolve through `SinkSection.effective`, and every malformed shape is its
// own named config error. R2: nothing here touches witnessing.

let private parseWith (sinkJson: string) : Result<Config.Config> =
    Config.parse (sprintf """{ "model": { "path": "m.json" }, "output": { "dir": "out/" }%s }""" sinkJson)

let private mustOk (r: Result<Config.Config>) : Config.Config =
    match r with
    | Ok c -> c
    | Error es -> failwithf "config refused: %A" es

[<Fact>]
let ``an absent sink section is the off default (byte-identical standing behavior)`` () =
    let cfg = mustOk (parseWith "")
    Assert.Equal(Config.defaultSink, cfg.Sink)
    Assert.Equal(Config.SinkPolicy.Off, cfg.Sink.Policy)

[<Fact>]
let ``every policy the schema advertises parses, and only those (A44, both directions)`` () =
    for policy in Config.SinkPolicy.all do
        let cfg = mustOk (parseWith (sprintf """, "sink": { "policy": "%s" }""" (Config.SinkPolicy.label policy)))
        Assert.Equal(policy, cfg.Sink.Policy)
    match parseWith """, "sink": { "policy": "always" }""" with
    | Error es ->
        Assert.Contains(es, fun (e: ValidationError) -> e.Code = "pipeline.config.sink.policyUnknown")
        Assert.Contains(es, fun (e: ValidationError) -> e.Message.Contains "off | auto | pinned")
    | Ok _ -> Assert.Fail "an unknown policy must refuse by name, never default silently"

[<Fact>]
let ``perEnvironment refinements bind and resolve through effective; unlisted environments ride the standing policy`` () =
    let cfg = mustOk (parseWith """, "sink": { "policy": "auto", "perEnvironment": { "prod": "pinned", "dev": "off" } }""")
    Assert.Equal(Config.SinkPolicy.Pinned, Config.SinkSection.effective (Some "prod") cfg.Sink)
    Assert.Equal(Config.SinkPolicy.Off, Config.SinkSection.effective (Some "dev") cfg.Sink)
    Assert.Equal(Config.SinkPolicy.Auto, Config.SinkSection.effective (Some "uat") cfg.Sink)
    Assert.Equal(Config.SinkPolicy.Auto, Config.SinkSection.effective None cfg.Sink)

[<Fact>]
let ``a malformed perEnvironment value names its environment in the refusal`` () =
    match parseWith """, "sink": { "perEnvironment": { "prod": "sometimes" } }""" with
    | Error es ->
        Assert.Contains(es, fun (e: ValidationError) -> e.Code = "pipeline.config.sink.policyUnknown" && e.Message.Contains "prod")
    | Ok _ -> Assert.Fail "an unknown per-env policy must refuse by name"

[<Fact>]
let ``malformed section shapes are their own named refusals`` () =
    match parseWith """, "sink": "auto" """ with
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "pipeline.config.sink.shape")
    | Ok _ -> Assert.Fail "a non-object sink section must refuse by name"
    match parseWith """, "sink": { "perEnvironment": [ "prod" ] }""" with
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "pipeline.config.sink.perEnvironmentShape")
    | Ok _ -> Assert.Fail "a non-object perEnvironment must refuse by name"

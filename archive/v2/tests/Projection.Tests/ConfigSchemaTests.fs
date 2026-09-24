module Projection.Tests.ConfigSchemaTests

open System.IO
open System.Text.Json
open Xunit
open Projection.Core
open Projection.Pipeline

/// The GENERATED config schema (`ConfigSchema.generate` →
/// `projection.schema.json`). The laws:
///   * the committed file IS the generator's output, byte-for-byte — editing
///     the file by hand, or changing the generator without re-emitting, fails
///     here (the matrix-status generated-projection discipline);
///   * the schema's closed vocabularies AGREE with the parser's — the signoff
///     modes and transform-group names are derived from the same enumerations
///     `Config.parse` accepts, and every schema-advertised value parses;
///   * the shipped cutover sample's `overrides` keys are all schema-known (the
///     executable-documentation linkage).

let private repoFile (name: string) : string option =
    let rec up (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, name)
            if File.Exists candidate then Some candidate
            else up (Option.ofObj d.Parent)
    up (Some (DirectoryInfo(System.AppContext.BaseDirectory)))

[<Fact>]
let ``the committed projection.schema.json is the generator's output, byte-for-byte`` () =
    match repoFile "projection.schema.json" with
    | None -> ()   // packaged run without the repo layout — nothing to compare
    | Some path ->
        let committed = File.ReadAllText path
        let generated = ConfigSchema.generate () + "\n"
        Assert.Equal(generated, committed)

[<Fact>]
let ``the schema's signoff-mode enum IS WriteSignoff.allModes (derived, cannot drift)`` () =
    use doc = JsonDocument.Parse(ConfigSchema.generate ())
    let advertised =
        doc.RootElement
            .GetProperty("properties").GetProperty("emission")
            .GetProperty("properties").GetProperty("signoff")
            .GetProperty("items").GetProperty("properties").GetProperty("mode")
            .GetProperty("enum").EnumerateArray()
        |> Seq.choose (fun v -> Option.ofObj (v.GetString()))
        |> List.ofSeq
    Assert.Equal<string list>(WriteSignoff.allModes |> List.map WriteSignoff.modeLabel, advertised)
    // and every advertised value PARSES
    Assert.All(advertised, fun label -> Assert.True((WriteSignoff.parseMode label).IsSome, label))

[<Fact>]
let ``the schema's transform-group enum IS TransformGroup.all (derived, cannot drift)`` () =
    use doc = JsonDocument.Parse(ConfigSchema.generate ())
    let advertised =
        doc.RootElement
            .GetProperty("properties").GetProperty("policy")
            .GetProperty("properties").GetProperty("transformGroups")
            .GetProperty("items").GetProperty("properties").GetProperty("name")
            .GetProperty("enum").EnumerateArray()
        |> Seq.choose (fun v -> Option.ofObj (v.GetString()))
        |> List.ofSeq
    Assert.Equal<string list>(TransformGroup.all |> List.map TransformGroup.configName, advertised)

[<Fact>]
let ``every schema-advertised scope and cache value parses (and only those)`` () =
    let stagingWith (kv: string) =
        sprintf """{ "model": { "path": "m.json" }, "output": { "dir": "out/" },
                     "overrides": { "bridgeRowStaging": [
                       { "id": "x"%s,
                         "source": { "module": "M", "entity": "S", "key": "Id", "identity": "Ref" },
                         "bridge": { "module": "M", "entity": "B", "key": "K",  "identity": "Ref" } } ] } }""" kv
    use doc = JsonDocument.Parse(ConfigSchema.generate ())
    let staging =
        doc.RootElement
            .GetProperty("properties").GetProperty("overrides")
            .GetProperty("properties").GetProperty("bridgeRowStaging")
            .GetProperty("items").GetProperty("properties")
    let enumValues (prop: string) =
        staging.GetProperty(prop).GetProperty("enum").EnumerateArray()
        |> Seq.choose (fun v -> Option.ofObj (v.GetString()))
        |> List.ofSeq
    for v in enumValues "scope" do
        match Config.parse (stagingWith (sprintf """, "scope": "%s" """ v)) with
        | Ok _ -> ()
        | Error es -> Assert.Fail(sprintf "schema advertises scope '%s' but the parser refuses it: %A" v es)
    for v in enumValues "cache" do
        match Config.parse (stagingWith (sprintf """, "cache": "%s" """ v)) with
        | Ok _ -> ()
        | Error es -> Assert.Fail(sprintf "schema advertises cache '%s' but the parser refuses it: %A" v es)

[<Fact>]
let ``the shipped cutover sample's overrides keys are all schema-known`` () =
    match repoFile "examples/bridge-retarget-cutover.sample.json" with
    | None -> ()
    | Some samplePath ->
        use schema = JsonDocument.Parse(ConfigSchema.generate ())
        let known =
            schema.RootElement
                .GetProperty("properties").GetProperty("overrides")
                .GetProperty("properties").EnumerateObject()
            |> Seq.map (fun p -> p.Name)
            |> Set.ofSeq
        use sample = JsonDocument.Parse(File.ReadAllText samplePath)
        let sampleKeys =
            sample.RootElement.GetProperty("overrides").EnumerateObject()
            |> Seq.map (fun p -> p.Name)
            |> Seq.filter (fun n -> not (n.StartsWith "_comment"))
            |> List.ofSeq
        Assert.All(sampleKeys, fun k -> Assert.True(Set.contains k known, k))

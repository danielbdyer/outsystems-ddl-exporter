module Projection.Tests.ComposeAtomicWriteTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline

// -----------------------------------------------------------------------
// Tests for `Projection.Pipeline.Compose.write` atomicity.
//
// L3-Boundary-AtomicEmission: a successful Compose.write produces an
// outputDir containing exactly the artifacts the in-memory Outputs
// named. A failed Compose.write leaves outputDir byte-identical to its
// pre-call state (or absent if it didn't exist before).
//
// Mechanism: write to a sibling staging directory; on success, replace
// outputDir; on failure, delete staging.
//
// Cite the axiom each test enforces with the L3 ID in the test name.
// -----------------------------------------------------------------------

let private newTempRoot () : string =
    let path =
        Path.Combine(
            Path.GetTempPath(),
            sprintf "compose-atomic-%s" (Guid.NewGuid().ToString("N").Substring(0, 12)))
    Directory.CreateDirectory(path) |> ignore
    path

let private withTempRoot (action: string -> 'a) : 'a =
    let root = newTempRoot ()
    try action root
    finally
        if Directory.Exists root then
            try Directory.Delete(root, recursive = true) with _ -> ()

let private trivialOutputs () : Compose.Outputs =
    let bundle =
        Map.ofList [
            "Modules/Sales/dbo.Customer.sql", "-- customer ddl\nGO\n"
            "Modules/Sales/dbo.Order.sql",    "-- order ddl\nGO\n"
            "manifest.json",                  "{\"kinds\":[\"Customer\",\"Order\"]}"
        ]
    let json = System.Text.Json.Nodes.JsonNode.Parse("""{"v":1}""")
    let dist = System.Text.Json.Nodes.JsonNode.Parse("""{"distributions":[]}""")
    let suggestCfg = System.Text.Json.Nodes.JsonNode.Parse("""{"suggestedEdits":[]}""")
    match json, dist, suggestCfg with
    | null, _, _ | _, null, _ | _, _, null -> failwith "JsonNode.Parse returned null on trivial input"
    | j, d, sc ->
        {
            SsdtBundle        = bundle
            DataBundle        = Map.empty
            Json              = j
            Distributions     = d
            RemediationSql    = "-- no remediation candidates"
            SummaryText       = "Tightening decision summary\n(empty fixture)"
            SuggestConfigJson = sc
            Dacpac            = None
            Manifest          = Projection.Targets.SSDT.ManifestEmitter.build Fixtures.sampleCatalog
            Trail             = []
            PassEntries       = []
        }

let private listAllFiles (dir: string) : string list =
    if not (Directory.Exists dir) then []
    else
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
        |> Seq.map (fun p -> Path.GetRelativePath(dir, p))
        |> Seq.sort
        |> List.ofSeq

let private listStagingSiblings (outputDir: string) : string list =
    let parent =
        match Path.GetDirectoryName outputDir with
        | null -> "."
        | "" -> "."
        | p -> p
    if not (Directory.Exists parent) then []
    else
        Directory.EnumerateDirectories(parent)
        |> Seq.choose (fun path ->
            match Path.GetFileName(path: string) with
            | null -> None
            | "" -> None
            | n -> if n.Contains(".staging-") then Some n else None)
        |> List.ofSeq

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        failwithf "Expected Ok, got Error(s): %s" codes

let private mustFail (r: Result<'a>) : ValidationError list =
    match r with
    | Ok _ -> failwith "Expected Error, got Ok."
    | Error es -> es

let private hasCode (code: string) (errors: ValidationError list) : bool =
    errors |> List.exists (fun e -> e.Code = code)

// -----------------------------------------------------------------------
// Happy path
// -----------------------------------------------------------------------

[<Fact>]
let ``L3-Boundary-AtomicEmission: happy path writes all artifacts and reports their paths`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        let outputs = trivialOutputs ()
        let paths = Compose.write outputDir outputs |> mustOk
        // Expect: 3 bundle entries + json + distributions + remediation + summary + suggest-config = 8
        // (chapter 5+ slices 5.13.remediation-emitter + 5.13.summary-formatter
        //  add `manifest.remediation.sql` + `manifest.summary.txt`; H-032 adds
        //  `suggest-config.json` — all are operator-UX projections of the
        //  post-chain DecisionSets.)
        Assert.Equal(8, List.length paths)
        // Every reported path exists on disk
        for p in paths do
            Assert.True(File.Exists p, sprintf "Expected file at %s" p))

[<Fact>]
let ``L3-Boundary-AtomicEmission: success path leaves no staging-* directories behind`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        let outputs = trivialOutputs ()
        let _ = Compose.write outputDir outputs |> mustOk
        Assert.Empty(listStagingSiblings outputDir))

// -----------------------------------------------------------------------
// Atomicity under induced failure
// -----------------------------------------------------------------------

/// Build a FileWriter that throws after the Nth invocation.
/// Used to inject a mid-stream failure into the staging phase.
let private failAfterNWrites (n: int) : Compose.FileWriter =
    let mutable count = 0
    fun absPath body ->
        count <- count + 1
        if count > n then
            raise (IOException(sprintf "induced failure at write #%d (%s)" count absPath))
        else
            // Real write so the staging dir contains valid partial state
            match Path.GetDirectoryName absPath with
            | null -> ()
            | "" -> ()
            | parent -> Directory.CreateDirectory parent |> ignore
            File.WriteAllText(absPath, body)

[<Fact>]
let ``L3-Boundary-AtomicEmission: induced failure mid-write leaves a pre-absent outputDir absent`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        Assert.False(Directory.Exists outputDir)
        let outputs = trivialOutputs ()
        let errors = Compose.writeWith (failAfterNWrites 2) outputDir outputs |> mustFail
        Assert.True(hasCode "pipeline.compose.write.atomicFailure" errors)
        // outputDir must remain absent
        Assert.False(Directory.Exists outputDir,
            sprintf "outputDir '%s' must remain absent after failed write" outputDir))

[<Fact>]
let ``L3-Boundary-AtomicEmission: induced failure leaves a pre-existing outputDir byte-identical`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        Directory.CreateDirectory(outputDir) |> ignore
        // Seed outputDir with sentinel content
        let sentinelPath = Path.Combine(outputDir, "sentinel.txt")
        let sentinelContent = "pre-emit-state\nv1.0\n"
        File.WriteAllText(sentinelPath, sentinelContent)
        let nestedPath = Path.Combine(outputDir, "nested", "deep.txt")
        Directory.CreateDirectory(Path.Combine(outputDir, "nested")) |> ignore
        File.WriteAllText(nestedPath, "nested content")
        let preState = listAllFiles outputDir |> List.sort
        // Induce failure during emit
        let outputs = trivialOutputs ()
        let _ = Compose.writeWith (failAfterNWrites 2) outputDir outputs |> mustFail
        // Pre-emit state preserved
        let postState = listAllFiles outputDir |> List.sort
        Assert.Equal<string list>(preState, postState)
        Assert.Equal(sentinelContent, File.ReadAllText sentinelPath)
        Assert.Equal("nested content", File.ReadAllText nestedPath))

[<Fact>]
let ``L3-Boundary-AtomicEmission: induced failure leaves no staging-* directories behind`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        let outputs = trivialOutputs ()
        let _ = Compose.writeWith (failAfterNWrites 1) outputDir outputs |> mustFail
        Assert.Empty(listStagingSiblings outputDir))

[<Fact>]
let ``L3-Boundary-AtomicEmission: failure on the very first write still leaves outputDir untouched`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        let preSentinelPath = Path.Combine(outputDir, "sentinel.txt")
        Directory.CreateDirectory(outputDir) |> ignore
        File.WriteAllText(preSentinelPath, "stable")
        let outputs = trivialOutputs ()
        let _ = Compose.writeWith (failAfterNWrites 0) outputDir outputs |> mustFail
        Assert.True(File.Exists preSentinelPath)
        Assert.Equal("stable", File.ReadAllText preSentinelPath))

// -----------------------------------------------------------------------
// Replace semantics on success (outputDir is V2-owned)
// -----------------------------------------------------------------------

[<Fact>]
let ``L3-Boundary-AtomicEmission: successful write replaces (not merges) pre-existing outputDir`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        Directory.CreateDirectory(outputDir) |> ignore
        // Place a stale file that should NOT survive a successful emit
        let stalePath = Path.Combine(outputDir, "stale.txt")
        File.WriteAllText(stalePath, "stale-from-previous-emit")
        let outputs = trivialOutputs ()
        let paths = Compose.write outputDir outputs |> mustOk
        // Reported paths are all under the new outputDir
        for p in paths do
            Assert.True(p.StartsWith outputDir)
            Assert.True(File.Exists p)
        // Stale file must be gone (replace, not merge)
        Assert.False(File.Exists stalePath,
            "Successful emit should replace outputDir, not merge with prior contents"))

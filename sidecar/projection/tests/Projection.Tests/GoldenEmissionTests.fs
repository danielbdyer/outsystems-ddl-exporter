module Projection.Tests.GoldenEmissionTests

// ---------------------------------------------------------------------
// THE GOLDEN EMISSION comparator (THE_GOLDEN_EMISSION.md). The Platonic
// catalog (GoldenCatalog.fs) is emitted through the PRODUCTION
// config-driven composition under a small matrix of operator configs;
// the resulting artifacts are compared byte-for-byte against the
// committed corpus at tests/Projection.Tests/Golden/<scenario>/.
//
// Blessing protocol (THE_GOLDEN_EMISSION.md §2):
//   - GOLDEN_RECORD=1 rewrites the corpus instead of asserting
//     (mirrors PERF_GATE_RECORD). The scenario directory is cleared
//     first so removals surface as git deletions.
//   - A golden re-record lands only with a DECISIONS note naming why.
//
// Negative invariants (§4) are asserted on every scenario — laws that
// hold regardless of the bytes.
// ---------------------------------------------------------------------

open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

let private goldenRoot : string =
    Path.Combine(__SOURCE_DIRECTORY__, "Golden")

let private recording : bool =
    System.Environment.GetEnvironmentVariable "GOLDEN_RECORD" = "1"

let private mustOk (r: Result<'a>) : 'a =
    match r with Ok v -> v | Error e -> failwithf "expected Ok, got %A" e

// ---------------------------------------------------------------------
// Scenario configs — part of the pinned intent. Every scenario carries
// a non-default Output.Dir so `projectWithConfig` routes the FULL
// policy-built path (its defaultConfig short-circuit is the schema-only
// movement face, not the full-export face this corpus pins).
// ---------------------------------------------------------------------

let private baseConfig : Config.Config =
    { Config.defaultConfig with
        Output = { Dir = "golden" } }

let private scenarios : (string * Config.Config) list =
    [ "default", baseConfig
      "pruned-platform-auto",
        { baseConfig with
            Emission = { baseConfig.Emission with IncludePlatformAutoIndexes = false } }
      "delete-scope",
        { baseConfig with
            Emission =
                { baseConfig.Emission with
                    // NB (first-recording finding): the term resolves against
                    // the POST-CHAIN catalog — LogicalColumnEmission has
                    // substituted logical column names by then — so under the
                    // default logical rendition the term names the LOGICAL
                    // column ("TenantId"), although the DeleteScopePolicy doc
                    // says "PHYSICAL columns". The wart is recorded in
                    // THE_GOLDEN_EMISSION.md §4 and the reconciliation plan.
                    DeleteScope = Some { Terms = [ { Column = "TenantId"; Value = "42" } ] } } } ]

/// The baseline scenario whose directory holds the FULL, standalone-
/// readable artifact set — the one place a reviewer reads a complete
/// emission. Every OTHER scenario commits ONLY the files whose bytes
/// differ from this baseline (DECISIONS 2026-06-13 — golden corpus
/// contraction; amends THE_GOLDEN_EMISSION.md §3's full-set choice). The
/// comparator asserts `scenario = baseline ⊕ {committed deltas}`, so a
/// file equal to the baseline is never duplicated into a non-baseline
/// scenario, and an axis-orthogonal variance touches only the baseline.
let private baselineScenario : string = "default"

let private baselineConfig : Config.Config =
    scenarios |> List.find (fun (n, _) -> n = baselineScenario) |> snd

/// Emit one scenario's artifact set: the per-table SSDT bundle (minus
/// manifest.json — its VersionedPolicy stamp carries a wall clock; the
/// inventory holds the TODO), the Data bundle, and the flat-stream
/// realization (stream.sql — where GO framing and the constraint
/// ladder live).
let private emitScenario (cfg: Config.Config) : Map<string, string> =
    let outputs = Compose.projectWithConfig cfg GoldenCatalog.catalog |> mustOk
    let postChain = Compose.applyShapingToCatalog cfg GoldenCatalog.catalog |> mustOk
    let emitted =
        EmissionPolicy.filterPlatformAutoIndexes
            (EmissionPolicy.withIncludePlatformAutoIndexes
                cfg.Emission.IncludePlatformAutoIndexes
                EmissionPolicy.empty)
            postChain
    let stream = SsdtDdlEmitter.statements emitted |> Render.toText
    let bundleFiles =
        outputs.SsdtBundle
        |> Map.filter (fun path _ -> path.EndsWith ".sql")
    let dataFiles = outputs.DataBundle
    bundleFiles
    |> Map.fold (fun acc k v -> Map.add k v acc) dataFiles
    |> Map.add "stream.sql" stream

let private listGoldenFiles (scenarioDir: string) : Map<string, string> =
    if not (Directory.Exists scenarioDir) then Map.empty
    else
        Directory.EnumerateFiles(scenarioDir, "*", SearchOption.AllDirectories)
        |> Seq.map (fun f ->
            let rel = Path.GetRelativePath(scenarioDir, f).Replace('\\', '/')
            rel, File.ReadAllText f)
        |> Map.ofSeq

let private record (scenarioDir: string) (files: Map<string, string>) : unit =
    if Directory.Exists scenarioDir then Directory.Delete(scenarioDir, true)
    Directory.CreateDirectory scenarioDir |> ignore
    for KeyValue (rel, body) in files do
        let path = Path.Combine(scenarioDir, rel)
        match Path.GetDirectoryName path with
        | null | "" -> ()
        | dir -> Directory.CreateDirectory dir |> ignore
        File.WriteAllText(path, body)

/// Byte-exact assertion with a first-divergence context window. Shared by
/// the baseline (full-set) and delta comparators.
let private assertByteMatch (scenario: string) (rel: string) (expectedBody: string) (actualBody: string) : unit =
    if expectedBody <> actualBody then
        let firstDiffAt =
            Seq.zip (expectedBody :> char seq) (actualBody :> char seq)
            |> Seq.tryFindIndex (fun (a, b) -> a <> b)
            |> Option.defaultValue (min expectedBody.Length actualBody.Length)
        let context (s: string) =
            let from = max 0 (firstDiffAt - 80)
            let len = min 160 (s.Length - from)
            if len <= 0 then "" else s.Substring(from, len)
        Assert.Fail(
            sprintf "scenario %s: %s drifted from the golden at char %d.\n--- golden ---\n%s\n--- emitted ---\n%s\n(bless deliberately with GOLDEN_RECORD=1 + a DECISIONS note)"
                scenario rel firstDiffAt (context expectedBody) (context actualBody))

/// Baseline comparator (the `default` scenario) — the FULL artifact set is
/// pinned and read standalone; the artifact-set drift check lives here.
let private compare (scenario: string) (expected: Map<string, string>) (actual: Map<string, string>) : unit =
    let expectedKeys = expected |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    let actualKeys = actual |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    let missing = Set.difference expectedKeys actualKeys
    let extra = Set.difference actualKeys expectedKeys
    Assert.True(Set.isEmpty missing && Set.isEmpty extra,
        sprintf "scenario %s: artifact set drifted.\n  missing from emission: %A\n  not in goldens: %A\n  (re-record deliberately with GOLDEN_RECORD=1 + a DECISIONS note)"
            scenario (Set.toList missing) (Set.toList extra))
    for KeyValue (rel, expectedBody) in expected do
        assertByteMatch scenario rel expectedBody (Map.find rel actual)

/// Delta comparator (every non-baseline scenario). The committed corpus
/// holds ONLY the files whose bytes differ from the baseline; the law is
/// `scenario = baseline ⊕ {committed deltas}`. Three sub-laws:
///   1. same Platonic catalog ⇒ identical artifact keyset to the baseline;
///   2. each committed delta genuinely differs from the baseline AND
///      byte-matches the emission (no equal-to-baseline duplication);
///   3. every NON-committed file equals the baseline (the invariance law).
let private compareDelta
    (scenario: string)
    (baseline: Map<string, string>)
    (committed: Map<string, string>)
    (actual: Map<string, string>)
    : unit =
    let actualKeys = actual |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    let baselineKeys = baseline |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    Assert.True((actualKeys = baselineKeys),
        sprintf "scenario %s: artifact keyset diverged from the %s baseline.\n  only here: %A\n  only in baseline: %A"
            scenario baselineScenario
            (Set.difference actualKeys baselineKeys |> Set.toList)
            (Set.difference baselineKeys actualKeys |> Set.toList))
    for KeyValue (rel, committedBody) in committed do
        match Map.tryFind rel actual with
        | None ->
            Assert.Fail(sprintf "scenario %s: committed delta %s is absent from the emission (re-record with GOLDEN_RECORD=1)" scenario rel)
        | Some actualBody ->
            match Map.tryFind rel baseline with
            | Some b when b = actualBody ->
                Assert.Fail(sprintf "scenario %s: committed delta %s equals the %s baseline — a non-baseline scenario must not duplicate baseline bytes; re-record to drop it" scenario rel baselineScenario)
            | _ -> assertByteMatch scenario rel committedBody actualBody
    for KeyValue (rel, actualBody) in actual do
        if not (Map.containsKey rel committed) then
            match Map.tryFind rel baseline with
            | Some b when b = actualBody -> ()
            | _ ->
                Assert.Fail(sprintf "scenario %s: %s differs from the %s baseline but is not a committed delta (re-record with GOLDEN_RECORD=1 + a DECISIONS note)" scenario rel baselineScenario)

/// Record only the files whose bytes differ from the baseline emission —
/// the corpus-contraction recorder for non-baseline scenarios.
let private recordDelta (scenarioDir: string) (baseline: Map<string, string>) (actual: Map<string, string>) : unit =
    actual
    |> Map.filter (fun rel body -> Map.tryFind rel baseline <> Some body)
    |> record scenarioDir

// ---------------------------------------------------------------------
// Negative invariants — THE_GOLDEN_EMISSION.md §4, asserted per scenario.
// ---------------------------------------------------------------------

let private assertNegativeInvariants (scenario: string) (files: Map<string, string>) : unit =
    for KeyValue (rel, body) in files do
        // 4 — newline-terminated, no CRLF anywhere (slice 3 restored
        // the termination: per-table bodies render via Render.toText).
        Assert.True(body.EndsWith "\n", sprintf "%s/%s does not end with a newline" scenario rel)
        Assert.DoesNotContain("\r\n", body)
        // 1 — per-table GO is framed (blank line both sides) and never
        // trailing (operator decision, DECISIONS 2026-06-13 — V1's
        // per-file form; supersedes the prior no-GO contract).
        if rel.StartsWith "Modules/" then
            let lines = body.Split('\n')
            let goIndexes =
                lines
                |> Array.indexed
                |> Array.filter (fun (_, l) -> l.Trim() = "GO")
                |> Array.map fst
            for i in goIndexes do
                Assert.True(i > 0 && lines.[i - 1].Trim() = "",
                    sprintf "%s/%s: GO at line %d lacks its leading blank" scenario rel i)
                Assert.True(i + 1 < lines.Length && lines.[i + 1].Trim() = "",
                    sprintf "%s/%s: GO at line %d lacks its trailing blank" scenario rel i)
            match lines |> Array.tryFindBack (fun l -> not (System.String.IsNullOrWhiteSpace l)) with
            | Some l ->
                Assert.False(l.Trim() = "GO",
                    sprintf "%s/%s ends with a trailing GO (V1 joins between statements only)" scenario rel)
            | None -> ()
    // 3 — no FK owned by the pure-target kind (the inverse-exclusion law).
    for KeyValue (rel, body) in files do
        if rel.EndsWith "GOLD_USER.sql" then
            Assert.DoesNotContain("FOREIGN KEY", body)
    // 2 — no duplicate (schema-scoped) FK constraint names across the set.
    let fkNames =
        files
        |> Map.toList
        |> List.collect (fun (rel, body) ->
            if rel.StartsWith "Modules/" || rel = "stream.sql" then
                body.Split('\n')
                |> Array.toList
                |> List.collect (fun line ->
                    let marker = "CONSTRAINT [FK_"
                    match line.IndexOf marker with
                    | -1 -> []
                    | i ->
                        let from = i + "CONSTRAINT [".Length
                        match line.IndexOf(']', from) with
                        | -1 -> []
                        | j -> [ rel, line.Substring(from, j - from) ])
            else [])
    let perFileNames = fkNames |> List.distinct
    let collisions =
        perFileNames
        |> List.filter (fun (rel, _) -> rel <> "stream.sql")
        |> List.groupBy snd
        |> List.filter (fun (_, hits) -> List.length hits > 1)
    Assert.True(List.isEmpty collisions,
        sprintf "%s: duplicate FK constraint names: %A" scenario collisions)
    // 6 — the stream splits into batches with no empty batch.
    match Map.tryFind "stream.sql" files with
    | None -> ()
    | Some stream ->
        let batches =
            stream.Split('\n')
            |> Array.fold (fun (acc, current) line ->
                if line.Trim() = "GO" then (List.rev current :: acc, [])
                else (acc, line :: current)) ([], [])
            |> fun (acc, last) -> List.rev (List.rev last :: acc)
            |> List.map (fun lines -> System.String.Join("\n", lines).Trim())
        for b in batches |> List.take (List.length batches - 1) do
            Assert.False(System.String.IsNullOrWhiteSpace b,
                sprintf "%s: empty GO batch in stream.sql" scenario)

// ---------------------------------------------------------------------
// The comparator facts — one per scenario.
// ---------------------------------------------------------------------

let private runScenario (name: string) : unit =
    let cfg = scenarios |> List.find (fun (n, _) -> n = name) |> snd
    let actual = emitScenario cfg
    // The negative invariants run on the FULL emission for every scenario,
    // independent of the delta-recording layout.
    assertNegativeInvariants name actual
    let scenarioDir = Path.Combine(goldenRoot, name)
    if name = baselineScenario then
        if recording then record scenarioDir actual
        else
            let expected = listGoldenFiles scenarioDir
            Assert.False(Map.isEmpty expected,
                sprintf "no goldens recorded for scenario %s — run with GOLDEN_RECORD=1 first" name)
            compare name expected actual
    else
        // Non-baseline: pin only the delta from the baseline emission.
        let baseline = emitScenario baselineConfig
        if recording then recordDelta scenarioDir baseline actual
        else
            Assert.True(Directory.Exists scenarioDir,
                sprintf "no goldens recorded for scenario %s — run with GOLDEN_RECORD=1 first" name)
            compareDelta name baseline (listGoldenFiles scenarioDir) actual

[<Fact>]
let ``golden emission: default scenario matches the blessed corpus byte-for-byte`` () =
    runScenario "default"

[<Fact>]
let ``golden emission: pruned-platform-auto scenario matches the blessed corpus byte-for-byte`` () =
    runScenario "pruned-platform-auto"

[<Fact>]
let ``golden emission: delete-scope scenario matches the blessed corpus byte-for-byte`` () =
    runScenario "delete-scope"

[<Fact>]
let ``golden emission: scenario artifact sets are deterministic across repeated emission (T1 face)`` () =
    for name, cfg in scenarios do
        let a = emitScenario cfg
        let b = emitScenario cfg
        Assert.Equal<Map<string, string>>(a, b)
        ignore name

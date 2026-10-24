module Projection.Tests.RelaxationStoreTests

open System
open System.IO
open Xunit
open Projection.Cli
open Projection.Pipeline

/// Relax-ALWAYS persistence — the blessed tightening relaxations recorded in
/// projection.json. The load-bearing property: the surgical JSON merge records
/// the blessing WITHOUT dropping any other config key (the config doctrine —
/// it rides alongside the movement vocabulary, untouched by renderConfig / A44).

let private tempFile () =
    Path.Combine(Path.GetTempPath(), "proj-relax-" + Guid.NewGuid().ToString("N") + ".json")

[<Fact>]
let ``read returns empty for a missing file (no blessing)`` () =
    Assert.True(Set.isEmpty (RelaxationStore.read (tempFile ())))

[<Fact>]
let ``persist then read round-trips the relaxation keys`` () =
    let path = tempFile ()
    try
        Assert.Equal<Result<unit, string>>(Ok (), RelaxationStore.persist path [ "OSUSR_X_ORDER.Notes"; "OSUSR_X_ORDER.Memo" ])
        Assert.Equal<Set<string>>(
            Set.ofList [ "OSUSR_X_ORDER.Notes"; "OSUSR_X_ORDER.Memo" ],
            RelaxationStore.read path)
    finally
        (try File.Delete path with _ -> ())

[<Fact>]
let ``persist merges and dedups without losing prior blessings`` () =
    let path = tempFile ()
    try
        RelaxationStore.persist path [ "A.x" ] |> ignore
        RelaxationStore.persist path [ "A.x"; "B.y" ] |> ignore
        Assert.Equal<Set<string>>(Set.ofList [ "A.x"; "B.y" ], RelaxationStore.read path)
    finally
        (try File.Delete path with _ -> ())

[<Fact>]
let ``persist is a SURGICAL merge — every other projection.json key survives`` () =
    let path = tempFile ()
    try
        File.WriteAllText(
            path,
            """{ "model": { "ossys": "file:./secrets/ossys.conn" }, "flows": { "publish": { "to": "onprem-dev" } } }""")
        Assert.Equal<Result<unit, string>>(Ok (), RelaxationStore.persist path [ "T.c" ])
        let text = File.ReadAllText path
        // the blessing rode alongside the existing keys — none dropped.
        Assert.Contains("tighteningRelaxations", text)
        Assert.Contains("ossys.conn", text)
        Assert.Contains("publish", text)
        Assert.Equal<Set<string>>(Set.singleton "T.c", RelaxationStore.read path)
    finally
        (try File.Delete path with _ -> ())

/// The guided-plan wizard's greenlight-write (2026-07-09): `setFlowSignoff` writes
/// `flows.<flow>.signoff` as an array the config parser reads back to the SAME
/// approvals, and — the surgical-merge invariant — leaves every other key intact.
[<Fact>]
let ``setFlowSignoff writes a signoff the config parser reads back to the same approvals`` () =
    let path = tempFile ()
    try
        File.WriteAllText(
            path,
            """{ "model": { "path": "m.json" }, "output": { "dir": "out/" },
                "environments": {
                  "qa":  { "access": "direct", "conn": "file:./q.conn", "rendition": "physical", "archetype": "managed-dml", "grant": "data" },
                  "uat": { "access": "direct", "conn": "file:./u.conn", "rendition": "physical", "archetype": "managed-dml", "grant": "data" } },
                "flows": { "golden": { "from": "qa", "to": "uat", "scope": "data", "tables": ["Customer"], "strategy": "replace" } } }""")
        let approval =
            { WriteSignoff.greenlit WriteSignoff.WriteMode.Replace with
                AcknowledgedImpact = Some (WriteSignoff.impactOf WriteSignoff.WriteMode.Replace) }
        Assert.Equal<Result<unit, string>>(Ok (), RelaxationStore.setFlowSignoff path "golden" [ approval ])
        // the config parser reads the written array back to the same approvals.
        match ProjectionConfig.parse (File.ReadAllText path) with
        | Error es -> Assert.Fail(sprintf "reparse failed: %A" es)
        | Ok cfg ->
            let flow = Map.find "golden" cfg.Flows
            Assert.Equal<WriteSignoff.WriteApproval list>([ approval ], flow.Signoff)
            // the surgical merge preserved the flow's other keys (strategy/tables).
            let text = File.ReadAllText path
            Assert.Contains("Customer", text)
            Assert.Contains("replace", text)
    finally
        (try File.Delete path with _ -> ())

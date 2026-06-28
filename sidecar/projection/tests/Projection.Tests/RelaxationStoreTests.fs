module Projection.Tests.RelaxationStoreTests

open System
open System.IO
open Xunit
open Projection.Cli

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

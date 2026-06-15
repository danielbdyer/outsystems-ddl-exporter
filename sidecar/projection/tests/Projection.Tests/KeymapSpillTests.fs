namespace Projection.Tests

// Slice S — the at-scale keymap SPILL (REVERSE_LEG_WORK_PLAN §3 Slice S). The
// pure chooser (threshold + estimate → residence) is DB-free; the equivalence
// canary proves the spilled server-side `UPDATE…JOIN` re-point produces a
// byte-identical sink state to the resident per-row re-point over the same
// captured keymap — the work plan's "spill-on vs resident → byte-identical sink
// state" witness, at the mechanism grain. Serial via Docker-SqlServer.

open Xunit
open Projection.Core
open Projection.Pipeline

module KeymapSpillPure =

    // -- the chooser: pure + total, testable without a connection --------------

    [<Fact>]
    let ``KeymapResidence.choose: no threshold is ALWAYS resident (the inert default)`` () =
        Assert.Equal<KeymapResidence>(KeymapResidence.Resident, KeymapResidence.choose None 0)
        Assert.Equal<KeymapResidence>(KeymapResidence.Resident, KeymapResidence.choose None 200_000_000)

    [<Fact>]
    let ``KeymapResidence.choose: a threshold spills strictly ABOVE it, resident at or below`` () =
        Assert.Equal<KeymapResidence>(KeymapResidence.Resident, KeymapResidence.choose (Some 1000) 1000)
        Assert.Equal<KeymapResidence>(KeymapResidence.Resident, KeymapResidence.choose (Some 1000) 999)
        Assert.Equal<KeymapResidence>(KeymapResidence.Spilled,  KeymapResidence.choose (Some 1000) 1001)

    [<Fact>]
    let ``KeymapResidence.describe: the armed spill is NARRATED (no silent path change)`` () =
        let resident = KeymapResidence.describe KeymapResidence.Resident None 50
        Assert.Contains("resident", resident)
        let spilled = KeymapResidence.describe KeymapResidence.Spilled (Some 10) 50
        Assert.Contains("SPILLED", spilled)
        Assert.Contains("UPDATE", spilled)


[<Xunit.Collection("Docker-SqlServer")>]
type KeymapSpillEquivalenceTests(fixture: EphemeralContainerFixture) =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// Read the FK column for every row, ordered by ID — the comparable sink state.
    let fkState (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT [ID], [FK] FROM %s ORDER BY [ID];" table
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<int * int option>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then
                    let fk = if reader.IsDBNull 1 then None else Some (reader.GetInt32 1)
                    acc.Add(reader.GetInt32 0, fk)
                else go <- false
            return List.ofSeq acc
        }

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``Slice S: the spilled UPDATE…JOIN re-point produces a byte-identical sink state to the resident per-row re-point (observationally pure)`` () =
        if not (skipIfNoDocker "SliceSEquivalence") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "SliceSSpill" (fun cnn _ ->
                task {
                    // Two identical sink tables, each holding SOURCE surrogates in
                    // their FK column (>= 1000), one row (9999) with NO mapping.
                    let kindKey = "OS_TEST::Account"
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[T_resident] ([ID] INT NOT NULL PRIMARY KEY, [FK] INT NULL); " +
                             "CREATE TABLE [dbo].[T_spill]    ([ID] INT NOT NULL PRIMARY KEY, [FK] INT NULL); " +
                             "INSERT INTO [dbo].[T_resident] ([ID],[FK]) VALUES (10,1000),(20,1001),(30,1002),(40,9999); " +
                             "INSERT INTO [dbo].[T_spill]    ([ID],[FK]) VALUES (10,1000),(20,1001),(30,1002),(40,9999);")

                    // The captured (source → assigned) pairs, identical for both backends.
                    let pairs = [ ("1000", "500"); ("1001", "501"); ("1002", "502") ]

                    // RESIDENT re-point — the PackedSurrogateRemap per-row path
                    // (the essence of the streaming phase2Chunks loop): tryFind the
                    // FK's source value, UPDATE that row. An unmatched FK is left.
                    let remap = PackedSurrogateRemap.create ()
                    for (s, a) in pairs do PackedSurrogateRemap.capture (SsKey.synthesizedComposite "OS_TEST" [ "Account" ] |> Result.value) s a remap
                    let kKey = SsKey.synthesizedComposite "OS_TEST" [ "Account" ] |> Result.value
                    let! residentRows = fkState cnn "[dbo].[T_resident]"
                    for (id, fk) in residentRows do
                        match fk with
                        | Some v ->
                            match PackedSurrogateRemap.tryFind remap kKey (string v) with
                            | Some assigned -> do! Deploy.executeBatch cnn (sprintf "UPDATE [dbo].[T_resident] SET [FK] = %s WHERE [ID] = %d;" assigned id)
                            | None -> ()
                        | None -> ()

                    // SPILLED re-point — capture the SAME pairs to the session
                    // #-temp keymap, then ONE server-side UPDATE…JOIN per kind.
                    do! SqlKeymap.createTable cnn
                    do! SqlKeymap.captureMany cnn kindKey pairs
                    do! SqlKeymap.repointJoin cnn (TableId.create "dbo" "T_spill" |> Result.value) kindKey "FK"

                    // Byte-identical sink state: both re-point 1000→500, 1001→501,
                    // 1002→502, and leave the unmatched 9999 untouched.
                    let! residentFinal = fkState cnn "[dbo].[T_resident]"
                    let! spillFinal     = fkState cnn "[dbo].[T_spill]"
                    Assert.Equal<(int * int option) list>(residentFinal, spillFinal)
                    Assert.Equal<(int * int option) list>(
                        [ (10, Some 500); (20, Some 501); (30, Some 502); (40, Some 9999) ], spillFinal)
                }))

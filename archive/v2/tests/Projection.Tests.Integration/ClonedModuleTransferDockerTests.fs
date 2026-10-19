namespace Projection.Tests

open Xunit
open Projection.Core
open Projection.Pipeline
open PeerEstateHarness

// The cloned-module (name-aligned) peer leg at the LIVE seam (2026-07-09).
// Cell B is a CLONE of cell A's estate — same logical entity/attribute names +
// structure, but every native GUID `SS_Key` RE-MINTED (`OssysSeedBuilder.
// asClonedModule`), so the two contracts share NO identity and cannot align by
// SsKey. `NameAlignment.align` re-keys the source's identities to the sink's BY
// NAME; the SAME peer engine the SsKey-aligned witnesses use then lands the
// rows with FKs re-pointed. Contrast `PeerAlignedTransferDockerTests` (same-GUID
// cells). Serial via the Docker-SqlServer collection.
[<Xunit.Collection("Docker-SqlServer")>]
type ClonedModuleTransferDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// A subset transfer between two CLONED modules: the raw contracts share no
    /// SsKey (the shape gate refuses), but after `NameAlignment.align` maps the
    /// AppCore identities across by name, the City/Customer subset loads into the
    /// clone sink with the FK re-pointed by identity.
    [<Fact>]
    member _.``witness: a subset transfer between cloned modules aligns by name and lands rows with FKs re-pointed`` () =
        if not (skipIfNoDocker "ClonedModule") then () else
        run2CellWith fixture "ClonedModule" (OssysSeedBuilder.asClonedModule "X") (fun src sink srcConnStr sinkConnStr srcContract sinkContract ->
            task {
                do! Deploy.executeBatch src sourceRows
                // Control: WITHOUT alignment the re-minted clone shares no identity
                // with the source, so the shape gate reads every subset entity as
                // absent and refuses — the by-SsKey leg genuinely cannot do this.
                let subset = Transfer.resolveLoadSet srcContract [ "City"; "Customer" ] |> value
                match PeerTransfer.shapeGate subset srcContract sinkContract with
                | Ok _ -> failwith "un-aligned cloned contracts must not read as one shape"
                | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.peer.shapeDivergence")
                // Align AppCore's identities across by name (strict over the
                // transferred City/Customer subset), then run the peer engine.
                let alignMap = Map.ofList [ "AppCore", "AppCore" ]
                let aligned = NameAlignment.alignForMode AlignmentMode.ByName alignMap [ "City"; "Customer" ] srcContract sinkContract |> value
                // The aligned pair now reads as one shape over the subset.
                match PeerTransfer.shapeGate subset aligned sinkContract with
                | Error es -> failwithf "the aligned pair must read as one shape: %A" (es |> List.map (fun e -> e.Code))
                | Ok _ -> ()
                // Reseed the sink IDENTITY ranges away from the source surrogates
                // (City→501+, Customer→901+) so a verbatim FK copy CANNOT pass — only
                // a genuine remap through sink-minted keys satisfies the join below.
                do! Deploy.executeBatch sink reseedSinkIdentities
                let! r =
                    throughConnections srcConnStr sinkConnStr false (fun connections ->
                        TransferActs.blessAllAndRun (fun blessings ->
                            Transfer.runReverseLegThroughConnections
                                Transfer.Execute EmissionMode.Incremental false true false
                                [ "City"; "Customer" ] connections aligned sinkContract Map.empty Set.empty [] blessings Set.empty))
                let _ = value r     // the name-aligned load succeeds
                // The subset rows land in the CLONE sink (its own physical layout).
                let! cities = countRows sink "[dbo].[OSUSR_XDEF_CITY]"
                let! customers = countRows sink "[dbo].[OSUSR_XABC_CUSTOMER]"
                Assert.Equal(2, cities)
                Assert.Equal(2, customers)
                // The Customer→City FK is re-pointed through sink-minted keys, NOT
                // copied verbatim: no customer keeps a source CityId (1/2), and no
                // customer dangles (every CITYID resolves to a sink City row).
                let! verbatimFks = countRows sink "[dbo].[OSUSR_XABC_CUSTOMER] WHERE [CITYID] IN (1, 2)"
                Assert.Equal(0, verbatimFks)
                let! danglingFks =
                    countRows sink "[dbo].[OSUSR_XABC_CUSTOMER] c WHERE NOT EXISTS (SELECT 1 FROM [dbo].[OSUSR_XDEF_CITY] ci WHERE ci.[ID] = c.[CITYID])"
                Assert.Equal(0, danglingFks)
            })

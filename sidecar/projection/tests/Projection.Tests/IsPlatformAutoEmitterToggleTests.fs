module Projection.Tests.IsPlatformAutoEmitterToggleTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Chapter 4.8 slice γ — EmissionPolicy.IncludePlatformAutoIndexes toggle.
//
// V1 reference: SsdtManifestOptions.IncludePlatformAutoIndexes. V2 lifts
// to EmissionPolicy + EmissionPolicy.filterPlatformAutoIndexes Catalog-
// projection. Per A18 amended: filter lives at composition layer; the
// emitter consumes the filtered Catalog.
// ---------------------------------------------------------------------------

let private autoIdx = mkIndex (testKey "Auto") (Name.create "IX_Auto" |> Result.value) []
let private autoIdx' = { autoIdx with IsPlatformAuto = true }
let private userIdx = mkIndex (testKey "User") (Name.create "IX_User" |> Result.value) []

let private kindWithBothIndexes =
    let physical = TableId.create "dbo" "T" |> Result.value
    let k = mkKind (testKey "K") (Name.create "K" |> Result.value) physical []
    { k with Indexes = [ autoIdx'; userIdx ] }

let private mod1 = mkModule (testKey "M") (Name.create "M" |> Result.value) [ kindWithBothIndexes ]
let private catalogBoth = mkCatalog [ mod1 ]

[<Fact>]
let ``EmissionPolicy defaults to IncludePlatformAutoIndexes = true (V1 parity)`` () =
    Assert.True EmissionPolicy.empty.IncludePlatformAutoIndexes
    Assert.True EmissionPolicy.combined.IncludePlatformAutoIndexes
    Assert.True EmissionPolicy.dataOnly.IncludePlatformAutoIndexes

[<Fact>]
let ``filterPlatformAutoIndexes: policy.true returns catalog unchanged`` () =
    let policy = EmissionPolicy.empty  // IncludePlatformAutoIndexes = true
    let filtered = EmissionPolicy.filterPlatformAutoIndexes policy catalogBoth
    let kindAfter = filtered.Modules.[0].Kinds.[0]
    Assert.Equal (2, kindAfter.Indexes.Length)

[<Fact>]
let ``filterPlatformAutoIndexes: policy.false prunes platform-auto indexes`` () =
    let policy =
        EmissionPolicy.empty
        |> EmissionPolicy.withIncludePlatformAutoIndexes false
    let filtered = EmissionPolicy.filterPlatformAutoIndexes policy catalogBoth
    let kindAfter = filtered.Modules.[0].Kinds.[0]
    Assert.Equal (1, kindAfter.Indexes.Length)
    Assert.False kindAfter.Indexes.[0].IsPlatformAuto

[<Fact>]
let ``filterPlatformAutoIndexes: T1 byte-determinism`` () =
    let policy =
        EmissionPolicy.empty
        |> EmissionPolicy.withIncludePlatformAutoIndexes false
    let r1 = EmissionPolicy.filterPlatformAutoIndexes policy catalogBoth
    let r2 = EmissionPolicy.filterPlatformAutoIndexes policy catalogBoth
    Assert.Equal<Catalog> (r1, r2)

[<Fact>]
let ``withIncludePlatformAutoIndexes: pure setter preserves other axes`` () =
    let policy = EmissionPolicy.combined
    let toggled = policy |> EmissionPolicy.withIncludePlatformAutoIndexes false
    Assert.Equal (policy.EmitSchema, toggled.EmitSchema)
    Assert.Equal (policy.EmitData, toggled.EmitData)
    Assert.Equal (policy.EmitDiagnostics, toggled.EmitDiagnostics)
    Assert.Equal (policy.DataComposition, toggled.DataComposition)
    Assert.False toggled.IncludePlatformAutoIndexes

// ---------------------------------------------------------------------------
// Chapter 4.9 slice δ — Compose.project wiring. The EmissionPolicy
// reaches the post-chain filter seam; SSDT bundle reflects the filter
// when IncludePlatformAutoIndexes = false.
// ---------------------------------------------------------------------------

let private countCreateIndexInBundle (bundle: Map<string, string>) : int =
    bundle
    |> Map.toSeq
    |> Seq.sumBy (fun (_, body) ->
        let mutable n = 0
        let mutable i = 0
        while i >= 0 do
            i <- body.IndexOf("CREATE", i + 1)
            if i >= 0 then
                let tail = body.Substring(i)
                if tail.StartsWith("CREATE INDEX") || tail.StartsWith("CREATE UNIQUE INDEX") then
                    n <- n + 1
        n)

[<Fact>]
let ``Slice δ: Compose.project with IncludePlatformAutoIndexes=true keeps platform-auto indexes`` () =
    let outputs = Compose.project EmissionPolicy.empty catalogBoth
    Assert.Equal(2, countCreateIndexInBundle outputs.SsdtBundle)

[<Fact>]
let ``Slice δ: Compose.project with IncludePlatformAutoIndexes=false prunes platform-auto indexes`` () =
    let policy =
        EmissionPolicy.empty
        |> EmissionPolicy.withIncludePlatformAutoIndexes false
    let outputs = Compose.project policy catalogBoth
    Assert.Equal(1, countCreateIndexInBundle outputs.SsdtBundle)

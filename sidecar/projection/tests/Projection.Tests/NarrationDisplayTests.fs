module Projection.Tests.NarrationDisplayTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql          // IntegrityReport / RowCountDelta / NullCountDelta + the `row` measure
open Projection.Cli
open Projection.Tests.Fixtures

/// Cross-surface legibility (the displayName chapter): the run/apply narration
/// surfaces are SsKey-keyed, and `SsKey.rootOriginal` is a bare GUID for an
/// `OssysOriginal` key — so on a real OSSYS estate they were a wall of hex. Where a
/// narration face holds a `Catalog`, it now names by `Name` (the diff already did).
/// Proven with OssysOriginal (GUID) keys — the synthesized fixtures can't catch it.

[<Fact>]
let ``verify-data payload names the table by Name, not its GUID rootOriginal`` () =
    let g = System.Guid("c0000000-0000-0000-0000-000000000003")
    let kindK = SsKey.ossysOriginal g
    let kind =
        Kind.create kindK (mkName "Orders") (mkTableId "dbo" "OSUSR_X_ORDERS")
            [ Attribute.create (attrKey ["Orders"; "Id"]) (mkName "Id") Integer ]
    let contract = Catalog.create [ IRBuilders.mkModule (modKey "X") (mkName "X") [ kind ] ] [] |> Result.value
    let report : IntegrityReport =
        { RowCountDeltas  = [ { Kind = kindK; Before = 10L<row>; After = 12L<row> } ]
          NullCountDeltas = []
          Warnings        = [] }
    let rowDeltas = RunFaces.integrityPayload contract report |> Map.find "rowDeltas" |> string
    Assert.Contains("Orders", rowDeltas)                    // the readable Name
    Assert.DoesNotContain("c0000000", rowDeltas)            // not the GUID

[<Fact>]
let ``verify-data payload names a column by Name in the null-count deltas`` () =
    let kindK = SsKey.ossysOriginal (System.Guid("c0000000-0000-0000-0000-000000000004"))
    let attrK = SsKey.ossysOriginal (System.Guid("c0000000-0000-0000-0000-000000000005"))
    let attr = { Attribute.create attrK (mkName "Email") Text with SsKey = attrK }
    let kind = Kind.create kindK (mkName "Customer") (mkTableId "dbo" "OSUSR_X_CUSTOMER") [ attr ]
    let contract = Catalog.create [ IRBuilders.mkModule (modKey "X") (mkName "X") [ kind ] ] [] |> Result.value
    let report : IntegrityReport =
        { RowCountDeltas  = []
          NullCountDeltas = [ { Kind = kindK; Attribute = attrK; Before = 0L<row>; After = 3L<row> } ]
          Warnings        = [] }
    let nullDeltas = RunFaces.integrityPayload contract report |> Map.find "nullDeltas" |> string
    Assert.Contains("Customer", nullDeltas)
    Assert.Contains("Email", nullDeltas)
    Assert.DoesNotContain("c0000000", nullDeltas)

module Projection.Tests.JointDependencyDiagnosticsTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// H-026 — JointDependencyDiagnostics.emit contract tests.
/// Three structural cases: empty profile (identity), below-threshold
/// uniqueness (no entry), above-threshold uniqueness (entry emitted).

let private kindKey (name: string) : SsKey =
    SsKey.synthesized "KIND" name |> Result.value

let private attrKey (name: string) : SsKey =
    SsKey.synthesized "ATTR" name |> Result.value

let private mkJoint
    (kKey: SsKey)
    (attrKeys: SsKey list)
    (frequencies: (string * int64) list)
    (distinctCount: int64)
    (isTruncated: bool)
    : JointDistribution =
    JointDistribution.create
        kKey attrKeys frequencies distinctCount isTruncated
        (ProbeStatus.observed distinctCount)
    |> Result.value

[<Fact>]
let ``H-026: empty profile → empty diagnostics`` () =
    let result = JointDependencyDiagnostics.emit Profile.empty
    Assert.Empty result

[<Fact>]
let ``H-026: low uniqueness ratio → no entry`` () =
    // 10 distinct pairs, 100 rows → uniquenessRatio = 0.10 (well below 0.95)
    let jd =
        mkJoint
            (kindKey "Orders")
            [ attrKey "CustomerId"; attrKey "RegionId" ]
            [ for i in 1..10 -> string i, 10L ]
            10L false
    let profile = { Profile.empty with JointDistributions = [ jd ] }
    let result = JointDependencyDiagnostics.emit profile
    Assert.Empty result

[<Fact>]
let ``H-026: below minDistinctCount guard → no entry`` () =
    // Only 3 distinct pairs (< 5 guard), even if uniquenessRatio would be 1.0
    let jd =
        mkJoint
            (kindKey "TinyTable")
            [ attrKey "AId"; attrKey "BId" ]
            [ "1|2", 1L; "3|4", 1L; "5|6", 1L ]
            3L false
    let profile = { Profile.empty with JointDistributions = [ jd ] }
    let result = JointDependencyDiagnostics.emit profile
    Assert.Empty result

[<Fact>]
let ``H-026: near-unique co-occurrence → Info entry emitted`` () =
    // 19 distinct pairs, 20 rows → uniquenessRatio = 0.95 (at threshold)
    let jd =
        mkJoint
            (kindKey "Orders")
            [ attrKey "CustomerId"; attrKey "ProductId" ]
            // 18 unique pairs + 1 pair appearing twice
            ([ for i in 1..18 -> sprintf "%d|%d" i i, 1L ]
             @ [ "99|99", 2L ])
            19L false
    let profile = { Profile.empty with JointDistributions = [ jd ] }
    let result = JointDependencyDiagnostics.emit profile
    Assert.Single result |> ignore
    let entry = List.head result
    Assert.Equal(DiagnosticSeverity.Info, entry.Severity)
    Assert.Equal("profiling.jointDistribution.nearUniqueComposite", entry.Code)
    Assert.Equal(Some jd.KindKey, entry.SsKey)
    Assert.True(entry.Metadata.ContainsKey "distinctCount")
    Assert.True(entry.Metadata.ContainsKey "uniquenessRatio")
    Assert.True(entry.Metadata.ContainsKey "attributeCount")

[<Fact>]
let ``H-026: perfectly unique pairs → Info entry emitted`` () =
    // 10 rows, 10 distinct pairs → uniquenessRatio = 1.0
    let jd =
        mkJoint
            (kindKey "UniqueOrderLines")
            [ attrKey "OrderId"; attrKey "LineId" ]
            [ for i in 1..10 -> sprintf "%d|%d" i i, 1L ]
            10L false
    let profile = { Profile.empty with JointDistributions = [ jd ] }
    let result = JointDependencyDiagnostics.emit profile
    Assert.Single result |> ignore
    let entry = List.head result
    Assert.Equal("2", entry.Metadata["attributeCount"])

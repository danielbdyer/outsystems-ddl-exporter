module Projection.Tests.SsdtArtifactEmitterTests

open System.Xml.Linq
open Xunit
open Projection.Targets.SSDT

// ============================================================================
// PostDeployEmitter + SqlprojEmitter — the SSDT-deploy artifacts (the .sqlproj
// + post-deployment script that let V2's bundle stand up via `dotnet build` →
// dacpac → publish, with static seeds + migration in the post-deploy and the
// bootstrap MERGE run separately). Pure-string / typed-XML emitters; the live
// deploy is exercised by the Docker E2E + .sqlproj-build tests.
// ============================================================================

// Null-narrowing helpers for LINQ-to-XML (the test project has F# nullness on).
let private rootOf (xml: string) : XElement =
    match (XDocument.Parse xml).Root with
    | null -> failwith "empty XML document"
    | r -> r

let private attrVal (el: XElement) (attr: string) : string =
    match el.Attribute(XName.Get attr) with
    | null -> failwithf "missing attribute '%s'" attr
    | a -> a.Value

let private includeValues (root: XElement) (elem: string) (attr: string) : string list =
    root.Descendants(XName.Get elem)
    |> Seq.choose (fun (e: XElement) ->
        match e.Attribute(XName.Get attr) with
        | null -> None
        | a -> Some a.Value)
    |> List.ofSeq

// -- PostDeployEmitter -------------------------------------------------------

[<Fact>]
let ``PostDeploy: renderIncludes emits one :r per lane, in deploy order, bootstrap excluded`` () =
    let s = PostDeployEmitter.renderIncludes [ "Data/StaticSeeds.sql"; "Data/MigrationData.sql" ]
    Assert.Contains(":r Data/StaticSeeds.sql", s)
    Assert.Contains(":r Data/MigrationData.sql", s)
    Assert.True(
        s.IndexOf(":r Data/StaticSeeds.sql") < s.IndexOf(":r Data/MigrationData.sql"),
        "static seeds must :r before migration data")
    // The bootstrap lane is a SEPARATE post-publish step, never in the post-deploy.
    Assert.DoesNotContain("Bootstrap.sql", s)

[<Fact>]
let ``PostDeploy: renderIncludes with no lanes notes the empty case (no :r)`` () =
    let s = PostDeployEmitter.renderIncludes []
    Assert.DoesNotContain(":r ", s)
    Assert.Contains("no data lanes", s)

[<Fact>]
let ``PostDeploy: renderInlined concatenates lane SQL under banners and skips empty lanes`` () =
    let s =
        PostDeployEmitter.renderInlined
            [ "StaticSeeds", "MERGE [dbo].[Country] ...;"
              "MigrationData", "   "
              "Migration2", "MERGE [dbo].[Role] ...;" ]
    Assert.Contains("MERGE [dbo].[Country] ...;", s)
    Assert.Contains("MERGE [dbo].[Role] ...;", s)
    Assert.Contains("-- ---- StaticSeeds ----", s)
    // a whitespace-only lane contributes nothing (no banner, no body)
    Assert.DoesNotContain("-- ---- MigrationData ----", s)

// -- SqlprojEmitter ----------------------------------------------------------

[<Fact>]
let ``Sqlproj: emit is well-formed XML pinning the Microsoft.Build.Sql SDK`` () =
    let root = rootOf (SqlprojEmitter.emit [ "Data/StaticSeeds.sql"; "Data/MigrationData.sql" ] true)
    Assert.Equal("Project", root.Name.LocalName)
    Assert.Equal("Microsoft.Build.Sql/" + SqlprojEmitter.sdkVersion, attrVal root "Sdk")

[<Fact>]
let ``Sqlproj: data lanes are None + removed from the Build glob; post-deploy is PostDeploy; schema .sql not enumerated`` () =
    let xml = SqlprojEmitter.emit [ "Data/StaticSeeds.sql"; "Data/MigrationData.sql" ] true
    let root = rootOf xml
    // post-deploy script is the conventional PostDeploy item
    Assert.Equal<string list>([ PostDeployEmitter.fileName ], includeValues root "PostDeploy" "Include")
    // data lanes ride None (not schema), and are removed from the default Build glob
    let nones = includeValues root "None" "Include" |> Set.ofList
    Assert.Contains("Data/StaticSeeds.sql", nones)
    Assert.Contains("Data/MigrationData.sql", nones)
    let removes =
        root.Descendants(XName.Get "Build")
        |> Seq.choose (fun (e: XElement) ->
            match e.Attribute(XName.Get "Remove") with null -> None | a -> Some a.Value)
        |> Set.ofSeq
    Assert.Contains("Data/StaticSeeds.sql", removes)
    Assert.Contains(PostDeployEmitter.fileName, removes)
    // the per-table schema .sql are NOT enumerated — they ride the SDK default glob
    Assert.DoesNotContain("Modules/", xml)

[<Fact>]
let ``Sqlproj: emit without a post-deploy omits the PostDeploy item`` () =
    let root = rootOf (SqlprojEmitter.emit [] false)
    Assert.Empty(root.Descendants(XName.Get "PostDeploy"))

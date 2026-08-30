module Projection.Tests.ComposeEmitRefusalTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// -----------------------------------------------------------------------
// schema-L3.3a — the compose seam NAMES the emission refusal.
//
// The #669 pre-flight gates (`SsdtDdlEmitter.emissionRefusal`) always
// refused the BUNDLE path, but the refusal surfaced as `invalidOp` →
// a generic aborted run. Now `Compose.projectFromChainWithState` runs the
// pre-flight BEFORE rendering and the config-driven path
// (`projectWithConfig` / `runFromCatalogWith` / `runWithConfigCore`)
// returns the NAMED `emitter.ssdt.*` ValidationError — with the atomic-
// write guarantee intact (no artifact lands on a refusal).
//
// Three arms: refusal (named error + NO artifact), agreement (a clean
// catalog publishes; the artifact carries the real CREATE TRIGGER and no
// defense marker), and the gate-domain theorem (gate-pass ⟹ render-Some:
// a definition the strengthened `tryParseTriggerDefinition` accepts never
// hits the render marker — including the comments-only edge that
// previously slipped the gate).
// -----------------------------------------------------------------------

let private newTempRoot () : string =
    let path =
        Path.Combine(
            Path.GetTempPath(),
            sprintf "compose-refusal-%s" (Guid.NewGuid().ToString("N").Substring(0, 12)))
    Directory.CreateDirectory(path) |> ignore
    path

let private withTempRoot (action: string -> 'a) : 'a =
    let root = newTempRoot ()
    try action root
    finally
        if Directory.Exists root then
            try Directory.Delete(root, recursive = true) with _ -> ()

let private mustOkT (r: Result<Trigger>) : Trigger =
    match r with
    | Ok t -> t
    | Error e -> failwithf "trigger fixture: %A" e

/// `sampleCatalog` with one trigger (of the given body) on the Customer kind.
let private withCustomerTrigger (definition: string) : Catalog =
    let trigger =
        Trigger.create
            (attrKey [ "Customer"; "TrgAudit" ]) (Name.create "TRG_CUSTOMER_AUDIT" |> Result.value)
            false definition
        |> mustOkT
    let m =
        { salesModule with
            Kinds =
                salesModule.Kinds
                |> List.map (fun k ->
                    if k.SsKey = customerKey then { k with Triggers = [ trigger ] } else k) }
    { sampleCatalog with Modules = [ m ] }

[<Fact>]
let ``M2 closure (refusal arm): an unparseable trigger refuses the publish by name (emitter.ssdt.triggerUnparsed) and writes NO artifact`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        let catalog = withCustomerTrigger "THIS IS NOT VALID TSQL @@ )("
        match Compose.runFromCatalogWith Config.defaultConfig catalog outputDir with
        | Ok paths -> failwithf "expected the named refusal; the publish wrote %A" paths
        | Error errors ->
            let head = List.head errors
            Assert.Equal("emitter.ssdt.triggerUnparsed", head.Code)
            Assert.Equal(Some (Some "TRG_CUSTOMER_AUDIT"), Map.tryFind "trigger" head.Metadata)
            // The atomicity guarantee: the refusal preceded every write.
            Assert.False(Directory.Exists outputDir, "a refused publish must write NO artifact"))

[<Fact>]
let ``M2 closure (agreement arm): a parseable trigger publishes; the artifact carries its CREATE TRIGGER and no defense marker`` () =
    withTempRoot (fun root ->
        let outputDir = Path.Combine(root, "out")
        // The body speaks LOGICAL names — the gate's second predicate
        // (`firstPhysicalResidue`) refuses a surviving OSUSR_* physical
        // identifier, exactly as it should.
        let catalog =
            withCustomerTrigger
                "CREATE TRIGGER [dbo].[TRG_CUSTOMER_AUDIT] ON [dbo].[Customer] AFTER INSERT AS BEGIN SET NOCOUNT ON END"
        match Compose.runFromCatalogWith Config.defaultConfig catalog outputDir with
        | Error errors -> failwithf "expected the publish to land: %A" errors
        | Ok _ ->
            let bodies =
                Directory.EnumerateFiles(outputDir, "*.sql", SearchOption.AllDirectories)
                |> Seq.map File.ReadAllText
                |> String.concat "\n"
            Assert.Contains("CREATE TRIGGER", bodies)
            Assert.DoesNotContain("projection defense", bodies))

[<Fact>]
let ``M2 closure (gate-domain theorem): gate-pass implies render — a definition the gate accepts never hits the defense marker`` () =
    // The strengthened `tryParseTriggerDefinition` demands a first statement
    // in the first batch (the renderer's success domain), so gate-pass ⟹
    // render-Some by construction. The corpus includes the comments-only
    // edge that previously PASSED the gate and still hit the marker.
    let corpus =
        [ "CREATE TRIGGER [dbo].[T1] ON [dbo].[W] AFTER INSERT AS BEGIN SET NOCOUNT ON END"
          "-- comments only, no statement"
          "CREATE TRIGGER [dbo].[T2] ON [dbo].[W] AFTER UPDATE AS UPDATE [dbo].[W] SET [A] = [A]"
          "   "
          "THIS IS NOT VALID TSQL @@ )(" ]
    for definition in corpus do
        match ScriptDomGenerate.tryParseTriggerDefinition definition with
        | Error _ -> ()   // the gate refuses; the render is never reached on a gated lane
        | Ok () ->
            let rendered = Render.toText [ Statement.CreateTrigger definition ]
            Assert.False(
                rendered.Contains "projection defense",
                sprintf "gate-pass must imply render: %s" definition)

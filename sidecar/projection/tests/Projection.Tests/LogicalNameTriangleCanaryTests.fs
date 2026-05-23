[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.LogicalNameTriangleCanaryTests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Tests.SourceFixtures

// ---------------------------------------------------------------------------
// Slice D.1.c — canary triangle assertion.
//
// The triangle property closes chapter D's logical-name-emission arc:
//
//     source.Kind.Name = target.Kind.Name           (logical identity preserved)
//     source.Kind.Name = target.Kind.Physical.Table (V2 substituted)
//
// Both legs verified through ephemeral SQL Server:
//
//   1. Source DDL with `V2.LogicalName` extended properties is deployed.
//   2. ReadSide (slice D.1.b) hydrates `Kind.Name` from the property,
//      producing a source catalog with divergent `Kind.Name = "Customer"`
//      vs `Kind.Physical.Table = "OSUSR_*"`.
//   3. The slice-D.1.a pipeline (LogicalTableEmission + LogicalColumnEmission)
//      substitutes the logical name into the physical-realization slot.
//   4. V2 emits SSDT with logical-shaped CREATE TABLE + V2.LogicalName
//      extended properties carrying the logical names.
//   5. Deploy to target; ReadSide hydrates `Kind.Name` from extended
//      property on target readback.
//   6. Triangle predicate over `LogicalNameBinding` sets:
//      (a) every source binding's `(Schema, Column, LogicalName)` triple
//          appears in target; every target binding's same triple appears
//          in source — logical identity preserved set-wise (Table field
//          deliberately projected out because it differs by design;
//          source's is the OSSYS shape, target's is the logical name).
//      (b) every target binding satisfies `binding.Table = binding.LogicalName`
//          at the table level and `binding.Column = Some binding.LogicalName`
//          at the column level — substitution worked.
//
// Three Docker-bound facts exercise the property on the canary fixture
// (`fixtures/canary-gate.sql` after slice-D.1.c augmentation; same
// shape as `SourceSchema.realistic`).
// ---------------------------------------------------------------------------

module private LogicalNameTriangleCanaryFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn
                "SKIP %s: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run roundtrip tests."
                label
            false

    /// Pipeline-emit function: applies the slice-D.1.a substitution
    /// chain to the source catalog before emitting SSDT. Mirrors the
    /// production chain's relevant prefix (LogicalTableEmission +
    /// LogicalColumnEmission both Enabled), then delegates to
    /// `SsdtDdlEmitter.statements`. Suitable for `runWideCanary`'s
    /// `emit : Catalog -> seq<Statement>` parameter.
    let pipelineEmit (source: Catalog) : seq<Statement> =
        let substituted =
            source
            |> (LogicalTableEmission.registered LogicalTableEmission.Enabled).Run
            |> LineageDiagnostics.bind (fun c ->
                (LogicalColumnEmission.registered LogicalColumnEmission.Enabled).Run c)
            |> fun ld -> ld.Value.Value
        SsdtDdlEmitter.statements substituted

    /// Project a binding to its logical-identity triple. Drop the
    /// `Table` field (source's is OSSYS-shape, target's is logical
    /// — they differ by design under substitution) and drop the
    /// `Column` field at the column-level case (source's is the
    /// OSSYS column name, target's is the logical column name). Use
    /// the table-level binding's LogicalName as the table identifier;
    /// use the binding's own LogicalName as the column identifier.
    /// Both halves project to the same identity under the
    /// substitution-then-recovery chain.
    type IdentityTriple =
        {
            Schema : string
            TableLogicalName : string
            ColumnLogicalName : string option
        }

    /// Look up a binding's owning table's logical name. Returns None
    /// when the binding's owning table-level binding is absent —
    /// that's a structural fault separate from the triangle property;
    /// the diff-comparator surface catches it.
    let private tableLogicalNameFor
        (allBindings: Set<LogicalNameBinding>)
        (b: LogicalNameBinding)
        : string option =
        allBindings
        |> Seq.tryPick (fun other ->
            if other.Schema = b.Schema
               && other.Table = b.Table
               && other.Column = None
            then Some other.LogicalName
            else None)

    let identityOf
        (allBindings: Set<LogicalNameBinding>)
        (b: LogicalNameBinding)
        : IdentityTriple option =
        tableLogicalNameFor allBindings b
        |> Option.map (fun tableLogical ->
            { Schema = b.Schema
              TableLogicalName = tableLogical
              ColumnLogicalName =
                match b.Column with
                | None -> None
                | Some _ -> Some b.LogicalName })

    /// Triangle violation taxonomy. Each variant names a specific way
    /// the property fails — operator-readable when the canary fires.
    type TriangleViolation =
        | SourceIdentityMissingInTarget of triple: IdentityTriple
        | TargetIdentityMissingInSource of triple: IdentityTriple
        | TargetTableNotLogicalName of binding: LogicalNameBinding
        | TargetColumnNotLogicalName of binding: LogicalNameBinding

    let triangleViolations
        (source: PhysicalSchema)
        (target: PhysicalSchema)
        : TriangleViolation list =
        let sourceTriples =
            source.LogicalNameBindings
            |> Set.toSeq
            |> Seq.choose (identityOf source.LogicalNameBindings)
            |> Set.ofSeq
        let targetTriples =
            target.LogicalNameBindings
            |> Set.toSeq
            |> Seq.choose (identityOf target.LogicalNameBindings)
            |> Set.ofSeq
        let missing =
            Set.difference sourceTriples targetTriples
            |> Set.toList
            |> List.map SourceIdentityMissingInTarget
        let extra =
            Set.difference targetTriples sourceTriples
            |> Set.toList
            |> List.map TargetIdentityMissingInSource
        let substitutionFailures =
            target.LogicalNameBindings
            |> Set.toList
            |> List.collect (fun b ->
                match b.Column with
                | None ->
                    if b.Table = b.LogicalName then []
                    else [ TargetTableNotLogicalName b ]
                | Some col ->
                    if col = b.LogicalName then []
                    else [ TargetColumnNotLogicalName b ])
        missing @ extra @ substitutionFailures

    let renderViolation (v: TriangleViolation) : string =
        match v with
        | SourceIdentityMissingInTarget t ->
            sprintf "  source identity missing in target: schema=%s table-logical=%s column-logical=%A"
                t.Schema t.TableLogicalName t.ColumnLogicalName
        | TargetIdentityMissingInSource t ->
            sprintf "  target identity missing in source: schema=%s table-logical=%s column-logical=%A"
                t.Schema t.TableLogicalName t.ColumnLogicalName
        | TargetTableNotLogicalName b ->
            sprintf "  target table-binding: deployed Table=%s ≠ LogicalName=%s"
                b.Table b.LogicalName
        | TargetColumnNotLogicalName b ->
            sprintf "  target column-binding: deployed Column=%A ≠ LogicalName=%s (on table %s)"
                b.Column b.LogicalName b.Table

open LogicalNameTriangleCanaryFixtures

// ---------------------------------------------------------------------------
// Docker-bound triangle canary. Uses the operator-reality source
// fixture (post-slice-D.1.c augmentation); applies the pipeline-emit
// chain that includes substitution; asserts the triangle predicate.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice D.1.c triangle: pipeline-emit on realistic source preserves logical identity AND substitutes physical = logical`` () =
    if not (skipIfNoDocker "d1c-triangle-realistic") then () else
    let task =
        Deploy.runWideCanary
            SourceSchema.realistic
            pipelineEmit
    let outcome = task.GetAwaiter().GetResult()
    let report =
        match outcome with
        | Ok r -> r
        | Error errors ->
            let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
            failwithf "triangle canary failed: %s" codes
    Assert.True(report.SourceReport.Ok, "source deploy failed")
    Assert.True(report.TargetReport.Ok, "target deploy failed")
    let sourceSchema = PhysicalSchema.ofCatalog report.Source
    let targetSchema = PhysicalSchema.ofCatalog report.Target
    let violations = triangleViolations sourceSchema targetSchema
    Assert.True(
        List.isEmpty violations,
        sprintf
            "Triangle property violated:\n%s"
            (violations |> List.map renderViolation |> String.concat "\n"))

[<Fact>]
let ``Slice D.1.c triangle: source catalog from canary-gate fixture has divergent Kind.Name vs Physical.Table`` () =
    // Pre-check confirming the fixture really exercises divergence.
    // If this fails, the augmentation got lost (e.g., V2.LogicalName
    // extended-property statements were dropped from the SQL), so
    // the triangle test above passes trivially. Guard the test
    // surface from silent fixture drift.
    if not (skipIfNoDocker "d1c-triangle-divergence") then () else
    let task =
        Deploy.runWideCanary
            SourceSchema.realistic
            pipelineEmit
    let outcome = task.GetAwaiter().GetResult()
    let report =
        match outcome with
        | Ok r -> r
        | Error errors ->
            let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
            failwithf "triangle canary failed: %s" codes
    let sourceKinds = Catalog.allKinds report.Source
    let divergentKinds =
        sourceKinds
        |> List.filter (fun k -> Name.value k.Name <> k.Physical.Table)
    Assert.NotEmpty divergentKinds
    // Smoke check on the expected fixture: User and Customer should both diverge.
    let userKind = divergentKinds |> List.tryFind (fun k -> k.Physical.Table = "OSUSR_M3_USER")
    let customerKind = divergentKinds |> List.tryFind (fun k -> k.Physical.Table = "OSUSR_M3_CUSTOMER")
    Assert.True(userKind.IsSome, "expected OSUSR_M3_USER to have a recovered logical Kind.Name")
    Assert.True(customerKind.IsSome, "expected OSUSR_M3_CUSTOMER to have a recovered logical Kind.Name")
    Assert.Equal("User", Name.value userKind.Value.Name)
    Assert.Equal("Customer", Name.value customerKind.Value.Name)

[<Fact>]
let ``Slice D.1.c triangle: target catalog from pipeline-emit has Physical.Table = Kind.Name (substitution worked)`` () =
    // Pre-check on the target side. If this fails, the pipeline-emit
    // path silently fell through to raw-emit (substitution didn't
    // run); the triangle predicate would still hold on identity
    // alone but the "physical = logical" leg becomes vacuous.
    if not (skipIfNoDocker "d1c-triangle-substitution") then () else
    let task =
        Deploy.runWideCanary
            SourceSchema.realistic
            pipelineEmit
    let outcome = task.GetAwaiter().GetResult()
    let report =
        match outcome with
        | Ok r -> r
        | Error errors ->
            let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
            failwithf "triangle canary failed: %s" codes
    let targetKinds = Catalog.allKinds report.Target
    Assert.NotEmpty targetKinds
    Assert.All(
        targetKinds,
        fun k -> Assert.Equal(Name.value k.Name, k.Physical.Table))

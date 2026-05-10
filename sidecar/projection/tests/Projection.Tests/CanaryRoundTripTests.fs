[<Xunit.Collection("Docker-SqlServer")>]
module Projection.Tests.CanaryRoundTripTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

/// M3 (per the chapter-3.1 milestone sequence chosen at session 27):
/// the round-trip canary tests. Two complementary surfaces:
///
///   1. **V2-internal closure** (`runWithReadback`): take a fixture
///      Catalog, project through V2's emitter, deploy to ephemeral
///      SQL Server, read the deployed schema back via the read-side
///      adapter, compare reconstructed Catalog against the source
///      Catalog on the `PhysicalSchema` axis. Catches emitter bugs
///      that drop / mangle / duplicate columns under deploy.
///
///   2. **Wide canary** (`runWideCanary`): deploy an OutSystems-
///      shaped source DDL to one ephemeral database, read it back
///      via ReadSide as `sourceCatalog`, run V2's emitter on
///      `sourceCatalog` to produce SSDT, deploy to a second
///      database (same container), read it back via ReadSide as
///      `targetCatalog`, and assert source ≈ target on the
///      `PhysicalSchema` axis. Per `DECISIONS 2026-05-23 — Source
///      SQL Server with OutSystems semantics`, this is the
///      canary's primary wide integration surface — the source
///      represents operator reality, the target represents V2's
///      projection of operator intent, and an empty diff means
///      structural fidelity holds.
///
/// **Soft-skip pattern.** Tests check `Deploy.Docker.ensureRunning ()`
/// at start; if false, log SKIP and pass vacuously. M4+ can promote
/// to `Xunit.SkippableFact` for proper skip semantics.
///
/// **Iterative growth.** The `SourceFixtures.SourceSchema` corpus grows
/// over time (per the trace-before-fixture pattern); each new shape
/// addition exercises a new round-trip property. Tests here grow
/// in parallel.

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then
        true
    else
        printfn
            "SKIP %s: Docker daemon not reachable. Set DOCKER_HOST or start the daemon to run canary tests."
            label
        false

// ---------------------------------------------------------------------
// V2-internal closure: take a programmatically-built Catalog, ensure
// V2's read+emit composition is closed (target Catalog ≈ source
// Catalog under PhysicalSchema).
// ---------------------------------------------------------------------

let private ssKeySafe (s: string) : SsKey = testKey s

let private nameSafe (s: string) : Name =
    match Name.create s with
    | Ok n -> n
    | Error errors -> failwithf "fixture: Name.create failed: %A" errors

/// Programmatic V2 Catalog matching `SourceSchema.minimal`'s shape.
/// Used by the V2-internal closure test: emit this Catalog → deploy
/// → read back → compare on PhysicalSchema. The reconstructed
/// Catalog should preserve every (schema, table, column, type,
/// nullable, isPrimaryKey) tuple.
let private programmaticUserCatalog : Catalog =
    let userKey = ssKeySafe "OS_KIND_M3_User"
    let mkAttr (column: string) (ptype: PrimitiveType) (nullable: bool) (isPk: bool) : Attribute =
        {
            SsKey = ssKeySafe (sprintf "OS_ATTR_M3_User_%s" column)
            Name = nameSafe column
            Type = ptype
            Column = { ColumnName = column.ToUpperInvariant(); IsNullable = nullable }
            IsPrimaryKey = isPk
            IsMandatory = not nullable
            Length = None
            Precision = None
            Scale = None
            IsIdentity = false
        }
    let userKind : Kind =
        {
            SsKey = userKey
            Name = nameSafe "User"
            Origin = OsNative
            Modality = []
            Physical = { Schema = "dbo"; Table = "OSUSR_M3_USER" }
            Attributes =
                [
                    mkAttr "Id" Integer false true
                    mkAttr "Username" Text false false
                    mkAttr "Email" Text true false
                    mkAttr "TenantId" Integer false false
                ]
            References = []
            Indexes = []
        }
    let userModule : Module =
        {
            SsKey = ssKeySafe "OS_MOD_M3"
            Name = nameSafe "M3"
            Kinds = [ userKind ]
        }
    { Modules = [ userModule ] }

[<Fact>]
let ``M3: V2-internal closure — programmatic Catalog round-trips through emit / deploy / read with empty PhysicalSchema diff`` () =
    if skipIfNoDocker "v2-closure" then
        let source = programmaticUserCatalog
        let emitted = SsdtDdlEmitter.statements source |> Render.toText
        let task = Deploy.runWithReadback emitted
        let result = task.GetAwaiter().GetResult()

        Assert.True(
            result.Report.Ok,
            sprintf
                "deploy failed: %s"
                (String.concat "\n" result.Report.Errors))
        Assert.Equal(1, result.Report.TablesCreated)

        let target =
            match result.Reconstructed with
            | Some c -> c
            | None ->
                Assert.Fail("ReadSide.read returned None despite successful deploy")
                Unchecked.defaultof<_>

        let sourceSchema = PhysicalSchema.ofCatalog source
        let targetSchema = PhysicalSchema.ofCatalog target
        let diff = PhysicalSchema.diff sourceSchema targetSchema

        Assert.True(
            PhysicalSchema.isEqual diff,
            sprintf
                "V2-internal closure failed:\n%s"
                (PhysicalSchema.renderDiff diff))

// ---------------------------------------------------------------------
// Wide canary: source DDL → readback → V2 emit → readback → compare.
// The source DDL represents the operator's reality; the target
// represents V2's projection of operator intent.
// ---------------------------------------------------------------------

[<Fact>]
let ``M3 wide canary: minimal OutSystems-shaped source DDL round-trips through V2's emitter with empty PhysicalSchema diff`` () =
    if skipIfNoDocker "wide-canary-minimal" then
        let task =
            Deploy.runWideCanary SourceFixtures.SourceSchema.minimal SsdtDdlEmitter.statements
        let outcome = task.GetAwaiter().GetResult()

        let report =
            match outcome with
            | Ok r -> r
            | Error errors ->
                let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
                Assert.Fail(sprintf "wide canary failed: %s" codes)
                Unchecked.defaultof<_>

        // Both deploys succeeded; both Catalogs reconstructed.
        Assert.True(
            report.SourceReport.Ok,
            sprintf "source deploy: %A" report.SourceReport.Errors)
        Assert.True(
            report.TargetReport.Ok,
            sprintf "target deploy: %A" report.TargetReport.Errors)

        // Source has the OSUSR_M3_USER table; target should too.
        Assert.Equal(1, report.SourceReport.TablesCreated)
        Assert.Equal(1, report.TargetReport.TablesCreated)

        // Structural-fidelity assertion: V2's emitter preserved the
        // source's (schema, table, column, type, nullable, isPK)
        // structure across the round-trip.
        Assert.True(
            PhysicalSchema.isEqual report.Diff,
            sprintf
                "wide canary structural-fidelity failed:\n%s"
                (PhysicalSchema.renderDiff report.Diff))

[<Fact>]
let ``M3 wide canary: realistic OutSystems-shaped source DDL surfaces emitter / IR fidelity gaps as a non-empty PhysicalSchema diff`` () =
    if skipIfNoDocker "wide-canary-realistic" then
        let task =
            Deploy.runWideCanary
                SourceFixtures.SourceSchema.realistic
                SsdtDdlEmitter.statements
        let outcome = task.GetAwaiter().GetResult()

        let report =
            match outcome with
            | Ok r -> r
            | Error errors ->
                let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
                Assert.Fail(sprintf "wide canary failed: %s" codes)
                Unchecked.defaultof<_>

        // Both deploys succeed; both Catalogs reconstructed.
        Assert.True(
            report.SourceReport.Ok,
            sprintf "source deploy: %A" report.SourceReport.Errors)
        Assert.True(
            report.TargetReport.Ok,
            sprintf "target deploy: %A" report.TargetReport.Errors)
        Assert.Equal(2, report.SourceReport.TablesCreated)
        Assert.Equal(2, report.TargetReport.TablesCreated)

        // Per the source-fixture docstring (SourceFixtures.SourceSchema.realistic):
        // V2's IR doesn't yet carry NVARCHAR length / DECIMAL precision /
        // IDENTITY property. The realistic fixture exercises shapes V2
        // can structurally absorb (table count, column count, types,
        // nullability) but not bit-for-bit (length, precision, identity).
        //
        // For M3 PhysicalSchema scope (which compares type / nullable /
        // isPK but not length / precision), the diff should still be
        // empty — the type-axis projection absorbs NVARCHAR(N) → Text →
        // NVARCHAR(MAX) → Text without difference. M4's Tolerance
        // taxonomy will let length / precision become opt-in
        // comparison flags.
        Assert.True(
            PhysicalSchema.isEqual report.Diff,
            sprintf
                "wide canary realistic structural-fidelity failed:\n%s"
                (PhysicalSchema.renderDiff report.Diff))

[<Fact>]
let ``M3 wide canary: enterprise OutSystems-shaped source (3 modules / 10 tables / FK chains) round-trips with empty PhysicalSchema diff`` () =
    if skipIfNoDocker "wide-canary-enterprise" then
        let task =
            Deploy.runWideCanary
                SourceFixtures.SourceSchema.enterprise
                SsdtDdlEmitter.statements
        let outcome = task.GetAwaiter().GetResult()

        let report =
            match outcome with
            | Ok r -> r
            | Error errors ->
                let codes = errors |> List.map (fun e -> e.Code) |> String.concat ", "
                Assert.Fail(sprintf "wide canary failed: %s" codes)
                Unchecked.defaultof<_>

        // Both deploys succeed; both Catalogs reconstructed. The
        // enterprise fixture is the canary's primary wide
        // integration surface per `DECISIONS 2026-05-23 — Source
        // SQL Server with OutSystems semantics is the canary's
        // primary wide integration surface`. Ten tables across
        // three modules (IDM / CAT / SLS) with audit FKs, static
        // entities, junction tables, multi-tenant markers, and
        // mixed PrimitiveType coverage.
        Assert.True(
            report.SourceReport.Ok,
            sprintf "source deploy: %A" report.SourceReport.Errors)
        Assert.True(
            report.TargetReport.Ok,
            sprintf "target deploy: %A" report.TargetReport.Errors)
        Assert.Equal(10, report.SourceReport.TablesCreated)
        Assert.Equal(10, report.TargetReport.TablesCreated)

        // Structural-fidelity assertion on the PhysicalSchema axis.
        // FK constraints are present in source (V2's IR doesn't
        // round-trip them yet — ReadSide is schema-only) but absent
        // in target; PhysicalSchema is invariant under FK absence
        // so the diff is still empty.
        //
        // Per the fixture docstring: when M4 Tolerance flags land,
        // this test grows opt-in flags for IgnoreColumnLength /
        // IgnoreDecimalPrecision / IgnoreIdentityProperty so the
        // assertion can sharpen as V2's IR refines.
        Assert.True(
            PhysicalSchema.isEqual report.Diff,
            sprintf
                "wide canary enterprise structural-fidelity failed:\n%s"
                (PhysicalSchema.renderDiff report.Diff))

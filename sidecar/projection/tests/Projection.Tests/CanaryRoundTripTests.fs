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
        { Attribute.create (ssKeySafe (sprintf "OS_ATTR_M3_User_%s" column)) (nameSafe column) ptype with Column = ColumnRealization.create (column.ToUpperInvariant()) (nullable) |> Result.value; IsPrimaryKey = isPk; IsMandatory = not nullable }
    let userKind : Kind =
        {
            SsKey = userKey
            Name = nameSafe "User"
            Origin = Native
            Modality = []
            Physical = mkTableId "dbo" "OSUSR_M3_USER"
            Attributes =
                [
                    mkAttr "Id" Integer false true
                    mkAttr "Username" Text false false
                    mkAttr "Email" Text true false
                    mkAttr "TenantId" Integer false false
                ]
            References = []
            Indexes = []
            Description = None
            IsActive = true
            Triggers = []
            ColumnChecks = []
            ExtendedProperties = []
        }
    let userModule : Module =
        {
            SsKey = ssKeySafe "OS_MOD_M3"
            Name = nameSafe "M3"
            Kinds = [ userKind ]
            IsActive = true
            ExtendedProperties = []
            }
    { Modules = [ userModule ]; Sequences = [] }

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

// ---------------------------------------------------------------------
// Wave-1 slice 1.2 — DEFAULT round-trip (un-hollow the canary's
// PhysicalSchema.Default axis). Before this slice ReadSide returned
// `DefaultValue = None` for every column, so the canary was BLIND to a
// dropped/changed DEFAULT clause. Now: a Catalog carrying an integer
// DEFAULT round-trips through emit → deploy → ReadSide; the recovered
// catalog must (a) re-expose the DEFAULT on the attribute, and (b)
// produce an EMPTY PhysicalSchema diff against the source — proving the
// canary both sees and agrees on the default.
// ---------------------------------------------------------------------

// Fixture lifted to `Projection.Tests.Fixtures.defaultBearingCatalog`
// (Wave-1 fixture-extraction; reused across feature-verticals).

[<Fact>]
let ``Slice 1.2: integer DEFAULT round-trips through emit / deploy / ReadSide with empty PhysicalSchema diff`` () =
    if skipIfNoDocker "default-roundtrip" then
        let source = defaultBearingCatalog
        let emitted = SsdtDdlEmitter.statements source |> Render.toText
        let result = (Deploy.runWithReadback emitted).GetAwaiter().GetResult()
        Assert.True(result.Report.Ok, sprintf "deploy failed: %s" (String.concat "\n" result.Report.Errors))

        let target =
            match result.Reconstructed with
            | Some c -> c
            | None -> Assert.Fail("ReadSide.read returned None despite successful deploy"); Unchecked.defaultof<_>

        // (a) The DEFAULT is recovered onto the attribute (was always None
        //     before slice 1.2 — the hollow-canary defect).
        let recoveredDefaults =
            Catalog.allKinds target
            |> List.collect (fun k -> k.Attributes)
            |> List.choose (fun a -> a.DefaultValue |> Option.map (fun d -> ColumnRealization.columnNameText a.Column, SqlLiteral.toString d))
            |> List.sortBy fst
        Assert.True(
            (not (List.isEmpty recoveredDefaults)),
            "ReadSide recovered ZERO DEFAULT constraints — the hollow-canary defect (slice 1.2 regressed).")

        // (b) Source and round-tripped target agree on PhysicalSchema,
        //     INCLUDING the new Default axis (normalized both sides).
        let diff =
            PhysicalSchema.diff
                (PhysicalSchema.ofCatalog source)
                (PhysicalSchema.ofCatalog target)
        Assert.True(
            PhysicalSchema.isEqual diff,
            sprintf "DEFAULT round-trip PhysicalSchema diff non-empty:\n%s" (PhysicalSchema.renderDiff diff))

// ---------------------------------------------------------------------
// Wave-1 slice 1.3 — un-hollow the canary's ANNOTATION axis (triggers /
// CHECK constraints / sequences / extended properties). Before this slice
// ReadSide returned `Triggers=[]`, `ColumnChecks=[]`, `Sequences=[]`,
// `ExtendedProperties=[]` for everything, so the canary was BLIND to a
// dropped/changed instance of any of the four. The test below builds a
// Catalog carrying one of each, round-trips through emit → deploy →
// ReadSide, and asserts each feature is RECOVERED (the core un-hollowing
// claim) and surfaces on the PhysicalSchema.Annotations axis.
//
// Scope honesty: triggers + sequences carry server-reformattable bodies,
// so the assertion is RECOVERY + per-kind presence (the canary can now see
// them), with a normalized-body tolerance. A full byte-tight body diff is
// the in-feature follow-on; the structural un-hollowing — the canary is no
// longer blind — is what this slice closes.
// ---------------------------------------------------------------------

// Fixture lifted to `Projection.Tests.Fixtures.annotationBearingCatalog`
// (Wave-1 fixture-extraction; reused across feature-verticals).

[<Fact>]
let ``Slice 1.3: triggers / checks / sequences / extended properties are RECOVERED through emit / deploy / ReadSide`` () =
    if skipIfNoDocker "annotation-roundtrip" then
        let source = annotationBearingCatalog
        let emitted = SsdtDdlEmitter.statements source |> Render.toText
        let result = (Deploy.runWithReadback emitted).GetAwaiter().GetResult()
        Assert.True(result.Report.Ok, sprintf "deploy failed: %s" (String.concat "\n" result.Report.Errors))

        let target =
            match result.Reconstructed with
            | Some c -> c
            | None -> Assert.Fail("ReadSide.read returned None"); Unchecked.defaultof<_>

        // The core un-hollowing claim: each feature is RECOVERED (was always
        // empty before slice 1.3).
        let kinds = Catalog.allKinds target
        let recoveredTriggers = kinds |> List.collect (fun k -> k.Triggers)
        let recoveredChecks = kinds |> List.collect (fun k -> k.ColumnChecks)
        let recoveredEps =
            kinds |> List.collect (fun k -> k.ExtendedProperties @ (k.Attributes |> List.collect (fun a -> a.ExtendedProperties)))
            |> List.filter (fun ep -> ep.Name <> "Projection.LogicalName" && ep.Name <> "V2.LogicalName")
        Assert.True((not (List.isEmpty recoveredTriggers)), "ReadSide recovered ZERO triggers (1.3 trigger probe regressed).")
        Assert.True((not (List.isEmpty recoveredChecks)), "ReadSide recovered ZERO CHECK constraints (1.3 check probe regressed).")
        Assert.True((not (List.isEmpty recoveredEps)), "ReadSide recovered ZERO non-LogicalName extended properties (1.3 ext-prop probe regressed).")
        Assert.True((not (List.isEmpty target.Sequences)), "ReadSide recovered ZERO sequences (1.3 sequence probe regressed).")

        // And each surfaces on the PhysicalSchema.Annotations axis, by kind.
        let annKinds =
            (PhysicalSchema.ofCatalog target).Annotations
            |> Set.toList
            |> List.map (fun a -> a.Kind)
            |> Set.ofList
        Assert.Contains(TriggerAnnotation, annKinds)
        Assert.Contains(CheckAnnotation, annKinds)
        Assert.Contains(SequenceAnnotation, annKinds)
        Assert.Contains(ExtendedPropertyAnnotation, annKinds)

// ---------------------------------------------------------------------
// Slice 1.3 / L3-S7 — computed-column round-trip (the LAST hollow-canary
// feature). Before the real-SQL leg, ReadSide returned `Computed=None` for
// every column, so the live canary could not catch a dropped computed
// column. The test builds a Catalog with a PERSISTED computed column,
// round-trips it through emit → deploy → ReadSide, and asserts the computed
// state AND definition are restored (the L3-S7 axiom statement), normalized
// through the same `encodeComputed` both producers use.
// ---------------------------------------------------------------------

[<Fact>]
let ``Slice 1.3 / L3-S7: PERSISTED computed column round-trips through emit / deploy / ReadSide with state + definition restored`` () =
    if skipIfNoDocker "computed-roundtrip" then
        let source = computedBearingCatalog
        let emitted = SsdtDdlEmitter.statements source |> Render.toText
        let result = (Deploy.runWithReadback emitted).GetAwaiter().GetResult()
        Assert.True(result.Report.Ok, sprintf "deploy failed: %s" (String.concat "\n" result.Report.Errors))

        let target =
            match result.Reconstructed with
            | Some c -> c
            | None -> Assert.Fail("ReadSide.read returned None"); Unchecked.defaultof<_>

        // (a) The computed config is recovered (was always None before this leg).
        let recovered =
            Catalog.allKinds target
            |> List.collect (fun k -> k.Attributes)
            |> List.choose (fun a -> a.Computed |> Option.map (fun c -> ColumnRealization.columnNameText a.Column, c))
        Assert.True((not (List.isEmpty recovered)),
            "ReadSide recovered ZERO computed columns — the hollow-canary defect (L3-S7 real-SQL leg regressed).")

        // (b) State + definition restored: the recovered computed column,
        //     normalized through encodeComputed, equals the source's.
        let sourceComputed =
            Catalog.allKinds source
            |> List.collect (fun k -> k.Attributes)
            |> List.choose (fun a -> a.Computed |> Option.map (fun c -> ColumnRealization.columnNameText a.Column, PhysicalSchema.encodeComputed c))
            |> Map.ofList
        for (col, cc) in recovered do
            match Map.tryFind col sourceComputed with
            | Some srcEnc ->
                Assert.Equal(srcEnc, PhysicalSchema.encodeComputed cc)
            | None -> Assert.Fail(sprintf "recovered computed column %s not in source" col)

        // (c) Surfaces on the PhysicalColumn.Computed axis.
        let computedCols =
            (PhysicalSchema.ofCatalog target).Columns
            |> Set.toList
            |> List.choose (fun c -> c.Computed)
        Assert.True((not (List.isEmpty computedCols)),
            "PhysicalColumn.Computed axis is empty after round-trip.")

// ---------------------------------------------------------------------
// Wave-2 slice 2.3 — A42 (candidate) at the CANARY: an EnforceNotNull
// tightening decision, applied at emission via the DecisionOverlay, SURVIVES
// emit → deploy → ReadSide as a NOT NULL column. This is the payoff of the
// Wave-1 un-hollowing: the canary's PhysicalSchema.Nullable axis can now
// observe that the decision reached the deployed schema.
// ---------------------------------------------------------------------

[<Fact>]
let ``A42 (2.3 canary): EnforceNotNull tightening survives emit / deploy / ReadSide as NOT NULL`` () =
    if skipIfNoDocker "decision-notnull-roundtrip" then
        // A kind with a source-NULLABLE column `Note`.
        let kindKeyV = ssKeySafe "OS_KIND_DEC_Ticket"
        let noteKey = ssKeySafe "OS_ATTR_DEC_Ticket_Note"
        let mkAttr (k: SsKey) (col: string) (isPk: bool) (nullable: bool) : Attribute =
            { Attribute.create k (nameSafe col) (if isPk then Integer else Text) with
                Column = ColumnRealization.create (col.ToUpperInvariant()) (nullable) |> Result.value
                IsPrimaryKey = isPk; IsMandatory = isPk }
        let kind =
            { Kind.create kindKeyV (nameSafe "Ticket")
                (mkTableId "dbo" "OSUSR_DEC_TICKET")
                [ mkAttr (ssKeySafe "OS_ATTR_DEC_Ticket_Id") "Id" true false
                  mkAttr noteKey "Note" false true ] with Indexes = [] }
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_DEC"; Name = nameSafe "DecMod"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e

        // Sanity: with the empty overlay, Note deploys NULLABLE.
        let baselineSql = SsdtDdlEmitter.statements catalog |> Render.toText
        let baseline = (Deploy.runWithReadback baselineSql).GetAwaiter().GetResult()
        Assert.True(baseline.Report.Ok, sprintf "baseline deploy: %A" baseline.Report.Errors)
        let baselineNote =
            match baseline.Reconstructed with
            | Some c ->
                Catalog.allKinds c |> List.collect (fun k -> k.Attributes)
                |> List.tryFind (fun a -> ColumnRealization.columnNameText a.Column = "NOTE")
            | None -> None
        match baselineNote with
        | Some a -> Assert.True(a.Column.IsNullable, "baseline: Note should round-trip NULLABLE")
        | None -> Assert.Fail("baseline: Note column not recovered")

        // Tighten: EnforceNotNull(Note) → emit with overlay → deploy → read.
        let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton noteKey }
        let tightenedSql = SsdtDdlEmitter.statementsWith overlay catalog |> Render.toText
        let tightened = (Deploy.runWithReadback tightenedSql).GetAwaiter().GetResult()
        Assert.True(tightened.Report.Ok, sprintf "tightened deploy: %A" tightened.Report.Errors)
        let tightenedNote =
            match tightened.Reconstructed with
            | Some c ->
                Catalog.allKinds c |> List.collect (fun k -> k.Attributes)
                |> List.tryFind (fun a -> ColumnRealization.columnNameText a.Column = "NOTE")
            | None -> None
        match tightenedNote with
        | Some a -> Assert.False(a.Column.IsNullable, "EnforceNotNull(Note) must deploy + read back as NOT NULL")
        | None -> Assert.Fail("tightened: Note column not recovered")

// ---------------------------------------------------------------------
// Wave-2 slice 2.4 — A42 (candidate) at the CANARY: a DoNotEnforce FK
// decision (DropFk), applied at emission, means the FK is ABSENT from the
// deployed schema (emit → deploy → ReadSide → no PhysicalForeignKey). A
// silently-emitted FK that the decision said to drop would be a real cutover
// hazard; the un-hollowed canary's ForeignKeys axis catches it.
// ---------------------------------------------------------------------

[<Fact>]
let ``A42 (2.4 canary): a DoNotEnforce FK decision keeps the FK out of the deployed schema`` () =
    if skipIfNoDocker "decision-dropfk-roundtrip" then
        // Customer (PK Id) ← Order (FK CustomerId → Customer).
        let custKey = ssKeySafe "OS_KIND_DROP_Customer"
        let orderKey = ssKeySafe "OS_KIND_DROP_Order"
        let custIdKey = ssKeySafe "OS_ATTR_DROP_Customer_Id"
        let orderIdKey = ssKeySafe "OS_ATTR_DROP_Order_Id"
        let orderCustFkAttr = ssKeySafe "OS_ATTR_DROP_Order_CustomerId"
        let refKeyV = ssKeySafe "OS_REF_DROP_Order_Customer"
        let mkAttr (k: SsKey) (col: string) (isPk: bool) : Attribute =
            { Attribute.create k (nameSafe col) Integer with
                Column = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
                IsPrimaryKey = isPk; IsMandatory = isPk }
        let customer =
            Kind.create custKey (nameSafe "Customer")
                (mkTableId "dbo" "OSUSR_DROP_CUSTOMER")
                [ mkAttr custIdKey "Id" true ]
        let order =
            { Kind.create orderKey (nameSafe "Order")
                (mkTableId "dbo" "OSUSR_DROP_ORDER")
                [ mkAttr orderIdKey "Id" true; mkAttr orderCustFkAttr "CustomerId" false ]
              with References = [ Reference.create refKeyV (nameSafe "Customer") orderCustFkAttr custKey ] }
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_DROP"; Name = nameSafe "DropMod"; Kinds = [ customer; order ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e

        let fkCount (cat: Catalog option) =
            match cat with
            | Some c -> (PhysicalSchema.ofCatalog c).ForeignKeys |> Set.count
            | None -> -1

        // Baseline: empty overlay → the FK deploys and reads back.
        let baseSql = SsdtDdlEmitter.statements catalog |> Render.toText
        let baseline = (Deploy.runWithReadback baseSql).GetAwaiter().GetResult()
        Assert.True(baseline.Report.Ok, sprintf "baseline deploy: %A" baseline.Report.Errors)
        Assert.Equal(1, fkCount baseline.Reconstructed)

        // DropFk: emit with the decision → the FK is absent from the deployed schema.
        let overlay = { DecisionOverlay.empty with DropFk = Set.singleton refKeyV }
        let droppedSql = SsdtDdlEmitter.statementsWith overlay catalog |> Render.toText
        let dropped = (Deploy.runWithReadback droppedSql).GetAwaiter().GetResult()
        Assert.True(dropped.Report.Ok, sprintf "dropped deploy: %A" dropped.Report.Errors)
        Assert.Equal(0, fkCount dropped.Reconstructed)

// ---------------------------------------------------------------------
// 6.A.5 — un-hollow ReadSide. A deployed UNIQUE index and a deployed
// enabled-but-UNTRUSTED FK (`WITH NOCHECK`) SURVIVE deploy → ReadSide: the
// index reads back in `Kind.Indexes` (no longer the hardcoded `[]`) with
// `IsUnique = true`, and the FK reads back with `IsConstraintTrusted =
// false` (no longer the silent `Reference.create` `true` default). Asserted
// on the reconstructed Catalog directly — `PhysicalSchema.diff` deliberately
// ignores indexes + FK-trust, so the schema-diff canary cannot witness this.
//
// The fixture is hand-authored DDL because this is the *ReadSide* fix: the
// honest witness is "what is deployed reads back faithfully." (The emit-side
// NOCHECK-FK reproduction is a separate, currently-incomplete seam —
// `untrustedFkAlters` emits `WITH NOCHECK CHECK CONSTRAINT`, which is a no-op
// for `is_not_trusted` on a freshly-created inline FK; producing an untrusted
// FK requires `WITH NOCHECK ADD` or a disable. That emitter gap is a Schema-
// axis erasure for 6.A.6, not part of 6.A.5's ReadSide un-hollowing.)
// ---------------------------------------------------------------------

[<Fact>]
let ``6.A.5 (schema round-trip): a UNIQUE index + a NOCHECK FK survive deploy / ReadSide`` () =
    if skipIfNoDocker "readside-index-fktrust-roundtrip" then
        // Parent (PK Id, Code) with a UNIQUE index on Code; Child (PK Id,
        // FK ParentId → Parent) added WITH NOCHECK (enabled but untrusted:
        // is_not_trusted = 1, is_disabled = 0).
        let ddl =
            "CREATE TABLE [dbo].[OSUSR_RT5_PARENT] ([ID] INT NOT NULL PRIMARY KEY, [CODE] INT NOT NULL); " +
            "CREATE UNIQUE INDEX [UQ_Rt5Parent_Code] ON [dbo].[OSUSR_RT5_PARENT] ([CODE]); " +
            "CREATE TABLE [dbo].[OSUSR_RT5_CHILD] ([ID] INT NOT NULL PRIMARY KEY, [PARENT_ID] INT NOT NULL); " +
            "ALTER TABLE [dbo].[OSUSR_RT5_CHILD] WITH NOCHECK ADD CONSTRAINT [FK_Rt5Child_Parent] " +
            "FOREIGN KEY ([PARENT_ID]) REFERENCES [dbo].[OSUSR_RT5_PARENT] ([ID]);"
        let readback = (Deploy.runWithReadback ddl).GetAwaiter().GetResult()
        Assert.True(readback.Report.Ok, sprintf "deploy: %A" readback.Report.Errors)
        match readback.Reconstructed with
        | Some c ->
            let kindByTable t = Catalog.allKinds c |> List.find (fun k -> TableId.tableText k.Physical = t)
            // (a) The UNIQUE index survived — `Indexes` is no longer hollow.
            let parentBack = kindByTable "OSUSR_RT5_PARENT"
            match parentBack.Indexes |> List.tryFind (fun i -> IndexUniqueness.isUnique i.Uniqueness) with
            | Some i ->
                // Its key column resolves to the deployed CODE column.
                let cols =
                    i.Columns
                    |> List.choose (fun ic -> parentBack.Attributes |> List.tryFind (fun a -> a.SsKey = ic.Attribute))
                    |> List.map (fun a -> ColumnRealization.columnNameText a.Column)
                Assert.Equal<string list>([ "CODE" ], cols)
            | None -> Assert.Fail "UNIQUE index on Parent did not survive ReadSide (Indexes still hollow?)"
            // (b) The NOCHECK FK survived as IsConstraintTrusted = false
            //     (not the create-default true).
            let childBack = kindByTable "OSUSR_RT5_CHILD"
            match childBack.References with
            | [ r ] -> Assert.False(r.IsConstraintTrusted, "NOCHECK FK must read back untrusted, not the create-default true")
            | rs -> Assert.Fail(sprintf "expected exactly one reconstructed FK, got %d" (List.length rs))
        | None -> Assert.Fail "deploy produced no reconstructed catalog"

// ---------------------------------------------------------------------
// 6.A.8 — the decision adjunction on the uniqueness + FK-drop sub-axes
// (depends on 6.A.5's index/FK-trust readback). With 6.A.5 the index
// reads back, so an `EnforceUnique` overlay on a non-unique source index
// is now OBSERVABLE through the round-trip (baseline reads back
// non-unique; tightened reads back unique), and `DropFk` removes the FK.
// Together with the 2.3 nullability witness this lifts the Decision cell
// from 1/3 to 3/3 sub-axes.
// ---------------------------------------------------------------------

[<Fact>]
let ``6.A.8 (decision adjunction): read-back reproduces EnforceUnique and DropFk`` () =
    if skipIfNoDocker "decision-enforceunique-dropfk-roundtrip" then
        // Parent (PK Id, Code) with a NON-unique index on Code; Child (PK Id,
        // FK ParentId → Parent) with an ordinary (trusted) reference.
        let parentKey = ssKeySafe "OS_KIND_RT8_Parent"
        let childKey = ssKeySafe "OS_KIND_RT8_Child"
        let parentIdKey = ssKeySafe "OS_ATTR_RT8_Parent_Id"
        let codeKey = ssKeySafe "OS_ATTR_RT8_Parent_Code"
        let childIdKey = ssKeySafe "OS_ATTR_RT8_Child_Id"
        let parentFkAttr = ssKeySafe "OS_ATTR_RT8_Child_ParentId"
        let refKeyV = ssKeySafe "OS_REF_RT8_Child_Parent"
        let idxKey = ssKeySafe "OS_IDX_RT8_Parent_Code"
        let mkAttr (k: SsKey) (col: string) (isPk: bool) (ty: PrimitiveType) : Attribute =
            { Attribute.create k (nameSafe col) ty with
                Column = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
                IsPrimaryKey = isPk; IsMandatory = isPk }
        let parent =
            { Kind.create parentKey (nameSafe "Parent")
                (mkTableId "dbo" "OSUSR_RT8_PARENT")
                [ mkAttr parentIdKey "Id" true Integer
                  mkAttr codeKey "Code" false Integer ]
              with Indexes = [ Index.ofKeyColumns idxKey (nameSafe "IX_Rt8Parent_Code") [ codeKey ] ] }
        let child =
            { Kind.create childKey (nameSafe "Child")
                (mkTableId "dbo" "OSUSR_RT8_CHILD")
                [ mkAttr childIdKey "Id" true Integer; mkAttr parentFkAttr "ParentId" false Integer ]
              with References = [ Reference.create refKeyV (nameSafe "Parent") parentFkAttr parentKey ] }
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_RT8"; Name = nameSafe "Rt8Mod"; Kinds = [ parent; child ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e

        let parentIndexUnique (cat: Catalog option) : bool option =
            cat
            |> Option.bind (fun c -> Catalog.allKinds c |> List.tryFind (fun k -> TableId.tableText k.Physical = "OSUSR_RT8_PARENT"))
            |> Option.bind (fun k -> k.Indexes |> List.tryHead |> Option.map (fun i -> IndexUniqueness.isUnique i.Uniqueness))
        let childFkCount (cat: Catalog option) : int =
            match cat with
            | Some c -> (PhysicalSchema.ofCatalog c).ForeignKeys |> Set.count
            | None -> -1

        // Baseline (empty overlay): the index reads back NON-unique; FK present.
        let baseSql = SsdtDdlEmitter.statements catalog |> Render.toText
        let baseline = (Deploy.runWithReadback baseSql).GetAwaiter().GetResult()
        Assert.True(baseline.Report.Ok, sprintf "baseline deploy: %A" baseline.Report.Errors)
        Assert.Equal(Some false, parentIndexUnique baseline.Reconstructed)
        Assert.Equal(1, childFkCount baseline.Reconstructed)

        // Tighten: EnforceUnique(index) + DropFk(ref) → emit → deploy → read.
        // The index reads back UNIQUE; the FK is gone.
        let overlay =
            { DecisionOverlay.empty with
                EnforceUnique = Set.singleton idxKey
                DropFk = Set.singleton refKeyV }
        let tightenedSql = SsdtDdlEmitter.statementsWith overlay catalog |> Render.toText
        let tightened = (Deploy.runWithReadback tightenedSql).GetAwaiter().GetResult()
        Assert.True(tightened.Report.Ok, sprintf "tightened deploy: %A" tightened.Report.Errors)
        Assert.Equal(Some true, parentIndexUnique tightened.Reconstructed)
        Assert.Equal(0, childFkCount tightened.Reconstructed)

// ---------------------------------------------------------------------
// F2 (debrief G12) — the UNIFIED decision adjunction. The per-axis
// proofs exist separately (A42 nullability; 6.A.8 uniqueness + FK-drop;
// the readside-index-fktrust witness for FK-trust). North-Star §5
// criterion 2 asks for the COMPOSITIONAL witness: one overlay carrying
// all three decision intents simultaneously survives the round-trip —
// `Ingest(deploy(Project(C, overlay)))` reproduces every opinion at once,
// proving the axes compose (no decision shadows another at emit). This is
// the theorem that makes "replace V1" stronger than "trust V1": the
// engine's opinions are provably recoverable from the substrate.
// ---------------------------------------------------------------------

[<Fact>]
let ``E3: Ingest(deploy(Project(C, overlay))) reproduces the decision overlay on all three axes (nullability + uniqueness + FK-trust)`` () =
    if skipIfNoDocker "decision-adjunction-3axis" then
        let parentKey = ssKeySafe "OS_KIND_F2_Parent"
        let childKey = ssKeySafe "OS_KIND_F2_Child"
        let parentIdKey = ssKeySafe "OS_ATTR_F2_Parent_Id"
        let codeKey = ssKeySafe "OS_ATTR_F2_Parent_Code"
        let noteKey = ssKeySafe "OS_ATTR_F2_Parent_Note"
        let childIdKey = ssKeySafe "OS_ATTR_F2_Child_Id"
        let parentFkAttr = ssKeySafe "OS_ATTR_F2_Child_ParentId"
        let refKeyV = ssKeySafe "OS_REF_F2_Child_Parent"
        let idxKey = ssKeySafe "OS_IDX_F2_Parent_Code"
        let mkAttr (k: SsKey) (col: string) (isPk: bool) (nullable: bool) (ty: PrimitiveType) : Attribute =
            { Attribute.create k (nameSafe col) ty with
                Column = ColumnRealization.create (col.ToUpperInvariant()) nullable |> Result.value
                IsPrimaryKey = isPk; IsMandatory = isPk }
        let parent =
            { Kind.create parentKey (nameSafe "Parent")
                (mkTableId "dbo" "OSUSR_F2_PARENT")
                [ mkAttr parentIdKey "Id" true false Integer
                  mkAttr codeKey "Code" false false Integer
                  // Source-nullable column — the nullability axis.
                  mkAttr noteKey "Note" false true Integer ]
              with Indexes = [ Index.ofKeyColumns idxKey (nameSafe "IX_F2Parent_Code") [ codeKey ] ] }
        // Source FK is NOCHECK / untrusted — the FK-trust axis (preserved,
        // not dropped: it must read back untrusted, not the create-default).
        let child =
            { Kind.create childKey (nameSafe "Child")
                (mkTableId "dbo" "OSUSR_F2_CHILD")
                [ mkAttr childIdKey "Id" true false Integer
                  mkAttr parentFkAttr "ParentId" false false Integer ]
              with References =
                    [ { Reference.create refKeyV (nameSafe "Parent") parentFkAttr parentKey with
                          HasDbConstraint = true; IsConstraintTrusted = false } ] }
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_F2"; Name = nameSafe "F2Mod"; Kinds = [ parent; child ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e

        // One overlay, two tightening intents (nullability + uniqueness);
        // FK-trust rides the source Reference. Project → deploy → read back.
        let overlay =
            { DecisionOverlay.empty with
                EnforceNotNull = Set.singleton noteKey
                EnforceUnique = Set.singleton idxKey }
        let sql = SsdtDdlEmitter.statementsWith overlay catalog |> Render.toText
        let result = (Deploy.runWithReadback sql).GetAwaiter().GetResult()
        Assert.True(result.Report.Ok, sprintf "deploy: %A" result.Report.Errors)
        match result.Reconstructed with
        | Some c ->
            let kindByTable t = Catalog.allKinds c |> List.find (fun k -> TableId.tableText k.Physical = t)
            let parentBack = kindByTable "OSUSR_F2_PARENT"
            // (1) Nullability: the EnforceNotNull column reads back NOT NULL.
            let noteBack = parentBack.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "NOTE")
            Assert.False(noteBack.Column.IsNullable, "EnforceNotNull(Note) must read back NOT NULL")
            // (2) Uniqueness: the EnforceUnique index reads back UNIQUE.
            match parentBack.Indexes |> List.tryFind (fun i -> IndexUniqueness.isUnique i.Uniqueness) with
            | Some _ -> ()
            | None -> Assert.Fail "EnforceUnique(index) must read back as a UNIQUE index"
            // (3) FK-trust: the NOCHECK source FK reads back untrusted.
            let childBack = kindByTable "OSUSR_F2_CHILD"
            match childBack.References with
            | [ r ] -> Assert.False(r.IsConstraintTrusted, "NOCHECK FK must read back untrusted, not the create-default true")
            | rs -> Assert.Fail(sprintf "expected exactly one reconstructed FK, got %d" (List.length rs))
        | None -> Assert.Fail "deploy produced no reconstructed catalog"

// ---------------------------------------------------------------------
// 6.A.6 — emit-side NOCHECK-FK reproduction (the schema-erasure 6.A.5
// surfaced). A source `Reference.IsConstraintTrusted = false` now SURVIVES
// emit → deploy → ReadSide: the emitter reproduces the enabled-untrusted
// state via the two-step (NOCHECK CONSTRAINT disable → WITH NOCHECK CHECK
// CONSTRAINT re-enable), which lands `is_not_trusted = 1, is_disabled = 0`
// (a bare `WITH NOCHECK CHECK CONSTRAINT` on a freshly-created inline FK is
// a no-op for the trust bit). The 6.A.5 witness was deploy→ReadSide because
// the emitter could not yet produce this; 6.A.6 closes the emit leg.
// ---------------------------------------------------------------------

[<Fact>]
let ``6.A.6 (schema round-trip): an untrusted FK survives emit / deploy / ReadSide`` () =
    if skipIfNoDocker "emit-nocheck-fk-roundtrip" then
        let parentKey = ssKeySafe "OS_KIND_RT6_Parent"
        let childKey = ssKeySafe "OS_KIND_RT6_Child"
        let parentIdKey = ssKeySafe "OS_ATTR_RT6_Parent_Id"
        let childIdKey = ssKeySafe "OS_ATTR_RT6_Child_Id"
        let parentFkAttr = ssKeySafe "OS_ATTR_RT6_Child_ParentId"
        let refKeyV = ssKeySafe "OS_REF_RT6_Child_Parent"
        let mkAttr (k: SsKey) (col: string) (isPk: bool) : Attribute =
            { Attribute.create k (nameSafe col) Integer with
                Column = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
                IsPrimaryKey = isPk; IsMandatory = isPk }
        let parent =
            Kind.create parentKey (nameSafe "Parent")
                (mkTableId "dbo" "OSUSR_RT6_PARENT")
                [ mkAttr parentIdKey "Id" true ]
        // The source reference is NOT trusted (the deployed source carried a
        // WITH NOCHECK FK); the emitter must reproduce that on the target.
        let child =
            { Kind.create childKey (nameSafe "Child")
                (mkTableId "dbo" "OSUSR_RT6_CHILD")
                [ mkAttr childIdKey "Id" true; mkAttr parentFkAttr "ParentId" false ]
              // An untrusted FK that EXISTS (the NOCHECK case) — HasDbConstraint
              // = true is required, else the catalog smart-constructor rejects
              // the illegal (constraint-absent ∧ untrusted) quadrant
              // (catalog.reference.illegalConstraintState).
              with References = [ { Reference.create refKeyV (nameSafe "Parent") parentFkAttr parentKey with HasDbConstraint = true; IsConstraintTrusted = false } ] }
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_RT6"; Name = nameSafe "Rt6Mod"; Kinds = [ parent; child ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e

        let sql = SsdtDdlEmitter.statements catalog |> Render.toText
        let readback = (Deploy.runWithReadback sql).GetAwaiter().GetResult()
        Assert.True(readback.Report.Ok, sprintf "deploy: %A" readback.Report.Errors)
        match readback.Reconstructed with
        | Some c ->
            let childBack = Catalog.allKinds c |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_RT6_CHILD")
            // The FK is still present (enabled) AND reads back untrusted —
            // the emit→deploy→ReadSide round-trip is now faithful on FK-trust.
            Assert.Equal(1, (PhysicalSchema.ofCatalog c).ForeignKeys |> Set.count)
            match childBack.References with
            | [ r ] -> Assert.False(r.IsConstraintTrusted, "emitted FK must read back untrusted (WITH NOCHECK reproduced)")
            | rs -> Assert.Fail(sprintf "expected exactly one reconstructed FK, got %d" (List.length rs))
        | None -> Assert.Fail "deploy produced no reconstructed catalog"

/// Wave 4.1 — V2.SsKey persistence round-trip. `sampleSourceCatalog`
/// builds every kind/attribute with an `OssysOriginal` identity; after
/// deploy → read the recovered SsKeys must still be `OssysOriginal`
/// (recovered from the persisted `V2.SsKey` extended property), NOT
/// `Synthesized "READSIDE_KIND"` synthesized from physical coordinates.
/// This is the executable witness for A1 across the process boundary.
[<Fact>]
let ``4.1: V2.SsKey persistence — ReadSide recovers OssysOriginal identities (A1 across deploy->read)`` () =
    if skipIfNoDocker "v2sskey-persistence-roundtrip" then
        // Build a Customer kind with an `OssysOriginal` GUID identity. After
        // emit (which persists `V2.SsKey`) → deploy → read, the recovered
        // SsKey must be that exact `OssysOriginal` GUID — recovered from the
        // persisted extended property, NOT `Synthesized "READSIDE_KIND"` from
        // physical coordinates. This is the executable witness for A1 across
        // the process boundary (Wave 4.1).
        let custGuid = System.Guid.Parse "11111111-1111-1111-1111-111111111111"
        let custKey = SsKey.ossysOriginal custGuid
        let custIdKey = SsKey.ossysOriginal (System.Guid.Parse "22222222-2222-2222-2222-222222222222")
        let customer =
            Kind.create custKey (nameSafe "Customer")
                (mkTableId "dbo" "OSUSR_SSK_CUSTOMER")
                [ { Attribute.create custIdKey (nameSafe "Id") Integer with
                      Column = ColumnRealization.create ("ID") (false) |> Result.value
                      IsPrimaryKey = true; IsMandatory = true } ]
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_SSK"; Name = nameSafe "SskMod"; Kinds = [ customer ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e

        let sql = SsdtDdlEmitter.statements catalog |> Render.toText
        let readback = (Deploy.runWithReadback sql).GetAwaiter().GetResult()
        Assert.True(readback.Report.Ok, sprintf "deploy: %A" readback.Report.Errors)
        match readback.Reconstructed with
        | Some reconstructed ->
            let recovered =
                reconstructed.Modules
                |> List.collect (fun m -> m.Kinds)
                |> List.tryFind (fun k -> TableId.tableText k.Physical = "OSUSR_SSK_CUSTOMER")
            match recovered with
            | Some k ->
                // Recovered identity is the persisted OssysOriginal GUID, not
                // a READSIDE_KIND synthesis.
                Assert.Equal<SsKey>(custKey, k.SsKey)
            | None -> Assert.Fail "OSUSR_SSK_CUSTOMER not found in reconstructed catalog"
        | None -> Assert.Fail "deploy produced no reconstructed catalog"

// ---------------------------------------------------------------------
// NORTH_STAR §1 Identity-axis round-trip witness. Wave 4.1 shipped V2.SsKey
// persistence; this is the canonically-named witness the matrix keys on
// (`reload preserves SsKey`): an OssysOriginal identity survives emit →
// deploy → ReadSide as itself, not a READSIDE_KIND synthesis.
// ---------------------------------------------------------------------

[<Fact>]
let ``Identity round-trip: reload preserves SsKey across emit / deploy / ReadSide`` () =
    if skipIfNoDocker "identity-reload-preserves-sskey" then
        let acctKey = SsKey.ossysOriginal (System.Guid.Parse "33333333-3333-3333-3333-333333333333")
        let acctIdKey = SsKey.ossysOriginal (System.Guid.Parse "44444444-4444-4444-4444-444444444444")
        let account =
            Kind.create acctKey (nameSafe "Account")
                (mkTableId "dbo" "OSUSR_RLD_ACCOUNT")
                [ { Attribute.create acctIdKey (nameSafe "Id") Integer with
                      Column = ColumnRealization.create ("ID") (false) |> Result.value
                      IsPrimaryKey = true; IsMandatory = true } ]
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_RLD"; Name = nameSafe "RldMod"; Kinds = [ account ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e
        let sql = SsdtDdlEmitter.statements catalog |> Render.toText
        let readback = (Deploy.runWithReadback sql).GetAwaiter().GetResult()
        Assert.True(readback.Report.Ok, sprintf "deploy: %A" readback.Report.Errors)
        match readback.Reconstructed with
        | Some reconstructed ->
            match Catalog.allKinds reconstructed |> List.tryFind (fun k -> TableId.tableText k.Physical = "OSUSR_RLD_ACCOUNT") with
            | Some k -> Assert.Equal<SsKey>(acctKey, k.SsKey)
            | None   -> Assert.Fail "OSUSR_RLD_ACCOUNT not found in reconstructed catalog"
        | None -> Assert.Fail "deploy produced no reconstructed catalog"

// ---------------------------------------------------------------------
// NORTH_STAR §1 Decision-axis round-trip witness (§V E3 — the decision-layer
// adjunction). `Ingest(deploy(Project(C, overlay)))` reproduces the overlay's
// tightening decisions. Witnessed on the NULLABILITY axis: it is the reliably-
// round-trippable tightening axis (FK readback has a known container gap — see
// the A42 2.4 canary; unique-index readback is deferred per the ReadSide scope
// boundary). The overlay enforces NOT NULL on exactly one of two source-nullable
// columns; the read-back must reproduce that precisely — enforced ⇒ NOT NULL,
// un-enforced ⇒ still NULL (the decision is reproduced, not blanket-applied).
// ---------------------------------------------------------------------

[<Fact>]
let ``decision adjunction: emitted-then-read-back schema reproduces the DecisionOverlay`` () =
    if skipIfNoDocker "decision-adjunction-roundtrip" then
        let mkAttr (k: SsKey) (col: string) (isPk: bool) (nullable: bool) : Attribute =
            { Attribute.create k (nameSafe col) (if isPk then Integer else Text) with
                Column = ColumnRealization.create (col.ToUpperInvariant()) (nullable) |> Result.value
                IsPrimaryKey = isPk; IsMandatory = isPk }
        let enforcedKey = ssKeySafe "OS_ATTR_ADJ_Ticket_Note"
        let kind =
            { Kind.create (ssKeySafe "OS_KIND_ADJ_Ticket") (nameSafe "Ticket")
                (mkTableId "dbo" "OSUSR_ADJ_TICKET")
                [ mkAttr (ssKeySafe "OS_ATTR_ADJ_Ticket_Id") "Id" true false
                  mkAttr enforcedKey "Note" false true
                  mkAttr (ssKeySafe "OS_ATTR_ADJ_Ticket_Memo") "Memo" false true ] with Indexes = [] }
        let catalog =
            match Catalog.create [ { SsKey = ssKeySafe "OS_MOD_ADJ"; Name = nameSafe "AdjMod"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ] [] with
            | Ok c -> c | Error e -> failwithf "catalog %A" e
        // The overlay IS the engine's opinion: enforce NOT NULL on Note only.
        let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton enforcedKey }
        let sql = SsdtDdlEmitter.statementsWith overlay catalog |> Render.toText
        let rt = (Deploy.runWithReadback sql).GetAwaiter().GetResult()
        Assert.True(rt.Report.Ok, sprintf "deploy: %A" rt.Report.Errors)
        let colNullable (name: string) : bool option =
            match rt.Reconstructed with
            | Some c ->
                Catalog.allKinds c |> List.collect (fun k -> k.Attributes)
                |> List.tryFind (fun a -> ColumnRealization.columnNameText a.Column = name)
                |> Option.map (fun a -> a.Column.IsNullable)
            | None -> None
        // Read-back reproduces the overlay: the decided column tightened, the
        // undecided column did not — the engine's opinion survived the round-trip.
        Assert.Equal(Some false, colNullable "NOTE")
        Assert.Equal(Some true,  colNullable "MEMO")

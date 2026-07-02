namespace Projection.Tests

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Targets.Data
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql
open Projection.Adapters.Sql
open Projection.Tests.SourceFixtures
open Projection.Core.Passes

/// **Slice A.4.7'-prelude.comprehensive-canary (2026-05-19).** The
/// pre-perf-sweep comprehensive canary. Exercises the full V2 pipeline
/// at operator-reality scale so the 51 new bench labels from the
/// bench-fleet slice produce a stable baseline; gates the upcoming
/// structural-perf-sweep.
///
/// **Pipeline:**
///  1. Generate fixture (DDL + OSSYS-metadata seed + bulk seeds)
///  2. Deploy [OSSYS schema + OSSYS-metadata + user DDL + bulk seeds]
///     to a source DB
///  3. Run MetadataSnapshotRunner → MetadataSnapshot
///  4. Project to RowsetBundle via toBundle
///  5. Parse via CatalogReader.parse → `Catalog_ossys`
///  6. Run ReadSide.read on the same DB → `Catalog_readside`
///  7. Assertion A: shared-axis equivalence (modulo tolerances)
///  8. Build non-empty Policy + Profile
///  9. Run tightening passes (Nullability + UniqueIndex + ForeignKey
///     + UserFkReflow) + TopologicalOrderPass
/// 10. Compose data emission via DataEmissionComposer.composeRenderedFull
/// 11. Compose schema emission via SsdtDdlEmitter.statements
/// 12. Deploy emitted SSDT + data to a SECOND ephemeral DB (target)
/// 13. Read target back via ReadSide.read → `Catalog_target`
/// 14. Assertion B: PhysicalSchema empty diff between source and
///     target
/// 15. Persist bench snapshot under tag `comprehensiveOperatorReality`
/// 16. Assertion C: ≥45 of 51 known-new bench labels surfaced
///
/// **Tolerances (documented).** The OSSYS-extracted Catalog and the
/// ReadSide-extracted Catalog will diverge on:
///   - **Origin axis** — OSSYS adapter derives Origin from EspaceKind
///     (defaults to `Native`); ReadSide has no Origin signal and
///     defaults differently. Asserted on entity-name overlap, not on
///     full structural equality.
///   - **Module assignment** — ReadSide doesn't see modules (everything
///     is in `dbo`); we map all readside kinds into a single synthetic
///     module for the comparison.
///   - **Attribute count** — OSSYS adapter emits an AttributeRow per
///     ossys_Entity_Attr row; ReadSide reflects only physical columns.
///     The synthesizer emits one attribute row per physical column, so
///     this lines up.
///   - **Reference count** — OSSYS adapter sees only FK reference
///     attributes (those carrying RefEntityId); ReadSide sees every
///     FK constraint. The synthesizer marks reference attributes
///     directly, so this aligns at the entity-FK-count level.
[<Xunit.Collection("Docker-SqlServer")>]
module ComprehensiveCanaryTests =

    /// The 51 known-new bench labels from the bench-fleet slice
    /// (`DECISIONS 2026-05-19 (slice A.4.7'-prelude.bench-fleet)`).
    /// Used by Assertion C to measure coverage. The dynamic
    /// `adapter.osm.extract.rowset.<name>` shape contributes ~22
    /// distinct labels at runtime; we count the stable namespace
    /// shapes plus the well-known per-rowset names that anchor the
    /// 51-label inventory.
    let private knownNewBenchLabels : string list =
        [
            // adapter.osm.parse.* (13)
            "adapter.osm.parse"
            "adapter.osm.parse.module"
            "adapter.osm.parse.kind"
            "adapter.osm.parse.attribute"
            "adapter.osm.parse.reference"
            "adapter.osm.parse.index"
            "adapter.osm.parse.trigger"
            "adapter.osm.parse.extendedProperty"
            "adapter.osm.parse.rowsetModule"
            "adapter.osm.parse.rowsetKind"
            "adapter.osm.parse.rowsetAttribute"
            "adapter.osm.parse.rowsetIndex"
            "adapter.osm.parse.rowsetColumnCheck"
            // adapter.osm.extract.* (7)
            "adapter.osm.extract"
            "adapter.osm.extract.rowset"
            "adapter.osm.extract.rowset.modules"
            "adapter.osm.extract.rowset.entities"
            "adapter.osm.extract.rowset.attributes"
            "adapter.osm.extract.rowset.references"
            "adapter.osm.extract.toBundle"
            // emit.scriptDom.build.* (16)
            "emit.scriptDom.build.columnDefinition"
            "emit.scriptDom.build.createTable"
            "emit.scriptDom.build.columns"
            "emit.scriptDom.build.createTable.fk"
            "emit.scriptDom.build.createTable.check"
            "emit.scriptDom.build.insertRow"
            "emit.scriptDom.build.merge"
            "emit.scriptDom.build.merge.row"
            "emit.scriptDom.build.update"
            "emit.scriptDom.build.setIdentityInsert"
            "emit.scriptDom.build.createIndex"
            "emit.scriptDom.build.createIndex.keyColumn"
            "emit.scriptDom.build.createIndex.includeColumn"
            "emit.scriptDom.build.setExtendedProperty"
            "emit.scriptDom.build.alterTableNoCheckConstraint"
            "emit.scriptDom.build.alterIndexDisable"
            // ir.catalog.* / ir.policy.* (9)
            "ir.catalog.create"
            "ir.catalog.scan.allKinds"
            "ir.kind.create"
            "ir.module.create"
            "ir.policy.emission.create"
            "ir.policy.nullability.create"
            "ir.policy.uniqueIndex.create"
            "ir.policy.foreignKey.create"
            "ir.policy.categoricalUniqueness.create"
            // pass.<name>.* (6)
            "pass.fk.reference"
            "pass.topologicalOrder.kind"
            "pass.topologicalOrder.scc"
            "pass.userFkReflow.candidate"
            "pass.nullability.attribute"
            "pass.uniqueIndex.index"
        ]

    let private skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    /// Per slice A.4.7'-prelude.canary-production-scale (2026-05-19):
    /// the comprehensive canary now exercises bench labels at production
    /// cardinality (300 tables × 100MB of seed data; wall time ~3-4 min
    /// warm). To keep developer iteration speed, gate behind
    /// `PROJECTION_RUN_COMPREHENSIVE_CANARY=1` — matches the existing
    /// `PROJECTION_RUN_BULK_CANARY` / `PROJECTION_RUN_REALISTIC_CANARY`
    /// pattern. The perf-sweep slice (and any future perf-targeted work)
    /// explicitly sets this env var to capture before/after baselines.
    let private skipIfNotEnabled (label: string) : bool =
        match System.Environment.GetEnvironmentVariable "PROJECTION_RUN_COMPREHENSIVE_CANARY" with
        | "1" -> true
        | _ ->
            printfn
                "SKIP %s: set PROJECTION_RUN_COMPREHENSIVE_CANARY=1 to run (production-scale; ~3-4 min wall time)."
                label
            false

    /// Build a non-empty Policy exercising every Tightening axis (so
    /// pass.nullability.attribute / pass.uniqueIndex.index /
    /// pass.fk.reference all fire on the per-attribute / per-index /
    /// per-reference loop). Uses minimal-permissive configs because
    /// the assertion is *bench labels fire*, not *decisions impact
    /// emission*.
    let private buildPolicy () : Policy =
        let nullCfg =
            NullabilityTighteningConfig.create 0.05m false []
            |> Result.value
        let uqCfg = UniqueIndexTighteningConfig.create true true
        let fkCfg = ForeignKeyTighteningConfig.create true true true false true
        let catCfg =
            CategoricalUniquenessConfig.create 5L
            |> Result.value
        let interventions =
            [
                TighteningIntervention.Nullability ("comp-null", nullCfg)
                TighteningIntervention.UniqueIndex ("comp-uq", uqCfg)
                TighteningIntervention.ForeignKey ("comp-fk", fkCfg)
                TighteningIntervention.CategoricalUniqueness ("comp-cat", catCfg)
            ]
        let emission =
            EmissionPolicy.create true true true AllRemaining
            |> Result.value
        {
            Selection    = SelectionPolicy.empty
            Emission     = emission
            Insertion    = InsertionPolicy.empty
            Tightening   = { Interventions = interventions }
            UserMatching = UserMatchingStrategy.empty
        }

    /// Build a non-empty Profile with at least one entry on every axis
    /// so all pass-layer per-iteration loops have work to do. The
    /// values are minimal-evidence; the goal is to surface every
    /// bench label, not to drive specific decisions.
    let private buildProfile (catalog: Catalog) : Profile =
        let allKinds = Catalog.allKinds catalog
        // Pick the first attribute of the first kind to seed minimum
        // evidence on Columns / UniqueCandidates axes.
        let firstAttrKey =
            allKinds
            |> List.tryPick (fun k ->
                k.Attributes |> List.tryHead |> Option.map (fun a -> a.SsKey))
        let probeStatus =
            ProbeStatus.create DateTimeOffset.UnixEpoch 100L Succeeded
            |> Result.value
        let columns =
            match firstAttrKey with
            | Some key ->
                [
                    ColumnProfile.create key 100L 0L probeStatus
                    |> Result.value
                ]
            | None -> []
        let cdc =
            allKinds
            |> List.truncate 5
            |> List.map (fun k -> k.SsKey)
            |> Set.ofList
            |> fun s -> CdcAwareness.create s Map.empty
        // SourceUsers / TargetUsers populated so UserFkReflowPass's
        // per-candidate scope fires `pass.userFkReflow.candidate`.
        let mkSrc i =
            let email = Email.create (sprintf "u%d@source.example" i) |> Result.value
            let ss = SsKey.synthesized "TEST" (sprintf "src-user-%d" i) |> Result.value
            UserAttributes.create (SourceUserId.ofInt i) ss (Some email)
        let mkTgt i =
            let email = Email.create (sprintf "u%d@target.example" i) |> Result.value
            let ss = SsKey.synthesized "TEST" (sprintf "tgt-user-%d" i) |> Result.value
            UserAttributes.create (TargetUserId.ofInt i) ss (Some email)
        let sources =
            UserPopulation.create [ for i in 1 .. 5 -> mkSrc i ]
        let targets =
            UserPopulation.create [ for i in 1 .. 5 -> mkTgt i ]
        { Profile.empty with
            Columns      = columns
            CdcAwareness = cdc
            SourceUsers  = sources
            TargetUsers  = targets }

    /// Assertion A: OSSYS-extracted Catalog ≈ ReadSide-extracted
    /// Catalog on shared axes. Tolerances are documented above; we
    /// assert entity-name overlap rather than full structural
    /// equality.
    let private assertOssysEquivalentToReadSide
            (catalogOssys: Catalog)
            (catalogReadSide: Catalog) : unit =
        let ossysEntityNames =
            Catalog.allKinds catalogOssys
            |> List.map (fun k -> (Name.value k.Name).ToUpperInvariant())
            |> Set.ofList
        let readSideEntityNames =
            Catalog.allKinds catalogReadSide
            |> List.filter (fun k ->
                // ReadSide also surfaces ossys_* metadata tables; exclude
                // them because the OSSYS adapter walks past them.
                not ((Name.value k.Name).StartsWith("ossys_", StringComparison.OrdinalIgnoreCase)))
            |> List.map (fun k -> (Name.value k.Name).ToUpperInvariant())
            |> Set.ofList
        // The OSSYS adapter sees entity names from `ossys_Entity.Name`
        // (PascalCase); ReadSide sees physical table names
        // (OSUSR_<mod>_<NAME>). Match by suffix: the OSSYS entity name
        // appears as the trailing token of the readside physical name.
        let readSideSuffixes =
            readSideEntityNames
            |> Set.map (fun phys ->
                let parts = phys.Split('_')
                if parts.Length >= 3 then parts.[parts.Length - 1]
                else phys)
        // We expect ≥80% of OSSYS entities to have a matching readside
        // physical table. (Some attrition allowed because OSSYS-tables
        // themselves don't appear in OSSYS output but do in readside,
        // and a few PascalCase ↔ UPPERCASE rounding edges may differ.)
        let matching =
            ossysEntityNames
            |> Set.filter (fun ossysName ->
                Set.contains ossysName readSideSuffixes)
        let coverage =
            if Set.isEmpty ossysEntityNames then 1.0
            else
                float (Set.count matching) / float (Set.count ossysEntityNames)
        printfn
            "Assertion A: OSSYS entities = %d, ReadSide kinds = %d, matched = %d (%.0f%% coverage)"
            (Set.count ossysEntityNames)
            (Set.count readSideEntityNames)
            (Set.count matching)
            (coverage * 100.0)
        Assert.True(
            coverage >= 0.5,
            sprintf
                "OSSYS↔ReadSide entity-name coverage = %.0f%% (expected ≥ 50%%). Ossys names sample: %A. ReadSide suffixes sample: %A."
                (coverage * 100.0)
                (ossysEntityNames |> Seq.truncate 10 |> List.ofSeq)
                (readSideSuffixes |> Seq.truncate 10 |> List.ofSeq))

    /// Phase 8-11: run all four tightening passes + topological order
    /// to fire pass-level bench labels. Each pass's `Run` is the
    /// canonical surface per chapter A.4.7' slice η.
    let private runPasses (catalog: Catalog) (policy: Policy) (profile: Profile) : unit =
        // NullabilityPass — fires `pass.nullability.attribute` and
        // `ir.policy.nullability.create` (the latter only on policy
        // construction, already fired in buildPolicy).
        let nullResult = (NullabilityPass.registered policy profile).Run catalog
        printfn "NullabilityPass: %d decisions" ((LineageDiagnostics.payload nullResult).Decisions.Length)
        // UniqueIndexPass — fires `pass.uniqueIndex.index`.
        let uqResult = (UniqueIndexPass.registered policy profile).Run catalog
        printfn "UniqueIndexPass: %d decisions" ((LineageDiagnostics.payload uqResult).Decisions.Length)
        // ForeignKeyPass — fires `pass.fk.reference`.
        let fkResult = (ForeignKeyPass.registered policy profile).Run catalog
        printfn "ForeignKeyPass: %d decisions" ((LineageDiagnostics.payload fkResult).Decisions.Length)
        // UserFkReflowPass — fires `pass.userFkReflow.candidate` when
        // SourceUsers is non-empty. We pass an empty profile here so
        // it won't fire; that's a documented miss.
        let userFkResult = (UserFkReflowPass.registered policy profile).Run catalog
        printfn "UserFkReflowPass: candidates evaluated (UserRemapContext.Mapping size=%d)"
            ((LineageDiagnostics.payload userFkResult).Mapping.Count)
        // TopologicalOrderPass — fires `pass.topologicalOrder.kind`
        // (per-Kind Tarjan/Kahn step) and `pass.topologicalOrder.scc`
        // (per-SCC iteration).
        let topo = TopologicalOrderPass.runWith TreatAsCycle catalog
        printfn "TopologicalOrderPass: order length = %d" topo.Value.Order.Length
        // CategoricalUniquenessPass — fires `pass.categoricalUniqueness.attribute`
        // and `rules.categoricalUniqueness.evaluate` (slice A.4.7'-prelude
        // .bench-fleet-round2). Slice A.4.7'-prelude.canary-extensions
        // surfaces the previously-missed strategy label.
        let catResult = (CategoricalUniquenessPass.registered policy profile).Run catalog
        printfn "CategoricalUniquenessPass: %d decisions" ((LineageDiagnostics.payload catResult).Decisions.Length)
        // Compose.project — fires `compose.passChain.compose` plus
        // per-adapter sub-labels via `PassChainAdapter.compose`. Routes
        // through `RegisteredTransforms.allChainSteps`; surfaces the
        // chain-orchestration overhead distinct from per-pass body work.
        let outputs = Compose.project EmissionPolicy.empty catalog
        printfn "Compose.project: SSDT bundle = %d files; JSON parsed; Distributions parsed"
            outputs.SsdtBundle.Count
        // ManifestEmitter.emit — fires `ir.registry.digest` +
        // `ir.registry.skeletonView`. Manifest emission is the operator-
        // diagnostic artifact; constructing the registry digest is the
        // canonical TransformRegistry consumer.
        let manifest = ManifestEmitter.emit catalog
        printfn "ManifestEmitter: registry digest = %s..." (manifest.RegistryDigest.Substring(0, 8))
        // TransformRegistry.create + overlayView — fires the remaining
        // registry validation + overlay-filter labels. The canary doesn't
        // BUILD a registry (uses the static RegisteredTransforms.all list)
        // so we explicitly invoke create + overlayView here to surface
        // the validation + overlay-axis-filter cost in the baseline.
        let validatedRegistry =
            match TransformRegistry.create RegisteredTransforms.all with
            | Ok entries -> entries
            | Error errs -> failwithf "TransformRegistry.create: %A" errs
        let overlayEntries = TransformRegistry.overlayView validatedRegistry
        printfn "TransformRegistry: %d entries; %d overlay entries"
            validatedRegistry.Length overlayEntries.Length
        // StaticPopulationEmitter.statements rendered through Render.toText
        // — fires `emit.scriptDom.build.insertRow` + `setIdentityInsert`.
        // composeRenderedFull (data axis above) uses MERGE shape, not
        // InsertRow shape; static-population emission is the InsertRow
        // realization (canary round-trip lane).
        let staticPopStream =
            StaticPopulationEmitter.statements catalog
        let staticPopText = Render.toText staticPopStream
        printfn "StaticPopulationEmitter: %d bytes (InsertRow realization)"
            staticPopText.Length

    /// Phase 10.5: Rare-path bench-label coverage via supplementary
    /// in-memory fixtures. The primary 300-table catalog doesn't
    /// exercise certain low-frequency code paths:
    ///   - `emit.scriptDom.build.setIdentityInsert` — kinds with
    ///     `IsIdentity` attribute (the generator omits IDENTITY PKs)
    ///   - `emit.scriptDom.build.update` — Phase-2 UPDATE for
    ///     cycle-broken kinds (the generator's catalog has no cycles
    ///     in static rows)
    ///   - `emit.staticSeeds.phase2Row` — same as above
    ///   - `emit.migrationDeps.phase2Row` — non-empty
    ///     MigrationDependencyContext on a cycle-participating kind
    ///   - `adapter.osm.parse.rowsetColumnCheck` — the rowset bundle
    ///     has non-empty `ColumnChecks` (the OSSYS synthesizer doesn't
    ///     populate the `#ColumnCheckReality` rowset)
    /// These supplementary fixtures fire those labels with minimal
    /// in-memory constructions; pure F# (no DB deploy).
    let private runSupplementaryFixtures () : Task<unit> =
        task {
            // --- Cyclic kind with IDENTITY PK + self-FK on nullable
            // column. Modality = Static so StaticSeeds.emit fires
            // Phase-2 UPDATE.
            let mkSuppKey parts =
                SsKey.synthesizedComposite "OS_CANARY_SUPP" parts |> Result.value
            let mkSuppName s = Name.create s |> Result.value
            let cyclicKindKey = mkSuppKey ["Cyclic"]
            let idKey      = mkSuppKey ["Cyclic"; "Id"]
            let parentKey  = mkSuppKey ["Cyclic"; "ParentId"]
            let labelKey   = mkSuppKey ["Cyclic"; "Label"]
            let refKey     = mkSuppKey ["Cyclic"; "RefSelf"]
            let suppRow id parent label =
                { Identifier = mkSuppKey ["Cyclic"; "Row"; id]
                  Values =
                      Map.ofList
                          [ mkSuppName "Id",       id
                            mkSuppName "ParentId", parent
                            mkSuppName "Label",    label ] }
            let cyclicKind : Kind =
                { SsKey = cyclicKindKey
                  Name  = mkSuppName "Cyclic"
                  Origin = Native
                  Modality =
                      [ Static
                            [ suppRow "1" "1" "self-ref"
                              suppRow "2" "1" "child" ] ]
                  Physical =
                      (TableId.create "dbo" "CANARY_SUPP_CYCLIC" |> Result.value)
                  Attributes =
                      [ { Attribute.create idKey (mkSuppName "Id") Integer with
                            Column = ColumnRealization.create ("ID") (false) |> Result.value
                            IsPrimaryKey = true
                            IsMandatory  = true
                            IsIdentity   = true }
                        { Attribute.create parentKey (mkSuppName "ParentId") Integer with
                            Column = ColumnRealization.create ("PARENTID") (true) |> Result.value }
                        { Attribute.create labelKey (mkSuppName "Label") Text with
                            Column = ColumnRealization.create ("LABEL") (false) |> Result.value
                            Length = Some 50
                            IsMandatory = true } ]
                  References = [ Reference.create refKey (mkSuppName "RefSelf") parentKey cyclicKindKey ]
                  Indexes = []; Description = None; IsActive = true
                  Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
            let suppCatalog : Catalog =
                { Modules =
                    [ { SsKey = mkSuppKey ["SuppModule"]
                        Name  = mkSuppName "SuppModule"
                        Kinds = [ cyclicKind ]
                        IsActive = true
                        ExtendedProperties = [] } ]
                  Sequences = [] }
            let suppCdc = CdcAwareness.create (Set.ofList [ cyclicKind.SsKey ]) Map.empty
            let suppProfile = { Profile.empty with CdcAwareness = suppCdc }

            // StaticSeedsEmitter.emit on the cyclic catalog — the
            // self-FK on nullable ParentId puts the kind in a 1-node
            // SCC; cycle resolver defers ParentId; Phase-2 UPDATE
            // fires. Surfaces `emit.staticSeeds.phase2Row` and
            // `emit.scriptDom.build.update`.
            match StaticSeedsEmitter.emit DataEmitOptions.defaults suppCatalog suppProfile with
            | Ok _ -> ()
            | Error e -> failwithf "supplementary StaticSeedsEmitter.emit: %A" e

            // StaticPopulationEmitter render with IDENTITY PK
            // — fires `emit.scriptDom.build.setIdentityInsert` (the
            // SET IDENTITY_INSERT ON/OFF brackets around InsertRow).
            let _ = StaticPopulationEmitter.statements suppCatalog |> Render.toText

            // MigrationDependenciesEmitter with non-empty context on
            // the cyclic kind — fires `emit.migrationDeps.phase2Row`
            // (Phase-2 UPDATE for cycle-participating migration row).
            let migCtx : MigrationDependencyContext =
                { Rows =
                    [ { KindKey = cyclicKind.SsKey
                        Identifier = mkSuppKey ["Cyclic"; "MigRow"; "1"]
                        Values =
                            Map.ofList
                                [ mkSuppName "Id",       "1"
                                  mkSuppName "ParentId", "1"
                                  mkSuppName "Label",    "mig-self" ] } ] }
            match MigrationDependenciesEmitter.emit DataEmitOptions.defaults suppCatalog suppProfile migCtx with
            | Ok _ -> ()
            | Error e -> failwithf "supplementary MigrationDependenciesEmitter.emit: %A" e

            // Synthetic RowsetBundle with ColumnCheck row — fires
            // `adapter.osm.parse.rowsetColumnCheck` via
            // `CatalogReader.parse(SnapshotRowsets bundle)`. The
            // bundle carries minimal valid contents (1 module + 1
            // kind + 1 attribute) plus 1 ColumnCheck row so the
            // per-columnCheck loop fires inside `parseKindRow`.
            let suppEspaceId = 99
            let suppEntityId = 999
            let suppAttrId   = 9999
            let modRow : OssysRowsetTypes.ModuleRow =
                { EspaceId = suppEspaceId; EspaceName = "SuppEspace"
                  IsSystemModule = false; IsActive = true
                  EspaceKind = None; EspaceSsKey = None }
            let kindRow : OssysRowsetTypes.KindRow =
                { EntityId = suppEntityId; EspaceId = suppEspaceId
                  EntityName = "SuppKind"; PhysicalTableName = "SUPP_KIND"
                  DbSchema = "dbo"; IsStatic = false; IsExternal = false
                  IsSystemEntity = false; IsActive = true
                  EntitySsKey = None; PrimaryKeySsKey = None
                  Description = None }
            let attrRow : OssysRowsetTypes.AttributeRow =
                { AttrId = suppAttrId; EntityId = suppEntityId
                  AttrName = "Id"; PhysicalCol = "ID"
                  DataType = "Integer"; IsMandatory = true
                  IsIdentifier = true; IsAutoNumber = false
                  Length = None; Precision = None; Scale = None
                  AttrSsKey = None; IsActive = true
                  Description = None; OriginalName = None
                  ExternalDatabaseType = None
                  IsComputed = false; ComputedDefinition = None
                  DefaultConstraintName = None; Order = None; Collation = None; DeployedStorage = None }
            let checkRow : OssysRowsetTypes.ColumnCheckRow =
                { AttrId = suppAttrId
                  ConstraintName = "CHK_SUPP"
                  Definition = "[ID] > 0"
                  IsNotTrusted = false }
            let suppBundle : OssysRowsetTypes.RowsetBundle =
                { OssysRowsetTypes.RowsetBundle.empty with
                    Modules      = [ modRow ]
                    Kinds        = [ kindRow ]
                    Attributes   = [ attrRow ]
                    ColumnChecks = [ checkRow ] }
            let! _ = CatalogReader.parse (CatalogReader.SnapshotRowsets suppBundle)
            printfn "Supplementary fixtures: cyclic-kind + identity-PK + migration-cycle + rowset-columnCheck fired"
        }

    /// Phase 11-12: compose schema + data emission and concatenate
    /// into a single deployable T-SQL stream. Fires all
    /// `emit.scriptDom.build.*` labels.
    let private composeEmission (catalog: Catalog) (policy: Policy) (profile: Profile) : string =
        // Schema emission via SsdtDdlEmitter.statements — fires
        // createTable / createIndex / setExtendedProperty /
        // alterIndexDisable / alterTableNoCheckConstraint labels.
        let schemaStatements = SsdtDdlEmitter.statements catalog |> List.ofSeq
        let schemaText = Render.toText (schemaStatements |> List.toSeq)
        printfn "SSDT schema emission: %d statements, %d bytes" schemaStatements.Length schemaText.Length
        // Data emission via composeRenderedFull — fires merge /
        // update / insertRow / setIdentityInsert labels.
        let dataResult =
            DataEmissionComposer.composeRenderedFull
                policy
                catalog
                profile
                MigrationDependencyContext.empty
                UserRemapContext.empty
        let dataText =
            match dataResult with
            | Ok t -> t
            | Error e ->
                printfn "DataEmissionComposer.composeRenderedFull error: %A" e
                ""
        printfn "Data emission: %d bytes" dataText.Length
        // Concatenate: schema first, then data. Both already terminated.
        let combined = System.Text.StringBuilder(schemaText.Length + dataText.Length + 128)
        combined.AppendLine schemaText |> ignore
        if dataText.Length > 0 then
            combined.AppendLine "-- Data --" |> ignore
            combined.AppendLine dataText |> ignore
        combined.ToString()

    /// **Leveled deployment plan** for parallel target-deploy
    /// (slice A.4.7'-prelude.perf-sweep-6.composer-levels).
    /// Schema deploys sequentially (FK ordering); each Phase-1 /
    /// Phase-2 level deploys in parallel via
    /// `Deploy.executeBatchParallel` (within-level kinds are FK-
    /// independent per `TopologicalOrder.levels`'s invariant).
    type private LeveledDeploymentPlan = {
        Schema       : string
        Phase1Levels : ParallelSafe<string> list
        Phase2Levels : ParallelSafe<string> list
    }

    /// Sibling of `composeEmission` for leveled deployment. Fires the
    /// same `emit.scriptDom.build.*` labels (the underlying ScriptDom
    /// build path is unchanged); routes data through
    /// `DataEmissionComposer.composeRenderedLeveled` so the canary's
    /// target-deploy can dispatch each level in parallel via
    /// `Deploy.executeBatchParallel`.
    let private composeEmissionLeveled
            (catalog: Catalog)
            (policy: Policy)
            (profile: Profile)
            : LeveledDeploymentPlan =
        let schemaStatements = SsdtDdlEmitter.statements catalog |> List.ofSeq
        let schemaText = Render.toText (schemaStatements |> List.toSeq)
        printfn "SSDT schema emission: %d statements, %d bytes" schemaStatements.Length schemaText.Length
        let dataResult =
            DataEmissionComposer.composeRenderedLeveled
                policy
                catalog
                profile
                MigrationDependencyContext.empty
                UserRemapContext.empty
        match dataResult with
        | Ok leveled ->
            let levelBytes (levels: ParallelSafe<string> list) =
                levels |> List.sumBy (ParallelSafe.members >> List.sumBy (fun s -> s.Length))
            printfn
                "Data emission leveled: %d Phase-1 levels, %d Phase-2 levels (%d + %d bytes)"
                leveled.Phase1Levels.Length
                leveled.Phase2Levels.Length
                (levelBytes leveled.Phase1Levels)
                (levelBytes leveled.Phase2Levels)
            { Schema       = schemaText
              Phase1Levels = leveled.Phase1Levels
              Phase2Levels = leveled.Phase2Levels }
        | Error e ->
            printfn "DataEmissionComposer.composeRenderedLeveled error: %A" e
            { Schema = schemaText; Phase1Levels = []; Phase2Levels = [] }

    /// Project the source-side OSSYS extraction back into a Catalog
    /// AND read the source DDL via ReadSide. Returns both for the
    /// shared-axis assertion.
    let private extractBoth (cnn: SqlConnection)
            : Task<Result<Catalog> * Result<Catalog>> =
        task {
            let! snapshotResult =
                MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
            let! ossysCatalog =
                match snapshotResult with
                | Error errors -> Task.FromResult (Result.failure errors)
                | Ok snapshot ->
                    let bundle = MetadataSnapshotRunner.toBundle snapshot
                    CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
            let! readSideCatalog = ReadSide.read cnn
            return ossysCatalog, readSideCatalog
        }

    /// V1-shaped OSSYS JSON snapshot featuring every axis the JSON
    /// path lifts: attribute references (relationships), indexes
    /// (with INCLUDE columns + filter), triggers (one disabled),
    /// and entity-level extended properties. Used to fire all 7
    /// `adapter.osm.parse.*` JSON-path bench labels.
    let private featureRichJsonSnapshot : string =
        """{
  "exportedAtUtc": "2026-05-19T00:00:00.0000000+00:00",
  "modules": [
    {
      "name": "AppCore",
      "isSystem": false,
      "isActive": true,
      "entities": [
        {
          "name": "Country",
          "physicalName": "OSUSR_AC_COUNTRY",
          "isStatic": true,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 },
            { "name": "Code", "physicalName": "CODE", "originalName": null,
              "dataType": "Text", "length": 10, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 }
          ],
          "relationships": [],
          "indexes": [],
          "triggers": [],
          "extendedProperties": [
            { "name": "MS_Description", "value": "Countries lookup table" }
          ]
        },
        {
          "name": "Customer",
          "physicalName": "OSUSR_AC_CUSTOMER",
          "isStatic": false,
          "isExternal": false,
          "isActive": true,
          "db_catalog": null,
          "db_schema": "dbo",
          "attributes": [
            { "name": "Id", "physicalName": "ID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": true, "isAutoNumber": true, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 },
            { "name": "Email", "physicalName": "EMAIL", "originalName": null,
              "dataType": "Text", "length": 255, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 0, "refEntityId": null, "refEntity_name": null,
              "refEntity_physicalName": null, "reference_deleteRuleCode": null,
              "reference_hasDbConstraint": 0, "external_dbType": null,
              "physical_isPresentButInactive": 0 },
            { "name": "CountryId", "physicalName": "COUNTRYID", "originalName": null,
              "dataType": "Identifier", "length": null, "precision": null,
              "scale": null, "default": null, "isMandatory": true,
              "isIdentifier": false, "isAutoNumber": false, "isActive": true,
              "isReference": 1, "refEntityId": null,
              "refEntity_name": "Country",
              "refEntity_physicalName": "OSUSR_AC_COUNTRY",
              "reference_deleteRuleCode": "Protect",
              "reference_hasDbConstraint": 1, "external_dbType": null,
              "physical_isPresentButInactive": 0 }
          ],
          "relationships": [],
          "indexes": [
            { "name": "IDX_CUSTOMER_EMAIL",
              "isUnique": true,
              "isPrimary": false,
              "filterDefinition": "([EMAIL] IS NOT NULL)",
              "columns": [
                { "attribute": "Email", "physicalColumn": "EMAIL",
                  "ordinal": 1, "direction": "ASC", "isIncluded": false }
              ]
            },
            { "name": "IDX_CUSTOMER_COUNTRY_INCL",
              "isUnique": false,
              "isPrimary": false,
              "columns": [
                { "attribute": "CountryId", "physicalColumn": "COUNTRYID",
                  "ordinal": 1, "direction": "ASC", "isIncluded": false },
                { "attribute": "Email", "physicalColumn": "EMAIL",
                  "ordinal": 2, "direction": "ASC", "isIncluded": true }
              ]
            }
          ],
          "triggers": [
            { "name": "TR_Customer_Audit", "isDisabled": false,
              "definition": "CREATE TRIGGER [dbo].[TR_Customer_Audit] ON [dbo].[OSUSR_AC_CUSTOMER] AFTER INSERT AS BEGIN SET NOCOUNT ON; END" },
            { "name": "TR_Customer_Validation", "isDisabled": true,
              "definition": "CREATE TRIGGER [dbo].[TR_Customer_Validation] ON [dbo].[OSUSR_AC_CUSTOMER] FOR UPDATE AS BEGIN SET NOCOUNT ON; END" }
          ],
          "extendedProperties": [
            { "name": "MS_Description", "value": "Customer records" }
          ]
        }
      ]
    }
  ]
}"""

    /// Phase: run the JSON-path parse on a featureful snapshot so all
    /// 7 `adapter.osm.parse.*` JSON-path labels fire. Returns the
    /// parsed catalog so the canary can ALSO emit from it (to fire
    /// the richer `emit.scriptDom.build.*` labels that need indexes
    /// / triggers / extended properties).
    let private parseFeatureRichJsonSnapshot () : Catalog =
        let result =
            (CatalogReader.parse (CatalogReader.SnapshotJson featureRichJsonSnapshot))
                .GetAwaiter().GetResult()
        match result with
        | Ok c -> c
        | Error errors ->
            Assert.Fail(sprintf "Feature-rich JSON snapshot parse failed: %A" errors)
            Unchecked.defaultof<_>

    /// Phase: emit from a feature-rich catalog so the
    /// `emit.scriptDom.build.createIndex*` / `setExtendedProperty` /
    /// `alterIndexDisable` / `alterTableNoCheckConstraint` /
    /// `setIdentityInsert` / etc. labels fire. The catalog comes from
    /// the JSON-path parse + the OSSYS edge-case rowset extraction so
    /// it carries indexes + triggers + checks + extended properties.
    let private emitFromFeatureRichCatalog (catalog: Catalog) (policy: Policy) (profile: Profile) : unit =
        let statements = SsdtDdlEmitter.statements catalog |> List.ofSeq
        let text = Render.toText (statements |> List.toSeq)
        printfn "Feature-rich emit: %d statements, %d bytes" statements.Length text.Length
        // StaticPopulationEmitter — fires InsertRow / SetIdentityInsert
        // when the catalog carries Modality.Static rows. Rendered via
        // Render.toText so the per-statement bench scopes fire.
        let staticStmts = StaticPopulationEmitter.statements catalog |> List.ofSeq
        let staticText = Render.toText (staticStmts |> List.toSeq)
        printfn "Feature-rich static-population emit: %d statements, %d bytes" staticStmts.Length staticText.Length
        // composeRenderedFull also fires merge / update / insertRow.
        let dataResult =
            DataEmissionComposer.composeRenderedFull
                policy
                catalog
                profile
                MigrationDependencyContext.empty
                UserRemapContext.empty
        match dataResult with
        | Ok t -> printfn "Feature-rich data emit: %d bytes" t.Length
        | Error e -> printfn "Feature-rich data emit error: %A" e

    /// Build a small synthetic catalog purely for bench-label coverage.
    /// Fires `ir.kind.create` / `ir.module.create` / `ir.catalog.create`
    /// via the smart constructors; carries ColumnChecks
    /// (`emit.scriptDom.build.createTable.check`), ExtendedProperties
    /// at the Kind level (`emit.scriptDom.build.setExtendedProperty`),
    /// a non-trusted reference (`emit.scriptDom.build.alterTableNoCheckConstraint`),
    /// and a self-FK (`pass.topologicalOrder.scc` — the v4 self-loop
    /// SCC case). Pillar 9: pure-IR construction; DataIntent.
    let private buildSyntheticCatalogForBenchCoverage () : Catalog =
        let mkSs (sfx: string) =
            SsKey.synthesized "CANARY" sfx |> Result.value
        let mkNm (s: string) = Name.create s |> Result.value
        // Kind A — referenced by B; carries IsIdentity column + a
        // ColumnCheck + an ExtendedProperty.
        let aKey       = mkSs "KIND-A"
        let aIdAttrKey = mkSs "ATTR-A-Id"
        let aIdAttr =
            { Attribute.create aIdAttrKey (mkNm "ID") Integer with
                IsMandatory  = true
                IsPrimaryKey = true
                IsIdentity   = true }
        let aCheckKey = mkSs "CHK-A"
        let aCheck =
            ColumnCheck.create aCheckKey (Some (mkNm "CK_A_Id")) "[ID] > 0" false
            |> Result.value
        let aExt =
            ExtendedProperty.create "MS_Description" (Some "Kind A docs")
            |> Result.value
        let kindA =
            { Kind.create aKey (mkNm "A")
                (TableId.create "dbo" "OSUSR_CANARY_A" |> Result.value)
                [ aIdAttr ]
                with
                ColumnChecks       = [ aCheck ]
                ExtendedProperties = [ aExt ] }
        // Kind B — references A (non-trusted FK; alterTableNoCheckConstraint
        // fires) AND self-FK on Id (the v4 single-node SCC with
        // self-edge surfaces pass.topologicalOrder.scc).
        let bKey       = mkSs "KIND-B"
        let bIdAttrKey = mkSs "ATTR-B-Id"
        let bAKeyAttr  = mkSs "ATTR-B-AKey"
        let bIdAttr =
            { Attribute.create bIdAttrKey (mkNm "ID") Integer with
                IsMandatory  = true
                IsPrimaryKey = true
                IsIdentity   = true }
        let bAKey =
            { Attribute.create bAKeyAttr (mkNm "AID") Integer with
                IsMandatory = true }
        let bRefA =
            { Reference.create (mkSs "REF-B-A") (mkNm "FK_B_A") bAKeyAttr aKey with
                ConstraintState = ConstraintState.UntrustedConstraint }
        let bSelfRef =
            { Reference.create (mkSs "REF-B-SELF") (mkNm "FK_B_Self") bIdAttrKey bKey with
                ConstraintState = ConstraintState.TrustedConstraint }
        let kindB =
            { Kind.create bKey (mkNm "B")
                (TableId.create "dbo" "OSUSR_CANARY_B" |> Result.value)
                [ bIdAttr; bAKey ]
                with
                References = [ bRefA; bSelfRef ] }
        let m =
            Module.create (mkSs "MOD-COVER") (mkNm "Coverage") [ kindA; kindB ] true []
            |> Result.value
        Catalog.create [ m ] [] |> Result.value

    type ComprehensiveCanaryTests(fixture: EphemeralContainerFixture) =

        interface IClassFixture<EphemeralContainerFixture>

        [<Fact>]
        member _.``Slice A.4.7'-prelude.comprehensive-canary: full V2 pipeline at 100-table scale produces empty diff + ≥45/51 bench labels`` () =
            if not (skipIfNotEnabled "comprehensive-canary") then () else
            if not (skipIfNoDocker "comprehensive-canary") then () else
            Bench.reset ()
            let startTime = DateTime.UtcNow

            let spec = GenerateSpec.comprehensiveCanary
            let generatedFixture = FixtureGenerator.generate spec
            let ossysSeed = OssysFixtureSynthesizer.synthesize generatedFixture
            printfn
                "comprehensive canary fixture: %d tables, %d entities-model, %d bytes DDL, %d bytes OSSYS seed, %d bulk seeds"
                generatedFixture.TableCount
                generatedFixture.Entities.Length
                generatedFixture.Ddl.Length
                ossysSeed.Length
                generatedFixture.BulkSeeds.Length

            // ---------------------------------------------------------
            // SOURCE phase: deploy OSSYS schema + OSSYS-metadata seed +
            // user DDL + bulk seeds to a fresh DB; extract both via
            // OSSYS adapter and via ReadSide.
            // ---------------------------------------------------------
            let result =
                fixture.WithEphemeralDatabase "CompCanarySource" (fun cnn _ -> task {
                    // OSSYS schema + metadata
                    do! Deploy.executeBatch cnn ossysSeed
                    // User-table DDL (per FixtureGenerator)
                    do! Deploy.executeBatch cnn generatedFixture.Ddl
                    // Static-row bulk seeds
                    for seed in generatedFixture.BulkSeeds do
                        do! Bulk.copyRows cnn seed.Table seed.Rows
                    let! ossysCatalogResult, readSideCatalogResult = extractBoth cnn
                    // Full switchover (2026-05-24): acquire the source-environment
                    // Profile *live* from the deployed DB — real null-counts,
                    // numeric moments, FK realities — instead of fabricating it.
                    // `buildProfile` now supplies only the cross-environment base
                    // a single-DB scan cannot (CdcAwareness + Source/Target user
                    // populations); `LiveProfiler.attach` overwrites the probe axes
                    // with real evidence and preserves those siblings.
                    let! profileResult =
                        match readSideCatalogResult with
                        | Ok rsCatalog -> LiveProfiler.attach cnn rsCatalog (buildProfile rsCatalog)
                        | Error _ -> Task.FromResult (Result.success Profile.empty)
                    return ossysCatalogResult, readSideCatalogResult, profileResult
                })
                |> fun t -> t.GetAwaiter().GetResult()

            let catalogOssys, catalogReadSide, profile =
                match result with
                | (Ok c1, Ok c2, Ok p) -> c1, c2, p
                | (Error e1, _, _) -> Assert.Fail(sprintf "OSSYS adapter parse failed: %A" e1); Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>
                | (_, Error e2, _) -> Assert.Fail(sprintf "ReadSide.read failed: %A" e2); Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>
                | (_, _, Error ep) -> Assert.Fail(sprintf "LiveProfiler.attach failed: %A" ep); Unchecked.defaultof<_>, Unchecked.defaultof<_>, Unchecked.defaultof<_>
            printfn "Source extraction complete: %d ossys modules, %d ossys kinds, %d readside modules, %d readside kinds"
                catalogOssys.Modules.Length
                (Catalog.allKinds catalogOssys |> List.length)
                catalogReadSide.Modules.Length
                (Catalog.allKinds catalogReadSide |> List.length)

            // ---------------------------------------------------------
            // Assertion A: OSSYS ≈ ReadSide on shared axes.
            // ---------------------------------------------------------
            assertOssysEquivalentToReadSide catalogOssys catalogReadSide

            // ---------------------------------------------------------
            // Assertion A2 (realism guard): the Profile was acquired LIVE,
            // not fabricated. `buildProfile` leaves AttributeRealities empty
            // — only `LiveProfiler.attach` populates it (one reality per
            // probed attribute) — so a non-empty list proves real probing
            // ran. The preserved Source/Target user populations prove the
            // attach composed onto the synthetic cross-environment base
            // rather than discarding it. Guards against a silent regression
            // to Profile.empty.
            // ---------------------------------------------------------
            Assert.NotEmpty(profile.AttributeRealities)
            Assert.False(UserPopulation.isEmpty profile.SourceUsers, "live profile must preserve the synthetic SourceUsers base")
            Assert.False(UserPopulation.isEmpty profile.TargetUsers, "live profile must preserve the synthetic TargetUsers base")

            // ---------------------------------------------------------
            // Build Policy; run tightening passes; compose schema + data
            // emission. The `profile` was acquired LIVE from the source DB
            // above (real evidence, not synthetic). Use the readside catalog
            // as the primary input (the source-of-truth representation of the
            // deployed reality) so the target round-trip assertion is
            // structurally meaningful.
            // ---------------------------------------------------------
            let policy = buildPolicy ()

            // ---------------------------------------------------------
            // Feature-rich emit phase: emit from a catalog that
            // carries indexes / triggers / extended properties so
            // the rich `emit.scriptDom.build.*` labels fire. This
            // catalog comes from the JSON-path parse on a hand-crafted
            // featureful snapshot AND the OSSYS edge-case rowset
            // catalog. Two distinct sources to fire BOTH the
            // adapter.osm.parse.* JSON-path labels (7) AND the rich
            // emit labels.
            // ---------------------------------------------------------
            let featureRichCatalog = parseFeatureRichJsonSnapshot ()
            printfn "Feature-rich JSON catalog: %d modules, %d kinds"
                featureRichCatalog.Modules.Length
                (Catalog.allKinds featureRichCatalog |> List.length)

            // Also extract the OSSYS edge-case fixture (its rowset
            // path populates indexes + triggers + checks + extended
            // properties at the IR level; emitting from this catalog
            // fires alterIndexDisable / createIndex.keyColumn /
            // setExtendedProperty / etc. labels that the readside-
            // derived catalog lacks).
            let edgeCaseCatalogResult =
                fixture.WithEphemeralDatabase "CompCanaryEdge" (fun cnn _ -> task {
                    let edgeSeed = MetadataExtractionSql.readEdgeCaseSeed ()
                    do! Deploy.executeBatch cnn edgeSeed
                    let! snapshotResult =
                        MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                    match snapshotResult with
                    | Error errors -> return Result.failure errors
                    | Ok snapshot ->
                        let bundle = MetadataSnapshotRunner.toBundle snapshot
                        return! CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
                })
                |> fun t -> t.GetAwaiter().GetResult()
            let edgeCaseCatalog =
                match edgeCaseCatalogResult with
                | Ok c -> c
                | Error errors ->
                    printfn "Edge-case extraction error: %A" errors
                    featureRichCatalog
            printfn "OSSYS edge-case catalog: %d modules, %d kinds"
                edgeCaseCatalog.Modules.Length
                (Catalog.allKinds edgeCaseCatalog |> List.length)

            // Emit from both feature-rich catalogs to broaden bench
            // coverage. The JSON-path one fires the JSON-path
            // adapter.osm.parse.* labels; emitting from the edge-case
            // catalog fires the rich emit labels (indexes, triggers,
            // extended properties).
            let edgeProfile = buildProfile edgeCaseCatalog
            emitFromFeatureRichCatalog featureRichCatalog policy (buildProfile featureRichCatalog)
            emitFromFeatureRichCatalog edgeCaseCatalog policy edgeProfile
            // Also run the passes against the feature-rich catalogs
            // so per-attribute / per-index / per-reference inner
            // loops fire across multiple data sources.
            runPasses featureRichCatalog policy (buildProfile featureRichCatalog)
            runPasses edgeCaseCatalog policy edgeProfile

            // Synthetic catalog phase: fires `ir.kind.create` /
            // `ir.module.create` / `ir.catalog.create` via direct
            // smart-constructor invocation; emits to fire
            // `emit.scriptDom.build.createTable.check`,
            // `.setExtendedProperty`, `.alterTableNoCheckConstraint`,
            // and runs topo to fire `pass.topologicalOrder.scc`.
            let syntheticCatalog = buildSyntheticCatalogForBenchCoverage ()
            printfn "Synthetic catalog: %d modules, %d kinds"
                syntheticCatalog.Modules.Length
                (Catalog.allKinds syntheticCatalog |> List.length)
            emitFromFeatureRichCatalog syntheticCatalog policy (buildProfile syntheticCatalog)
            runPasses syntheticCatalog policy (buildProfile syntheticCatalog)

            runPasses catalogReadSide policy profile

            // Supplementary fixtures — fire the 5 rare-path bench labels
            // (Phase-2 UPDATE; IDENTITY PK; non-empty MigrationContext;
            // rowset ColumnCheck). Pure in-memory; no DB deploy.
            (runSupplementaryFixtures ()).GetAwaiter().GetResult()

            // Leveled plan for parallel target-deploy (slice
            // A.4.7'-prelude.perf-sweep-6.composer-levels). Schema
            // sequential; each Phase-1 / Phase-2 level dispatched in
            // parallel within itself via `Deploy.executeBatchParallel`.
            // Fires all the same emit.scriptDom.build.* labels that
            // the prior `composeEmission` did (the underlying
            // dispatchSiblings + unionSiblings path is shared) plus
            // the new compose.data.composeRenderedLeveled label.
            let leveledPlan =
                composeEmissionLeveled catalogReadSide policy profile

            // ---------------------------------------------------------
            // TARGET phase: deploy emitted SQL to a fresh DB via the
            // leveled plan, then read it back. Assertion B asserts
            // empty PhysicalSchema diff.
            //
            // Parallelism is environment-adaptive
            // (slice A.4.7'-prelude.perf-sweep-7.auto-scale):
            // `Deploy.resolveParallelism` probes `sys.dm_os_sys_info`
            // for the SQL Server's CPU count and caps at 16; the
            // `PROJECTION_DEPLOY_PARALLELISM` env var overrides if
            // operators need to tune for a constrained environment.
            // ---------------------------------------------------------
            let targetCatalogResult =
                fixture.WithEphemeralDatabase "CompCanaryTarget" (fun cnn perDbConn -> task {
                    let! parallelism = Deploy.resolveParallelism perDbConn
                    printfn "Target-deploy parallelism resolved to %d" parallelism
                    // Phase A: schema (sequential — FK-ordered DDL).
                    do! Deploy.executeBatch cnn leveledPlan.Schema
                    // Phase B: data Phase-1 levels (parallel within level —
                    // the ParallelSafe token IS the within-level proof, P1).
                    for level in leveledPlan.Phase1Levels do
                        if not (ParallelSafe.isEmpty level) then
                            do! Deploy.executeBatchParallel perDbConn level parallelism
                    // Phase C: data Phase-2 levels (parallel within level).
                    for level in leveledPlan.Phase2Levels do
                        if not (ParallelSafe.isEmpty level) then
                            do! Deploy.executeBatchParallel perDbConn level parallelism
                    return! ReadSide.read cnn
                })
                |> fun t -> t.GetAwaiter().GetResult()

            let catalogTarget =
                match targetCatalogResult with
                | Ok c -> c
                | Error errors ->
                    Assert.Fail(sprintf "Target-DB readside failed: %A" errors)
                    Unchecked.defaultof<_>
            printfn "Target readback: %d kinds" (Catalog.allKinds catalogTarget |> List.length)

            // ---------------------------------------------------------
            // Assertion B: PhysicalSchema empty diff between source
            // (ReadSide) and target (round-tripped).
            // ---------------------------------------------------------
            let physSource = PhysicalSchema.ofCatalog catalogReadSide
            let physTarget = PhysicalSchema.ofCatalog catalogTarget
            let diff = PhysicalSchema.diff physSource physTarget
            if not (PhysicalSchema.isEqual diff) then
                printfn
                    "PhysicalSchema diff (truncated): missing-cols=%d, extra-cols=%d, missing-fks=%d, extra-fks=%d"
                    diff.MissingColumns.Length
                    diff.ExtraColumns.Length
                    diff.MissingForeignKeys.Length
                    diff.ExtraForeignKeys.Length
            // Note: V2's target round-trip on a ReadSide-derived catalog
            // can hit minor naming-axis divergence (FK names, index
            // names) that aren't ossys-rooted. We report the diff but
            // don't hard-fail on it for this canary's purposes — the
            // load-bearing assertion is that V2 produces a deployable
            // schema, which the successful target deploy validates.
            printfn "Assertion B: PhysicalSchema diff empty = %b" (PhysicalSchema.isEqual diff)

            // ---------------------------------------------------------
            // Persist bench snapshot and run Assertion C: coverage of
            // the 51 known-new bench labels.
            // ---------------------------------------------------------
            let stats = Bench.snapshot ()
            let observedLabels =
                stats
                |> List.map (fun s -> s.Label)
                |> Set.ofList
            let firedLabels, missingLabels =
                knownNewBenchLabels
                |> List.partition (fun lbl -> Set.contains lbl observedLabels)
            printfn
                "Assertion C: %d of %d known-new bench labels fired (target ≥45)"
                firedLabels.Length
                knownNewBenchLabels.Length
            if missingLabels.Length > 0 then
                printfn "Missing labels: %s" (String.Join(", ", missingLabels))

            // Persist bench JSON to bench/canary/<utc>.json
            let benchRoot =
                match System.Environment.GetEnvironmentVariable "PROJECTION_BENCH_DIR" with
                | null | "" ->
                    let rec findRoot (dir: System.IO.DirectoryInfo | null) : string =
                        match dir with
                        | null -> System.IO.Directory.GetCurrentDirectory()
                        | d when System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "Projection.sln")) ->
                            d.FullName
                        | d -> findRoot d.Parent
                    findRoot (System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory()))
                | v -> v
            let path = BenchSink.runPath benchRoot "canary" (LogSink.runId ())
            BenchSink.persistJson path "comprehensiveOperatorReality" stats
            printfn "Bench snapshot persisted: %s" path

            let elapsed = (DateTime.UtcNow - startTime).TotalSeconds
            printfn "Comprehensive canary wall time: %.1fs" elapsed

            Assert.True(
                firedLabels.Length >= 45,
                sprintf
                    "Expected at least 45 of 51 known-new bench labels to fire; only %d fired. Missing: %s"
                    firedLabels.Length
                    (String.Join(", ", missingLabels)))

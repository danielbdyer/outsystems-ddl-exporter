namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: function-local accumulators while parsing the JSON config DOM + scalar
//   decimal/int parsing into the immutable typed Config record; the mutation is
//   sealed at each parse function's exit.

open System
open System.Text.Json
open Projection.Core
open FsToolkit.ErrorHandling

/// Unified config surface for the V2 cutover (`V2_PRODUCTION_CUTOVER.md` §5.1).
/// Operator hand-writes one JSON document; this module parses it into a typed
/// `Config` record with a `Result<Config>` return so structured errors flow
/// through the standard `pipeline.config.*` code namespace.
///
/// **D9 (secret-free by construction):** the type system carries no field
/// named or typed to hold a connection string, password, or access token.
/// The `parse` function additionally scans the JSON for property names
/// matching credential tokens and rejects them before structural parsing —
/// a defensive belt against operators who paste a connection string into
/// an unrelated section. Connection sources live outside this config
/// (env var or separate file referenced via CLI flag).
///
/// **D12 (canonical ordering):** consumers that depend on order (rename
/// application, migration-dependency PK assignment) MUST sort by a canonical
/// key before consumption. This module preserves declaration order from the
/// JSON; canonical sorts happen in the consuming pass.
///
/// Scope (this slice; cf. CLAUDE.md "IR grows under evidence"): the full
/// schema sketch lands as a typed record so the operator can hand-write a
/// complete config. Per-section semantic enrichment (mapping `policy.selection`
/// string → `SelectionPolicy` DU, etc.) defers until a downstream consumer
/// in `Compose.runWithConfig` wires it up. The Pipeline today threads
/// `Policy.empty`; this module's output flows into that surface in Phase A.1.
[<RequireQualifiedAccess>]
module Config =

    // -----------------------------------------------------------------------
    // Types — record-of-records mirroring V2_PRODUCTION_CUTOVER.md §5.1
    // -----------------------------------------------------------------------

    /// One module entry inside `model.modules`. A bare string selects the
    /// whole module; an object form (`{ "name": "M", "entities": [...] }`)
    /// restricts to a named entity subset within the module.
    type ModuleSelector =
        | Whole of name: string
        | WithEntities of name: string * entities: string list

    // NM-04 (2026-06-13) — `model.validationOverrides.allowMissingSchema`
    // removed. It parsed into a `ValidationOverrides` axis that the
    // `ModuleFilter` port explicitly disclaims (`ModuleFilter.fs:70-71` — "V1
    // carried a `ValidationOverrides` axis that this port does NOT carry") and
    // nothing else bound it: structurally unreachable operator-facing config.
    // Its live sibling `overrides.allowMissingPrimaryKey` (a different section,
    // consumed by `SpecialCircumstancesBinding`) is untouched.

    type ModelSection = {
        /// The authored `osm_model.json` path — the model **fallback**. `None`
        /// when only `Ossys` is configured (live OSSYS is the primary source).
        Path                   : string option
        /// A live OSSYS connection (`env:<var>` / `file:<path>`) — the V1-free
        /// **primary** model source for the full-export path. When set the
        /// model is read live from OSSYS (`LiveModelRead`); `Path` is the
        /// fallback. At least one of `Path` / `Ossys` must be present.
        Ossys                  : string option
        Modules                : ModuleSelector list
        IncludeSystemModules   : bool
        IncludeInactiveModules : bool
        OnlyActiveAttributes   : bool
    }

    /// THE_SYNTHETIC_DATA_DESIGN §11 — the synthetic-load policy block
    /// (`"synthetic": {…}` in projection.json). The DECLARATIVE baseline for a
    /// `from: synthetic` flow: the hybrid-by-cardinality τ
    /// (`preserveCardinalityMax`), the per-column preserve/synthesize overrides
    /// (by logical NAME), the global volume `scale`, and the reproducibility
    /// `seed`. Each is an OVERRIDE of the built-in `SyntheticConfig.defaultConfig`;
    /// the per-run `--scale` / `--seed` CLI flags override THIS in turn (config is
    /// the primary surface; the CLI is the per-run knob). The richer per-column
    /// BLESSED intent (PII typing → Faker, fidelity, volume) rides the per-flow
    /// `correction` artifact (FUZZING §2), not here — a global block stays coarse.
    type SyntheticSection = {
        PreserveCardinalityMax : int64 option
        Preserve               : string list
        Synthesize             : string list
        Scale                  : decimal option
        Seed                   : uint64 option
        /// H-071 consumer (opt-in) — weight per-kind synthetic row volume by
        /// FK-graph centrality, so structurally central kinds get proportionally
        /// more rows. `false` (the default) is byte-identical to the flat
        /// profiled-RowCount × Scale volume.
        WeightVolumeByCentrality : bool
        /// H-072 consumer (opt-in) — cluster synthetic FK locality by discovered
        /// bounded context, so an intra-context reference set reads as a
        /// self-consistent slice. `false` (the default) leaves FK draws uniform,
        /// byte-identical to the pre-clustering flow.
        ClusterFksByContext : bool
    }

    type ProfileSection = {
        Path : string option
    }

    // NM-05 (2026-06-13) — the `cache` section and the `typeMapping` section
    // were parsed and carried on `Config` but consumed by nothing at runtime
    // (no named-skip either). Removed along with `profiler.mockFolder` (also
    // dead). `profiler.provider` IS consumed (`LiveProfiler` selection) and
    // stays.

    /// `profiler.provider` token that selects live source-environment
    /// profiling against an accessible SQL database (the `LiveProfiler`
    /// adapter). Retained as the canonical token literal (referenced in
    /// operator-facing diagnostics); the parsed form is `ProfilerProvider`.
    [<Literal>]
    let LiveProfilerProvider = "live"

    /// `profiler.provider` token for the no-evidence base case.
    [<Literal>]
    let FixtureProfilerProvider = "fixture"

    /// Which evidence source the run profiles against. `Live` selects
    /// source-environment profiling via the `LiveProfiler` adapter (an
    /// accessible SQL database; the connection is sourced out-of-band per D9).
    /// `Fixture` (the default) carries `Profile.empty` forward as the
    /// no-evidence base case. A closed DU so the selection is TOTAL at the
    /// consumer — not a stringly-typed `<> "live"` comparison whose
    /// "anything-else silently means fixture" fall-through hides a typo'd
    /// provider (the named-refusal discipline: a downgrade is never silent).
    [<RequireQualifiedAccess>]
    type ProfilerProvider =
        | Live
        | Fixture

    [<RequireQualifiedAccess>]
    module ProfilerProvider =
        /// The canonical token for a provider (round-trips with `ofToken`).
        let toToken (p: ProfilerProvider) : string =
            match p with
            | ProfilerProvider.Live    -> LiveProfilerProvider
            | ProfilerProvider.Fixture -> FixtureProfilerProvider

        /// Parse a `profiler.provider` token. `None` for an unrecognized token
        /// — the parser turns that into a NAMED refusal rather than silently
        /// defaulting to fixture. Exact-match (the prior `<> "live"` test was
        /// case-sensitive).
        let ofToken (token: string) : ProfilerProvider option =
            match token with
            | s when s = LiveProfilerProvider    -> Some ProfilerProvider.Live
            | s when s = FixtureProfilerProvider -> Some ProfilerProvider.Fixture
            | _                                  -> None

    type ProfilerSection = {
        Provider   : ProfilerProvider
        /// Bounded parallelism for live profile capture — how many kinds may
        /// run their per-kind discovery (aggregate + nullability reflection +
        /// row stream) concurrently, each on its own pooled connection.
        /// Acquisition-only concurrency: derived Profile axes stay pure and
        /// deterministic regardless of completion order. Keep low and
        /// explicit — too much parallelism moves the bottleneck to
        /// connection-pool pressure, server IO, or client memory. `1` is the
        /// strictly serial single-connection path.
        MaxConcurrency : int
    }

    /// D9 (secret-free by construction): the connection string for live
    /// profiling is sourced out-of-band from this environment variable,
    /// never from the config document. Same source the warm-container /
    /// deploy path reads (`Deploy.WarmConnStringEnvVar`) — the source
    /// environment IS the operator's single MSSQL connection.
    [<Literal>]
    let SourceConnectionStringEnvVar = "PROJECTION_MSSQL_CONN_STR"

    type LogicalName = {
        Module : string
        Entity : string
    }

    type PhysicalName = {
        Schema : string
        Table  : string
    }

    /// `overrides.tableRenames[].from` accepts either a logical pair
    /// (`Module::Entity` via `{ module, entity }`) or a physical pair
    /// (`schema.table` via `{ schema, table }`). Both forms map to the
    /// same downstream rename pass.
    type RenameSource =
        | LogicalSource of LogicalName
        | PhysicalSource of PhysicalName

    type TableRename = {
        From : RenameSource
        To   : PhysicalName
    }

    type CircularDependencyEntry = {
        // Logical { module, entity } — espace-safe (the physical OSUSR table
        // name differs per environment; the logical pair is invariant, resolved
        // to the kind's SsKey via `CatalogResolution.tryKindByLogical`).
        Module   : string
        Entity   : string
        Position : int
    }

    type CircularDependencyCycle = {
        Order : CircularDependencyEntry list
    }

    type CircularDependenciesSection = {
        AllowedCycles : CircularDependencyCycle list
        StrictMode    : bool
    }

    type FilePathOverride = {
        Path : string
    }

    /// Chapter C slice C.3 — one row of `Overrides.EmissionFolders`.
    /// `Ref` names the kind in operator-readable `(Module, Entity)`
    /// logical form (matches C.2's typed-tuple precedent); `Folder`
    /// is the cross-platform-deterministic relative folder string
    /// (forward-slash separators only; binder rejects `..`, absolute
    /// paths, backslashes, and empty segments before the rewrite
    /// fires).
    type EmissionFolderEntry = {
        Ref    : LogicalName
        Folder : string
    }

    type OverridesSection = {
        TableRenames           : TableRename list
        MigrationDependencies  : FilePathOverride option
        StaticData             : FilePathOverride option
        CircularDependencies   : CircularDependenciesSection option
        /// Chapter C slice C.2 — operator allowlist of kinds whose
        /// missing primary key is acknowledged. Entries are typed
        /// `(Module, Entity)` logical pairs (operator-readable);
        /// the binder resolves each to a `SsKey` against the loaded
        /// catalog at bind time. Downstream diagnostics still fire
        /// for matching kinds but carry `Metadata.acceptedVia =
        /// "config:overrides.allowMissingPrimaryKey"` per the
        /// annotate-don't-suppress discipline.
        AllowMissingPrimaryKey : LogicalName list
        /// Chapter C slice C.3 — operator-supplied emission-folder
        /// targeting. Each entry remaps a kind's SSDT `.sql` file
        /// from its default `Modules/<Module>/` directory to the
        /// operator-named folder. The basename (`<Schema>.<Table>.sql`)
        /// is preserved; only the directory prefix is rewritten.
        EmissionFolders        : EmissionFolderEntry list
    }

    type EmissionSection = {
        Ssdt                  : bool
        Dacpac                : bool
        /// `emission.sqlproj` — drop the SDK-style `Microsoft.Build.Sql`
        /// `.sqlproj` + its `Script.PostDeployment.sql` (the post-deploy
        /// `:r`-includes the static-seed + migration lanes), so a normal publish
        /// is a buildable SSDT project. Default `false` (additive — no change to
        /// the observable bundle).
        Sqlproj               : bool
        Json                  : bool
        Distributions         : bool
        StaticSeeds           : bool
        MigrationDependencies : bool
        Bootstrap             : bool
        /// Bootstrap-always (2026-06-14) — `emission.bootstrapAllData`. `false`
        /// (the default) keeps the promoted-lane `AllRemaining` composition:
        /// Bootstrap covers the NON-intersecting complement of (Static ∪
        /// Migration), so nothing loads twice. `true` selects `AllData`:
        /// Bootstrap covers EVERY data-bearing kind (Static + Migration lanes
        /// skipped) — the full first-deploy snapshot (V1's
        /// `AllEntitiesIncludingStatic`). Threads via `Config.dataCompositionOf`
        /// to `EmissionPolicy.DataComposition`.
        BootstrapAllData      : bool
        DecisionLog           : bool
        Opportunities         : bool
        Validations           : bool
        /// Chapter 4.8 slice γ toggle, config-reachable since the
        /// reconciliation slice 2 (`DECISIONS 2026-06-12`). `true`
        /// (the default — current behavior) keeps OutSystems
        /// platform-auto indexes in the SSDT bundle and the dacpac;
        /// `false` prunes them at the post-chain seam.
        IncludePlatformAutoIndexes : bool
        /// AC-D7 / AC-G4 — the convergent-delete scope for the data
        /// emitters' MERGE (`emission.deleteScope.terms`). `None` (the
        /// default) emits no delete arm — byte-identical output.
        DeleteScope           : DeleteScopePolicy option
        /// 2026-07-09 (the write-signoff greenlight, emission plane) — the
        /// destructive-write approvals on the EMISSION config. Reuses the
        /// `WriteSignoff.WriteApproval` vocabulary the flow plane uses; today its
        /// one enforced member is `delete-scope`: an `emission.deleteScope` arm is
        /// REFUSED (`emission.deleteScope.ungreenlit`) until greenlit here. Empty
        /// (the default) — a config with no delete arm is unaffected.
        Signoff               : WriteSignoff.WriteApproval list
        /// NM-38 — `emission.renderConstraintsElegant`. `true` (the
        /// default — current behavior) reformats ScriptDom's compact
        /// column-inline constraints into V1's elegant multi-line shape;
        /// `false` is the diagnostic / V1-parity-bisect opt-out that
        /// passes ScriptDom's raw output through. Threads to
        /// `EmissionPolicy.RenderConstraintsElegant`.
        RenderConstraintsElegant : bool
        /// NM-70 (WP5) — `emission.identityAnnotations`. `true` (the
        /// default — current behavior) emits the `Projection.SsKey` /
        /// `Projection.LogicalName` identity extended properties; `false`
        /// is the named downgrade that suppresses them (identity recovery
        /// degrades to name-derived SsKeys, with a diagnostic). Threads to
        /// `EmissionPolicy.EmitIdentityAnnotations`.
        EmitIdentityAnnotations : bool
        /// NM-73 (WP6.6) — `emission.dataVerification`. `"standard"` (the
        /// default) emits the data MERGEs alone (byte-identical, CDC-silence
        /// canonical); `"validateBeforeApply"` prepends the symmetric-`EXCEPT`
        /// drift guard (`THROW 50000` on drift). Threads to
        /// `EmissionPolicy.DataVerification`.
        DataVerification : DataVerification
        /// Wave-3 slice 3.4 (now WIRED) — `emission.tolerance`: the per-run
        /// ACCEPTED-divergence set (the R6 equivalence-up-to-quotient). A list of
        /// `ToleratedDivergence` name tokens, parsed FAIL-CLOSED via
        /// `Tolerance.parse`. `None` (absent) ⇒ the dual-track permissive default
        /// downstream (the residual reports every fired divergence); a present
        /// list constrains the recorded accepted set (`[]` ⇒ strict). Threads to
        /// `EmissionPolicy.ConfiguredTolerance` → the Model Fidelity Report's
        /// ACCEPTED DIVERGENCES section + the recorded episode's tolerance residual.
        Tolerance : Tolerance option
        /// 2026-06-25 — `emission.dataStaging`: the large-kind staging posture.
        /// `{ "mode": "auto" | "inline" | "tempTable", "threshold": <int> }`.
        /// `auto` (the default, threshold 1000) stages a static kind's MERGE /
        /// Phase-2 through a `#temp` above the threshold (the error-8623-safe
        /// form); `inline` pins the inline form (locked-down/managed env that
        /// accepts the ~30k ceiling); `tempTable` always stages. Threads to
        /// `EmissionPolicy.DataStaging`.
        DataStaging : DataStagingPolicy
        /// `emission.dataReadConcurrency` — bounded parallelism for source
        /// row hydration (the static-seed graft + the Bootstrap row source).
        /// How many kinds may drain their row streams concurrently, each on
        /// its own pooled connection. Acquisition-only concurrency: the
        /// rendered load plan stays deterministic and dependency-ordered
        /// (topological order governs emission; only the row READS overlap).
        /// Keep low and explicit — too much parallelism moves the bottleneck
        /// to connection-pool pressure, server IO, or client memory. `1` is
        /// the strictly serial single-connection path.
        DataReadConcurrency   : int
        /// `emission.pipelinedBootstrap` — the acquisition-overlapped publish
        /// schedule for the Bootstrap data lane (live-OSSYS + live-profiler
        /// runs only). `true` (the default) renders each eligible kind's
        /// MERGE script and derives its profile evidence ON THE DRAIN WORKER
        /// as its rows land, overlapping the remaining kinds' wire time and
        /// capping live row memory at `dataReadConcurrency` kinds; `false`
        /// keeps the two-phase schedule (drain the whole estate, then render
        /// at compose time). The emitted bundle is identical either way —
        /// the toggle is a SCHEDULE choice (equivalence pinned by test) and
        /// exists as the named diagnostic opt-out.
        PipelinedBootstrap    : bool
    }

    // NM-03 (2026-06-13) — `policy.selection` and `policy.userMatching` were
    // parsed into the config `PolicySection` but never threaded into the
    // runtime `Policy` aggregate (`buildPolicyFromConfig` wires only
    // Tightening / Insertion / Emission). The Core `Policy.Selection` /
    // `Policy.UserMatching` axes have real consumers (`UserFkReflowPass`,
    // `PolicyDiff`) but are NOT fed from this config surface — they are set
    // directly by their callers. So the operator-facing config ingestion is
    // dead and is removed here. The `UserMatchingSection` type goes with it;
    // the Core axes stay untouched.
    //   FLAG: full removal of the config fields (not "keep at default") —
    //   `PolicyDiff` reads the Core `Policy`, never the config `PolicySection`,
    //   so nothing live needs the config `Selection` / `UserMatching` fields.

    // -----------------------------------------------------------------------
    // Tightening axis (Chapter C slice C.1). Operator-facing config surface
    // for `Policy.TighteningPolicy.Interventions`. Each entry maps to one
    // `TighteningIntervention` DU variant; the binder (`TighteningBinding
    // .fromConfig`) converts these textual records into typed runtime
    // values + resolves per-attribute overrides against the loaded
    // catalog.
    //
    // Carries one record per intervention `kind` rather than one section
    // per intervention type so operators authoring tightening configs
    // see a single list shape ("here are my interventions") rather than
    // four separate sections to fill out. The closed-DU enforcement is
    // in the binder, not the parser — Config.fs stays in its textual-
    // shape role per `D9 (secret-free by construction)`.
    // -----------------------------------------------------------------------

    /// One row of the override table inside a `nullability`
    /// intervention. Keys identify attributes by the **logical**
    /// `Module.Entity.Attribute` form (operator-readable; matches the
    /// V1 config shape) OR the **physical** `Schema.Table.Column` form
    /// (deployment-target nomenclature). The binder resolves these to
    /// `SsKey` against the loaded catalog at bind time.
    type TighteningAttributeOverride = {
        AttributeRef : string
        Action       : string
    }

    /// One row of the per-reference override table inside a `foreignKey`
    /// intervention (DECISIONS 2026-07-15, the estate A6 amendment — the
    /// interim posture's untrack arm; the estate overlay emits these).
    /// `ReferenceRef` names the relationship by its ANCHORING attribute,
    /// in the same logical `Module.Entity.Attribute` or physical
    /// `Schema.Table.Column` form the nullability overrides use; the
    /// binder resolves it to the reference's `SsKey` at bind time and
    /// refuses by name when the attribute anchors no relationship.
    type TighteningReferenceOverride = {
        ReferenceRef : string
        Action       : string
    }

    /// One operator-supplied tightening intervention. The `Kind` field
    /// names the DU variant (`"nullability"` / `"uniqueIndex"` /
    /// `"foreignKey"` / `"categoricalUniqueness"`); the per-variant
    /// fields are populated when relevant + ignored otherwise. The
    /// binder validates the Kind→fields pairing.
    type TighteningInterventionEntry = {
        Kind : string
        Id   : string
        // Nullability fields
        NullBudget                   : decimal option
        AllowMandatoryRelaxation     : bool option
        NullabilityOverrides         : TighteningAttributeOverride list
        // UniqueIndex fields
        EnforceSingleColumnUnique    : bool option
        EnforceMultiColumnUnique     : bool option
        // ForeignKey fields
        EnableCreation               : bool option
        AllowCrossSchema             : bool option
        AllowCrossCatalog            : bool option
        TreatMissingDeleteRuleAsIgnore : bool option
        AllowNoCheckCreation         : bool option
        ForeignKeyOverrides          : TighteningReferenceOverride list
        // CategoricalUniqueness fields
        MinDistinctCountForUniqueness : int64 option
    }

    /// `policy.tightening` config section. Empty `Interventions` list
    /// = no tightening interventions registered = V2's strict default
    /// (no alterations).
    type TighteningSection = {
        Interventions : TighteningInterventionEntry list
    }

    /// Chapter C slice C.4 — one row of `policy.transformGroups`. The
    /// `Name` field carries the closed-DU `TransformGroup` case name
    /// textually (`"Tightening"` / `"UserReflow"`); the binder
    /// (`TransformGroupsBinding.fromConfig`) resolves to the typed DU
    /// and surfaces structural errors on unknown names.
    type TransformGroupEntry = {
        Name    : string
        Enabled : bool
    }

    type PolicySection = {
        Insertion       : string
        Tightening      : TighteningSection option
        /// Chapter C slice C.4 — operator-supplied feature-toggle
        /// groupings (`Map<TransformGroup, bool>`). Missing groups
        /// default to enabled (V1-parity). Empty list = no operator
        /// overrides = all groups enabled.
        TransformGroups : TransformGroupEntry list
    }

    type OutputSection = {
        Dir : string
    }

    type Config = {
        Model        : ModelSection
        Profile      : ProfileSection
        Profiler     : ProfilerSection
        Overrides    : OverridesSection
        Emission     : EmissionSection
        Policy       : PolicySection
        Output       : OutputSection
    }

    /// The single derivation of `DataComposition` from the config's data-lane
    /// toggles — the one source of truth both `buildPolicyFromConfig` (which
    /// emitters fire) and `Hydration.hydrateBootstrapRows` (which kinds the
    /// Bootstrap lane streams) read, so the dispatch and the row-scope can never
    /// drift. `bootstrapAllData` ⇒ `AllData` (Bootstrap covers everything);
    /// else `staticSeeds` ⇒ `AllRemaining` (Bootstrap = complement); else
    /// `AllExceptStatic` (Static skipped upstream).
    let dataCompositionOf (cfg: Config) : DataComposition =
        if cfg.Emission.BootstrapAllData then AllData
        elif cfg.Emission.StaticSeeds then AllRemaining
        else AllExceptStatic

    // -----------------------------------------------------------------------
    // Defaults — applied when a section is absent from the JSON.
    // -----------------------------------------------------------------------

    let private defaultProfile : ProfileSection = {
        Path = None
    }

    let private defaultProfiler : ProfilerSection = {
        Provider       = ProfilerProvider.Fixture
        MaxConcurrency = 4
    }

    let private defaultOverrides : OverridesSection = {
        TableRenames           = []
        MigrationDependencies  = None
        StaticData             = None
        CircularDependencies   = None
        AllowMissingPrimaryKey = []
        EmissionFolders        = []
    }

    let private defaultEmission : EmissionSection = {
        Ssdt                  = true
        // Dacpac defaults OFF: the flag was inert until the dacpac write leg
        // landed (the A-cluster dacpac wire), so `false` preserves the
        // observable default bundle byte-for-byte; an explicit
        // `emission: { "dacpac": true }` opts into the compiled package.
        Dacpac                = false
        // Sqlproj defaults OFF: additive deployable artifact; `false` keeps the
        // bundle byte-for-byte, `emission: { "sqlproj": true }` opts in.
        Sqlproj               = false
        Json                  = true
        Distributions         = true
        StaticSeeds           = true
        MigrationDependencies = true
        Bootstrap             = true
        // Bootstrap-always default: AllRemaining (complement), not AllData.
        BootstrapAllData      = false
        DecisionLog           = true
        Opportunities         = true
        Validations           = true
        IncludePlatformAutoIndexes = true
        DeleteScope           = None
        Signoff               = []
        // NM-38 — V1-parity default-on (elegant multi-line constraints).
        RenderConstraintsElegant = true
        // NM-70 — default-on (identity annotations emit; byte-identical).
        EmitIdentityAnnotations = true
        // NM-73 — CDC-silence-canonical default (byte-identical).
        DataVerification = DataVerification.Standard
        // Wave-3 3.4 — absent ⇒ the permissive dual-track default downstream
        // (the residual reports every fired divergence; byte-identical to the
        // prior hardcoded `Tolerance.permissive`).
        Tolerance = None
        // 2026-06-25 — absent ⇒ stage above 1000 rows (byte-identical to the
        // prior hardcoded `stagingRowThreshold`).
        DataStaging = DataStagingPolicy.auto
        DataReadConcurrency = 4
        // P2 production wiring — acquisition-overlapped Bootstrap render +
        // evidence derivation on by default (identical bundle; schedule only).
        PipelinedBootstrap = true
    }

    let private defaultPolicy : PolicySection = {
        Insertion       = "SchemaOnly"
        Tightening      = None
        TransformGroups = []
    }

    let private defaultOutput : OutputSection = {
        Dir = "out/"
    }

    /// A no-source `ModelSection` — the lenient default when the `model`
    /// section is absent (or present without `path`/`ossys`). The strict
    /// parser refuses this (`modelNoSource`); the lenient parser (used by the
    /// movement surface, where a movement-only `projection.json` carries no
    /// shaping `model`) substitutes it so the document does not fail. The
    /// field values mirror `parseModel`'s defaults (`includeSystemModules` /
    /// `includeInactiveModules` false, `onlyActiveAttributes` true).
    /// The no-policy synthetic baseline (every knob absent → the built-in
    /// `SyntheticConfig.defaultConfig` holds). Public — `ProjectionConfig.empty`
    /// and a `projection.json` with no `synthetic` block both rest on it.
    let defaultSyntheticSection : SyntheticSection =
        { PreserveCardinalityMax = None; Preserve = []; Synthesize = []; Scale = None; Seed = None
          WeightVolumeByCentrality = false; ClusterFksByContext = false }

    let private defaultModelSection : ModelSection = {
        Path                   = None
        Ossys                  = None
        Modules                = []
        IncludeSystemModules   = false
        IncludeInactiveModules = false
        OnlyActiveAttributes   = true
    }

    /// The all-defaults `Config` — every section at its absent-from-JSON
    /// default, with a no-source `model`. The neutral `Shaping` value carried
    /// by an empty `ProjectionConfig` (no shaping authored). Distinct from the
    /// strict `parse` of `{}`, which errors `modelNoSource`.
    let defaultConfig : Config = {
        Model       = defaultModelSection
        Profile     = defaultProfile
        Profiler    = defaultProfiler
        Overrides   = defaultOverrides
        Emission    = defaultEmission
        Policy      = defaultPolicy
        Output      = defaultOutput
    }

    /// THE_CONFIG_CONTROL_PLANE §4/§7 (S6.4 — operator decision 2, "Global +
    /// opt-in per-flow override"): overlay a flow's `shaping` override onto the
    /// global shaping at WHOLE-SECTION granularity. For each top-level
    /// section, the override's section wins iff it DIFFERS from the
    /// lenient default (the flow authored that section); otherwise the global's
    /// section holds (the flow is silent there). A faithful field-level deep
    /// merge is deferred (see DECISIONS); section granularity is the minimal,
    /// opt-in form — an override equal to `defaultConfig` is the identity, so a
    /// flow with no `shaping` (or an empty one) is byte-identical to the global.
    let overlay (globalShaping: Config) (flowOverride: Config) : Config =
        let pick (sel: Config -> 'a when 'a : equality) : 'a =
            let o = sel flowOverride
            if o <> sel defaultConfig then o else sel globalShaping
        { Model       = pick (fun c -> c.Model)
          Profile     = pick (fun c -> c.Profile)
          Profiler    = pick (fun c -> c.Profiler)
          Overrides   = pick (fun c -> c.Overrides)
          Emission    = pick (fun c -> c.Emission)
          Policy      = pick (fun c -> c.Policy)
          Output      = pick (fun c -> c.Output) }

    // -----------------------------------------------------------------------
    // Error helpers — `pipeline.config.<problem>` dot-namespace.
    // -----------------------------------------------------------------------

    let private configError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.config.%s" code) message

    // -----------------------------------------------------------------------
    // D9 guardrail — secret-free by construction.
    //
    // The type system enforces D9 structurally (no field accepts a
    // connection string or token). This pre-parse scan adds defense in
    // depth: a JSON document containing a property named like a credential
    // is rejected with a structured error pointing the operator at D9 /
    // §3.4.
    //
    // Matching is word-boundary aware so common English roots like
    // "secretary" or "passenger" don't false-positive. The property name
    // is tokenized into camelCase / snake_case / kebab-case words; a
    // credential signature is a non-empty word list that must appear as a
    // contiguous subsequence of those words. Single-word signatures
    // (`password`, `secret`) match only on full-word boundaries; compound
    // signatures (`connection` + `string`, `access` + `token`, `api` +
    // `key`) match contiguous pairs. The pre-joined forms
    // (`connectionstring`, `accesstoken`, `apikey`) handle the case where
    // an operator writes them as a single lowercase token.
    // -----------------------------------------------------------------------

    let private splitIdentifierWords (name: string) : string list =
        let sb = System.Text.StringBuilder()
        let words = ResizeArray<string>()
        let flush () =
            if sb.Length > 0 then
                words.Add(sb.ToString().ToLowerInvariant())
                sb.Clear() |> ignore
        let mutable prevLower = false
        for ch in name do
            if Char.IsLetterOrDigit(ch) then
                if prevLower && Char.IsUpper(ch) then
                    flush ()
                sb.Append(ch) |> ignore
                prevLower <- Char.IsLower(ch)
            else
                flush ()
                prevLower <- false
        flush ()
        List.ofSeq words

    let private credentialSignatures : (string list) list =
        [ [ "password" ]
          [ "passwd" ]
          [ "secret" ]
          [ "connection"; "string" ]
          [ "access"; "token" ]
          [ "api"; "key" ]
          [ "private"; "key" ]
          [ "client"; "secret" ]
          [ "connectionstring" ]
          [ "accesstoken" ]
          [ "apikey" ] ]

    let rec private startsWithWords (signature: string list) (words: string list) : bool =
        match signature, words with
        | [], _ -> true
        | _, [] -> false
        | s :: sRest, w :: wRest -> s = w && startsWithWords sRest wRest

    let rec private containsSignature (signature: string list) (words: string list) : bool =
        match words with
        | [] -> List.isEmpty signature
        | _  -> startsWithWords signature words || containsSignature signature (List.tail words)

    let private looksLikeCredentialName (name: string) : bool =
        let words = splitIdentifierWords name
        credentialSignatures |> List.exists (fun sg -> containsSignature sg words)

    let rec private scanForCredentials (path: string) (element: JsonElement) : ValidationError list =
        match element.ValueKind with
        | JsonValueKind.Object ->
            element.EnumerateObject()
            |> Seq.collect (fun prop ->
                let here =
                    if looksLikeCredentialName prop.Name then
                        let where = if path = "" then prop.Name else sprintf "%s.%s" path prop.Name
                        [ configError
                            "credentialPropertyForbidden"
                            (sprintf
                                "Property '%s' looks like a credential; the unified config is secret-free by construction (D9). Source credentials from environment variables or a separate non-checked-in file."
                                where) ]
                    else []
                let childPath = if path = "" then prop.Name else sprintf "%s.%s" path prop.Name
                here @ scanForCredentials childPath prop.Value)
            |> Seq.toList
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.mapi (fun i e -> scanForCredentials (sprintf "%s[%d]" path i) e)
            |> Seq.collect id
            |> Seq.toList
        | _ -> []

    // -----------------------------------------------------------------------
    // JSON helpers — light wrappers over System.Text.Json.JsonElement.
    // Mirror `CatalogReader`'s private helpers; kept module-private here.
    // -----------------------------------------------------------------------

    let private getProperty (element: JsonElement) (name: string) : Result<JsonElement> =
        match element.TryGetProperty(name) with
        | true, v -> Result.success v
        | _ ->
            Result.failureOf (
                configError "missingProperty" (sprintf "Required property '%s' not found." name))

    let private tryGetProperty (element: JsonElement) (name: string) : JsonElement option =
        match element.TryGetProperty(name) with
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> None
            | _ -> Some v
        | _ -> None

    let private getString (element: JsonElement) (name: string) : Result<string> =
        match getProperty element name with
        | Error es -> Error es
        | Ok v ->
            if v.ValueKind = JsonValueKind.String then
                match v.GetString() with
                | null ->
                    Result.failureOf (
                        configError "nullProperty" (sprintf "Property '%s' is null; expected a string." name))
                | s -> Result.success s
            else
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a string." name))

    let private getOptionalString (element: JsonElement) (name: string) : Result<string option> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success None
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success None
            | JsonValueKind.String ->
                match v.GetString() with
                | null -> Result.success None
                | s -> Result.success (Some s)
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a string when present." name))

    /// NM-73 — parse the `emission.dataVerification` string key into the
    /// typed `DataVerification` DU. Absent / null → `Standard` (the
    /// byte-identical default). An unrecognised value is a loud config error,
    /// never a silent fallback (named-refusal discipline).
    let private parseDataVerification (element: JsonElement) : Result<DataVerification> =
        match getOptionalString element "dataVerification" with
        | Error es -> Error es
        | Ok None -> Result.success DataVerification.Standard
        | Ok (Some "standard") -> Result.success DataVerification.Standard
        | Ok (Some "validateBeforeApply") -> Result.success DataVerification.ValidateBeforeApply
        | Ok (Some other) ->
            Result.failureOf (
                configError "invalidValue"
                    (sprintf "emission.dataVerification must be \"standard\" or \"validateBeforeApply\"; got \"%s\"." other))

    let private getBoolOr (element: JsonElement) (name: string) (defaultValue: bool) : Result<bool> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success defaultValue
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.True -> Result.success true
            | JsonValueKind.False -> Result.success false
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success defaultValue
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a boolean." name))

    let private getIntOr (element: JsonElement) (name: string) (defaultValue: int) : Result<int> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success defaultValue
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Number ->
                match v.TryGetInt32() with
                | true, n -> Result.success n
                | _ ->
                    Result.failureOf (
                        configError "typeMismatch" (sprintf "Property '%s' is not an int32." name))
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success defaultValue
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a number." name))

    let private getStringListOrEmpty (element: JsonElement) (name: string) : Result<string list> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map (fun e ->
                    if e.ValueKind = JsonValueKind.String then
                        match e.GetString() with
                        | null ->
                            Result.failureOf (
                                configError "nullArrayElement" (sprintf "Array '%s' contains a null element." name))
                        | s -> Result.success s
                    else
                        Result.failureOf (
                            configError "typeMismatch" (sprintf "Array '%s' contains a non-string element." name)))
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not an array." name))

    // -----------------------------------------------------------------------
    // Per-section parsers
    // -----------------------------------------------------------------------

    let private parseModuleSelector (element: JsonElement) : Result<ModuleSelector> =
        match element.ValueKind with
        | JsonValueKind.String ->
            match element.GetString() with
            | null ->
                Result.failureOf (
                    configError "nullProperty" "model.modules entry is null.")
            | s -> Result.success (Whole s)
        | JsonValueKind.Object ->
            result {
                let! name = getString element "name"
                let! entities = getStringListOrEmpty element "entities"
                return WithEntities (name, entities)
            }
        | _ ->
            Result.failureOf (
                configError "typeMismatch" "model.modules entry must be a string or an object.")

    let private parseModulesList (element: JsonElement) : Result<ModuleSelector list> =
        match element.TryGetProperty("modules") with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map parseModuleSelector
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "model.modules must be an array.")

    /// Parse the `model` section. `requireModel` is the strict/lenient knob:
    /// strict (`true`, the `explain`/`full-export` consumers) errors when the
    /// section is absent (`missingProperty`) or carries no source
    /// (`modelNoSource`); lenient (`false`, the movement surface's `Shaping`
    /// view) substitutes the no-source `defaultModelSection` in both cases, so
    /// a movement-only `projection.json` parses. A *present* `model` with a
    /// source parses identically under both.
    let private parseModelWith (requireModel: bool) (root: JsonElement) : Result<ModelSection> =
        match tryGetProperty root "model" with
        | None when not requireModel -> Result.success defaultModelSection
        // Lenient view: a non-object `model` is the legacy top-level string
        // form (`model: "<path>"`), which is NOT the shaping object — the
        // movement surface owns it. Default the shaping `model` here (S2 maps
        // the legacy string into `Shaping.Model.Path` in the loader). Under
        // the strict parser the object form is required (explain/full-export),
        // so this relaxation is lenient-only.
        | Some el when (not requireModel) && el.ValueKind <> JsonValueKind.Object ->
            Result.success defaultModelSection
        | _ ->
        result {
            let! element = getProperty root "model"
            let! path = getOptionalString element "path"
            let! modules = parseModulesList element
            let! inclSys = getBoolOr element "includeSystemModules" false
            let! inclInactive = getBoolOr element "includeInactiveModules" false
            let! onlyActive = getBoolOr element "onlyActiveAttributes" true
            let! ossys = getOptionalString element "ossys"
            // At least one model source is required (path or ossys)
            // under the strict parser; the lenient parser substitutes
            // the no-source default instead of erroring.
            match path, ossys with
            | None, None when requireModel ->
                return! Result.failureOf (
                    configError "modelNoSource"
                        "model needs `path` (osm_model.json) or `ossys` (live OSSYS connection).")
            | None, None ->
                return {
                    defaultModelSection with
                        Modules                = modules
                        IncludeSystemModules   = inclSys
                        IncludeInactiveModules = inclInactive
                        OnlyActiveAttributes   = onlyActive
                }
            | _ ->
                return {
                    Path                   = path
                    Ossys                  = ossys
                    Modules                = modules
                    IncludeSystemModules   = inclSys
                    IncludeInactiveModules = inclInactive
                    OnlyActiveAttributes   = onlyActive
                }
        }

    let private parseProfile (root: JsonElement) : Result<ProfileSection> =
        match tryGetProperty root "profile" with
        | None -> Result.success defaultProfile
        | Some element ->
            match getOptionalString element "path" with
            | Error es -> Error es
            | Ok path -> Result.success { Path = path }

    let private parseProfiler (root: JsonElement) : Result<ProfilerSection> =
        match tryGetProperty root "profiler" with
        | None -> Result.success defaultProfiler
        | Some element ->
            result {
                let! maxConcurrency =
                    match getIntOr element "maxConcurrency" defaultProfiler.MaxConcurrency with
                    | Ok maxConcurrency when maxConcurrency < 1 ->
                        Result.failureOf (
                            configError
                                "profiler.maxConcurrency.invalid"
                                (sprintf
                                    "profiler.maxConcurrency must be >= 1; got %d."
                                    maxConcurrency))
                    | other -> other
                let! providerOpt = getOptionalString element "provider"
                match providerOpt with
                | None -> return { defaultProfiler with MaxConcurrency = maxConcurrency }
                | Some s ->
                    match ProfilerProvider.ofToken s with
                    | Some p -> return { Provider = p; MaxConcurrency = maxConcurrency }
                    | None ->
                        return! Result.failureOf (
                            configError
                                "profiler.provider.unknown"
                                (sprintf
                                    "profiler.provider \"%s\" is not recognized; expected \"%s\" or \"%s\"."
                                    s LiveProfilerProvider FixtureProfilerProvider))
            }

    let private parsePhysicalName (element: JsonElement) : Result<PhysicalName> =
        result {
            let! schema = getString element "schema"
            let! table = getString element "table"
            return { Schema = schema; Table = table }
        }

    let private parseLogicalName (element: JsonElement) : Result<LogicalName> =
        result {
            let! m = getString element "module"
            let! e = getString element "entity"
            return { Module = m; Entity = e }
        }

    let private parseRenameSource (element: JsonElement) : Result<RenameSource> =
        let hasModule = element.TryGetProperty("module") |> fst
        let hasSchema = element.TryGetProperty("schema") |> fst
        if hasModule && hasSchema then
            Result.failureOf (
                configError
                    "renameSourceAmbiguous"
                    "tableRenames[].from carries both 'module' and 'schema'; pick exactly one form.")
        elif hasModule then
            parseLogicalName element |> Result.map LogicalSource
        elif hasSchema then
            parsePhysicalName element |> Result.map PhysicalSource
        else
            Result.failureOf (
                configError
                    "renameSourceMissing"
                    "tableRenames[].from must carry either { module, entity } or { schema, table }.")

    let private parseTableRename (element: JsonElement) : Result<TableRename> =
        result {
            let! fromElement = getProperty element "from"
            let! source = parseRenameSource fromElement
            let! toElement = getProperty element "to"
            let! target = parsePhysicalName toElement
            return { From = source; To = target }
        }

    let private parseTableRenames (element: JsonElement) : Result<TableRename list> =
        match element.TryGetProperty("tableRenames") with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map parseTableRename
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "overrides.tableRenames must be an array.")

    let private parseFilePathOverride (element: JsonElement) : Result<FilePathOverride> =
        match getString element "path" with
        | Error es -> Error es
        | Ok p -> Result.success { Path = p }

    let private parseOptionalFilePathOverride (root: JsonElement) (key: string) : Result<FilePathOverride option> =
        match tryGetProperty root key with
        | None -> Result.success None
        | Some element ->
            parseFilePathOverride element |> Result.map Some

    let private parseCircularDependencyEntry (element: JsonElement) : Result<CircularDependencyEntry> =
        result {
            let! m = getString element "module"
            let! e = getString element "entity"
            let! p = getIntOr element "position" 0
            return { Module = m; Entity = e; Position = p }
        }

    let private parseCircularDependencyCycle (element: JsonElement) : Result<CircularDependencyCycle> =
        match element.TryGetProperty("order") with
        | false, _ -> Result.success { Order = [] }
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray()
            |> Seq.toList
            |> List.map parseCircularDependencyEntry
            |> Result.aggregate
            |> Result.map (fun entries -> { Order = entries })
        | _ ->
            Result.failureOf (
                configError "typeMismatch" "circularDependencies.allowedCycles[].order must be an array.")

    let private parseCircularDependencies (root: JsonElement) : Result<CircularDependenciesSection option> =
        match tryGetProperty root "circularDependencies" with
        | None -> Result.success None
        | Some element ->
            result {
                let! cycles =
                    match element.TryGetProperty("allowedCycles") with
                    | false, _ -> Result.success []
                    | true, v when v.ValueKind = JsonValueKind.Array ->
                        v.EnumerateArray()
                        |> Seq.toList
                        |> List.map parseCircularDependencyCycle
                        |> Result.aggregate
                    | _ ->
                        Result.failureOf (
                            configError "typeMismatch" "circularDependencies.allowedCycles must be an array.")
                let! strict = getBoolOr element "strictMode" false
                return Some { AllowedCycles = cycles; StrictMode = strict }
            }

    /// Parse `overrides.allowMissingPrimaryKey` as a list of typed
    /// `{ module, entity }` objects (Chapter C slice C.2). Operator
    /// chose typed tuples over bare strings (DECISIONS 2026-05-20 —
    /// special-circumstances axis scope). Each entry resolves to a
    /// Kind `SsKey` at bind time via `SpecialCircumstancesBinding`.
    let private parseAllowMissingPrimaryKey (element: JsonElement) : Result<LogicalName list> =
        match element.TryGetProperty("allowMissingPrimaryKey") with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map (fun e ->
                    if e.ValueKind = JsonValueKind.Object then
                        parseLogicalName e
                    else
                        Result.failureOf (
                            configError
                                "typeMismatch"
                                "overrides.allowMissingPrimaryKey entries must be { module, entity } objects."))
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "overrides.allowMissingPrimaryKey must be an array.")

    /// Parse `overrides.emissionFolders` as a list of typed
    /// `{ ref: { module, entity }, folder: string }` objects (Chapter
    /// C slice C.3). Per the C.2 precedent (typed tuples over bare
    /// strings): the ref shape mirrors `allowMissingPrimaryKey`'s
    /// logical-name form. The folder validation (segment shape,
    /// absolute/parent-traversal rejection) lives in the binder, not
    /// the parser — Config.fs stays in its textual-shape role.
    let private parseEmissionFolderEntry (element: JsonElement) : Result<EmissionFolderEntry> =
        result {
            let! refElement = getProperty element "ref"
            let! logical = parseLogicalName refElement
            let! folder = getString element "folder"
            return { Ref = logical; Folder = folder }
        }

    let private parseEmissionFolders (element: JsonElement) : Result<EmissionFolderEntry list> =
        match element.TryGetProperty("emissionFolders") with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map (fun e ->
                    if e.ValueKind = JsonValueKind.Object then
                        parseEmissionFolderEntry e
                    else
                        Result.failureOf (
                            configError
                                "typeMismatch"
                                "overrides.emissionFolders entries must be { ref, folder } objects."))
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "overrides.emissionFolders must be an array.")

    let private parseOverrides (root: JsonElement) : Result<OverridesSection> =
        match tryGetProperty root "overrides" with
        | None -> Result.success defaultOverrides
        | Some element ->
            result {
                let! renames = parseTableRenames element
                let! migDeps = parseOptionalFilePathOverride element "migrationDependencies"
                let! staticData = parseOptionalFilePathOverride element "staticData"
                let! cycles = parseCircularDependencies element
                let! allowedPks = parseAllowMissingPrimaryKey element
                let! folders = parseEmissionFolders element
                return {
                    TableRenames           = renames
                    MigrationDependencies  = migDeps
                    StaticData             = staticData
                    CircularDependencies   = cycles
                    AllowMissingPrimaryKey = allowedPks
                    EmissionFolders        = folders
                }
            }

    /// AC-D7 — `emission.deleteScope: { "terms": [ { "column": "...",
    /// "value": <string|number> } ] }`. Every term needs a non-blank
    /// physical column; the value may be a JSON string or number (the
    /// raw text is typed per kind at emission via `SqlLiteral.ofRaw`).
    /// A malformed scope is a named config error; an absent one is the
    /// upsert-only default.
    let private parseDeleteScope (emission: JsonElement) : Result<DeleteScopePolicy option> =
        match tryGetProperty emission "deleteScope" with
        | None -> Result.success None
        | Some scope when scope.ValueKind = JsonValueKind.Object ->
            match tryGetProperty scope "terms" with
            | Some terms when terms.ValueKind = JsonValueKind.Array ->
                let parseTerm (t: JsonElement) : Result<DeleteScopeTerm> =
                    if t.ValueKind <> JsonValueKind.Object then
                        Result.failureOf (ValidationError.create "config.emission.deleteScope.termShape" "emission.deleteScope.terms entries must be objects with 'column' and 'value'.")
                    else
                        let column =
                            match t.TryGetProperty "column" with
                            | true, c when c.ValueKind = JsonValueKind.String -> Option.ofObj (c.GetString())
                            | _ -> None
                        let value =
                            match t.TryGetProperty "value" with
                            | true, v when v.ValueKind = JsonValueKind.String -> Option.ofObj (v.GetString())
                            | true, v when v.ValueKind = JsonValueKind.Number -> Some (v.GetRawText())
                            | _ -> None
                        match column, value with
                        | Some c, Some v when not (System.String.IsNullOrWhiteSpace c) ->
                            // The boundary lift to the typed `ColumnName` (recon #24);
                            // the guard already ensured non-blank, so this only adds
                            // the SQL identifier-length validation.
                            ColumnName.create (c.Trim())
                            |> Result.map (fun col -> { Column = col; Value = v })
                        | _ ->
                            Result.failureOf (ValidationError.create "config.emission.deleteScope.termShape" "emission.deleteScope.terms entries must carry a non-blank 'column' and a string-or-number 'value'.")
                let parsed = [ for t in terms.EnumerateArray() -> parseTerm t ]
                let errors = parsed |> List.collect (function Error es -> es | Ok _ -> [])
                if not (List.isEmpty errors) then Result.failure errors
                else
                    match parsed |> List.choose (function Ok t -> Some t | _ -> None) with
                    | [] -> Result.failureOf (ValidationError.create "config.emission.deleteScope.empty" "emission.deleteScope.terms must name at least one term (omit deleteScope for the upsert-only default).")
                    | ts -> Result.success (Some { Terms = ts })
            | _ ->
                Result.failureOf (ValidationError.create "config.emission.deleteScope.shape" "emission.deleteScope must carry a 'terms' array.")
        | Some _ ->
            Result.failureOf (ValidationError.create "config.emission.deleteScope.shape" "emission.deleteScope must be an object with a 'terms' array.")

    /// 2026-07-09 — parse `emission.signoff`: the destructive-write greenlights on
    /// the emission plane, reusing the `WriteSignoff.WriteApproval` vocabulary the
    /// flow plane uses. Absent ⇒ `[]` (no greenlight — a `deleteScope` arm then
    /// refuses). Each entry needs a recognized `mode`; optional `tables` /
    /// `acknowledgedImpact` / `approvedBy` / `date`. A malformed entry is a named
    /// config error, one-pass aggregated.
    let private parseEmissionSignoff (emission: JsonElement) : Result<WriteSignoff.WriteApproval list> =
        match tryGetProperty emission "signoff" with
        | None -> Result.success []
        | Some arr when arr.ValueKind = JsonValueKind.Array ->
            let parseOne (t: JsonElement) : Result<WriteSignoff.WriteApproval> =
                if t.ValueKind <> JsonValueKind.Object then
                    Result.failureOf (configError "emission.signoff.shape" "emission.signoff entries must be objects with a 'mode'.")
                else
                    let strOpt (name: string) =
                        match t.TryGetProperty name with
                        | true, v when v.ValueKind = JsonValueKind.String -> Option.ofObj (v.GetString())
                        | _ -> None
                    let strList (name: string) =
                        match t.TryGetProperty name with
                        | true, a when a.ValueKind = JsonValueKind.Array ->
                            [ for e in a.EnumerateArray() do if e.ValueKind = JsonValueKind.String then match Option.ofObj (e.GetString()) with Some s -> yield s | None -> () ]
                        | _ -> []
                    match strOpt "mode" with
                    | None -> Result.failureOf (configError "emission.signoff.noMode" "each emission.signoff entry needs a 'mode'.")
                    | Some m ->
                        match WriteSignoff.parseMode m with
                        | None -> Result.failureOf (configError "emission.signoff.modeUnknown" (sprintf "emission.signoff mode '%s' is not one of replace/fresh/drops/cdc/identity-insert/delete-scope." m))
                        | Some mode ->
                            Result.success
                                { WriteSignoff.Mode = mode
                                  WriteSignoff.Tables = strList "tables"
                                  WriteSignoff.AcknowledgedImpact = strOpt "acknowledgedImpact"
                                  WriteSignoff.ApprovedBy = strOpt "approvedBy"
                                  WriteSignoff.Date = strOpt "date" }
            let parsed = [ for e in arr.EnumerateArray() -> parseOne e ]
            let errors = parsed |> List.collect (function Error es -> es | Ok _ -> [])
            if not (List.isEmpty errors) then Result.failure errors
            else Result.success (parsed |> List.choose (function Ok a -> Some a | _ -> None))
        | Some _ ->
            Result.failureOf (configError "emission.signoff.shape" "emission.signoff must be an array of { mode, ... } objects.")

    /// Wave-3 slice 3.4 (now WIRED) — parse `emission.tolerance`: an array of
    /// `ToleratedDivergence` name tokens → `Tolerance option`. Absent ⇒ `None`
    /// (the permissive dual-track default applies downstream). Present ⇒
    /// `Tolerance.parse` (FAIL-CLOSED: an unrecognized token is a named config
    /// error, never silently ignored — silently widening the accepted set would
    /// corrupt the R6 quotient semantics). An empty `[]` parses to `Tolerance.strict`.
    let private parseTolerance (element: JsonElement) : Result<Tolerance option> =
        match tryGetProperty element "tolerance" with
        | None -> Result.success None
        | Some arr when arr.ValueKind = JsonValueKind.Array ->
            let tokens =
                arr.EnumerateArray()
                |> Seq.choose (fun t ->
                    if t.ValueKind = JsonValueKind.String then Option.ofObj (t.GetString()) else None)
                |> List.ofSeq
            match Tolerance.parse tokens with
            | Ok tol -> Result.success (Some tol)
            | Error (ToleranceError.UnknownDivergence token) ->
                let known =
                    ToleratedDivergence.allKnown
                    |> Set.toList
                    |> List.map ToleratedDivergence.name
                    |> String.concat ", "
                Result.failureOf (
                    configError "invalidValue"
                        (sprintf "emission.tolerance: unknown divergence token '%s'. Known tokens: %s." token known))
        | Some _ ->
            Result.failureOf (
                configError "typeMismatch" "emission.tolerance must be an array of divergence-name strings.")

    /// 2026-06-25 — `emission.dataStaging: { "mode": "auto"|"inline"|"tempTable",
    /// "threshold": <int>, "indexThreshold": <int> }`. Absent ⇒
    /// `DataStagingPolicy.auto` (stage above 1000 rows; index the `#temp` above
    /// the measured 100k floor; byte-identical to the prior hardcoded threshold).
    /// `mode` / `threshold` / `indexThreshold` are each optional within the
    /// object. FAIL-CLOSED: an unrecognized mode or a non-positive threshold /
    /// indexThreshold is a named config error.
    let private parseDataStaging (element: JsonElement) : Result<DataStagingPolicy> =
        match tryGetProperty element "dataStaging" with
        | None -> Result.success DataStagingPolicy.auto
        | Some staging when staging.ValueKind = JsonValueKind.Object ->
            result {
                let! mode =
                    match getOptionalString staging "mode" with
                    | Error es -> Error es
                    | Ok None                 -> Result.success DataStagingMode.Auto
                    | Ok (Some "auto")        -> Result.success DataStagingMode.Auto
                    | Ok (Some "inline")      -> Result.success DataStagingMode.Inline
                    | Ok (Some "tempTable")   -> Result.success DataStagingMode.TempTable
                    | Ok (Some other) ->
                        Result.failureOf (
                            configError "invalidValue"
                                (sprintf "emission.dataStaging.mode must be \"auto\", \"inline\", or \"tempTable\"; got \"%s\"." other))
                let! threshold =
                    match getIntOr staging "threshold" DataStagingPolicy.auto.Threshold with
                    | Ok threshold when threshold < 1 ->
                        Result.failureOf (
                            configError "invalidValue"
                                (sprintf "emission.dataStaging.threshold must be >= 1; got %d." threshold))
                    | other -> other
                let! indexThreshold =
                    match getIntOr staging "indexThreshold" DataStagingPolicy.auto.IndexThreshold with
                    | Ok indexThreshold when indexThreshold < 1 ->
                        Result.failureOf (
                            configError "invalidValue"
                                (sprintf "emission.dataStaging.indexThreshold must be >= 1; got %d." indexThreshold))
                    | other -> other
                return { Mode = mode; Threshold = threshold; IndexThreshold = indexThreshold }
            }
        | Some _ ->
            Result.failureOf (
                configError "typeMismatch" "emission.dataStaging must be an object with optional 'mode', 'threshold', and 'indexThreshold'.")

    let private parseEmission (root: JsonElement) : Result<EmissionSection> =
        match tryGetProperty root "emission" with
        | None -> Result.success defaultEmission
        | Some element ->
            let read name (defaultValue: bool) = getBoolOr element name defaultValue
            // The `result { }` CE threads the same short-circuit these fields
            // hand-rolled as a 20-deep `| Error es -> Error es` pyramid; the bind
            // ORDER is preserved verbatim, so the first-error surfaced on a
            // multi-field malformed block is byte-identical.
            result {
                let! ssdt = read "ssdt" defaultEmission.Ssdt
                let! dacpac = read "dacpac" defaultEmission.Dacpac
                // `emission.sqlproj` (2026-06-24) — flat rung (sibling of `dacpac`).
                let! sqlproj = read "sqlproj" defaultEmission.Sqlproj
                let! json = read "json" defaultEmission.Json
                let! dist = read "distributions" defaultEmission.Distributions
                let! seeds = read "staticSeeds" defaultEmission.StaticSeeds
                let! migDeps = read "migrationDependencies" defaultEmission.MigrationDependencies
                let! boot = read "bootstrap" defaultEmission.Bootstrap
                let! bootAll = read "bootstrapAllData" defaultEmission.BootstrapAllData
                let! dlog = read "decisionLog" defaultEmission.DecisionLog
                let! opps = read "opportunities" defaultEmission.Opportunities
                let! vals = read "validations" defaultEmission.Validations
                let! includeAuto = read "includePlatformAutoIndexes" defaultEmission.IncludePlatformAutoIndexes
                let! renderElegant = read "renderConstraintsElegant" defaultEmission.RenderConstraintsElegant
                // NM-70 — `emission.identityAnnotations`; default true.
                let! identityAnnotations = read "identityAnnotations" defaultEmission.EmitIdentityAnnotations
                let! dataVerification = parseDataVerification element
                let! deleteScope = parseDeleteScope element
                let! emissionSignoff = parseEmissionSignoff element
                let! tolerance = parseTolerance element
                let! dataStaging = parseDataStaging element
                let! dataReadConcurrency =
                    match getIntOr element "dataReadConcurrency" defaultEmission.DataReadConcurrency with
                    | Ok c when c < 1 ->
                        Result.failureOf (
                            configError
                                "emission.dataReadConcurrency.invalid"
                                (sprintf "emission.dataReadConcurrency must be >= 1; got %d." c))
                    | other -> other
                let! pipelinedBootstrap = read "pipelinedBootstrap" defaultEmission.PipelinedBootstrap
                return {
                    Ssdt = ssdt
                    Dacpac = dacpac
                    Sqlproj = sqlproj
                    Json = json
                    Distributions = dist
                    StaticSeeds = seeds
                    MigrationDependencies = migDeps
                    Bootstrap = boot
                    BootstrapAllData = bootAll
                    DecisionLog = dlog
                    Opportunities = opps
                    Validations = vals
                    IncludePlatformAutoIndexes = includeAuto
                    DeleteScope = deleteScope
                    Signoff = emissionSignoff
                    RenderConstraintsElegant = renderElegant
                    EmitIdentityAnnotations = identityAnnotations
                    DataVerification = dataVerification
                    Tolerance = tolerance
                    DataStaging = dataStaging
                    DataReadConcurrency = dataReadConcurrency
                    PipelinedBootstrap = pipelinedBootstrap
                }
            }

    let private getOptionalBool (element: JsonElement) (name: string) : Result<bool option> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success None
        | true, v when v.ValueKind = JsonValueKind.True -> Result.success (Some true)
        | true, v when v.ValueKind = JsonValueKind.False -> Result.success (Some false)
        | _ ->
            Result.failureOf (configError "typeMismatch" (sprintf "'%s' must be a boolean." name))

    let private getOptionalDecimal (element: JsonElement) (name: string) : Result<decimal option> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success None
        | true, v when v.ValueKind = JsonValueKind.Number ->
            let mutable d : decimal = 0m
            if v.TryGetDecimal(&d) then Result.success (Some d)
            else
                Result.failureOf (configError "typeMismatch" (sprintf "'%s' must be a decimal." name))
        | _ ->
            Result.failureOf (configError "typeMismatch" (sprintf "'%s' must be a numeric value." name))

    let private getOptionalInt64 (element: JsonElement) (name: string) : Result<int64 option> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success None
        | true, v when v.ValueKind = JsonValueKind.Number ->
            let mutable n : int64 = 0L
            if v.TryGetInt64(&n) then Result.success (Some n)
            else
                Result.failureOf (configError "typeMismatch" (sprintf "'%s' must be a 64-bit integer." name))
        | _ ->
            Result.failureOf (configError "typeMismatch" (sprintf "'%s' must be an integer." name))

    let private parseTighteningAttributeOverride (element: JsonElement) : Result<TighteningAttributeOverride> =
        result {
            let! ref = getString element "attributeRef"
            let! action = getString element "action"
            return { AttributeRef = ref; Action = action }
        }

    let private parseTighteningOverrides (element: JsonElement) : Result<TighteningAttributeOverride list> =
        match element.TryGetProperty("overrides") with
        | false, _ -> Result.success []
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray()
            |> Seq.toList
            |> List.map parseTighteningAttributeOverride
            |> Result.aggregate
        | _ ->
            Result.failureOf (
                configError "typeMismatch" "tightening intervention 'overrides' must be an array.")

    let private parseTighteningReferenceOverride (element: JsonElement) : Result<TighteningReferenceOverride> =
        result {
            let! ref = getString element "referenceRef"
            let! action = getString element "action"
            return { ReferenceRef = ref; Action = action }
        }

    /// The `referenceOverrides` array on a `foreignKey` intervention
    /// (DECISIONS 2026-07-15, the estate A6 amendment). A distinct key
    /// from the nullability `overrides` so the two override grains
    /// (attribute / reference) never share a shape ambiguously.
    let private parseTighteningReferenceOverrides (element: JsonElement) : Result<TighteningReferenceOverride list> =
        match element.TryGetProperty("referenceOverrides") with
        | false, _ -> Result.success []
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray()
            |> Seq.toList
            |> List.map parseTighteningReferenceOverride
            |> Result.aggregate
        | _ ->
            Result.failureOf (
                configError "typeMismatch" "tightening intervention 'referenceOverrides' must be an array.")

    let private parseTighteningIntervention (element: JsonElement) : Result<TighteningInterventionEntry> =
        result {
            let! kind = getString element "kind"
            let! id = getString element "id"
            let! nullBudget = getOptionalDecimal element "nullBudget"
            let! allowMand = getOptionalBool element "allowMandatoryRelaxation"
            let! overrides = parseTighteningOverrides element
            let! ensSc = getOptionalBool element "enforceSingleColumnUnique"
            let! ensMc = getOptionalBool element "enforceMultiColumnUnique"
            let! enable = getOptionalBool element "enableCreation"
            let! crossSchema = getOptionalBool element "allowCrossSchema"
            let! crossCatalog = getOptionalBool element "allowCrossCatalog"
            let! missingDR = getOptionalBool element "treatMissingDeleteRuleAsIgnore"
            let! nocheck = getOptionalBool element "allowNoCheckCreation"
            let! refOverrides = parseTighteningReferenceOverrides element
            let! minDist = getOptionalInt64 element "minDistinctCountForUniqueness"
            return {
                Kind = kind
                Id = id
                NullBudget = nullBudget
                AllowMandatoryRelaxation = allowMand
                NullabilityOverrides = overrides
                EnforceSingleColumnUnique = ensSc
                EnforceMultiColumnUnique = ensMc
                EnableCreation = enable
                AllowCrossSchema = crossSchema
                AllowCrossCatalog = crossCatalog
                TreatMissingDeleteRuleAsIgnore = missingDR
                AllowNoCheckCreation = nocheck
                ForeignKeyOverrides = refOverrides
                MinDistinctCountForUniqueness = minDist
            }
        }

    let private parseTightening (root: JsonElement) : Result<TighteningSection option> =
        match tryGetProperty root "tightening" with
        | None -> Result.success None
        | Some element ->
            match element.TryGetProperty("interventions") with
            | false, _ -> Result.success (Some { Interventions = [] })
            | true, v when v.ValueKind = JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map parseTighteningIntervention
                |> Result.aggregate
                |> Result.map (fun entries -> Some { Interventions = entries })
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "tightening.interventions must be an array.")

    /// Parse one `policy.transformGroups[]` entry — a `{ name, enabled }`
    /// pair. The binder (`TransformGroupsBinding.fromConfig`) resolves
    /// the string `name` to the closed-DU `TransformGroup` value at
    /// bind time.
    let private parseTransformGroupEntry (element: JsonElement) : Result<TransformGroupEntry> =
        result {
            let! name = getString element "name"
            let! enabled = getBoolOr element "enabled" true
            return { Name = name; Enabled = enabled }
        }

    let private parseTransformGroups (element: JsonElement) : Result<TransformGroupEntry list> =
        match element.TryGetProperty("transformGroups") with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map (fun e ->
                    if e.ValueKind = JsonValueKind.Object then
                        parseTransformGroupEntry e
                    else
                        Result.failureOf (
                            configError
                                "typeMismatch"
                                "policy.transformGroups entries must be { name, enabled } objects."))
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "policy.transformGroups must be an array.")

    let private parsePolicy (root: JsonElement) : Result<PolicySection> =
        match tryGetProperty root "policy" with
        | None -> Result.success defaultPolicy
        | Some element ->
            validation {
                let! insertion =
                    getOptionalString element "insertion"
                    |> Result.map (Option.defaultValue defaultPolicy.Insertion)
                and! tightening = parseTightening element
                and! transformGroups = parseTransformGroups element
                return {
                    Insertion       = insertion
                    Tightening      = tightening
                    TransformGroups = transformGroups
                }
            }

    let private parseOutput (root: JsonElement) : Result<OutputSection> =
        match tryGetProperty root "output" with
        | None -> Result.success defaultOutput
        | Some element ->
            match getOptionalString element "dir" with
            | Error es -> Error es
            | Ok None -> Result.success defaultOutput
            | Ok (Some d) -> Result.success { Dir = d }

    // -----------------------------------------------------------------------
    // Top-level parser
    // -----------------------------------------------------------------------

    let private parseRootWith (requireModel: bool) (root: JsonElement) : Result<Config> =
        if root.ValueKind <> JsonValueKind.Object then
            Result.failureOf (
                configError "typeMismatch" "Config root must be a JSON object.")
        else
            // Applicative accumulation (FsToolkit `validation`): every top-level
            // section is parsed and ALL section errors surface in one pass — the
            // operator sees every malformed section at once, fulfilling `parse`'s
            // docstring promise ("every malformed entry in one pass, not just the
            // first"). The prior hand-threaded `match … | Error es -> Error es`
            // ladder short-circuited at the first failing section, so a config
            // bad in both `profiler` and `output` reported only the profiler error.
            // Mirrors the binding layer's idiom (`EmissionFoldersBinding.bindEntry`).
            validation {
                let! model     = parseModelWith requireModel root
                and! profile   = parseProfile root
                and! profiler  = parseProfiler root
                and! overrides = parseOverrides root
                and! emission  = parseEmission root
                and! policy    = parsePolicy root
                and! output    = parseOutput root
                return {
                    Model       = model
                    Profile     = profile
                    Profiler    = profiler
                    Overrides   = overrides
                    Emission    = emission
                    Policy      = policy
                    Output      = output
                }
            }

    /// Parse a JSON string into a typed `Config`. Order of operations:
    ///   1. Parse the JSON syntactically. Malformed JSON returns
    ///      `pipeline.config.jsonInvalid`.
    ///   2. Scan the entire document for property names that look like
    ///      credentials (D9 guardrail). Any hit returns
    ///      `pipeline.config.credentialPropertyForbidden`; the structural
    ///      parser is bypassed.
    ///   3. Parse each top-level section. Missing sections fall back to
    ///      typed defaults; missing required fields (`model.path`) error.
    ///      Type mismatches return `pipeline.config.typeMismatch`.
    ///
    /// Returns all accumulated errors (per `Result.aggregate` semantics
    /// in list-parsing helpers) — the operator sees every malformed entry
    /// in one pass, not just the first.
    let parse (json: string) : Result<Config> =
        try
            use document = JsonDocument.Parse(json)
            let root = document.RootElement
            match scanForCredentials "" root with
            | [] -> parseRootWith true root
            | errors -> Error errors
        with
        | :? JsonException as ex ->
            Result.failureOf (
                configError "jsonInvalid" (sprintf "Failed to parse JSON: %s" ex.Message))

    /// Lenient parse — identical to `parse` (same D9 credential pre-scan, same
    /// section parsing, same `typeMismatch`/structural errors) EXCEPT that an
    /// absent or no-source `model` section yields the no-source
    /// `defaultModelSection` instead of erroring `modelNoSource`. This is the
    /// `Shaping` view used by the movement surface, where a movement-only
    /// `projection.json` legitimately carries no shaping `model`. The strict
    /// `parse`/`fromFile` are UNCHANGED for the `explain`/`full-export`
    /// consumers. An empty/whitespace document is `defaultConfig`.
    let parseLenient (json: string) : Result<Config> =
        if System.String.IsNullOrWhiteSpace json then Result.success defaultConfig
        else
        try
            use document = JsonDocument.Parse(json)
            let root = document.RootElement
            match scanForCredentials "" root with
            | [] -> parseRootWith false root
            | errors -> Error errors
        with
        | :? JsonException as ex ->
            Result.failureOf (
                configError "jsonInvalid" (sprintf "Failed to parse JSON: %s" ex.Message))

    /// Read and parse a config file from disk. Layer thin on top of `parse`:
    /// surfaces `pipeline.config.fileNotFound` when the path is missing and
    /// `pipeline.config.fileReadError` when the file exists but cannot be
    /// read. Successful read flows into `parse` which produces all
    /// structural / D9 errors as `Result<Config>`.
    let fromFile (path: string) : Result<Config> =
        if not (System.IO.File.Exists path) then
            Result.failureOf (
                configError "fileNotFound" (sprintf "Config file not found: %s" path))
        else
            let readResult =
                try Ok (System.IO.File.ReadAllText path)
                with ex ->
                    Error [
                        configError
                            "fileReadError"
                            (sprintf "Failed to read config file '%s': %s" path ex.Message)
                    ]
            match readResult with
            | Error es -> Error es
            | Ok json -> parse json

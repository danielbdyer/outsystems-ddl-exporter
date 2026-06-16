namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE-MUTATION: BCL System.Text.Json.Nodes APIs require
//   mutation on JsonObject / JsonArray for typed JSON tree
//   construction (`obj.Add(key, value)`, `arr.Add(value)`). This is
//   the canonical BCL primitive (per pillar 4: OOP where BCL forces
//   it; pillar 7: gold-standard library precedence). The mutation is
//   reified at the file level; the typed JsonNode tree IS the
//   structural carrier through to the Utf8JsonWriter terminal.

open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// Π_Manifest — chapter 4.1.A slice 9 substantive deliverable. The
/// production-deployment SSDT manifest emitter (sibling Π) producing
/// `<outDir>/manifest.json` per V1 `SsdtManifest` schema (V1 source
/// `src/Osm.Emission/SsdtManifest.cs:6-14`; mirrored here per V1↔V2
/// ubiquitous-language commitment per pillar 8 + cherry-pick
/// discipline per `DECISIONS 2026-05-06`).
///
/// **V2-driver KPI Phase 2 (per `V2_DRIVER.md`).** The manifest is
/// part of the SSDT directory bundle V2 emits under V2-driver mode;
/// V1's existing pipeline expects it. Without it, V2's bundle isn't
/// SSDT-compatible.
///
/// **Per chapter pre-scope §8 slice 9 + V2-driver KPI smart-product-
/// choices:** v0 manifest carries the structural fields V2 currently
/// has evidence for (Tables array per kind; Emission stamp). Fields
/// that chapter 4.4 fills (Coverage / PredicateCoverage /
/// PreRemediation / Unsupported) emit explicitly as `null` / empty
/// arrays so the schema shape is V1-compatible while the semantic
/// payload defers to its rightful chapter. Per pillar 1: the typed
/// `Manifest` record IS the canonical form; the JSON text emerges
/// only at the absolute terminal `Utf8JsonWriter` boundary.
/// Slice α (chapter 4.4) — per-axis emit-vs-total breakdown.
/// Mirrors V1's `Osm.Emission.CoverageBreakdown`
/// (`SsdtManifest.cs:68-90`) including the percentage-rounding
/// contract (`Math.Round(value, 2, MidpointRounding.AwayFromZero)`).
///
/// Pillar-9 classification: DataIntent. Reachable from `Catalog`
/// alone without operator opinion; lands in the skeleton.
///
/// Pillar 8 four-question naming analysis. Concept-shaped: a
/// `CoverageBreakdown` IS the axis's emit-vs-total summary, not
/// a verb. Sibling vocabulary with V1's `CoverageBreakdown`.
type CoverageBreakdown = {
    /// Count of units the emitter emitted. Always ≤ `Total`
    /// (smart-constructor invariant).
    Emitted    : int
    /// Count of units in the catalog. Always ≥ 0.
    Total      : int
    /// `Emitted` as a percentage of `Total`, rounded to 2 decimal
    /// places (AwayFromZero, mirroring V1). Edge cases: `Total = 0`
    /// → 100m (vacuous full coverage); `Emitted = 0` with
    /// `Total > 0` → 0m.
    Percentage : decimal
}

[<RequireQualifiedAccess>]
module CoverageBreakdown =

    let private emittedNegative =
        ValidationError.create
            "coverage.emittedNegative"
            "Coverage Emitted count cannot be negative."
    let private totalNegative =
        ValidationError.create
            "coverage.totalNegative"
            "Coverage Total count cannot be negative."
    let private emittedExceedsTotal =
        ValidationError.create
            "coverage.emittedExceedsTotal"
            "Coverage Emitted count cannot exceed Total."

    let private computePercentage (emitted: int) (total: int) : decimal =
        if total <= 0 then 100m
        elif emitted <= 0 then 0m
        else
            let value = (decimal emitted) / (decimal total) * 100m
            System.Math.Round(value, 2, System.MidpointRounding.AwayFromZero)

    /// Smart constructor (A39). Enforces non-negative counts +
    /// `Emitted ≤ Total`. Percentage computed via V1's contract.
    let create (emitted: int) (total: int) : Result<CoverageBreakdown> =
        if emitted < 0 then Result.failureOf emittedNegative
        elif total < 0 then Result.failureOf totalNegative
        elif emitted > total then Result.failureOf emittedExceedsTotal
        else
            Result.success
                { Emitted    = emitted
                  Total      = total
                  Percentage = computePercentage emitted total }


/// Slice α (chapter 4.4) — per-axis coverage summary. Mirrors V1's
/// `Osm.Emission.SsdtCoverageSummary` (`SsdtManifest.cs:54-66`).
/// Three axes: Tables / Columns / Constraints. The constraint
/// breakdown counts the union (PKs + non-PK unique indexes + FKs +
/// CHECK constraints) of every structural constraint the emitter
/// renders for a kind.
type CoverageSummary = {
    Tables      : CoverageBreakdown
    Columns     : CoverageBreakdown
    Constraints : CoverageBreakdown
}

[<RequireQualifiedAccess>]
module CoverageSummary =

    /// Build a summary where every axis reports `Emitted = Total`
    /// (V2's emit-everything default; T11 keyset coverage holds
    /// structurally). Mirrors V1's
    /// `SsdtCoverageSummary.CreateComplete`.
    let createComplete (tables: int) (columns: int) (constraints: int)
        : Result<CoverageSummary> =
        match CoverageBreakdown.create tables tables,
              CoverageBreakdown.create columns columns,
              CoverageBreakdown.create constraints constraints with
        | Ok t, Ok c, Ok r ->
            Result.success { Tables = t; Columns = c; Constraints = r }
        | tablesR, columnsR, constraintsR ->
            // Aggregate errors across all three axes for diagnostic
            // completeness.
            [tablesR; columnsR; constraintsR]
            |> List.collect Result.errors
            |> Result.failure


/// Slice β (chapter 4.4) — V1's 17 named manifest predicates as a
/// closed DU. Mirrors V1's `SsdtPredicateNames` constants
/// (`/home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtPredicateCoverage.cs:7-25`).
/// Each variant names a structural property of a Kind that
/// operators consult at the manifest surface.
///
/// Pillar-9 classification: DataIntent. Each evaluation reads the
/// kind's IR fields; no operator opinion enters.
///
/// Pillar 8 four-question naming analysis. Each variant is
/// concept-shaped (a `HasTrigger` IS the property "the kind has a
/// trigger"; not "HandleTrigger" or "ProcessTrigger"). Variant
/// names preserve V1's ubiquitous language verbatim.
[<RequireQualifiedAccess>]
type PredicateName =
    /// Kind's `Modality` carries `ModalityMark.Temporal`.
    | HasTemporalHistory
    /// Kind's `Triggers` list is non-empty.
    | HasTrigger
    /// Kind's `Modality` carries `ModalityMark.Static`.
    | IsStaticEntity
    /// Kind's `Origin` is not `Native` (`ExternalIndirect`
    /// or `ExternalDirect`).
    | IsExternalEntity
    /// Kind's `IsActive = false`.
    | IsInactiveEntity
    /// Kind has at least one Attribute with `IsActive = false`.
    | HasInactiveColumns
    /// Kind has at least one Attribute with `DefaultValue = Some _`.
    | HasDefaultConstraint
    /// Kind's `ColumnChecks` list is non-empty.
    | HasCheckConstraint
    /// Kind has at least one ExtendedProperty at table / column /
    /// or index level.
    | HasExtendedProperties
    /// Kind has at least one non-PK Index with `IsUnique = true`.
    | HasUniqueIndex
    /// Kind has at least one non-PK unique Index with more than
    /// one column.
    | HasCompositeUniqueIndex
    /// Kind has at least one Index with a filter predicate. V2 IR
    /// doesn't carry filter expressions on `Index` (no `Filter`
    /// field); always emits false. Forward signal: lifts to true
    /// when an IR refinement adds the field under V1-fixture
    /// pressure.
    | HasFilteredIndex
    /// Kind has at least one Index that distinguishes key columns
    /// from included columns. V2 IR's `Index.Columns` is a flat
    /// SsKey list (no key/included split); always emits false.
    /// Forward signal: lifts to true when an IR refinement adds
    /// the split.
    | HasIncludedIndexColumns
    /// Kind has at least one Reference (FK).
    | HasLogicalForeignKey
    /// Kind has a Reference that does NOT materialize as a DB
    /// constraint. V2 IR's `Reference` doesn't carry the logical-
    /// vs-physical distinction (every Reference is logical at IR;
    /// whether it materializes a DB constraint is a Tightening
    /// pass decision). Always emits false. Forward signal: lifts
    /// when the tightening decision flows into the manifest.
    | HasLogicalForeignKeyWithoutDbConstraint
    /// Kind has a Reference that DOES materialize as a DB
    /// constraint. Same V2 IR gap as
    /// `HasLogicalForeignKeyWithoutDbConstraint`; always emits
    /// false. Forward signal: lifts when tightening decision
    /// surfaces.
    | HasLogicalForeignKeyWithDbConstraint

[<RequireQualifiedAccess>]
module PredicateName =

    /// V1-vocabulary string for the predicate (matches V1's
    /// `SsdtPredicateNames` constants verbatim). Mirroring this
    /// preserves operator ubiquitous-language continuity across the
    /// V1↔V2 manifest surface (pillar 8).
    let toString (p: PredicateName) : string =
        match p with
        | PredicateName.HasTemporalHistory                       -> "HasTemporalHistory"
        | PredicateName.HasTrigger                               -> "HasTrigger"
        | PredicateName.IsStaticEntity                           -> "IsStaticEntity"
        | PredicateName.IsExternalEntity                         -> "IsExternalEntity"
        | PredicateName.IsInactiveEntity                         -> "IsInactiveEntity"
        | PredicateName.HasInactiveColumns                       -> "HasInactiveColumns"
        | PredicateName.HasDefaultConstraint                     -> "HasDefaultConstraint"
        | PredicateName.HasCheckConstraint                       -> "HasCheckConstraint"
        | PredicateName.HasExtendedProperties                    -> "HasExtendedProperties"
        | PredicateName.HasUniqueIndex                           -> "HasUniqueIndex"
        | PredicateName.HasCompositeUniqueIndex                  -> "HasCompositeUniqueIndex"
        | PredicateName.HasFilteredIndex                         -> "HasFilteredIndex"
        | PredicateName.HasIncludedIndexColumns                  -> "HasIncludedIndexColumns"
        | PredicateName.HasLogicalForeignKey                     -> "HasLogicalForeignKey"
        | PredicateName.HasLogicalForeignKeyWithoutDbConstraint  -> "HasLogicalForeignKeyWithoutDbConstraint"
        | PredicateName.HasLogicalForeignKeyWithDbConstraint     -> "HasLogicalForeignKeyWithDbConstraint"

    /// Sorted canonical order — used at emission to guarantee T1
    /// byte-determinism of the manifest's `PredicateCounts` array
    /// shape (per chapter 4.4 open Q2: V2 emits PredicateCounts as
    /// a sorted-by-name array of `{name, count}` objects, not as
    /// a JSON dict).
    let all : PredicateName list =
        [ PredicateName.HasCheckConstraint
          PredicateName.HasCompositeUniqueIndex
          PredicateName.HasDefaultConstraint
          PredicateName.HasExtendedProperties
          PredicateName.HasFilteredIndex
          PredicateName.HasInactiveColumns
          PredicateName.HasIncludedIndexColumns
          PredicateName.HasLogicalForeignKey
          PredicateName.HasLogicalForeignKeyWithDbConstraint
          PredicateName.HasLogicalForeignKeyWithoutDbConstraint
          PredicateName.HasTemporalHistory
          PredicateName.HasTrigger
          PredicateName.HasUniqueIndex
          PredicateName.IsExternalEntity
          PredicateName.IsInactiveEntity
          PredicateName.IsStaticEntity ]

    /// Evaluate a single predicate against a Kind. Pure function;
    /// the closed-DU exhaustive match ensures no variant goes
    /// unevaluated. Variants marked "no V2 IR evidence" always
    /// return false until the corresponding IR refinement lands.
    let evaluate (p: PredicateName) (k: Kind) : bool =
        match p with
        | PredicateName.HasTemporalHistory ->
            k.Modality
            |> List.exists (function
                | ModalityMark.Temporal _ -> true
                | _ -> false)
        | PredicateName.HasTrigger ->
            not (List.isEmpty k.Triggers)
        | PredicateName.IsStaticEntity ->
            k.Modality
            |> List.exists (function
                | ModalityMark.Static _ -> true
                | _ -> false)
        | PredicateName.IsExternalEntity ->
            match k.Origin with
            | Origin.Native -> false
            | Origin.ExternalIndirect
            | Origin.ExternalDirect -> true
        | PredicateName.IsInactiveEntity ->
            not k.IsActive
        | PredicateName.HasInactiveColumns ->
            k.Attributes |> List.exists (fun a -> not a.IsActive)
        | PredicateName.HasDefaultConstraint ->
            k.Attributes |> List.exists (fun a -> Option.isSome a.DefaultValue)
        | PredicateName.HasCheckConstraint ->
            not (List.isEmpty k.ColumnChecks)
        | PredicateName.HasExtendedProperties ->
            not (List.isEmpty k.ExtendedProperties)
            || k.Attributes |> List.exists (fun a -> not (List.isEmpty a.ExtendedProperties))
            || k.Indexes |> List.exists (fun i -> not (List.isEmpty i.ExtendedProperties))
        | PredicateName.HasUniqueIndex ->
            k.Indexes
            |> List.exists (fun i ->
                match i.Uniqueness with
                | Unique -> true
                | PrimaryKey | NotUnique -> false)
        | PredicateName.HasCompositeUniqueIndex ->
            k.Indexes
            |> List.exists (fun i ->
                (match i.Uniqueness with
                 | Unique -> true
                 | PrimaryKey | NotUnique -> false)
                && List.length i.Columns > 1)
        | PredicateName.HasFilteredIndex ->
            // Chapter 4.5 slice α — IR evidence lifted via
            // `Index.Filter : string option`. Kind has a filtered
            // index iff any of its indexes carry a Some filter.
            k.Indexes |> List.exists (fun i -> Option.isSome i.Filter)
        | PredicateName.HasIncludedIndexColumns ->
            // Chapter 4.5 slice β — IR evidence lifted via
            // `Index.IncludedColumns : SsKey list`. Kind has a
            // covering index iff any of its indexes has at least one
            // included column.
            k.Indexes |> List.exists (fun i -> not (List.isEmpty i.IncludedColumns))
        | PredicateName.HasLogicalForeignKey ->
            not (List.isEmpty k.References)
        | PredicateName.HasLogicalForeignKeyWithoutDbConstraint ->
            // Chapter 4.6 slice α — IR evidence lifted via
            // `Reference.HasDbConstraint : bool`. Kind has a
            // logical-only FK iff any of its references is
            // unbacked by a DB constraint.
            k.References |> List.exists (fun r -> not (Reference.hasDbConstraint r))
        | PredicateName.HasLogicalForeignKeyWithDbConstraint ->
            // Chapter 4.6 slice α — sibling: kind has a DB-constraint-
            // backed FK iff any of its references is DB-constraint
            // backed.
            k.References |> List.exists (fun r -> Reference.hasDbConstraint r)


/// Slice β (chapter 4.4) — per-table predicate satisfaction entry.
/// Mirrors V1's `Osm.Emission.PredicateCoverageEntry`
/// (`SsdtPredicateCoverage.cs:27-40`). Predicates carried as the
/// typed `PredicateName` list (V2-native typed form); JSON
/// serialization renders the names at the terminal boundary per
/// pillar 1.
type PredicateCoverageEntry = {
    Module     : string
    Schema     : string
    Table      : string
    Predicates : PredicateName list
}

/// Slice β (chapter 4.4) — manifest's predicate coverage summary.
/// Mirrors V1's `Osm.Emission.SsdtPredicateCoverage`
/// (`SsdtPredicateCoverage.cs:42-49`). `Tables` lists per-table
/// satisfied-predicate entries; `PredicateCounts` is a Map from
/// each predicate name to the count of tables that satisfy it.
///
/// Forward signal (per chapter 4.4 open Q2 resolved-at-open):
/// JSON serialization emits `PredicateCounts` as a sorted-by-name
/// array of `{ name, count }` objects (not as a JSON dict) to
/// preserve T1 byte-determinism. Tolerance entry needed if V1
/// differential demands JSON-shape parity.
type PredicateCoverage = {
    Tables          : PredicateCoverageEntry list
    PredicateCounts : Map<PredicateName, int>
}

[<RequireQualifiedAccess>]
module PredicateCoverage =

    /// Empty predicate coverage — mirrors V1's
    /// `SsdtPredicateCoverage.Empty`.
    let empty : PredicateCoverage =
        { Tables = []; PredicateCounts = Map.empty }

    /// Evaluate every predicate against a kind; returns the
    /// predicates that satisfy in canonical sorted order
    /// (`PredicateName.all`).
    let satisfiedBy (k: Kind) : PredicateName list =
        PredicateName.all
        |> List.filter (fun p -> PredicateName.evaluate p k)

    /// Compute predicate coverage for a Catalog. Per A18 amended:
    /// Catalog only. Per T1 byte-determinism: pure; same input →
    /// same output. Tables emitted in catalog declaration order
    /// (matches `ManifestEmitter.buildWith` Tables ordering);
    /// PredicateCounts aggregated across every Kind in the catalog.
    let compute (catalog: Catalog) : PredicateCoverage =
        use _ = Bench.scope "emit.manifest.predicateCoverage"
        let tables =
            catalog.Modules
            |> List.collect (fun m ->
                m.Kinds
                |> List.map (fun k ->
                    {
                        Module     = Name.value m.Name
                        Schema     = TableId.schemaText k.Physical
                        Table      = TableId.tableText k.Physical
                        Predicates = satisfiedBy k
                    }))
        let counts =
            tables
            |> List.collect (fun entry -> entry.Predicates)
            |> List.countBy id
            |> Map.ofList
        { Tables = tables; PredicateCounts = counts }


/// Slice γ (chapter 4.4) — manifest's `Unsupported` field
/// renders each `ToleratedDivergence` as its discriminator name,
/// sorted by string comparison for T1 byte-determinism. Mirrors
/// V1's `Unsupported : IReadOnlyList<string>` field shape.
///
/// Pillar-9 classification: DataIntent — `allKnown` is the
/// closed-DU enumeration of V1↔V2 emit divergences V2 currently
/// exhibits; no operator opinion at compute time.
///
/// Forward signal (per chapter 4.4 open Q3): when a downstream
/// consumer demands per-divergence rationale strings (not just
/// names), `Unsupported` widens to a typed record list and this
/// module's signature changes accordingly.
[<RequireQualifiedAccess>]
module Unsupported =

    /// Render `ToleratedDivergence` as its V1-vocabulary
    /// discriminator name (matches the F# DU case name; preserves
    /// V1 ubiquitous language per pillar 8). Delegates to
    /// `ToleratedDivergence.name` — the single source of truth for
    /// the config-token spelling — so a new variant lands its token
    /// once (and this emitter inherits it without a parallel match).
    let private toName (d: ToleratedDivergence) : string =
        ToleratedDivergence.name d

    /// Compute the manifest's `Unsupported` list. Renders every
    /// empirically-known `ToleratedDivergence` variant as a string
    /// in sorted order. T1 byte-determinism: same input → same
    /// output (the input is empty unit; the output is the closed
    /// enumeration). Bench scope per iterator-logging-as-first-
    /// class-outcome discipline.
    let compute () : string list =
        use _ = Bench.scope "emit.manifest.unsupported"
        ToleratedDivergence.allKnown
        |> Set.toList
        |> List.map toName
        |> List.sort


/// Slice α (chapter 4.4) — `Catalog -> CoverageSummary` computation.
/// V2 emits every kind from the catalog (T11 keyset coverage); the
/// computation reduces to `CreateComplete` over per-axis catalog
/// counts. A18 amended preserved: Catalog only.
///
/// Forward signal: if a future `EmissionPolicy.Selection` axis
/// filters kinds, the emitted-vs-total split widens — `Coverage
/// .compute` then takes the emit-set as a second argument.
[<RequireQualifiedAccess>]
module Coverage =

    /// Count constraints attached to a kind for the manifest
    /// coverage axis. PK counts as 1 if the kind has any PK
    /// attributes (a kind has at most one primary key). Non-PK
    /// unique indexes + FK references + CHECK constraints each
    /// contribute their list length.
    let private constraintsOf (k: Kind) : int =
        let pkCount =
            if k.Attributes |> List.exists (fun a -> a.IsPrimaryKey) then 1
            else 0
        let uniqueIndexCount =
            k.Indexes
            |> List.filter (fun i ->
                match i.Uniqueness with
                | Unique -> true
                | PrimaryKey | NotUnique -> false)
            |> List.length
        let fkCount = List.length k.References
        let checkCount = List.length k.ColumnChecks
        pkCount + uniqueIndexCount + fkCount + checkCount

    /// Compute the coverage summary for a Catalog. Per A18 amended:
    /// Catalog only; no Policy, no Profile. Per T1 byte-determinism:
    /// pure function of the input; same input → same output.
    let compute (catalog: Catalog) : CoverageSummary =
        use _ = Bench.scope "emit.manifest.coverage"
        let allKinds =
            catalog.Modules |> List.collect (fun m -> m.Kinds)
        let tableCount = List.length allKinds
        let columnCount =
            allKinds |> List.sumBy (fun k -> List.length k.Attributes)
        let constraintCount =
            allKinds |> List.sumBy constraintsOf
        // Result.value safe here: counts are catalog-derived and
        // therefore non-negative; emitted = total by construction.
        CoverageSummary.createComplete tableCount columnCount constraintCount
        |> Result.value


[<RequireQualifiedAccess>]
module ManifestEmitter =

    [<Literal>]
    let version : int = 1

    /// Per-column statistical moments summary. Emitted when
    /// `Profile` carries a `NumericDistribution` with `Moments`
    /// for the corresponding attribute (H-027). Absent when the
    /// attribute has no numeric profiling data or no moments
    /// (profile was `Profile.empty` or the profiler ran without
    /// `AVG`/`STDEVP` aggregates).
    type ColumnProfileSummary = {
        Schema  : string
        Table   : string
        Column  : string
        Mean    : decimal
        StdDev  : decimal
    }

    /// Per-table manifest entry. Concept-shaped per pillar 8: the
    /// entry IS the table's per-kind summary in the manifest.
    /// Mirrors V1's `TableManifestEntry` (V1 source
    /// `SsdtManifest.cs:16-26`); fields V2 has evidence for ship
    /// directly; fields that depend on chapter 4.4 enrichment emit
    /// as defaults under V2-driver KPI smart-product-choices.
    type TableManifestEntry =
        {
            /// Owning module name (V1 convention: directory in
            /// `<outDir>/Modules/<Module>/`).
            Module : string
            /// SQL Server schema (typically `dbo`).
            Schema : string
            /// SQL Server table name (V1 physical name).
            Table : string
            /// Relative path of the per-table .sql file from
            /// `<outDir>` root. Forward-slash separators per V1
            /// convention (cross-platform deterministic per
            /// chapter pre-scope §6 + V2-driver KPI).
            TableFile : string
            /// Count of non-PK indexes emitted for this table.
            IndexCount : int
            /// Count of FK constraints emitted (inline) for this
            /// table.
            ForeignKeyCount : int
        }

    /// V2 manifest shape. Mirrors V1's `SsdtManifest`
    /// (`src/Osm.Emission/SsdtManifest.cs:6-14`) at the structural
    /// level; per chapter pre-scope §8 slice 9 + V2-driver KPI:
    /// Coverage / PredicateCoverage / PreRemediation / Unsupported
    /// emit as defaults until chapter 4.4 surfaces real evidence.
    type Manifest =
        {
            /// One entry per kind in the catalog (sorted by Module
            /// then by Schema/Table within the module). Determinism:
            /// the order is a function of the input Catalog.
            Tables : TableManifestEntry list
            /// Emitter-version stamp. Used by downstream consumers
            /// to detect manifest-schema changes; bump
            /// `ManifestEmitter.version` when the shape changes.
            EmitterVersion : int
            /// **Chapter A.4.7' slice ζ** — deterministic SHA256 digest
            /// of the registered-transforms metadata. Stable across
            /// emit calls when the registry is unchanged; changes when
            /// any Name / Domain / StageBinding / Sites / Status field
            /// changes. The 5th bidirectional property test
            /// (registry-digest round-trip) asserts both halves of the
            /// stability + perturbation contract.
            RegistryDigest : string
            /// **Chapter 4.4 slice α** — per-axis emit-vs-total
            /// summary (Tables / Columns / Constraints). Retires the
            /// `chapter 4.4 fills` deferral for the Coverage axis.
            /// V2 emits every kind in the catalog (T11 keyset
            /// coverage); the value is `CreateComplete` over the
            /// catalog's counts.
            Coverage : CoverageSummary
            /// **Chapter 4.4 slice β** — per-table predicate
            /// satisfaction + PredicateCounts aggregation. Retires
            /// the `chapter 4.4 fills` deferral for the
            /// PredicateCoverage axis. Mirrors V1's
            /// `SsdtPredicateCoverage` shape; the V2 surface carries
            /// typed `PredicateName` values that render at the JSON
            /// terminal.
            PredicateCoverage : PredicateCoverage
            /// **Chapter 4.4 slice γ** — V1↔V2 emit divergences in
            /// play, rendered as sorted strings. Retires the
            /// `chapter 4.4 fills` deferral for the Unsupported
            /// axis. Mirrors V1's `Unsupported : IReadOnlyList<string>`
            /// shape.
            Unsupported : string list
            /// **H-027** — per-column statistical moments (Mean +
            /// StdDev) derived from `NumericDistribution.Moments`.
            /// Empty when `Profile.isEmpty` (default for non-LiveProfiler
            /// pipeline paths). Sorted by Schema → Table → Column for
            /// T1 byte-determinism.
            ColumnProfiles : ColumnProfileSummary list
            /// **H-038** — FK-safe parallel deployment batches. Each
            /// inner list is one Kahn-level from `TopologicalOrder
            /// .levels`; kinds within a batch have no FK dependency on
            /// each other and may be deployed in parallel. Empty when
            /// `TopologicalOrder` is not available (e.g., no pass was
            /// run). Outer list ordered dependency-depth ascending;
            /// inner lists sorted by SsKey for T1 byte-determinism.
            DeploymentBatches : SsKey list list
            /// **H-085** — Policy version stamp. The `VersionedPolicy`
            /// captures both the content digest and the SemVer of the
            /// policy that drove this projection. `None` for
            /// projection paths that did not carry a versioned policy
            /// (e.g., `build` overload with no policy argument);
            /// `Some vp` when the caller threaded a versioned policy
            /// through. Consumers can diff two manifests by digest to
            /// determine whether DDL deltas trace to schema changes or
            /// policy changes.
            PolicyVersion : VersionedPolicy option
            /// **H-034** — `PolicyConflict` entries detected by
            /// `ConflictDetector.detectConflicts` over the projection's
            /// lineage trail and diagnostics. Empty when no conflicts
            /// were detected or the caller did not request the scan.
            /// Sorted by `SsKey` for T1 byte-determinism (PolicyConflict
            /// DU ordering provides stable comparison).
            PolicyConflicts : PolicyConflict list
            /// **§5.5** — per-artifact overlay enumeration derived from the
            /// composed pipeline's lineage trail (each `LineageEvent` carries
            /// `SsKey` + `Classification`). `DataIntent → None` (skeleton-
            /// only); `OperatorIntent axis → Some axis` (one row per distinct
            /// overlay axis that touched the artifact). Sorted by
            /// `(SsKey, OverlayAxis option)` for T1 byte-determinism. Empty
            /// for the convenience builders (`build` / `buildWith`) that have
            /// no pipeline trail; populated via `buildFull`. Cashes the PRIME
            /// slice-ζ forward signal and completes the CLAUDE.md load-bearing
            /// commitment "manifest names every applied overlay per artifact."
            AppliedTransforms : (SsKey * OverlayAxis option) list
        }

    /// Build the manifest from a Profile + optional TopologicalOrder +
    /// registry metadata list + Catalog. The `topology` parameter
    /// feeds the H-038 `DeploymentBatches` field; pass `None` when no
    /// pass has been run (the field will be empty).
    let buildWithTopology
        (profile: Profile)
        (registry: RegisteredTransformMetadata list)
        (topology: TopologicalOrder option)
        (catalog: Catalog)
        : Manifest =
        use _ = Bench.scope "emit.manifest.build"
        let entries =
            catalog.Modules
            |> List.collect (fun m ->
                m.Kinds
                |> List.map (fun k ->
                    let nonPkIndexCount =
                        k.Indexes
                        |> List.filter (fun idx -> not (IndexUniqueness.isPrimaryKey idx.Uniqueness))
                        |> List.length
                    let schemaStr, tableStr = TableId.qualifiedParts k.Physical
                    {
                        Module = Name.value m.Name
                        Schema = schemaStr
                        Table = tableStr
                        // Same shape as SsdtDdlEmitter.relativePath (V1
                        // convention; forward slashes for cross-platform
                        // determinism per V2-driver KPI).
                        TableFile =
                            System.String.Concat(  // LINT-ALLOW: cross-platform-deterministic relative path; Path.Combine considered + rejected (platform-specific separators violate T1 byte-determinism); segments are typed (Name.value m.Name + k.Physical.Schema + k.Physical.Table from Coordinates.TableId)
                                "Modules/",
                                Name.value m.Name,
                                "/",
                                schemaStr,
                                ".",
                                tableStr,
                                ".sql")
                        IndexCount = nonPkIndexCount
                        ForeignKeyCount = List.length k.References
                    }))
        let columnProfiles =
            catalog.Modules
            |> List.collect (fun m ->
                m.Kinds
                |> List.collect (fun k ->
                    k.Attributes
                    |> List.choose (fun a ->
                        match Profile.tryFindNumeric a.SsKey profile with
                        | None -> None
                        | Some dist ->
                            match dist.Moments with
                            | None -> None
                            | Some moments ->
                                let schemaStr, tableStr = TableId.qualifiedParts k.Physical
                                Some { Schema = schemaStr
                                       Table  = tableStr
                                       Column = ColumnRealization.columnNameText a.Column
                                       Mean   = moments.Mean
                                       StdDev = moments.StdDev })))
            |> List.sortBy (fun cp -> cp.Schema, cp.Table, cp.Column)
        let deploymentBatches =
            // P1 — `levels` now mints `ParallelSafe`; the manifest LISTS the
            // groups (read-only view), so its wire shape is unchanged.
            topology
            |> Option.map (TopologicalOrder.levels >> List.map ParallelSafe.members)
            |> Option.defaultValue []
        {
            Tables = entries
            EmitterVersion = version
            RegistryDigest = TransformRegistry.digest registry
            Coverage = Coverage.compute catalog
            PredicateCoverage = PredicateCoverage.compute catalog
            Unsupported = Unsupported.compute ()
            ColumnProfiles = columnProfiles
            DeploymentBatches = deploymentBatches
            PolicyVersion = None
            PolicyConflicts = []
            AppliedTransforms = []
        }

    /// **§5.5** — derive the per-artifact overlay enumeration from a
    /// composed pipeline's lineage trail. Per pillar 9, each `LineageEvent`
    /// carries an `SsKey` + a `Classification`: `DataIntent` is skeleton
    /// (no operator overlay), `OperatorIntent axis` names the overlay that
    /// touched the artifact. For each SsKey, emit one row per distinct
    /// `OverlayAxis` that touched it, or a single `None` row when only
    /// `DataIntent` events touched it. Sorted by `(SsKey, OverlayAxis
    /// option)` for T1 byte-determinism. The `None`-rows make the surface
    /// the manifest witness for skeleton-purity (a `Policy.empty` run yields
    /// all-`None`); the `Some`-rows are the overlay-exercise witness.
    let appliedTransforms (trail: LineageEvent list) : (SsKey * OverlayAxis option) list =
        trail
        |> List.groupBy (fun e -> e.SsKey)
        |> List.collect (fun (ssKey, events) ->
            let axes =
                events
                |> List.choose (fun e ->
                    match e.Classification with
                    | OperatorIntent axis -> Some axis
                    | DataIntent -> None)
                |> List.distinct
            match axes with
            | [] -> [ ssKey, None ]
            | _  -> axes |> List.map (fun axis -> ssKey, Some axis))
        |> List.sort

    /// Build the full manifest including the H-085 PolicyVersion stamp,
    /// the H-034 PolicyConflict entries, and the §5.5 per-artifact
    /// `AppliedTransforms` overlay enumeration (derived from the composed
    /// pipeline's lineage `trail`). Pipeline-level callers that have a
    /// `VersionedPolicy`, a conflict scan result, and the lineage trail use
    /// this overload; topology-only callers continue to use
    /// `buildWithTopology` (which leaves `AppliedTransforms` empty).
    let buildFull
        (profile: Profile)
        (registry: RegisteredTransformMetadata list)
        (topology: TopologicalOrder option)
        (policyVersion: VersionedPolicy option)
        (policyConflicts: PolicyConflict list)
        (trail: LineageEvent list)
        (catalog: Catalog)
        : Manifest =
        let manifest = buildWithTopology profile registry topology catalog
        { manifest with
            PolicyVersion     = policyVersion
            PolicyConflicts   = policyConflicts
            AppliedTransforms = appliedTransforms trail }

    /// Build the manifest from a Profile + Catalog + explicit registry-
    /// metadata list. The `registry` parameter feeds the slice-ζ
    /// `RegistryDigest` field; production callers use
    /// `RegisteredTransforms.all`; the 5th bidirectional property test
    /// supplies a perturbed list to exercise digest sensitivity.
    /// `profile` provides per-column statistical moments (H-027);
    /// pass `Profile.empty` when no profiling data is available.
    /// Topology-unaware callers use this overload; topology-aware
    /// callers use `buildWithTopology` directly.
    let buildWith (profile: Profile) (registry: RegisteredTransformMetadata list) (catalog: Catalog) : Manifest =
        buildWithTopology profile registry None catalog

    /// Build with the canonical production registry
    /// (`RegisteredTransforms.all` prepended with the SSDT emitter's
    /// `registeredMetadata`). Slice 5.13.emit-features-registry
    /// (2026-05-18) wired the SSDT emitter's `RegisteredTransform`
    /// surface into the manifest path so the totality-coverage scan
    /// reaches the emit-stage Sites. Preserves the existing emit
    /// signature; production callers continue to use this form. The
    /// OSSYS adapter's `CatalogReader.registeredMetadata` lives in
    /// `Projection.Adapters.Osm` (cherry-pick boundary) — Pipeline-
    /// level assembly prepends it when consumed alongside the
    /// adapter.
    let build (catalog: Catalog) : Manifest =
        let registry = SsdtDdlEmitter.registeredMetadata :: RegisteredTransforms.all
        buildWith Profile.empty registry catalog

    let private requireValue (label: string) (v: JsonValue | null) : JsonNode =
        match Option.ofObj v with
        | Some node -> node :> JsonNode
        | None ->
            invalidOp (sprintf "ManifestEmitter.%s: JsonValue.Create returned null (unreachable for non-null input)" label)

    /// Render the manifest to a typed `JsonNode`. Per pillar 1: the
    /// typed JsonNode IS the canonical intermediate form; the JSON
    /// text emerges only at `toJson`. Per pillar 7: `JsonObject` /
    /// `JsonArray` / `JsonValue.Create` are the BCL gold-standard
    /// primitives for typed JSON tree construction.
    let toNode (manifest: Manifest) : JsonNode =
        let doc = JsonObject()
        doc.Add("emitter", requireValue "emitter" (JsonValue.Create("Projection.Targets.SSDT.ManifestEmitter")))
        doc.Add("version", requireValue "version" (JsonValue.Create(manifest.EmitterVersion)))
        // Chapter A.4.7' slice ζ — load-bearing per A41 (registry as
        // execution-totality surface). Stable across emit calls when
        // the registry is unchanged; perturbation surfaces here.
        let registryObj = JsonObject()
        registryObj.Add("digest", requireValue "registry.digest" (JsonValue.Create(manifest.RegistryDigest)))
        doc.Add("registry", registryObj :> JsonNode)
        let tablesArr = JsonArray()
        for entry in manifest.Tables do
            let entryObj = JsonObject()
            entryObj.Add("module", requireValue "module" (JsonValue.Create(entry.Module)))
            entryObj.Add("schema", requireValue "schema" (JsonValue.Create(entry.Schema)))
            entryObj.Add("table", requireValue "table" (JsonValue.Create(entry.Table)))
            entryObj.Add("tableFile", requireValue "tableFile" (JsonValue.Create(entry.TableFile)))
            entryObj.Add("indexCount", requireValue "indexCount" (JsonValue.Create(entry.IndexCount)))
            entryObj.Add("foreignKeyCount", requireValue "foreignKeyCount" (JsonValue.Create(entry.ForeignKeyCount)))
            tablesArr.Add(entryObj)
        doc.Add("tables", tablesArr)
        // Chapter 4.4 slice α — per-axis Coverage breakdown.
        // Retires the prior `null` default for this field. Mirrors V1's
        // `SsdtCoverageSummary` shape (Tables / Columns / Constraints
        // each with Emitted / Total / Percentage).
        let buildBreakdown (b: CoverageBreakdown) : JsonNode =
            let obj = JsonObject()
            obj.Add("emitted", requireValue "emitted" (JsonValue.Create(b.Emitted)))
            obj.Add("total", requireValue "total" (JsonValue.Create(b.Total)))
            obj.Add("percentage", requireValue "percentage" (JsonValue.Create(b.Percentage)))
            obj :> JsonNode
        let coverageObj = JsonObject()
        coverageObj.Add("tables", buildBreakdown manifest.Coverage.Tables)
        coverageObj.Add("columns", buildBreakdown manifest.Coverage.Columns)
        coverageObj.Add("constraints", buildBreakdown manifest.Coverage.Constraints)
        doc.Add("coverage", coverageObj :> JsonNode)
        // Chapter 4.4 slice β — per-table predicate satisfaction +
        // PredicateCounts aggregation. PredicateCounts emits as a
        // sorted-by-name array of `{name, count}` objects (per
        // chapter open Q2) to preserve T1 byte-determinism — V1
        // serializes as a JSON dict; V2 documents the divergence.
        let predicateCoverageObj = JsonObject()
        let predicateTablesArr = JsonArray()
        for entry in manifest.PredicateCoverage.Tables do
            let entryObj = JsonObject()
            entryObj.Add("module", requireValue "predicateCoverage.module" (JsonValue.Create(entry.Module)))
            entryObj.Add("schema", requireValue "predicateCoverage.schema" (JsonValue.Create(entry.Schema)))
            entryObj.Add("table", requireValue "predicateCoverage.table" (JsonValue.Create(entry.Table)))
            let predsArr = JsonArray()
            for p in entry.Predicates do
                predsArr.Add(requireValue "predicateCoverage.predicate" (JsonValue.Create(PredicateName.toString p)))
            entryObj.Add("predicates", predsArr)
            predicateTablesArr.Add(entryObj)
        predicateCoverageObj.Add("tables", predicateTablesArr)
        let predicateCountsArr = JsonArray()
        // Iterate in canonical sorted order (PredicateName.all) so
        // every emit produces the same byte sequence regardless of
        // Map.ofList's internal ordering.
        for p in PredicateName.all do
            let count =
                manifest.PredicateCoverage.PredicateCounts
                |> Map.tryFind p
                |> Option.defaultValue 0
            let countObj = JsonObject()
            countObj.Add("name", requireValue "predicateCounts.name" (JsonValue.Create(PredicateName.toString p)))
            countObj.Add("count", requireValue "predicateCounts.count" (JsonValue.Create(count)))
            predicateCountsArr.Add(countObj)
        predicateCoverageObj.Add("predicateCounts", predicateCountsArr)
        doc.Add("predicateCoverage", predicateCoverageObj :> JsonNode)
        // Chapter 4.4 slice γ — V1↔V2 emit divergences in play,
        // sorted by string comparison. Retires the prior empty-array
        // default.
        let unsupportedArr = JsonArray()
        for name in manifest.Unsupported do
            unsupportedArr.Add(requireValue "unsupported.entry" (JsonValue.Create(name)))
        doc.Add("unsupported", unsupportedArr)
        // PreRemediation stays empty-array per V2_DRIVER §154
        // (RemediationEmitter deferred to chapter 5+).
        doc.Add("preRemediation", JsonArray() :> JsonNode)
        // H-027 — per-column statistical moments. Empty array when
        // Profile.empty (no profiling data available); non-empty when
        // LiveProfiler ran and Moments are present on numeric columns.
        let columnProfilesArr = JsonArray()
        for cp in manifest.ColumnProfiles do
            let cpObj = JsonObject()
            cpObj.Add("schema", requireValue "columnProfiles.schema" (JsonValue.Create(cp.Schema)))
            cpObj.Add("table",  requireValue "columnProfiles.table"  (JsonValue.Create(cp.Table)))
            cpObj.Add("column", requireValue "columnProfiles.column" (JsonValue.Create(cp.Column)))
            cpObj.Add("mean",   requireValue "columnProfiles.mean"   (JsonValue.Create(cp.Mean)))
            cpObj.Add("stdDev", requireValue "columnProfiles.stdDev" (JsonValue.Create(cp.StdDev)))
            columnProfilesArr.Add(cpObj)
        doc.Add("columnProfiles", columnProfilesArr)
        // H-038 / H-041 — FK-safe parallel deployment batches.
        // Each inner array is one Kahn level from `TopologicalOrder
        // .levels`; SsKeys rendered via `SsKey.rootOriginal` for
        // operator-facing readability. Empty outer array when
        // topology was not computed.
        let batchesArr = JsonArray()
        for batch in manifest.DeploymentBatches do
            let batchArr = JsonArray()
            for key in batch do
                batchArr.Add(requireValue "deploymentBatches.key" (JsonValue.Create(SsKey.rootOriginal key)))
            batchesArr.Add(batchArr)
        doc.Add("deploymentBatches", batchesArr)
        // H-085 — Policy version stamp. Emits `{digest, version[, changeLog]}`
        // or omits the `policy` key when no versioned policy was
        // supplied. **`At` is intentionally excluded** — including it
        // would break T1 byte-determinism because `VersionedPolicy.now`
        // captures `DateTimeOffset.UtcNow` and two repeat runs would
        // emit different manifests for the same policy. The digest +
        // SemVer pair is byte-stable; the in-memory `VersionedPolicy`
        // retains `At` for operator queries that don't flow through the
        // manifest JSON.
        match manifest.PolicyVersion with
        | None -> ()
        | Some vp ->
            let policyObj = JsonObject()
            policyObj.Add("digest",  requireValue "policy.digest"  (JsonValue.Create(vp.Digest)))
            policyObj.Add("version", requireValue "policy.version" (JsonValue.Create(SemVer.toString vp.Version)))
            match vp.ChangeLog with
            | Some log -> policyObj.Add("changeLog", requireValue "policy.changeLog" (JsonValue.Create(log)))
            | None     -> ()
            doc.Add("policy", policyObj :> JsonNode)
        // H-034 — PolicyConflict entries. Emits a `policyConflicts`
        // array (empty when no conflicts were detected). Each entry is
        // `{kind, key, code?, message?, passName?}` — the variant
        // discriminator names the conflict shape.
        let conflictsArr = JsonArray()
        for conflict in manifest.PolicyConflicts do
            let conflictObj = JsonObject()
            match conflict with
            | UnreachableTransform (passName, ssKey) ->
                conflictObj.Add("kind",     requireValue "conflict.kind"     (JsonValue.Create("UnreachableTransform")))
                conflictObj.Add("passName", requireValue "conflict.passName" (JsonValue.Create(passName)))
                conflictObj.Add("ssKey",    requireValue "conflict.ssKey"    (JsonValue.Create(SsKey.rootOriginal ssKey)))
            | AxisContradiction (axis, ssKey, code, message) ->
                conflictObj.Add("kind",    requireValue "conflict.kind"    (JsonValue.Create("AxisContradiction")))
                conflictObj.Add("axis",    requireValue "conflict.axis"    (JsonValue.Create(sprintf "%A" axis)))
                conflictObj.Add("ssKey",   requireValue "conflict.ssKey"   (JsonValue.Create(SsKey.rootOriginal ssKey)))
                conflictObj.Add("code",    requireValue "conflict.code"    (JsonValue.Create(code)))
                conflictObj.Add("message", requireValue "conflict.message" (JsonValue.Create(message)))
            conflictsArr.Add(conflictObj)
        doc.Add("policyConflicts", conflictsArr)
        // §5.5 — per-artifact overlay enumeration. Each entry is
        // `{ssKey, overlay}` where `overlay` is the OverlayAxis case name
        // (rendered via `%A`, matching the AxisContradiction precedent above)
        // or JSON `null` for a skeleton-only (DataIntent) artifact. Already
        // sorted by `(SsKey, OverlayAxis option)` in `appliedTransforms`.
        let appliedTransformsArr = JsonArray()
        for (ssKey, axisOpt) in manifest.AppliedTransforms do
            let entryObj = JsonObject()
            entryObj.Add("ssKey", requireValue "appliedTransforms.ssKey" (JsonValue.Create(SsKey.rootOriginal ssKey)))
            match axisOpt with
            | Some axis -> entryObj.Add("overlay", requireValue "appliedTransforms.overlay" (JsonValue.Create(sprintf "%A" axis)))
            | None      -> entryObj.Add("overlay", (null: JsonNode | null))
            appliedTransformsArr.Add(entryObj)
        doc.Add("appliedTransforms", appliedTransformsArr)
        doc :> JsonNode

    /// Render the manifest to JSON text. The terminal serialization
    /// flows through `Utf8JsonWriter` with `JsonOptions.indented()`
    /// (the chapter-3.6 reified option-builder + chapter-3.7 slice ε
    /// JsonEmitter precedent). T1 byte-determinism: pinned options
    /// guarantee byte-identical output across repeat invocations.
    let toJson (manifest: Manifest) : string =
        use _ = Bench.scope "emit.manifest.toJson"
        let doc = toNode manifest
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, (JsonOptions.indented ()))
            doc.WriteTo(writer)
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    /// Π port realization (chapter 4.1.A slice 9). Per A18 amended:
    /// Catalog only, no Profile, no Policy. Returns the typed
    /// `Manifest` value; downstream realization (`Render
    /// .toSsdtDirectory` per slice 10) emits the manifest.json file
    /// alongside the per-table .sql files.
    let emit : Catalog -> Manifest = build

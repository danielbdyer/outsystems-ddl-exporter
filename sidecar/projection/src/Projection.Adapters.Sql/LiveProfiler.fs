namespace Projection.Adapters.Sql

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// Live-SQL adapter that captures `Profile.AttributeRealities` by
/// probing a deployed SQL Server instance. Sibling to
/// `ProfileSnapshot.attach` (consumes V1's JSON snapshot) +
/// `ProfileStatistics.attach` (consumes V2-defined distribution JSON);
/// LiveProfiler is the **live-probe** path — runs deterministic
/// SQL queries against the deployed target and populates
/// per-attribute reality evidence directly.
///
/// **Per matrix row 49 + V2_DRIVER per-axis stakes (DATA-axis
/// cutover-blocker).** V1's `LiveProfiler.cs` (chapter 5.4.δ
/// profiling territory) populates V1's `AttributeReality.cs` via
/// runtime probes; V2 mirrors at this adapter. Sibling-Π consumers
/// (tightening passes: `NullabilityRules`, `UniqueIndexRules`,
/// `ForeignKeyRules`) read `Profile.AttributeRealities` to refine
/// their decisions.
///
/// **Per pillar 9: all sites carry DataIntent.** The adapter
/// observes deployed reality; no operator policy enters at probe
/// time. Sampling policy (when V2 adopts non-full-table probing)
/// lives in `Pipeline.Config` per matrix row 90's prior decision
/// — `DECISIONS 2026-05-18 (slice 5.4.δ.profiling) — Sampling policy
/// is operator intent; lives in the orchestrator, not in Profile
/// IR`.
///
/// **Per A18 amended + A34.** Profile is independent of Catalog and
/// Policy; the LiveProfiler reads Catalog (to identify which
/// attributes to probe) but emits Profile evidence only — no
/// catalog mutation, no policy consumption. The Catalog ↔ deployed
/// schema alignment is the caller's contract (the canary's
/// PhysicalSchema diff gates this elsewhere).
///
/// **Scope (cumulative across slices):**
///   - Slice A.4.7'-prelude.live-profiler (2026-05-19): captures
///     `IsNullableInDatabase` + `HasNulls` + `HasDuplicates` +
///     `IsPresentButInactive` per non-PK attribute. Surfaces as
///     `captureAttributeRealities`.
///   - Slice B.3.1.foreign-key-reality (2026-05-19): captures
///     `Profile.ForeignKeys[ssKey].HasOrphan` + `OrphanCount` per
///     `Reference`. Surfaces as `captureForeignKeyRealities`.
///     Single-column PK targets only; composite-PK targets defer
///     with named trigger (`Outcome = AmbiguousMapping`).
///   - Slice B.3.2.column-null-counts (2026-05-19): captures
///     `Profile.Columns[ssKey].RowCount` + `NullCount` +
///     `NullCountProbeStatus` per attribute. Surfaces as
///     `captureColumnProfiles`. Batched per-kind probe: one
///     `COUNT_BIG`-based query per non-static table yielding row
///     count + per-attribute null count.
///   - Slice B.3.3.unique-candidates (2026-05-19): populates
///     `Profile.UniqueCandidates` (single-column) via attach-time
///     projection from `AttributeRealities.HasDuplicates` (no extra
///     SQL — projects the existing witness). Populates
///     `Profile.CompositeUniqueCandidates` via
///     `captureCompositeUniqueCandidates` — per-Index `GROUP BY …
///     HAVING COUNT_BIG(*) > 1` probe.
///   - Slice B.3.4.fk-orphan-samples (2026-05-19): emits
///     `DiagnosticEntry list` per orphan-bearing FK via the
///     standalone `captureForeignKeyOrphanSamples` capture. Pillar 9
///     pivot — operational samples land in Diagnostics, not Profile
///     axis.
///   - Sibling-adapter composability holds: `attach` runs every
///     Profile-side capture and composes into the input Profile;
///     `captureForeignKeyOrphanSamples` is a separate caller-invoked
///     surface (Diagnostics output, not Profile mutation).
///
/// Per the chapter B.3 open document — slices 1-4 of the
/// LiveProfiler deep-probe sweep cashing out the deferrals named
/// at slice A.4.7'-prelude.live-profiler. Slices 5-7 (IR keystone
/// for statistical moments + single-scan-per-kind probe
/// consolidation + sampling/multi-env) land subsequently per the
/// chapter open's 7-slice expansion.
[<RequireQualifiedAccess>]
module LiveProfiler =

    let private encode =
        Microsoft.SqlServer.TransactSql.ScriptDom.Identifier.EncodeIdentifier

    /// Per-attribute probe SQL — combined `HasNulls` + `HasDuplicates`
    /// in a single round-trip. Returns two `bit` columns. `HasNulls`
    /// uses `EXISTS (… IS NULL)`; `HasDuplicates` uses `EXISTS (…
    /// GROUP BY col HAVING COUNT_BIG(*) > 1)` with `IS NOT NULL` to
    /// document the SQL Server convention (NULLs don't participate
    /// in GROUP-BY equality regardless; the explicit filter is the
    /// readable form).
    let private probeSql (kind: Kind) (attr: Attribute) : string =
        let table =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed (each via Identifier.EncodeIdentifier)
                ".",
                [| encode kind.Physical.Schema; encode kind.Physical.Table |])
        let col = encode attr.Column.ColumnName
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; encode outputs are typed safe segments
            "SELECT ",
            "CASE WHEN EXISTS (SELECT 1 FROM ", table, " WHERE ", col, " IS NULL) THEN 1 ELSE 0 END AS HasNulls, ",
            "CASE WHEN EXISTS (SELECT 1 FROM ", table, " WHERE ", col, " IS NOT NULL GROUP BY ", col, " HAVING COUNT_BIG(*) > 1) THEN 1 ELSE 0 END AS HasDuplicates")

    /// Per-kind nullability reflection via `INFORMATION_SCHEMA.COLUMNS`.
    /// Returns a `Map<ColumnName, IsNullable>` for the kind's deployed
    /// table; one round-trip per kind regardless of column count.
    /// Identifiers parameterize via SQL parameters (defense-in-depth
    /// against injection though Coordinates.TableId structurally
    /// excludes hostile input).
    let private nullabilityReflectionSql : string =
        "SELECT COLUMN_NAME, IS_NULLABLE \
         FROM INFORMATION_SCHEMA.COLUMNS \
         WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table"

    let private probeAttribute
        (cnn: SqlConnection)
        (kind: Kind)
        (attr: Attribute)
        : Task<bool * bool> =
        task {
            use _ = Bench.scope "profile.live.probeAttribute"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- probeSql kind attr
            cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
            use! reader = cmd.ExecuteReaderAsync()
            let! advanced = reader.ReadAsync()
            if advanced then
                let hasNulls = reader.GetInt32(0) = 1
                let hasDuplicates = reader.GetInt32(1) = 1
                return hasNulls, hasDuplicates
            else
                // Empty table → no nulls, no duplicates (vacuously).
                return false, false
        }

    let private reflectNullability
        (cnn: SqlConnection)
        (kind: Kind)
        : Task<Map<string, bool>> =
        task {
            use _ = Bench.scope "profile.live.reflectNullability"
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- nullabilityReflectionSql
            cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
            cmd.Parameters.AddWithValue("@schema", kind.Physical.Schema) |> ignore
            cmd.Parameters.AddWithValue("@table",  kind.Physical.Table)  |> ignore
            use! reader = cmd.ExecuteReaderAsync()
            let mutable acc : Map<string, bool> = Map.empty
            let mutable keepGoing = true
            while keepGoing do
                let! advanced = reader.ReadAsync()
                if advanced then
                    let colName = reader.GetString(0)
                    let isNullable =
                        System.String.Equals(
                            reader.GetString(1),
                            "YES",
                            System.StringComparison.OrdinalIgnoreCase)
                    acc <- Map.add colName isNullable acc
                else
                    keepGoing <- false
            return acc
        }

    /// Per-kind capture: walks the kind's non-PK attributes, runs
    /// the combined HasNulls + HasDuplicates probe per attribute,
    /// joins with the deployed-target nullability reflection.
    /// Primary-key attributes are skipped (PK is by construction
    /// NOT NULL and unique; probing them is redundant).
    let private captureKind
        (cnn: SqlConnection)
        (kind: Kind)
        : Task<AttributeReality list> =
        task {
            use _ = Bench.scope "profile.live.captureKind"
            let! nullabilityMap = reflectNullability cnn kind
            let realities =
                System.Collections.Generic.List<AttributeReality>()
            for attr in kind.Attributes do
                if attr.IsPrimaryKey then
                    // PK attributes — no probe needed; carry IsNullableInDatabase
                    // from reflection (defaults to false when reflection is silent).
                    let isNullable =
                        Map.tryFind attr.Column.ColumnName nullabilityMap
                        |> Option.defaultValue false
                    realities.Add(
                        { AttributeReality.create attr.SsKey with
                            IsNullableInDatabase = isNullable })
                else
                    let! (hasNulls, hasDuplicates) = probeAttribute cnn kind attr
                    let isNullable =
                        Map.tryFind attr.Column.ColumnName nullabilityMap
                        |> Option.defaultValue false
                    // Derive IsPresentButInactive in F#: the column
                    // exists at the deployed target (nullabilityMap
                    // contains it) AND the OS logical attribute is
                    // inactive. No live SQL needed.
                    let isPresentButInactive =
                        Map.containsKey attr.Column.ColumnName nullabilityMap
                        && not attr.IsActive
                    realities.Add(
                        { AttributeReality.create attr.SsKey with
                            IsNullableInDatabase = isNullable
                            HasNulls             = hasNulls
                            HasDuplicates        = hasDuplicates
                            IsPresentButInactive = isPresentButInactive })
            return List.ofSeq realities
        }

    /// Capture `AttributeReality` for every non-PK attribute in the
    /// catalog. PK attributes get IsNullableInDatabase populated
    /// (probing for nulls/duplicates is redundant on a PK).
    ///
    /// **What this slice covers:**
    ///   - `IsNullableInDatabase` — from `INFORMATION_SCHEMA.COLUMNS`
    ///     (one round-trip per kind)
    ///   - `HasNulls` — from `EXISTS (… IS NULL)` per-attribute
    ///   - `HasDuplicates` — from `EXISTS (… GROUP BY HAVING > 1)`
    ///     per-attribute
    ///   - `IsPresentButInactive` — derived from `Attribute.IsActive`
    ///     + nullabilityMap presence (no live SQL)
    ///
    /// **Deferred axes (named follow-up triggers):**
    ///   - `HasOrphans` — requires per-`Reference` probe (`EXISTS
    ///     (FK source row WHERE PK target absent)`). Trigger:
    ///     `ForeignKeyRules` consumer demands orphan-evidence
    ///     refinement of its `Outcome` decisions.
    ///   - Sampling policy — full-table scans today. Trigger:
    ///     production canary surfaces a profile-capture latency
    ///     concern at operator-reality scale (300 tables × 50k
    ///     rows); cash-out via `SqlProfilerOptions.Sampling`
    ///     (matrix row 90 prior `DECISIONS 2026-05-18 (slice
    ///     5.4.δ.profiling)` named the orchestrator-side home).
    ///
    /// Per A34: Profile is independent of Catalog and Policy. The
    /// captured `AttributeReality list` composes via
    /// `attach catalog cnn profile` into an existing Profile; the
    /// shape mirrors `ProfileSnapshot.attach` / `ProfileStatistics
    /// .attach`'s sibling-adapter discipline (`DECISIONS 2026-05-11
    /// — the rich-profiling agenda`).
    let private isStaticKind (k: Kind) : bool =
        k.Modality
        |> List.exists (function
            | Static _ -> true
            | _ -> false)

    /// Per-kind batched null-count probe SQL — one round-trip per
    /// kind returning the table's row count (column 0) and the null
    /// count for every attribute (columns 1..N). V1's
    /// `NullCountQueryBuilder.BuildCommandText()` shape adapted to
    /// `COUNT_BIG` projections for BIGINT type-safety on the F# read
    /// side. The `CASE WHEN col IS NULL THEN 1 END` form (no ELSE)
    /// emits NULL for non-null rows so `COUNT_BIG` skips them
    /// naturally.
    let private columnProfileSql (kind: Kind) : string =
        let table =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed via Identifier.EncodeIdentifier
                ".",
                [| encode kind.Physical.Schema; encode kind.Physical.Table |])
        let perColumnNullCount (idx: int) (attr: Attribute) : string =
            let col = encode attr.Column.ColumnName
            System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; encode + integer index are typed
                "COUNT_BIG(CASE WHEN ", col, " IS NULL THEN 1 END) AS [c", string idx, "]")
        let selectList =
            kind.Attributes
            |> List.mapi perColumnNullCount
            |> String.concat ", "  // LINT-ALLOW: positional comma joiner for typed segments
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed
            "SELECT COUNT_BIG(*) AS [c_rows], ", selectList, " FROM ", table)

    /// Per-kind capture for column null-count statistics. Empty
    /// attribute lists yield empty result (defensive; the schema
    /// invariants forbid attribute-less kinds in production).
    /// Empty source tables yield row-count = 0 and null-count = 0
    /// per attribute (SQL Server `COUNT_BIG` of an empty set is 0,
    /// not NULL — distinct from `SUM` of an empty set).
    let private captureKindColumnProfiles
        (cnn: SqlConnection)
        (kind: Kind)
        : Task<ColumnProfile list> =
        task {
            use _ = Bench.scope "profile.live.captureKindColumnProfiles"
            if List.isEmpty kind.Attributes then return []
            else
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- columnProfileSql kind
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                use! reader = cmd.ExecuteReaderAsync()
                let! advanced = reader.ReadAsync()
                if not advanced then return []
                else
                    let rowCount =
                        if reader.IsDBNull(0) then 0L else reader.GetInt64(0)
                    let probeStatus = ProbeStatus.observed rowCount
                    let profiles = System.Collections.Generic.List<ColumnProfile>()
                    let mutable idx = 0
                    for attr in kind.Attributes do
                        let nullCount =
                            if reader.IsDBNull(1 + idx) then 0L
                            else reader.GetInt64(1 + idx)
                        // `ColumnProfile.create` enforces
                        // `nullCount ≤ rowCount` etc. The SQL
                        // semantics guarantee these by construction
                        // (`COUNT_BIG(CASE WHEN col IS NULL THEN 1)`
                        // cannot exceed `COUNT_BIG(*)`); any
                        // violation is a deeper invariant breach
                        // that should propagate. Raise via
                        // `failwithf` so the outer try-with surfaces
                        // a `ValidationError` with full context.
                        match ColumnProfile.create attr.SsKey rowCount nullCount probeStatus with
                        | Ok p -> profiles.Add p
                        | Error errs ->
                            let codes = errs |> List.map (fun e -> e.Code) |> String.concat ", "
                            failwithf
                                "ColumnProfile.create rejected (rowCount=%d, nullCount=%d, attr=%A): %s"
                                rowCount nullCount attr.SsKey codes
                        idx <- idx + 1
                    return List.ofSeq profiles
        }

    /// Capture `ColumnProfile` per non-static kind. Mirrors V1's
    /// `NullCountQueryBuilder` shape: one query per table; null
    /// counts batched across attributes. Static kinds skipped
    /// (catalog-resident data).
    ///
    /// **What this slice covers (B.3.2):**
    ///   - `ColumnProfile.RowCount` per attribute (shared per kind
    ///     via the single `COUNT_BIG(*)` projection)
    ///   - `ColumnProfile.NullCount` per attribute via `COUNT_BIG(
    ///     CASE WHEN col IS NULL THEN 1 END)` projection
    ///   - `ColumnProfile.NullCountProbeStatus` with `Succeeded`
    ///     outcome + SampleSize = rowCount (full-table scan today)
    ///
    /// **Deferred (named follow-up triggers; inherited from
    /// `captureAttributeRealities`):**
    ///   - Sampling policy — full-table scans today. Trigger:
    ///     operator-reality canary latency at scale; cash-out at
    ///     chapter B.3 slice 6 wires `SqlProfilerOptions.Sampling`
    ///     through every capture function.
    let captureColumnProfiles
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<ColumnProfile list>> =
        task {
            use _ = Bench.scope "profile.live.captureColumnProfiles"
            try
                let nonStaticKinds =
                    catalog.Modules
                    |> List.collect (fun m -> m.Kinds)
                    |> List.filter (fun k -> not (isStaticKind k))
                let profiles = System.Collections.Generic.List<ColumnProfile>()
                for kind in nonStaticKinds do
                    let! perKind = captureKindColumnProfiles cnn kind
                    profiles.AddRange perKind
                return Result.success (List.ofSeq profiles)
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureColumnProfilesFailed"
                            (System.String.Concat("LiveProfiler.captureColumnProfiles failed: ", ex.Message)))
        }

    let captureAttributeRealities
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<AttributeReality list>> =
        task {
            use _ = Bench.scope "profile.live.captureAttributeRealities"
            try
                let nonStaticKinds =
                    catalog.Modules
                    |> List.collect (fun m -> m.Kinds)
                    |> List.filter (fun k -> not (isStaticKind k))
                let realities = System.Collections.Generic.List<AttributeReality>()
                for kind in nonStaticKinds do
                    let! perKind = captureKind cnn kind
                    realities.AddRange perKind
                return Result.success (List.ofSeq realities)
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureAttributeRealitiesFailed"
                            (System.String.Concat("LiveProfiler.captureAttributeRealities failed: ", ex.Message)))
        }

    /// Per-Reference probe SQL — combined `OrphanCount` (via the
    /// canonical V1 anti-join shape: `source LEFT JOIN target … WHERE
    /// source.col IS NOT NULL AND target.pk IS NULL`) plus
    /// `RowCount` (source rows examined; populates `ProbeStatus
    /// .SampleSize` so downstream consumers can read confidence-of-
    /// observation). Single round-trip per reference.
    let private foreignKeyProbeSql
        (srcKind: Kind) (srcAttr: Attribute)
        (tgtKind: Kind) (tgtPkAttr: Attribute)
        : string =
        let srcTable =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed via Identifier.EncodeIdentifier
                ".",
                [| encode srcKind.Physical.Schema; encode srcKind.Physical.Table |])
        let tgtTable =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed via Identifier.EncodeIdentifier
                ".",
                [| encode tgtKind.Physical.Schema; encode tgtKind.Physical.Table |])
        let srcCol = encode srcAttr.Column.ColumnName
        let tgtCol = encode tgtPkAttr.Column.ColumnName
        // `COUNT_BIG` projections return `BIGINT` consistently —
        // distinct from `SUM(int)` which infers `INT` and can overflow
        // / mistype on the F# read side. The `CASE WHEN … THEN 1 END`
        // shape (no ELSE) emits NULL for non-orphans so `COUNT_BIG`
        // skips them naturally.
        // Bracket-quoted aliases avoid T-SQL reserved-ish conflicts
        // (`RowCount` collides with `@@ROWCOUNT` / `SET ROWCOUNT`
        // tokenization).
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; encode outputs are typed safe segments
            "SELECT ",
            "COUNT_BIG(s.", srcCol, ") AS [RowCount], ",
            "COUNT_BIG(CASE WHEN t.", tgtCol, " IS NULL THEN 1 END) AS [OrphanCount] ",
            "FROM ", srcTable, " AS s ",
            "LEFT JOIN ", tgtTable, " AS t ON s.", srcCol, " = t.", tgtCol, " ",
            "WHERE s.", srcCol, " IS NOT NULL")

    /// Probe a single Reference for orphan rows. Returns the typed
    /// `ForeignKeyReality` with HasOrphan / OrphanCount / ProbeStatus
    /// populated. Returns a defaulted reality (via
    /// `ForeignKeyReality.create`) with `Outcome = AmbiguousMapping`
    /// when the target's PK shape isn't probable (zero or composite-
    /// column PK — single-column PK only for this slice).
    let private probeReference
        (cnn: SqlConnection)
        (catalog: Catalog)
        (srcKind: Kind)
        (reference: Reference)
        : Task<ForeignKeyReality> =
        task {
            use _ = Bench.scope "profile.live.probeReference"
            let defaulted =
                ForeignKeyReality.create reference.SsKey
            let ambiguous =
                { defaulted with ProbeStatus = ProbeStatus.ambiguous }
            match Kind.tryFindAttribute reference.SourceAttribute srcKind with
            | None -> return ambiguous
            | Some srcAttr ->
                match Catalog.tryFindKind reference.TargetKind catalog with
                | None -> return ambiguous
                | Some tgtKind ->
                    match Kind.primaryKey tgtKind with
                    | [ tgtPkAttr ] ->
                        // Single-column PK — probable.
                        use cmd = cnn.CreateCommand()
                        cmd.CommandText <-
                            foreignKeyProbeSql srcKind srcAttr tgtKind tgtPkAttr
                        cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                        use! reader = cmd.ExecuteReaderAsync()
                        let! advanced = reader.ReadAsync()
                        if advanced then
                            // SUM over empty set is NULL; coalesce defensively.
                            let rowCount =
                                if reader.IsDBNull(0) then 0L
                                else reader.GetInt64(0)
                            let orphanCount =
                                if reader.IsDBNull(1) then 0L
                                else reader.GetInt64(1)
                            return
                                { ReferenceKey = reference.SsKey
                                  HasOrphan    = orphanCount > 0L
                                  OrphanCount  = orphanCount
                                  IsNoCheck    = not reference.IsConstraintTrusted
                                  ProbeStatus  = ProbeStatus.observed rowCount }
                        else
                            // Empty source table — no orphans by vacuum.
                            return defaulted
                    | _ ->
                        // Zero PK columns or composite PK — defer per slice
                        // B.3.1 scope (composite-PK probe lifts in a later
                        // slice; cf. row 87 composite-unique deferral).
                        return ambiguous
        }

    /// Capture `ForeignKeyReality` for every reference whose source
    /// kind is non-static. Static kinds carry their data in the
    /// catalog itself (`Modality.Static`); probing the deployed
    /// table for orphans would duplicate the static-population
    /// analysis. References from static sources surface as defaulted
    /// realities with `Succeeded` outcome (no orphans by
    /// construction).
    ///
    /// **What this slice covers (B.3.1):**
    ///   - `HasOrphan` + `OrphanCount` per Reference with
    ///     single-column-PK target
    ///   - `IsNoCheck` from `Reference.IsConstraintTrusted` (negated;
    ///     no extra SQL needed — V1's `#FkReality.IsNoCheck` column
    ///     already flowed into the Reference IR at chapter 4.6
    ///     slice α)
    ///
    /// **Deferred (named follow-up triggers):**
    ///   - Composite-PK target probes — the per-Reference probe
    ///     returns `Outcome = AmbiguousMapping` for now. Trigger:
    ///     consumer demand or composite-key fixture surfaces in
    ///     operator-reality canary.
    ///   - `IsNoCheck` reflection from the *deployed* target's
    ///     `sys.foreign_keys` — current shape reads V1's source-side
    ///     evidence via the Reference IR. Trigger: deployed-target
    ///     orphans observed via this probe combined with the
    ///     deployed-target's NOCHECK state diverging from the
    ///     V1-source state.
    ///   - Sampling policy — full-table scans today. Same deferral
    ///     as `captureAttributeRealities`; trigger is operator-
    ///     reality latency concern at scale.
    /// Per-Index composite-uniqueness probe SQL. The probe queries
    /// whether ANY group of values across `cols` repeats — the
    /// canonical V1 shape via `EXISTS (… GROUP BY <cols> HAVING
    /// COUNT_BIG(*) > 1)`. Each column carries an `IS NOT NULL` gate
    /// (SQL Server treats NULL values as group-distinct under
    /// GROUP BY anyway; the explicit filter documents the V1 NULL-
    /// semantics convention). Returns row count + boolean witness
    /// via combined query (one round-trip per Index).
    let private compositeUniqueProbeSql
        (kind: Kind) (indexColumns: Attribute list)
        : string =
        let table =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed via Identifier.EncodeIdentifier
                ".",
                [| encode kind.Physical.Schema; encode kind.Physical.Table |])
        let cols =
            indexColumns
            |> List.map (fun a -> encode a.Column.ColumnName)
        let notNullFilter =
            cols
            |> List.map (fun c -> System.String.Concat(c, " IS NOT NULL"))  // LINT-ALLOW: typed encode segments
            |> String.concat " AND "  // LINT-ALLOW: SQL conjunction joiner over typed segments
        let groupByCols =
            cols |> String.concat ", "  // LINT-ALLOW: positional comma joiner over typed segments
        // `CASE WHEN EXISTS THEN 1 ELSE 0 END` returns INT (not BIT).
        // Bracket-quoted `[RowCount]` avoids the same reserved-keyword
        // collision noted at slice B.3.1 (V1 token tables tokenize
        // RowCount via @@ROWCOUNT).
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; encode outputs are typed safe segments
            "SELECT ",
            "(SELECT COUNT_BIG(*) FROM ", table, ") AS [RowCount], ",
            "CASE WHEN EXISTS (",
            "SELECT 1 FROM ", table, " ",
            "WHERE ", notNullFilter, " ",
            "GROUP BY ", groupByCols, " ",
            "HAVING COUNT_BIG(*) > 1",
            ") THEN 1 ELSE 0 END AS [HasDuplicate]")

    /// Determine whether an Index is a multi-column non-unique
    /// candidate worth probing. `IsUnique = true` indexes are
    /// already-unique by catalog declaration (no probe needed).
    /// Single-column candidates land via the attach-time projection
    /// from `AttributeReality.HasDuplicates`. Indexes with zero or
    /// one columns aren't composite by definition.
    let private isCompositeCandidate (index: Index) : bool =
        not index.IsUnique && List.length index.Columns >= 2

    /// Per-Index composite-uniqueness probe. Walks the index's
    /// columns, resolves each via `Kind.tryFindAttribute`, runs the
    /// combined probe. Returns a defaulted
    /// `CompositeUniqueCandidateProfile` with `Outcome =
    /// AmbiguousMapping` when any column fails to resolve.
    let private probeCompositeUnique
        (cnn: SqlConnection)
        (kind: Kind)
        (index: Index)
        : Task<CompositeUniqueCandidateProfile> =
        task {
            use _ = Bench.scope "profile.live.probeCompositeUnique"
            let attributeKeys =
                index.Columns |> List.map (fun ic -> ic.Attribute)
            let defaulted =
                CompositeUniqueCandidateProfile.create kind.SsKey attributeKeys
            let resolved =
                index.Columns
                |> List.map (fun ic -> Kind.tryFindAttribute ic.Attribute kind)
            if resolved |> List.exists Option.isNone then
                return { defaulted with ProbeStatus = ProbeStatus.ambiguous }
            else
                let attributes = resolved |> List.choose id
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- compositeUniqueProbeSql kind attributes
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                use! reader = cmd.ExecuteReaderAsync()
                let! advanced = reader.ReadAsync()
                if advanced then
                    let rowCount =
                        if reader.IsDBNull(0) then 0L else reader.GetInt64(0)
                    let hasDuplicate = reader.GetInt32(1) = 1
                    return
                        { KindKey       = kind.SsKey
                          AttributeKeys = attributeKeys
                          HasDuplicate  = hasDuplicate
                          ProbeStatus   = ProbeStatus.observed rowCount }
                else
                    return defaulted
        }

    /// Capture composite-uniqueness candidates per Index in the
    /// catalog. Single-column candidates land via the attach-time
    /// projection from `AttributeReality.HasDuplicates` (the
    /// existing per-attribute HasDuplicates probe in
    /// `captureAttributeRealities` already carries the witness).
    /// Static kinds skipped (catalog-resident data).
    ///
    /// **What this slice covers (B.3.3):**
    ///   - `CompositeUniqueCandidateProfile.HasDuplicate` per
    ///     non-unique multi-column Index via `GROUP BY … HAVING
    ///     COUNT_BIG(*) > 1` probe
    ///   - `RowCount` from a sibling sub-select populates
    ///     `ProbeStatus.SampleSize`
    ///   - Single-column `UniqueCandidateProfile.HasDuplicate`
    ///     projected at `attach` time from `AttributeReality
    ///     .HasDuplicates` (no extra SQL)
    let captureCompositeUniqueCandidates
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<CompositeUniqueCandidateProfile list>> =
        task {
            use _ = Bench.scope "profile.live.captureCompositeUniqueCandidates"
            try
                let candidates =
                    System.Collections.Generic.List<CompositeUniqueCandidateProfile>()
                for m in catalog.Modules do
                    for kind in m.Kinds do
                        if not (isStaticKind kind) then
                            for index in kind.Indexes do
                                if isCompositeCandidate index then
                                    let! profile = probeCompositeUnique cnn kind index
                                    candidates.Add profile
                return Result.success (List.ofSeq candidates)
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureCompositeUniqueCandidatesFailed"
                            (System.String.Concat("LiveProfiler.captureCompositeUniqueCandidates failed: ", ex.Message)))
        }

    /// Default sample limit for FK orphan-sample probes (slice
    /// B.3.4). Operator-tunable via `SqlProfilerOptions.Sampling` at
    /// slice 6; constant here so slice 4 ships independently.
    [<Literal>]
    let private defaultOrphanSampleLimit = 5

    /// Per-Reference orphan-sample probe SQL — `TOP N` extension of
    /// slice B.3.1's orphan-count shape. Returns up to N source-side
    /// FK values that don't resolve in the target table; ordered by
    /// the FK value itself for determinism (A1 — deterministic
    /// sampling under repeated probes).
    let private foreignKeyOrphanSampleSql
        (sampleLimit: int)
        (srcKind: Kind) (srcAttr: Attribute)
        (tgtKind: Kind) (tgtPkAttr: Attribute)
        : string =
        let srcTable =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed via Identifier.EncodeIdentifier
                ".",
                [| encode srcKind.Physical.Schema; encode srcKind.Physical.Table |])
        let tgtTable =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; segments are typed via Identifier.EncodeIdentifier
                ".",
                [| encode tgtKind.Physical.Schema; encode tgtKind.Physical.Table |])
        let srcCol = encode srcAttr.Column.ColumnName
        let tgtCol = encode tgtPkAttr.Column.ColumnName
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; encode + integer literal are typed safe segments
            "SELECT TOP (", string sampleLimit, ") ",
            "s.", srcCol, " AS [OrphanValue] ",
            "FROM ", srcTable, " AS s ",
            "LEFT JOIN ", tgtTable, " AS t ON s.", srcCol, " = t.", tgtCol, " ",
            "WHERE s.", srcCol, " IS NOT NULL AND t.", tgtCol, " IS NULL ",
            "ORDER BY s.", srcCol)

    /// Probe one Reference for orphan-row samples. Returns a
    /// `DiagnosticEntry` carrying the sample values in Metadata when
    /// orphans were observed; returns `None` when the FK is clean
    /// (no DiagnosticEntry emitted in the clean case — operators see
    /// orphan-bearing FKs only). Composite-PK targets and unresolved
    /// attributes skip silently (same shape as slice B.3.1's
    /// `probeReference` deferrals).
    let private probeOrphanSample
        (cnn: SqlConnection)
        (catalog: Catalog)
        (srcKind: Kind)
        (reality: ForeignKeyReality)
        (reference: Reference)
        (sampleLimit: int)
        : Task<DiagnosticEntry option> =
        task {
            use _ = Bench.scope "profile.live.probeOrphanSample"
            match Kind.tryFindAttribute reference.SourceAttribute srcKind with
            | None -> return None
            | Some srcAttr ->
                match Catalog.tryFindKind reference.TargetKind catalog with
                | None -> return None
                | Some tgtKind ->
                    match Kind.primaryKey tgtKind with
                    | [ tgtPkAttr ] ->
                        use cmd = cnn.CreateCommand()
                        cmd.CommandText <-
                            foreignKeyOrphanSampleSql sampleLimit srcKind srcAttr tgtKind tgtPkAttr
                        cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                        use! reader = cmd.ExecuteReaderAsync()
                        let samples = System.Collections.Generic.List<string>()
                        let mutable keepGoing = true
                        while keepGoing do
                            let! advanced = reader.ReadAsync()
                            if advanced then
                                // FK source values are typed at the
                                // column boundary (INT for OS native
                                // IDs; strings for natural keys); the
                                // diagnostic surface is Map<string,
                                // string>, so project via
                                // InvariantCulture ToString.
                                let value = reader.GetValue(0)
                                let formatted =
                                    match value with
                                    | :? int as i ->
                                        i.ToString System.Globalization.CultureInfo.InvariantCulture
                                    | :? int64 as i ->
                                        i.ToString System.Globalization.CultureInfo.InvariantCulture
                                    | :? string as s -> s
                                    | other ->
                                        // BCL `ToString()` returns `string | null` under
                                        // F# nullness analysis; defensive-default to "<null>"
                                        // covers the structurally-impossible-here case
                                        // (orphan-value filter excludes NULLs).
                                        match other.ToString() with
                                        | null -> "<null>"
                                        | s    -> s
                                samples.Add formatted
                            else
                                keepGoing <- false
                        if samples.Count = 0 then return None
                        else
                            let metadata =
                                let baseEntries =
                                    [ "orphanCount", reality.OrphanCount.ToString System.Globalization.CultureInfo.InvariantCulture
                                      "sampleSize",  samples.Count.ToString System.Globalization.CultureInfo.InvariantCulture
                                      "sourceColumn", srcAttr.Column.ColumnName
                                      "targetColumn", tgtPkAttr.Column.ColumnName ]
                                let perSample =
                                    samples
                                    |> Seq.mapi (fun i v ->
                                        System.String.Concat("sample.", string i), v)  // LINT-ALLOW: structured metadata key
                                    |> List.ofSeq
                                Map.ofList (baseEntries @ perSample)
                            return
                                Some
                                    { Source   = "adapter:LiveProfiler"
                                      Severity = DiagnosticSeverity.Warning
                                      Code     = "profiling.foreignKey.orphanSample"
                                      Message  =
                                        System.String.Concat(  // LINT-ALLOW: operator-facing prose narration; segments are typed (int64 ToString + reference Name)
                                            "Foreign key '",
                                            Name.value reference.Name,
                                            "' has ",
                                            reality.OrphanCount.ToString System.Globalization.CultureInfo.InvariantCulture,
                                            " orphan source row(s); sampled ",
                                            samples.Count.ToString System.Globalization.CultureInfo.InvariantCulture,
                                            ".")
                                      SsKey    = Some reference.SsKey
                                      Metadata = metadata }
                    | _ -> return None
        }

    /// Capture FK orphan-row samples as `DiagnosticEntry` records.
    /// Per pillar 9, operational samples are operator-intent
    /// observation, not data-intent evidence — so the surface is
    /// `Diagnostics<'_>`, not a `Profile` axis. Walks
    /// `Profile.ForeignKeys` filtering to `HasOrphan = true` (only
    /// probes FKs with observed orphans per slice B.3.1's evidence);
    /// per-FK TOP-N probe yields up to `sampleLimit` orphan values
    /// in deterministic order. Clean FKs emit no entry.
    ///
    /// **What this slice covers (B.3.4):**
    ///   - One `DiagnosticEntry` per orphan-bearing FK
    ///   - `Severity = Warning`
    ///   - `Code = "profiling.foreignKey.orphanSample"`
    ///   - `Metadata` carries `orphanCount`, `sampleSize`,
    ///     `sourceColumn`, `targetColumn`, plus `sample.0` ..
    ///     `sample.N-1` keys with the orphan values
    ///   - Up to `defaultOrphanSampleLimit` (5) samples per FK
    ///   - Deterministic ordering via `ORDER BY <srcCol>`
    ///
    /// **Deferred (named follow-up triggers):**
    ///   - Operator-tunable sample limit — fixed at 5 today via
    ///     `defaultOrphanSampleLimit`. Trigger: chapter B.3 slice 6
    ///     wires `SqlProfilerOptions.Sampling` through.
    ///   - Source-row PK identifiers in sample — current shape
    ///     emits only the orphan FK value. V1's
    ///     `ForeignKeyOrphanSampleQueryBuilder` also includes
    ///     source-row PK identifiers. Trigger: operator workflow
    ///     demands navigation back to the offending source row.
    ///   - Composite-PK target probes — same deferral as slice
    ///     B.3.1 (composite shape inherits from slice B.3.3).
    let captureForeignKeyOrphanSamples
        (cnn: SqlConnection)
        (catalog: Catalog)
        (profile: Profile)
        : Task<Result<DiagnosticEntry list>> =
        task {
            use _ = Bench.scope "profile.live.captureForeignKeyOrphanSamples"
            try
                let entries =
                    System.Collections.Generic.List<DiagnosticEntry>()
                for m in catalog.Modules do
                    for kind in m.Kinds do
                        if not (isStaticKind kind) then
                            for reference in kind.References do
                                match Profile.tryFindForeignKey reference.SsKey profile with
                                | Some reality when reality.HasOrphan ->
                                    let! sampleOpt =
                                        probeOrphanSample
                                            cnn catalog kind reality reference
                                            defaultOrphanSampleLimit
                                    match sampleOpt with
                                    | Some entry -> entries.Add entry
                                    | None -> ()
                                | _ -> ()
                return Result.success (List.ofSeq entries)
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureForeignKeyOrphanSamplesFailed"
                            (System.String.Concat("LiveProfiler.captureForeignKeyOrphanSamples failed: ", ex.Message)))
        }

    /// Project `AttributeReality.HasDuplicates` evidence into
    /// `UniqueCandidateProfile` records. The two IR axes carry
    /// semantically identical single-column-duplicate witnesses;
    /// `UniqueIndexRules.evaluate` reads `UniqueCandidates`, not
    /// `AttributeRealities`, so the projection fills the axis the
    /// rule actually consults. PK attributes project as
    /// `HasDuplicate = false` (`AttributeReality.create` default for
    /// PKs since the live-probe skips them per
    /// `captureAttributeRealities` logic).
    ///
    /// Per the chapter B.3 slice 3 design rationale (DECISIONS
    /// 2026-05-19 (slice B.3.3)): no extra SQL probes; the
    /// existing per-attribute HasDuplicates EXISTS+GROUP BY probe
    /// already provides the witness, so the projection is the
    /// canonical path. A future consolidation slice may retire one
    /// of the two axes (the dual-axis carriage is V1-historical).
    let private projectUniqueCandidates
        (realities: AttributeReality list)
        : UniqueCandidateProfile list =
        realities
        |> List.map (fun r ->
            { AttributeKey = r.AttributeKey
              HasDuplicate = r.HasDuplicates
              ProbeStatus  = ProbeStatus.noProbeRun })

    let captureForeignKeyRealities
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<ForeignKeyReality list>> =
        task {
            use _ = Bench.scope "profile.live.captureForeignKeyRealities"
            try
                let realities = System.Collections.Generic.List<ForeignKeyReality>()
                for m in catalog.Modules do
                    for kind in m.Kinds do
                        if not (isStaticKind kind) then
                            for reference in kind.References do
                                let! reality = probeReference cnn catalog kind reference
                                realities.Add reality
                return Result.success (List.ofSeq realities)
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureForeignKeyRealitiesFailed"
                            (System.String.Concat("LiveProfiler.captureForeignKeyRealities failed: ", ex.Message)))
        }

    // -----------------------------------------------------------------
    // Slice B.3.6 — EvidenceCache discovery + pure-F# derivations
    // -----------------------------------------------------------------

    /// Aggregate-query SQL for a kind: one row returning exact
    /// RowCount + per-attribute exact NullCount. Mirrors slice
    /// B.3.2's `columnProfileSql` shape.
    let private cacheAggregateSql (kind: Kind) : string =
        let table =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
                ".",
                [| encode kind.Physical.Schema; encode kind.Physical.Table |])
        let perColumnNullCount (idx: int) (attr: Attribute) : string =
            let col = encode attr.Column.ColumnName
            System.String.Concat(  // LINT-ALLOW: typed encode + integer index
                "COUNT_BIG(CASE WHEN ", col, " IS NULL THEN 1 END) AS [c", string idx, "]")
        let selectList =
            kind.Attributes
            |> List.mapi perColumnNullCount
            |> String.concat ", "  // LINT-ALLOW: positional comma joiner
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
            "SELECT COUNT_BIG(*) AS [c_rows], ", selectList, " FROM ", table)

    /// Row-streaming SQL for a kind: project every catalog attribute
    /// in declaration order (positional alignment with
    /// `kind.Attributes`). Slice 7 adds optional `TOP (@N)` cap +
    /// deterministic `ORDER BY <pk>` when a single-column PK
    /// exists. Without a PK, sample ordering is engine-defined; the
    /// adapter still applies the TOP cap but operators should treat
    /// the sampled rows as nondeterministic (a future
    /// `OrderingPolicy` operator-intent variant can stabilize via
    /// `ORDER BY <attr0>` or operator-supplied keys).
    let private cacheRowStreamSql
        (maxRows: int option)
        (kind: Kind)
        : string =
        let table =
            System.String.Join(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
                ".",
                [| encode kind.Physical.Schema; encode kind.Physical.Table |])
        let columnList =
            kind.Attributes
            |> List.map (fun a -> encode a.Column.ColumnName)
            |> String.concat ", "  // LINT-ALLOW: positional comma joiner over typed encode segments
        let topClause =
            match maxRows with
            | Some n when n > 0 -> System.String.Concat("TOP (", string n, ") ")
            | _                 -> ""
        let orderClause =
            match maxRows, Kind.primaryKey kind with
            | Some _, [ pk ] ->
                // Deterministic sampling: ORDER BY single-column PK
                // (A1 — deterministic sampling under repeated probes).
                System.String.Concat(" ORDER BY ", encode pk.Column.ColumnName)
            | _ -> ""
        System.String.Concat(  // LINT-ALLOW: terminal SQL-text-emission boundary; typed segments
            "SELECT ", topClause, columnList, " FROM ", table, orderClause)

    /// Discovery for one kind: 3 queries (aggregate + reflection +
    /// row-stream); composes into a `CachedKind`. Slice 7 accepts
    /// `maxRows` to opt into TOP-N sampling on the row-stream query.
    let private discoverKind
        (cnn: SqlConnection)
        (maxRows: int option)
        (kind: Kind)
        : Task<CachedKind option> =
        task {
            use _ = Bench.scope "profile.live.discoverKind"
            if List.isEmpty kind.Attributes then return None
            else
                // 1. Aggregate: exact RowCount + per-attribute NullCount.
                let! (rowCount, nullCounts) = task {
                    use cmd = cnn.CreateCommand()
                    cmd.CommandText <- cacheAggregateSql kind
                    cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                    use! reader = cmd.ExecuteReaderAsync()
                    let! advanced = reader.ReadAsync()
                    if not advanced then
                        return (0L, Map.empty)
                    else
                        let rc =
                            if reader.IsDBNull(0) then 0L else reader.GetInt64(0)
                        let counts =
                            kind.Attributes
                            |> List.mapi (fun idx attr ->
                                let nc =
                                    if reader.IsDBNull(1 + idx) then 0L
                                    else reader.GetInt64(1 + idx)
                                attr.SsKey, nc)
                            |> Map.ofList
                        return (rc, counts)
                }
                // 2. Reflection: per-column IsNullableInDatabase from
                //    INFORMATION_SCHEMA. Returns Map<colName, bool>.
                let! nullabilityMap = reflectNullability cnn kind
                // 3. Row-stream: SELECT * → per-row CachedValue array.
                //    Column-oriented final shape (transpose at end).
                let perColumnValues =
                    Array.init (List.length kind.Attributes) (fun _ ->
                        System.Collections.Generic.List<CachedValue>())
                use cmd = cnn.CreateCommand()
                cmd.CommandText <- cacheRowStreamSql maxRows kind
                cmd.CommandTimeout <- CommandTimeoutPolicy.resolve ()
                use! reader = cmd.ExecuteReaderAsync()
                let mutable keepReading = true
                while keepReading do
                    let! advanced = reader.ReadAsync()
                    if advanced then
                        for idx = 0 to (List.length kind.Attributes - 1) do
                            let cellValue =
                                CachedValue.ofReaderValue (reader.GetValue idx)
                            perColumnValues.[idx].Add cellValue
                    else
                        keepReading <- false
                let columns =
                    kind.Attributes
                    |> List.mapi (fun idx attr ->
                        let isNullable =
                            Map.tryFind attr.Column.ColumnName nullabilityMap
                            |> Option.defaultValue false
                        { AttributeKey         = attr.SsKey
                          IsNullableInDatabase = isNullable
                          Values               = perColumnValues.[idx].ToArray() })
                let columnsByKey =
                    columns
                    |> List.map (fun c -> c.AttributeKey, c)
                    |> Map.ofList
                return
                    Some
                        { KindKey      = kind.SsKey
                          RowCount     = rowCount
                          NullCounts   = nullCounts
                          Columns      = columns
                          ColumnsByKey = columnsByKey }
        }

    /// Capture a complete EvidenceCache for the catalog. Walks every
    /// non-static kind, runs the 3-query discovery, composes into a
    /// keyed cache.
    ///
    /// Slice B.3.6 architectural pivot: replaces the accreted
    /// per-attribute / per-Reference / per-Index SQL probes from
    /// slices 1-5 with ONE bulk-extraction phase per kind. All
    /// downstream Profile-axis derivations run in pure F# from the
    /// cache (see `Cache.derive*`). Net round-trip count per
    /// catalog drops from ~6000 (at 300 tables × 10 attrs) to ~900
    /// (3 per kind regardless of attribute count).
    /// Sampling-aware overload. `attach` uses `defaults` (full-scan);
    /// operator-driven CLIs supply explicit `SqlProfilerOptions` for
    /// large catalogs. Slice 7 cash-out.
    let captureEvidenceCacheWith
        (options: SqlProfilerOptions)
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<EvidenceCache>> =
        task {
            use _ = Bench.scope "profile.live.captureEvidenceCache"
            try
                let nonStaticKinds =
                    catalog.Modules
                    |> List.collect (fun m -> m.Kinds)
                    |> List.filter (fun k -> not (isStaticKind k))
                let mutable acc : Map<SsKey, CachedKind> = Map.empty
                for kind in nonStaticKinds do
                    let! result = discoverKind cnn options.MaxRowsPerKind kind
                    match result with
                    | Some cached -> acc <- Map.add cached.KindKey cached acc
                    | None        -> ()
                return Result.success { Kinds = acc }
            with
            | ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "profile.live.captureEvidenceCacheFailed"
                            (System.String.Concat("LiveProfiler.captureEvidenceCache failed: ", ex.Message)))
        }

    /// Default-options convenience: full-scan. Public-surface entry
    /// point for callers that don't need to control sampling.
    let captureEvidenceCache
        (cnn: SqlConnection)
        (catalog: Catalog)
        : Task<Result<EvidenceCache>> =
        captureEvidenceCacheWith SqlProfilerOptions.defaults cnn catalog

    /// Pure-F# derivations from EvidenceCache. Synchronous; no SQL.
    /// Each function takes the cache + the catalog (for attribute
    /// lookup) and returns the relevant Profile axis IR list.
    module Cache =

        /// Derive `ColumnProfile` per attribute. Uses the cache's
        /// exact aggregates (RowCount + NullCount per attribute);
        /// the smart-constructor invariants hold by SQL construction.
        let deriveColumnProfiles
            (cache: EvidenceCache)
            (catalog: Catalog)
            : ColumnProfile list =
            use _ = Bench.scope "profile.cache.deriveColumnProfiles"
            let probeStatus = ProbeStatus.noProbeRun
            catalog
            |> Catalog.allKinds
            |> List.collect (fun kind ->
                match Map.tryFind kind.SsKey cache.Kinds with
                | None        -> []
                | Some cached ->
                    let statusWithSample =
                        ProbeStatus.observed cached.RowCount
                    kind.Attributes
                    |> List.choose (fun attr ->
                        let nullCount =
                            Map.tryFind attr.SsKey cached.NullCounts
                            |> Option.defaultValue 0L
                        match
                            ColumnProfile.create
                                attr.SsKey cached.RowCount nullCount statusWithSample
                        with
                        | Ok p    -> Some p
                        | Error _ -> None))
            // The probeStatus binding above is a stylistic hold;
            // the real probe status uses the observed sample size
            // (full RowCount under full-scan default per slice 6).
            |> fun result ->
                ignore probeStatus
                result

        /// Derive `AttributeReality` per attribute. HasNulls /
        /// HasDuplicates computed via column-scan in F# (the
        /// no-overfetching premise — these come from the cache, not
        /// from new SQL probes). IsNullableInDatabase from the
        /// cache's reflection. IsPresentButInactive derived from
        /// catalog.IsActive AND nullabilityMap-presence (the
        /// existing slice A.4.7'-prelude.live-profiler logic, now
        /// cache-resident).
        let deriveAttributeRealities
            (cache: EvidenceCache)
            (catalog: Catalog)
            : AttributeReality list =
            use _ = Bench.scope "profile.cache.deriveAttributeRealities"
            catalog
            |> Catalog.allKinds
            |> List.collect (fun kind ->
                match Map.tryFind kind.SsKey cache.Kinds with
                | None        -> []
                | Some cached ->
                    cached.Columns
                    |> List.map (fun column ->
                        let hasNulls =
                            column.Values
                            |> Array.exists CachedValue.isNull
                        // HasDuplicates: ignore nulls; check whether
                        // any non-null value repeats. Use Set for
                        // O(n log n) detection on string projection
                        // (best-effort across heterogeneous types).
                        let hasDuplicates =
                            let seen = System.Collections.Generic.HashSet<string>()
                            let mutable found = false
                            for v in column.Values do
                                if not found then
                                    match v with
                                    | NullValue -> ()
                                    | _ ->
                                        let key =
                                            match v with
                                            | IntValue i -> "i:" + i.ToString System.Globalization.CultureInfo.InvariantCulture
                                            | DecimalValue d -> "d:" + d.ToString System.Globalization.CultureInfo.InvariantCulture
                                            | StringValue s -> "s:" + s
                                            | DateValue dto -> "t:" + dto.UtcTicks.ToString System.Globalization.CultureInfo.InvariantCulture
                                            | BinaryValue _ -> "b"  // binary-equality coarse
                                            | NullValue -> "n"
                                        if not (seen.Add key) then found <- true
                            found
                        let attr =
                            Kind.tryFindAttribute column.AttributeKey kind
                        let isPresentButInactive =
                            match attr with
                            | Some a -> column.IsNullableInDatabase |> ignore; not a.IsActive
                            | None   -> false
                        // Note: IsPresentButInactive simplifies to
                        // `not attr.IsActive` since the column being
                        // in the cache means it's present by
                        // construction (we discovered it via SELECT).
                        // The original A.4.7'-prelude logic also
                        // confirmed presence via nullabilityMap;
                        // both signals collapse here.
                        { AttributeReality.create column.AttributeKey with
                            IsNullableInDatabase = column.IsNullableInDatabase
                            HasNulls             = hasNulls
                            HasDuplicates        = hasDuplicates
                            IsPresentButInactive = isPresentButInactive }))

        /// Derive `NumericDistribution` per numeric attribute. Pure
        /// F# computation over cached column values:
        ///   - Min/Max/Mean via Array.min/max/average (decimal)
        ///   - StdDev via population formula (sqrt of mean of
        ///     squared deviations)
        ///   - Percentiles via sorted-array index
        ///   - Composed via NumericDistribution.create chained
        ///     through StatisticalMoments + withMoments (slice 5's
        ///     IR keystone primitives)
        let deriveNumericDistributions
            (cache: EvidenceCache)
            (catalog: Catalog)
            : NumericDistribution list =
            use _ = Bench.scope "profile.cache.deriveNumericDistributions"
            let isNumeric (attr: Attribute) : bool =
                match attr.Type with
                | PrimitiveType.Integer | PrimitiveType.Decimal -> true
                | _ -> false
            let percentile (sorted: decimal array) (p: decimal) : decimal =
                // Continuous linear-interpolation percentile (PERCENTILE_CONT
                // semantics from SQL Server). For sorted array of length N,
                // h = (N - 1) * p; floor(h) = lo; ceil(h) = hi; fraction = h - lo.
                if sorted.Length = 0 then 0M
                elif sorted.Length = 1 then sorted.[0]
                else
                    let n = decimal (sorted.Length - 1)
                    let h = n * p
                    let lo = int h
                    let frac = h - decimal lo
                    if lo >= sorted.Length - 1 then sorted.[sorted.Length - 1]
                    else
                        sorted.[lo] + frac * (sorted.[lo + 1] - sorted.[lo])
            catalog
            |> Catalog.allKinds
            |> List.collect (fun kind ->
                match Map.tryFind kind.SsKey cache.Kinds with
                | None        -> []
                | Some cached ->
                    kind.Attributes
                    |> List.filter isNumeric
                    |> List.choose (fun attr ->
                        let column = Map.tryFind attr.SsKey cached.ColumnsByKey
                        match column with
                        | None        -> None
                        | Some column ->
                            let nonNullValues =
                                column.Values
                                |> Array.choose CachedValue.tryDecimal
                            let sampleSize = int64 nonNullValues.Length
                            if sampleSize < 5L then None
                            else
                                let sorted = nonNullValues |> Array.sort
                                let min_  = sorted.[0]
                                let max_  = sorted.[sorted.Length - 1]
                                let mean  =
                                    (nonNullValues |> Array.sum)
                                        / decimal nonNullValues.Length
                                // Population standard deviation:
                                // sqrt(mean of squared deviations).
                                let variance =
                                    nonNullValues
                                    |> Array.sumBy (fun v ->
                                        let d = v - mean
                                        d * d)
                                    |> fun s -> s / decimal nonNullValues.Length
                                let stdDev =
                                    decimal (sqrt (float variance))
                                let probeStatus = ProbeStatus.observed sampleSize
                                let baseResult =
                                    NumericDistribution.create
                                        attr.SsKey
                                        min_
                                        (percentile sorted 0.25M)
                                        (percentile sorted 0.50M)
                                        (percentile sorted 0.75M)
                                        (percentile sorted 0.95M)
                                        (percentile sorted 0.99M)
                                        max_ sampleSize probeStatus
                                let momentsResult =
                                    StatisticalMoments.create mean stdDev
                                let enriched =
                                    baseResult
                                    |> Result.bind (fun dist ->
                                        momentsResult
                                        |> Result.bind (fun m ->
                                            NumericDistribution.withMoments m dist))
                                match enriched with
                                | Ok d    -> Some d
                                | Error _ ->
                                    match baseResult with
                                    | Ok d -> Some d
                                    | Error _ -> None))

        /// Default vocabulary cap for categorical distributions
        /// derived from the cache. Slice 7 wires this through
        /// `SqlProfilerOptions.Sampling` for operator tuning.
        [<Literal>]
        let private defaultCategoricalVocabularyLimit = 50

        /// Derive `CategoricalDistribution` per string-typed
        /// attribute. Pure-F# `Array.groupBy` over cached string
        /// values; sorts frequencies DESC by count then alphabetically
        /// by value (deterministic; matches the SQL ORDER BY shape
        /// V1's `UniqueCandidateQueryBuilder` used). Truncates at
        /// `defaultCategoricalVocabularyLimit`; sets `IsTruncated`
        /// when distinct count exceeds the cap.
        let deriveCategoricalDistributions
            (cache: EvidenceCache)
            (catalog: Catalog)
            : CategoricalDistribution list =
            use _ = Bench.scope "profile.cache.deriveCategoricalDistributions"
            let isCategorical (attr: Attribute) : bool =
                match attr.Type with
                | PrimitiveType.Text -> true
                | _ -> false
            catalog
            |> Catalog.allKinds
            |> List.collect (fun kind ->
                match Map.tryFind kind.SsKey cache.Kinds with
                | None        -> []
                | Some cached ->
                    kind.Attributes
                    |> List.filter isCategorical
                    |> List.choose (fun attr ->
                        let column = Map.tryFind attr.SsKey cached.ColumnsByKey
                        match column with
                        | None        -> None
                        | Some column ->
                            // Single-pass Dictionary frequency tally
                            // (slice 6b Big-O optimization #3): one
                            // walk over column values builds the
                            // frequency map; no intermediate
                            // Array.choose / Array.groupBy / Array.map.
                            let freq = System.Collections.Generic.Dictionary<string, int64>()
                            for v in column.Values do
                                match v with
                                | StringValue s ->
                                    match freq.TryGetValue s with
                                    | true, c  -> freq.[s] <- c + 1L
                                    | false, _ -> freq.[s] <- 1L
                                | _ -> ()
                            if freq.Count = 0 then None
                            else
                                let distinctCount = int64 freq.Count
                                let entries =
                                    freq
                                    |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                                    |> Seq.toArray
                                let sortedByCountDesc =
                                    entries
                                    |> Array.sortWith (fun (vA, cA) (vB, cB) ->
                                        // Count DESC, then value ASC (deterministic
                                        // tie-break — same shape as V1's SQL
                                        // ORDER BY COUNT_BIG(*) DESC, <col>).
                                        let cmpCount = compare cB cA
                                        if cmpCount <> 0 then cmpCount
                                        else compare vA vB)
                                let truncated =
                                    if sortedByCountDesc.Length > defaultCategoricalVocabularyLimit then
                                        Array.sub sortedByCountDesc 0 defaultCategoricalVocabularyLimit
                                    else sortedByCountDesc
                                let isTruncated =
                                    distinctCount > int64 defaultCategoricalVocabularyLimit
                                let probeStatus = ProbeStatus.observed distinctCount
                                match
                                    CategoricalDistribution.create
                                        attr.SsKey
                                        (List.ofArray truncated)
                                        distinctCount
                                        isTruncated
                                        probeStatus
                                with
                                | Ok d    -> Some d
                                | Error _ -> None))

        /// Project a CachedColumn's per-row values into a tuple-key
        /// projection across multiple columns. Returns an array of
        /// canonical-string tuples aligned by row index; rows where
        /// ANY column is null are excluded (SQL Server's GROUP BY
        /// convention; mirrors slice 3's `WHERE <col1> IS NOT NULL
        /// AND <col2> IS NOT NULL` filter).
        let private projectTupleKeys
            (columns: CachedColumn list)
            : string array option =
            if List.isEmpty columns then None
            else
                let rowCount = columns.Head.Values.Length
                // Same row count assumed across columns (cache invariant).
                let result = ResizeArray<string>()
                for rowIdx = 0 to rowCount - 1 do
                    let mutable hasNull = false
                    let parts = ResizeArray<string>()
                    for column in columns do
                        if not hasNull then
                            let v = column.Values.[rowIdx]
                            match v with
                            | NullValue -> hasNull <- true
                            | IntValue i -> parts.Add("i:" + i.ToString System.Globalization.CultureInfo.InvariantCulture)
                            | DecimalValue d -> parts.Add("d:" + d.ToString System.Globalization.CultureInfo.InvariantCulture)
                            | StringValue s -> parts.Add("s:" + s)
                            | DateValue dto -> parts.Add("t:" + dto.UtcTicks.ToString System.Globalization.CultureInfo.InvariantCulture)
                            | BinaryValue _ -> parts.Add("b")  // binary equality coarse
                    if not hasNull then
                        result.Add(System.String.Join("|", parts))
                Some (result.ToArray())

        /// Derive `CompositeUniqueCandidateProfile` per non-unique
        /// multi-column Index. Pure-F# tuple-keyed `Array.groupBy`;
        /// HasDuplicate iff any group has count > 1. Mirrors slice
        /// 3's SQL `GROUP BY <cols> HAVING COUNT_BIG(*) > 1` shape.
        let deriveCompositeUniqueCandidates
            (cache: EvidenceCache)
            (catalog: Catalog)
            : CompositeUniqueCandidateProfile list =
            use _ = Bench.scope "profile.cache.deriveCompositeUniqueCandidates"
            let isCompositeCandidate (index: Index) : bool =
                not index.IsUnique && List.length index.Columns >= 2
            catalog
            |> Catalog.allKinds
            |> List.collect (fun kind ->
                match Map.tryFind kind.SsKey cache.Kinds with
                | None        -> []
                | Some cached ->
                    kind.Indexes
                    |> List.filter isCompositeCandidate
                    |> List.map (fun index ->
                        let attributeKeys =
                            index.Columns |> List.map (fun ic -> ic.Attribute)
                        let resolvedColumns =
                            attributeKeys
                            |> List.map (fun key ->
                                Map.tryFind key cached.ColumnsByKey)
                        if resolvedColumns |> List.exists Option.isNone then
                            // One or more index columns absent from cache —
                            // mark ambiguous, conservative-safe.
                            let defaulted =
                                CompositeUniqueCandidateProfile.create
                                    kind.SsKey attributeKeys
                            { defaulted with ProbeStatus = ProbeStatus.ambiguous }
                        else
                            let columns = resolvedColumns |> List.choose id
                            match projectTupleKeys columns with
                            | None -> CompositeUniqueCandidateProfile.create kind.SsKey attributeKeys
                            | Some tuples ->
                                let hasDuplicate =
                                    tuples
                                    |> Array.groupBy id
                                    |> Array.exists (fun (_, occ) -> occ.Length > 1)
                                { KindKey       = kind.SsKey
                                  AttributeKeys = attributeKeys
                                  HasDuplicate  = hasDuplicate
                                  ProbeStatus   = ProbeStatus.observed cached.RowCount }))

        /// Project a CachedColumn value into a comparable string key
        /// (heterogeneous-equality bridge). Used by FK orphan
        /// derivation to compare source FK values against target PK
        /// values. Excludes NullValue (FK probes always filter
        /// `IS NOT NULL`).
        let private cacheValueKey (v: CachedValue) : string option =
            match v with
            | NullValue -> None
            | IntValue i -> Some ("i:" + i.ToString System.Globalization.CultureInfo.InvariantCulture)
            | DecimalValue d -> Some ("d:" + d.ToString System.Globalization.CultureInfo.InvariantCulture)
            | StringValue s -> Some ("s:" + s)
            | DateValue dto -> Some ("t:" + dto.UtcTicks.ToString System.Globalization.CultureInfo.InvariantCulture)
            | BinaryValue _ -> Some "b"

        /// Pre-built FK target PK sets, keyed by (targetKindKey,
        /// targetPkAttrKey). Built once per `attachFromCache` call;
        /// shared between `deriveForeignKeyRealities` and
        /// `deriveForeignKeyOrphanSamples`. Eliminates N-to-1
        /// duplicate Set construction when N references share a
        /// target AND eliminates 2x duplicate work across the two
        /// FK derivation passes (slice 6b Big-O audit).
        type private ForeignKeyTargetIndex = Map<SsKey * SsKey, Set<string>>

        let buildForeignKeyTargetIndex
            (cache: EvidenceCache)
            (catalog: Catalog)
            : ForeignKeyTargetIndex =
            use _ = Bench.scope "profile.cache.buildForeignKeyTargetIndex"
            // Collect distinct (targetKind, targetPkAttr) pairs that any
            // Reference points at; build the Set per pair once.
            let pairs =
                catalog
                |> Catalog.allKinds
                |> List.collect (fun srcKind ->
                    srcKind.References
                    |> List.choose (fun reference ->
                        match Catalog.tryFindKind reference.TargetKind catalog with
                        | None -> None
                        | Some tgtKind ->
                            match Kind.primaryKey tgtKind with
                            | [ tgtPkAttr ] ->
                                Some (tgtKind.SsKey, tgtPkAttr.SsKey)
                            | _ -> None))
                |> List.distinct
            pairs
            |> List.choose (fun (kindKey, attrKey) ->
                match EvidenceCache.tryFindColumn kindKey attrKey cache with
                | None -> None
                | Some col ->
                    let pkSet =
                        col.Values
                        |> Array.choose cacheValueKey
                        |> Set.ofArray
                    Some ((kindKey, attrKey), pkSet))
            |> Map.ofList

        /// Derive `ForeignKeyReality` per Reference. Cross-table
        /// in-memory derivation: build a `Set` from target PK column
        /// values; iterate source FK column values; orphan-count
        /// = source values not present in target set. Single-column
        /// PK targets only (composite-PK FK extension uses
        /// `projectTupleKeys` once a fixture surfaces). Mirrors
        /// slice 1's SQL LEFT JOIN orphan-count probe.
        let deriveForeignKeyRealitiesWith
            (targetIndex: ForeignKeyTargetIndex)
            (cache: EvidenceCache)
            (catalog: Catalog)
            : ForeignKeyReality list =
            use _ = Bench.scope "profile.cache.deriveForeignKeyRealities"
            let isStatic (k: Kind) : bool =
                k.Modality
                |> List.exists (function
                    | Static _ -> true
                    | _ -> false)
            catalog
            |> Catalog.allKinds
            |> List.collect (fun srcKind ->
                if isStatic srcKind then []
                else
                    srcKind.References
                    |> List.map (fun reference ->
                        let defaulted = ForeignKeyReality.create reference.SsKey
                        let ambiguous =
                            { defaulted with ProbeStatus = ProbeStatus.ambiguous }
                        match Catalog.tryFindKind reference.TargetKind catalog with
                        | None -> ambiguous
                        | Some tgtKind ->
                            match Kind.primaryKey tgtKind with
                            | [ tgtPkAttr ] ->
                                let srcColumn =
                                    EvidenceCache.tryFindColumn srcKind.SsKey reference.SourceAttribute cache
                                let targetSetOpt =
                                    Map.tryFind (tgtKind.SsKey, tgtPkAttr.SsKey) targetIndex
                                match srcColumn, targetSetOpt with
                                | Some sCol, Some targetSet ->
                                    let orphanCount =
                                        sCol.Values
                                        |> Array.choose cacheValueKey
                                        |> Array.filter (fun k -> not (Set.contains k targetSet))
                                        |> Array.length
                                        |> int64
                                    let rowCount =
                                        match Map.tryFind srcKind.SsKey cache.Kinds with
                                        | Some k -> k.RowCount
                                        | None   -> 0L
                                    { ReferenceKey = reference.SsKey
                                      HasOrphan    = orphanCount > 0L
                                      OrphanCount  = orphanCount
                                      IsNoCheck    = not reference.IsConstraintTrusted
                                      ProbeStatus  = ProbeStatus.observed rowCount }
                                | _ -> ambiguous
                            | _ -> ambiguous))

        /// Public-surface entry point: builds the target-PK-set index
        /// per-call. `attachFromCache` uses the With-overload directly
        /// to share the index across FK realities + orphan samples.
        let deriveForeignKeyRealities
            (cache: EvidenceCache)
            (catalog: Catalog)
            : ForeignKeyReality list =
            let targetIndex = buildForeignKeyTargetIndex cache catalog
            deriveForeignKeyRealitiesWith targetIndex cache catalog

        /// Derive `DiagnosticEntry list` carrying TOP-N orphan
        /// samples per orphan-bearing FK. Pillar 9: operational
        /// diagnostics, not data-intent. Mirrors slice 4's TOP-N
        /// SQL probe shape but runs from cache (no SQL).
        ///
        /// Default sample limit (cache-derivation copy; mirrors
        /// slice 4's per-Reference SQL constant). 5 per slice 4
        /// precedent; operator-tunable at slice 7.
        [<Literal>]
        let private cacheOrphanSampleLimit = 5

        let deriveForeignKeyOrphanSamplesWith
            (targetIndex: ForeignKeyTargetIndex)
            (cache: EvidenceCache)
            (catalog: Catalog)
            : DiagnosticEntry list =
            use _ = Bench.scope "profile.cache.deriveForeignKeyOrphanSamples"
            let isStatic (k: Kind) : bool =
                k.Modality
                |> List.exists (function
                    | Static _ -> true
                    | _ -> false)
            catalog
            |> Catalog.allKinds
            |> List.collect (fun srcKind ->
                if isStatic srcKind then []
                else
                    srcKind.References
                    |> List.choose (fun reference ->
                        match Catalog.tryFindKind reference.TargetKind catalog with
                        | None -> None
                        | Some tgtKind ->
                            match Kind.primaryKey tgtKind with
                            | [ tgtPkAttr ] ->
                                let srcAttr =
                                    Kind.tryFindAttribute reference.SourceAttribute srcKind
                                let srcColumn =
                                    EvidenceCache.tryFindColumn srcKind.SsKey reference.SourceAttribute cache
                                let targetSetOpt =
                                    Map.tryFind (tgtKind.SsKey, tgtPkAttr.SsKey) targetIndex
                                match srcAttr, srcColumn, targetSetOpt with
                                | Some srcAttr, Some sCol, Some targetSet ->
                                    // targetSet pre-built; no per-Reference Set.ofArray.
                                    let orphanValues =
                                        sCol.Values
                                        |> Array.choose (fun cv ->
                                            match cacheValueKey cv with
                                            | Some k when not (Set.contains k targetSet) ->
                                                // Render the orphan value as a
                                                // string for the diagnostic
                                                // payload (typed-to-string per
                                                // slice 4 precedent).
                                                let display =
                                                    match cv with
                                                    | IntValue i -> i.ToString System.Globalization.CultureInfo.InvariantCulture
                                                    | DecimalValue d -> d.ToString System.Globalization.CultureInfo.InvariantCulture
                                                    | StringValue s -> s
                                                    | DateValue dto -> dto.ToString System.Globalization.CultureInfo.InvariantCulture
                                                    | BinaryValue _ -> "<binary>"
                                                    | NullValue -> "<null>"
                                                Some display
                                            | _ -> None)
                                    if orphanValues.Length = 0 then None
                                    else
                                        // Deterministic ordering (A1): sort
                                        // orphan values ASC.
                                        let sortedOrphans = orphanValues |> Array.sort
                                        let sampled =
                                            if sortedOrphans.Length > cacheOrphanSampleLimit then
                                                Array.sub sortedOrphans 0 cacheOrphanSampleLimit
                                            else sortedOrphans
                                        let baseEntries =
                                            [ "orphanCount", sortedOrphans.Length.ToString System.Globalization.CultureInfo.InvariantCulture
                                              "sampleSize",  sampled.Length.ToString System.Globalization.CultureInfo.InvariantCulture
                                              "sourceColumn", srcAttr.Column.ColumnName
                                              "targetColumn", tgtPkAttr.Column.ColumnName ]
                                        let perSample =
                                            sampled
                                            |> Array.mapi (fun i v ->
                                                System.String.Concat("sample.", string i), v)  // LINT-ALLOW: structured metadata key
                                            |> List.ofArray
                                        let metadata = Map.ofList (baseEntries @ perSample)
                                        Some
                                            { Source   = "adapter:LiveProfiler"
                                              Severity = DiagnosticSeverity.Warning
                                              Code     = "profiling.foreignKey.orphanSample"
                                              Message  =
                                                System.String.Concat(  // LINT-ALLOW: operator-facing prose narration
                                                    "Foreign key '",
                                                    Name.value reference.Name,
                                                    "' has ",
                                                    sortedOrphans.Length.ToString System.Globalization.CultureInfo.InvariantCulture,
                                                    " orphan source row(s); sampled ",
                                                    sampled.Length.ToString System.Globalization.CultureInfo.InvariantCulture,
                                                    ".")
                                              SsKey    = Some reference.SsKey
                                              Metadata = metadata }
                                | _ -> None
                            | _ -> None))

        /// Public-surface entry point: builds the target-PK-set index
        /// per-call. `attachFromCache` uses the With-overload directly
        /// to share the index across FK realities + orphan samples.
        let deriveForeignKeyOrphanSamples
            (cache: EvidenceCache)
            (catalog: Catalog)
            : DiagnosticEntry list =
            let targetIndex = buildForeignKeyTargetIndex cache catalog
            deriveForeignKeyOrphanSamplesWith targetIndex cache catalog

    /// Attach Profile evidence by deriving from an in-memory cache.
    /// Synchronous; pure F#. Composes EVERY cache-derived Profile
    /// axis. Slice 6b completes the architectural pivot — all axes
    /// (AttributeRealities, Columns, UniqueCandidates, FK realities,
    /// composite uniqueness, numeric + categorical distributions)
    /// derive from cache without further SQL round-trips. The
    /// existing SQL captures (`captureAttributeRealities`,
    /// `captureColumnProfiles`, `captureForeignKeyRealities`,
    /// `captureCompositeUniqueCandidates`,
    /// `captureForeignKeyOrphanSamples`) remain as transitional
    /// public-surface accessors for callers that haven't migrated
    /// but are unused by `attach`.
    let attachFromCache
        (cache: EvidenceCache)
        (catalog: Catalog)
        (profile: Profile)
        : Profile =
        use _ = Bench.scope "profile.cache.attachFromCache"
        // Pre-build FK target PK sets ONCE; share across the two FK
        // derivations. Slice 6b Big-O optimization #2: eliminates
        // duplicate Set construction (per Reference + per pass).
        let fkTargetIndex = Cache.buildForeignKeyTargetIndex cache catalog
        let realities = Cache.deriveAttributeRealities cache catalog
        let columns = Cache.deriveColumnProfiles cache catalog
        let numericDists = Cache.deriveNumericDistributions cache catalog
        let categoricalDists = Cache.deriveCategoricalDistributions cache catalog
        let composites = Cache.deriveCompositeUniqueCandidates cache catalog
        let foreignKeys = Cache.deriveForeignKeyRealitiesWith fkTargetIndex cache catalog
        let distributions =
            (numericDists |> List.map AttributeDistribution.Numeric)
            @ (categoricalDists |> List.map AttributeDistribution.Categorical)
        { profile with
            AttributeRealities         = realities
            Columns                    = columns
            UniqueCandidates           = projectUniqueCandidates realities
            CompositeUniqueCandidates  = composites
            ForeignKeys                = foreignKeys
            Distributions              = distributions }

    /// Attach realities into an existing Profile. Sibling shape to
    /// `ProfileSnapshot.attach` / `ProfileStatistics.attach`: the
    /// adapter is composable, not authoritative — callers chain
    /// multiple sibling adapters per the rich-profiling agenda.
    /// Runs every LiveProfiler capture and composes the results into
    /// the input `Profile`. Pre-populated axes are overwritten with
    /// the freshly-probed values (the live-probe path is
    /// authoritative for axes it covers); sibling axes filled by
    /// other adapters (`Distributions`) are preserved untouched.
    ///
    /// Axes filled:
    ///   - `AttributeRealities` (slice A.4.7'-prelude.live-profiler)
    ///   - `ForeignKeys` (slice B.3.1.foreign-key-reality)
    ///   - `Columns` (slice B.3.2.column-null-counts)
    ///   - `UniqueCandidates` (slice B.3.3 — projected from
    ///     `AttributeRealities.HasDuplicates`; no extra SQL)
    ///   - `CompositeUniqueCandidates` (slice B.3.3 — per-Index
    ///     `GROUP BY … HAVING COUNT_BIG(*) > 1` probe)
    let attach
        (cnn: SqlConnection)
        (catalog: Catalog)
        (profile: Profile)
        : Task<Result<Profile>> =
        task {
            // Slice B.3.6b: cache-driven only. Capture EvidenceCache
            // once (3 queries per kind regardless of attribute count);
            // ALL Profile axes derive from cache in pure F# via
            // `attachFromCache`. The legacy SQL captures
            // (`captureAttributeRealities`, `captureColumnProfiles`,
            // `captureForeignKeyRealities`,
            // `captureCompositeUniqueCandidates`,
            // `captureForeignKeyOrphanSamples`) remain as transitional
            // public-surface accessors but `attach` no longer touches
            // them. Total SQL round-trips: 3 per non-static kind.
            let! cacheResult = captureEvidenceCache cnn catalog
            match cacheResult with
            | Error errors -> return Result.failure errors
            | Ok cache    -> return Result.success (attachFromCache cache catalog profile)
        }

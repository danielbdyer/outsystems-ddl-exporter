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
///   - Slice B.3.1.foreign-key-reality (this slice, 2026-05-19):
///     captures `Profile.ForeignKeys[ssKey].HasOrphan` +
///     `OrphanCount` per `Reference`. Surfaces as
///     `captureForeignKeyRealities`. Single-column PK targets only;
///     composite-PK targets defer with named trigger (the relevant
///     `Reference` returns a defaulted `ForeignKeyReality` with
///     `ProbeOutcome.AmbiguousMapping`). Sibling-adapter
///     composability holds: `attach` runs both captures.
///
/// Per the chapter B.3 open document — this is slice 1 of the
/// LiveProfiler deep-probe sweep cashing out the deferrals named
/// at slice A.4.7'-prelude.live-profiler.
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
                { defaulted with
                    ProbeStatus =
                        { defaulted.ProbeStatus with
                            Outcome = AmbiguousMapping } }
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
                                  ProbeStatus  =
                                    { CapturedAtUtc = DateTimeOffset.MinValue
                                      SampleSize    = rowCount
                                      Outcome       = Succeeded } }
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

    /// Attach realities into an existing Profile. Sibling shape to
    /// `ProfileSnapshot.attach` / `ProfileStatistics.attach`: the
    /// adapter is composable, not authoritative — callers chain
    /// multiple sibling adapters per the rich-profiling agenda.
    /// Runs both `captureAttributeRealities` and
    /// `captureForeignKeyRealities` and composes both into the input
    /// `Profile`; pre-populated `AttributeRealities` and
    /// `ForeignKeys` are overwritten with the freshly-probed values
    /// (the live-probe path is authoritative for the axes it
    /// covers); other sibling axes (`Columns`, `UniqueCandidates`,
    /// `Distributions`, …) are preserved untouched per the sibling-
    /// adapter composability discipline.
    let attach
        (cnn: SqlConnection)
        (catalog: Catalog)
        (profile: Profile)
        : Task<Result<Profile>> =
        task {
            let! attrCaptured = captureAttributeRealities cnn catalog
            match attrCaptured with
            | Error errors -> return Result.failure errors
            | Ok realities ->
                let! fkCaptured = captureForeignKeyRealities cnn catalog
                match fkCaptured with
                | Error errors -> return Result.failure errors
                | Ok fkRealities ->
                    return
                        Result.success
                            { profile with
                                AttributeRealities = realities
                                ForeignKeys        = fkRealities }
        }

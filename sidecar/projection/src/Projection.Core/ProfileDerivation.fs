namespace Projection.Core

// No `open System`: this module lives in `namespace Projection.Core`, and opening
// System would shadow the Core `Attribute` / `Index` types with `System.Attribute` /
// `System.Index`. Every System reference here is fully qualified.

/// **ProfileDerivation** — recon #5 (slice 2b): the pure derivation suite, lifted out
/// of `Projection.Adapters.Sql.LiveProfiler` into the core where it belongs.
///
/// Every Profile axis — column profiles, attribute realities, numeric / categorical
/// distributions, composite uniqueness, FK realities + cardinalities + selectivities +
/// multi-FK joint distributions — plus the FK orphan-sample diagnostics, is a TOTAL
/// FUNCTION of the `EvidenceCache` substrate and the `Catalog`. No SQL, no I/O, no
/// clock. The adapter runs the probe once (the `Ingest` boundary, `LiveProfiler`) and
/// fills the cache; everything here is the pure `Project` that reads it —
/// "discover-once at the boundary, derive-pure in the core." `attachFromCache` composes
/// the typed axes into a `Profile`. All of it is testable in the pure pool against a
/// hand-built cache, with no database.
///
/// (The orphan-sample derivation builds operator-facing `DiagnosticEntry.Message`
/// prose — the same pattern the Core diagnostic passes already use; the diagnostic
/// `Source` label keeps its `adapter:LiveProfiler` provenance, since the live-profiler
/// adapter is still what captured the evidence and invokes this.)
[<RequireQualifiedAccess>]
module ProfileDerivation =

    /// Per the chapter B.3 slice 3 design rationale (DECISIONS
    /// 2026-05-19 (slice B.3.3)) + chapter B.4.2 retirement: the
    /// per-attribute HasDuplicates evidence flows from the
    /// `EvidenceCache` row-stream substrate via
    /// `Cache.deriveAttributeRealities`; the projection is the
    /// canonical fill path for the `UniqueCandidates` axis.
    let private projectUniqueCandidates
        (realities: AttributeReality list)
        : UniqueCandidateProfile list =
        realities
        |> List.map (fun r ->
            { AttributeKey = r.AttributeKey
              HasDuplicate = r.HasDuplicates
              ProbeStatus  = ProbeStatus.noProbeRun })


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
                    // Max-observed STORAGE length: the pure-F# `MAX(LEN/DATALENGTH)`
                    // over the sampled cells (derive-pure from the row-stream the
                    // cache already holds — no extra SQL probe). `None` when the
                    // column carries no length-bearing value (numeric/date/all-NULL),
                    // so a non-text attribute never asserts a spurious length.
                    let maxObservedLength =
                        match Map.tryFind attr.SsKey cached.ColumnsByKey with
                        | None     -> None
                        | Some col ->
                            col.Values
                            |> Array.choose CachedValue.observedLength
                            |> fun lengths ->
                                if Array.isEmpty lengths then None
                                else Some (Array.max lengths)
                    match
                        ColumnProfile.create
                            attr.SsKey cached.RowCount nullCount statusWithSample
                    with
                    | Ok p    ->
                        match maxObservedLength with
                        | Some len -> Some (ColumnProfile.withMaxObservedLength len p)
                        | None     -> Some p
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
                        // F15 (audit 2026-06-17) — single source of truth: the
                        // statistical sample-size floor is owned by
                        // `NumericDistribution.sampleSizeFloor` (Profile.fs), not a
                        // re-inlined `5L`.
                        if sampleSize < NumericDistribution.sampleSizeFloor then None
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
                                    (Statistics.percentileCont sorted 0.25M)
                                    (Statistics.percentileCont sorted 0.50M)
                                    (Statistics.percentileCont sorted 0.75M)
                                    (Statistics.percentileCont sorted 0.95M)
                                    (Statistics.percentileCont sorted 0.99M)
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
            not (IndexUniqueness.isUnique index.Uniqueness) && List.length index.Columns >= 2
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
    /// PK targets only — composite-PK targets resolve to
    /// `ProbeStatus.ambiguous` (the `| _ -> ambiguous` branch
    /// below). Per `DECISIONS 2026-05-19 (slice B.4.3.composite-
    /// pk-fk)`, composite primary keys are not an OS use case
    /// the operator has encountered; the degenerate case stays
    /// resolved out-of-scope. Mirrors slice 1's SQL LEFT JOIN
    /// orphan-count probe.
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
                                  IsNoCheck    = not (Reference.isConstraintTrusted reference)
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
    /// SQL probe shape but runs from cache (no SQL). Single-
    /// column PK targets only — composite-PK targets produce
    /// no DiagnosticEntry (the `| _ -> None` branch at the end
    /// of the inner match). Same operator-confirmed scope as
    /// `deriveForeignKeyRealitiesWith` per `DECISIONS
    /// 2026-05-19 (slice B.4.3.composite-pk-fk)`.
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
                                          "sourceColumn", ColumnRealization.columnNameText srcAttr.Column
                                          "targetColumn", ColumnRealization.columnNameText tgtPkAttr.Column ]
                                    let perSample =
                                        sampled
                                        |> Array.mapi (fun i v ->
                                            System.String.Concat("sample.", string i), v)  // LINT-ALLOW: terminal structured-metadata key; segments are typed (literal prefix + integer sample index); BCL primitive at the diagnostic-metadata boundary
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
                                          Metadata = metadata
                                          SuggestedConfig = None }
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

    // -----------------------------------------------------------------
    // Slice B.3.8 — FK correlation triplet (Faker foundation).
    //   - deriveForeignKeyCardinalities: per-Reference fan-out
    //     (distribution of child-count-per-parent)
    //   - deriveForeignKeySelectivities: per-Reference clumping
    //     (value-frequency over source FK column)
    //   - deriveMultiFkJointDistributions: per-Kind joint
    //     distribution across the kind's FK columns (≥2)
    // -----------------------------------------------------------------

    let private isStaticKindForCorrelation (k: Kind) : bool =
        k.Modality
        |> List.exists (function
            | Static _ -> true
            | _ -> false)

    /// Derive per-Reference fan-out cardinality. Group source FK
    /// values by their target-PK value; per-parent child count
    /// = group size. Summarize the count distribution via
    /// `NumericDistribution.create` + `withMoments`.
    let deriveForeignKeyCardinalities
        (cache: EvidenceCache)
        (catalog: Catalog)
        : ForeignKeyCardinality list =
        use _ = Bench.scope "profile.cache.deriveForeignKeyCardinalities"
        catalog
        |> Catalog.allKinds
        |> List.collect (fun srcKind ->
            if isStaticKindForCorrelation srcKind then []
            else
                srcKind.References
                |> List.choose (fun reference ->
                    let srcColumnOpt =
                        EvidenceCache.tryFindColumn srcKind.SsKey reference.SourceAttribute cache
                    match srcColumnOpt with
                    | None -> None
                    | Some sCol ->
                        let nonNullKeys =
                            sCol.Values |> Array.choose cacheValueKey
                        if nonNullKeys.Length < 5 then None
                        else
                            let countsPerParent =
                                nonNullKeys
                                |> Array.groupBy id
                                |> Array.map (fun (_, occ) -> decimal occ.Length)
                            if countsPerParent.Length < 5 then None
                            else
                                let sorted = countsPerParent |> Array.sort
                                let min_ = sorted.[0]
                                let max_ = sorted.[sorted.Length - 1]
                                let mean =
                                    (countsPerParent |> Array.sum) / decimal countsPerParent.Length
                                let variance =
                                    countsPerParent
                                    |> Array.sumBy (fun v ->
                                        let d = v - mean
                                        d * d)
                                    |> fun s -> s / decimal countsPerParent.Length
                                let stdDev = decimal (sqrt (float variance))
                                let sampleSize = int64 countsPerParent.Length
                                let probeStatus = ProbeStatus.observed sampleSize
                                let baseResult =
                                    NumericDistribution.create
                                        reference.SsKey
                                        min_
                                        (Statistics.percentileCont sorted 0.25M)
                                        (Statistics.percentileCont sorted 0.50M)
                                        (Statistics.percentileCont sorted 0.75M)
                                        (Statistics.percentileCont sorted 0.95M)
                                        (Statistics.percentileCont sorted 0.99M)
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
                                | Ok dist -> Some (ForeignKeyCardinality.create reference.SsKey dist)
                                | Error _ ->
                                    match baseResult with
                                    | Ok dist -> Some (ForeignKeyCardinality.create reference.SsKey dist)
                                    | Error _ -> None))

    [<Literal>]
    let private defaultFkSelectivityVocabularyLimit = 50

    /// Derive per-Reference selectivity. Single-pass Dictionary
    /// frequency tally over source FK column values. Truncate at
    /// 50 by default (matches categorical limit).
    let deriveForeignKeySelectivities
        (cache: EvidenceCache)
        (catalog: Catalog)
        : ForeignKeySelectivity list =
        use _ = Bench.scope "profile.cache.deriveForeignKeySelectivities"
        catalog
        |> Catalog.allKinds
        |> List.collect (fun srcKind ->
            if isStaticKindForCorrelation srcKind then []
            else
                srcKind.References
                |> List.choose (fun reference ->
                    let srcColumnOpt =
                        EvidenceCache.tryFindColumn srcKind.SsKey reference.SourceAttribute cache
                    match srcColumnOpt with
                    | None -> None
                    | Some sCol ->
                        let freq = System.Collections.Generic.Dictionary<string, int64>()
                        for v in sCol.Values do
                            match cacheValueKey v with
                            | Some k ->
                                match freq.TryGetValue k with
                                | true, c  -> freq.[k] <- c + 1L
                                | false, _ -> freq.[k] <- 1L
                            | None -> ()
                        if freq.Count = 0 then None
                        else
                            let distinctCount = int64 freq.Count
                            let entries =
                                freq
                                |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                                |> Seq.toArray
                            let sorted =
                                entries
                                |> Array.sortWith (fun (vA, cA) (vB, cB) ->
                                    let cmpCount = compare cB cA
                                    if cmpCount <> 0 then cmpCount
                                    else compare vA vB)
                            let truncated =
                                if sorted.Length > defaultFkSelectivityVocabularyLimit then
                                    Array.sub sorted 0 defaultFkSelectivityVocabularyLimit
                                else sorted
                            let isTruncated =
                                distinctCount > int64 defaultFkSelectivityVocabularyLimit
                            let probeStatus = ProbeStatus.observed distinctCount
                            match
                                ForeignKeySelectivity.create
                                    reference.SsKey
                                    (List.ofArray truncated)
                                    distinctCount
                                    isTruncated
                                    probeStatus
                            with
                            | Ok s -> Some s
                            | Error _ -> None))

    [<Literal>]
    let private defaultJointVocabularyLimit = 100

    /// Derive per-Kind joint distribution across the kind's FK
    /// columns (≥2 References required). For each kind, project
    /// per-row tuples of source FK values; group by tuple key;
    /// truncate to top-N. Single emitted joint per kind covering
    /// ALL its FK columns; per-pair / per-triple joints land if
    /// a consumer surfaces (per "IR grows under evidence").
    let deriveMultiFkJointDistributions
        (cache: EvidenceCache)
        (catalog: Catalog)
        : JointDistribution list =
        use _ = Bench.scope "profile.cache.deriveMultiFkJointDistributions"
        catalog
        |> Catalog.allKinds
        |> List.choose (fun srcKind ->
            if isStaticKindForCorrelation srcKind then None
            else
                let fkAttributeKeys =
                    srcKind.References
                    |> List.map (fun r -> r.SourceAttribute)
                if List.length fkAttributeKeys < 2 then None
                else
                    let resolved =
                        fkAttributeKeys
                        |> List.map (fun key ->
                            EvidenceCache.tryFindColumn srcKind.SsKey key cache)
                    if resolved |> List.exists Option.isNone then None
                    else
                        let columns = resolved |> List.choose id
                        match projectTupleKeys columns with
                        | None -> None
                        | Some tuples ->
                            let freq = System.Collections.Generic.Dictionary<string, int64>()
                            for t in tuples do
                                match freq.TryGetValue t with
                                | true, c  -> freq.[t] <- c + 1L
                                | false, _ -> freq.[t] <- 1L
                            if freq.Count = 0 then None
                            else
                                let distinctCount = int64 freq.Count
                                let entries =
                                    freq
                                    |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                                    |> Seq.toArray
                                let sorted =
                                    entries
                                    |> Array.sortWith (fun (vA, cA) (vB, cB) ->
                                        let cmpCount = compare cB cA
                                        if cmpCount <> 0 then cmpCount
                                        else compare vA vB)
                                let truncated =
                                    if sorted.Length > defaultJointVocabularyLimit then
                                        Array.sub sorted 0 defaultJointVocabularyLimit
                                    else sorted
                                let isTruncated =
                                    distinctCount > int64 defaultJointVocabularyLimit
                                let probeStatus = ProbeStatus.observed distinctCount
                                match
                                    JointDistribution.create
                                        srcKind.SsKey
                                        fkAttributeKeys
                                        (List.ofArray truncated)
                                        distinctCount
                                        isTruncated
                                        probeStatus
                                with
                                | Ok j -> Some j
                                | Error _ -> None)

    /// Attach Profile evidence by deriving from an in-memory cache.
    /// Synchronous; pure F#. Composes EVERY cache-derived Profile
    /// axis (AttributeRealities, Columns, UniqueCandidates,
    /// CompositeUniqueCandidates, FK realities + cardinalities +
    /// selectivities + joint distributions, numeric + categorical
    /// distributions) from the `EvidenceCache` substrate without
    /// further SQL round-trips. Per chapter B.3 slice 6b: the
    /// architectural pivot was completing the cache-driven derivation;
    /// per chapter B.4.2: the predecessor SQL captures retired.
    let attachFromCache
        (cache: EvidenceCache)
        (catalog: Catalog)
        (profile: Profile)
        : Profile =
        use _ = Bench.scope "profile.cache.attachFromCache"
        // Pre-build FK target PK sets ONCE; share across the two FK
        // derivations. Slice 6b Big-O optimization #2: eliminates
        // duplicate Set construction (per Reference + per pass).
        let fkTargetIndex = buildForeignKeyTargetIndex cache catalog
        let realities = deriveAttributeRealities cache catalog
        let columns = deriveColumnProfiles cache catalog
        let numericDists = deriveNumericDistributions cache catalog
        let categoricalDists = deriveCategoricalDistributions cache catalog
        let composites = deriveCompositeUniqueCandidates cache catalog
        let foreignKeys = deriveForeignKeyRealitiesWith fkTargetIndex cache catalog
        let fkCardinalities = deriveForeignKeyCardinalities cache catalog
        let fkSelectivities = deriveForeignKeySelectivities cache catalog
        let jointDists = deriveMultiFkJointDistributions cache catalog
        let distributions =
            (numericDists |> List.map AttributeDistribution.Numeric)
            @ (categoricalDists |> List.map AttributeDistribution.Categorical)
        { profile with
            AttributeRealities         = realities
            Columns                    = columns
            UniqueCandidates           = projectUniqueCandidates realities
            CompositeUniqueCandidates  = composites
            ForeignKeys                = foreignKeys
            Distributions              = distributions
            ForeignKeyCardinalities    = fkCardinalities
            ForeignKeySelectivities    = fkSelectivities
            JointDistributions         = jointDists }

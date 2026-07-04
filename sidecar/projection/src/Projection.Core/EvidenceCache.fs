namespace Projection.Core

// LINT-ALLOW-FILE-MUTATION: the per-column null-count fold (`let mutable n = 0L` / `n <- n + 1L`
//   in `cachedKindFromPerColumn`) is a tight local accumulator loop over an already-materialized
//   `perColumn` array; a local mutable counter is the idiomatic, allocation-free realization —
//   no module-level or cross-call mutable state is introduced.

open System

/// **EvidenceCache** — the in-memory typed-row substrate of deployed reality.
///
/// V2's profiler pulls each non-static kind's rows once into this cache (one
/// `SELECT *` + one `COUNT_BIG` aggregate + one `INFORMATION_SCHEMA` reflection per
/// kind), then derives ALL Profile evidence — column profiles, attribute realities,
/// distributions, FK orphans, composite uniqueness, cardinalities — in pure F# from
/// the cache, with no further SQL. That is the "discover-once at the boundary,
/// derive-pure" pattern: the SQL probe is the only I/O; everything downstream is a
/// total function of the cache.
///
/// **Recon #5 — relocated to Core (was `Projection.Adapters.Sql`).** The cache is a
/// `DataIntent` surface (pure observation of deployed reality, the `Ingest` half of
/// the adjunction); its derivations are pure. Nothing here touches SQL — `ofReaderValue`
/// projects an already-boxed CLR `obj`, not a reader — so the substrate and its
/// derivation suite belong in the pure core. The SQL capture that FILLS the cache
/// (`LiveProfiler`) and the operator-supplied capture options (`SqlProfilerOptions`)
/// stay at the adapter boundary, where the I/O actually lives.
///
/// **Column-oriented storage.** Single-column aggregates (Min/Max/Mean, GROUP BY,
/// duplicate detection) iterate one column's values; row-oriented would force a
/// projection per row.

/// Typed value cell in the cache. Each variant covers a SQL Server column type
/// family; `NullValue` represents an observed NULL distinct from "value not yet read."
type CachedValue =
    | IntValue     of int64
    | DecimalValue of decimal
    | StringValue  of string
    | DateValue    of DateTimeOffset
    | BinaryValue  of byte array
    | NullValue


[<RequireQualifiedAccess>]
module CachedValue =

    /// Project a boxed CLR column value (as a SQL reader returns it via
    /// `reader.GetValue`) into a `CachedValue`. Null check first, then type-dispatch.
    /// Unknown types fall back to `StringValue (ToString())` so the cache never throws
    /// at extraction time. Pure: takes the already-boxed `obj`, never a reader.
    let ofReaderValue (raw: obj) : CachedValue =
        match raw with
        | :? DBNull                  -> NullValue
        | :? int as i                -> IntValue (int64 i)
        | :? int64 as i              -> IntValue i
        | :? int16 as i              -> IntValue (int64 i)
        | :? byte as i               -> IntValue (int64 i)
        | :? decimal as d            -> DecimalValue d
        | :? float as f              -> DecimalValue (decimal f)
        | :? float32 as f            -> DecimalValue (decimal f)
        | :? string as s             -> StringValue s
        | :? DateTimeOffset as dto   -> DateValue dto
        | :? DateTime as dt          ->
            DateValue (DateTimeOffset(dt, TimeSpan.Zero))
        | :? (byte array) as b       -> BinaryValue b
        // NM-19 — SQL `bit` arrives as a CLR bool; without this arm it fell
        // through to `StringValue "True"/"False"` and profiled as 2-value text
        // (OutSystems is bit-heavy). Project 0/1 as the sibling
        // `ReadSide.formatRawValue` does, so duplicate/distinct evidence is keyed
        // as the integer it is.
        | :? bool as flag            -> IntValue (if flag then 1L else 0L)
        | other ->
            match other.ToString() with
            | null -> NullValue
            | s    -> StringValue s

    /// Project a V2 raw-form cell (the `RawValueCodec` contract — the
    /// string shape `ReadSide.formatRawValue` emits and `StaticRow`
    /// carries) into the SAME `CachedValue` that `ofReaderValue` yields
    /// for the source column value. This equivalence table is the law
    /// the single-scan evidence path stands on (the data lanes paid for
    /// the stream; the evidence derives from it):
    ///
    ///   ""       → NullValue                    (the raw NULL sentinel)
    ///   Integer  → IntValue (invariant int64 parse — int/bigint/smallint/tinyint)
    ///   Boolean  → IntValue 0/1                 (NM-19: bit profiles as int)
    ///   Decimal  → DecimalValue (invariant parse; scale survives because
    ///              the raw form is the value's invariant ToString)
    ///   DateTime → DateValue (exact `DateTimeFormat` parse; offset Zero —
    ///              mirrors `ofReaderValue`'s DateTime arm)
    ///   Date     → DateValue (exact `DateFormat` parse; offset Zero)
    ///   Time     → StringValue raw              (a reader TimeSpan falls to
    ///              `ToString()` = the "c" format = the raw form)
    ///   Guid     → StringValue raw              (a reader Guid falls to
    ///              `ToString()` "D" = `formatGuid`'s raw form)
    ///   Text     → StringValue raw
    ///   Binary   → BinaryValue (FromHexString)
    let ofRaw (typ: PrimitiveType) (raw: string) : CachedValue =
        if raw = "" then NullValue
        else
            let inv = System.Globalization.CultureInfo.InvariantCulture
            match typ with
            | Integer  -> IntValue (System.Int64.Parse(raw, inv))
            | Boolean  -> IntValue (if RawValueCodec.parseBoolean raw then 1L else 0L)
            | Decimal  -> DecimalValue (System.Decimal.Parse(raw, inv))
            | DateTime ->
                let dt = System.DateTime.ParseExact(raw, RawValueCodec.DateTimeFormat, inv)
                DateValue (DateTimeOffset(dt, TimeSpan.Zero))
            | Date ->
                let dt = System.DateTime.ParseExact(raw, RawValueCodec.DateFormat, inv)
                DateValue (DateTimeOffset(dt, TimeSpan.Zero))
            | Time     -> StringValue raw
            | Guid     -> StringValue raw
            | Text     -> StringValue raw
            | Binary   -> BinaryValue (System.Convert.FromHexString raw)

    /// True iff the value is a NullValue. Pure predicate; consumers
    /// pattern-match without re-walking the DU.
    let isNull (v: CachedValue) : bool =
        match v with
        | NullValue -> true
        | _ -> false

    /// Project to `int64 option` for numeric aggregates. Returns
    /// `None` for NullValue and for non-numeric variants (the
    /// caller — numeric aggregation — applies this to columns it
    /// knows are numeric per Attribute.Type, so the non-numeric
    /// case shouldn't fire in practice).
    let tryInt (v: CachedValue) : int64 option =
        match v with
        | IntValue i     -> Some i
        | DecimalValue d -> Some (int64 d)
        | _              -> None

    /// Project to `decimal option` for decimal aggregates.
    let tryDecimal (v: CachedValue) : decimal option =
        match v with
        | IntValue i     -> Some (decimal i)
        | DecimalValue d -> Some d
        | _              -> None

    /// Project to `string option` for categorical aggregates.
    let tryString (v: CachedValue) : string option =
        match v with
        | StringValue s -> Some s
        | _             -> None

    /// The observed STORAGE length of one cell — the pure-F# twin of SQL's
    /// `LEN` (text) / `DATALENGTH` (binary), derived from the sampled value
    /// rather than a separate SQL probe (the discover-once / derive-pure
    /// pattern). `StringValue` → character length; `BinaryValue` → byte
    /// length; every other variant (numeric / date / NULL) carries no
    /// length axis → `None`. Feeds the `MaxObservedLength` column-profile
    /// axis, which the fidelity report's "Length / type overflow" category
    /// compares against the declared `Attribute.Length`.
    let observedLength (v: CachedValue) : int option =
        match v with
        | StringValue s -> Some s.Length
        | BinaryValue b -> Some b.Length
        | _             -> None


/// One column's sampled cell-values plus metadata. Column-oriented
/// for cheap iteration over single-column aggregates.
type CachedColumn = {
    AttributeKey         : SsKey
    /// `true` iff INFORMATION_SCHEMA.COLUMNS reflects the column as
    /// NULL-allowed. Distinct from observation (`Values` contains
    /// NULLs OR not) — reflection captures the schema declaration.
    IsNullableInDatabase : bool
    /// Sampled cell values; positional alignment across all columns
    /// of the owning `CachedKind` (Values.[i] across columns
    /// represents row i).
    Values               : CachedValue array
}


/// All sampled evidence for one Kind. RowCount is the EXACT (non-
/// sampled) row count derived from a SQL aggregate; `Values.Length`
/// may be ≤ RowCount when sampling caps fire. NullCounts is the
/// EXACT per-attribute null count from the same aggregate query
/// (cheaper than scanning sampled values in F#).
type CachedKind = {
    KindKey    : SsKey
    /// Exact deployed row count via `COUNT_BIG(*)` aggregate; not
    /// derived from `Values`.
    RowCount   : int64
    /// Per-attribute exact null count via `COUNT_BIG(CASE WHEN col
    /// IS NULL THEN 1 END)` aggregate. Indexed by attribute SsKey.
    NullCounts : Map<SsKey, int64>
    /// Column-oriented sampled values; one entry per attribute (in
    /// catalog order). Full-scan default ships `Columns.[i].Values.Length
    /// = RowCount` for all i; sampling caps relax that.
    Columns    : CachedColumn list
    /// Pre-indexed column lookup by attribute SsKey. Built once at
    /// `discoverKind` time so derivation consumers can do O(log C)
    /// column lookups instead of O(C) `List.tryFind` scans (slice 6b
    /// Big-O audit). Discipline: ColumnsByKey MUST correspond exactly
    /// to Columns; constructed-correctly by the discovery primitive.
    ColumnsByKey : Map<SsKey, CachedColumn>
}


/// The evidence cache for one catalog. Keyed by kind SsKey for
/// O(log n) lookup during cross-table derivations (FK orphans
/// require both source and target kinds).
type EvidenceCache = {
    Kinds : Map<SsKey, CachedKind>
}


[<RequireQualifiedAccess>]
module EvidenceCache =

    /// Empty cache. Unit of the implicit "cache monoid" (union by
    /// kind-key; left-biased for duplicate keys).
    let empty : EvidenceCache = { Kinds = Map.empty }

    /// Look up a cached kind by SsKey. Returns `None` when the
    /// catalog declared the kind but discovery skipped it (e.g.,
    /// static kinds; sampling-policy exclusions).
    let tryFindKind (kindKey: SsKey) (cache: EvidenceCache) : CachedKind option =
        Map.tryFind kindKey cache.Kinds

    /// **Single-scan derivation** — build one kind's `CachedKind` from
    /// ALREADY-HYDRATED rows, with ZERO further per-kind SQL: the data
    /// lanes paid for the row stream; the evidence derives from it.
    /// Pure — the `Ingest` half already happened; this is a total
    /// function of (kind, nullability reflection slice, rows).
    ///
    /// Exactness contract: equivalence with the live 3-query discovery
    /// holds ONLY for a FULLY hydrated kind — `RowCount` and `NullCounts`
    /// are exact because the rows are all of them (the live path's
    /// aggregate query is exact even under sampling, so a sampled kind
    /// must keep live discovery; callers gate on full hydration) — and
    /// MODULO the IR's universal NULL sentinel (NM-18): a stored empty
    /// Text cell arrives in raw form as `""` and derives as `NullValue`,
    /// where the live reader observes `StringValue ""` at the source.
    /// That is not a new erasure — it is the named, witnessed
    /// `Tolerance.EmptyTextNormalizedToNull` surfacing on the evidence
    /// plane: derived evidence describes the data as PUBLISHED (the data
    /// lane emits NULL for both), live evidence the source as stored.
    /// Cell projection rides `CachedValue.ofRaw`'s equivalence table; per-row
    /// order is the reader's PK order on both paths, so `Values` arrays
    /// align positionally for single-column-PK kinds (derivations are
    /// order-insensitive regardless). Attribute-less kinds yield `None`
    /// (the live discovery's own refusal shape).
    /// Shared assembly tail for the two derivation entries below: exact
    /// null counts, catalog-order columns, and the `ColumnsByKey`
    /// correspondence, from already-projected per-column cell lists. One
    /// implementation so the named-row and positional entries cannot
    /// drift.
    let private cachedKindFromPerColumn
        (nullability: Map<string, bool>)
        (kind: Kind)
        (rowCount: int64)
        (perColumn: System.Collections.Generic.List<CachedValue>[])
        : CachedKind =
        let attrs = kind.Attributes
        let nullCounts =
            attrs
            |> List.mapi (fun idx a ->
                let nulls =
                    let mutable n = 0L
                    for v in perColumn.[idx] do
                        if CachedValue.isNull v then n <- n + 1L
                    n
                a.SsKey, nulls)
            |> Map.ofList
        let columns =
            attrs
            |> List.mapi (fun idx a ->
                { AttributeKey         = a.SsKey
                  IsNullableInDatabase =
                      Map.tryFind (ColumnRealization.columnNameText a.Column) nullability
                      |> Option.defaultValue false
                  Values               = perColumn.[idx].ToArray() })
        let columnsByKey =
            columns |> List.map (fun c -> c.AttributeKey, c) |> Map.ofList
        { KindKey      = kind.SsKey
          RowCount     = rowCount
          NullCounts   = nullCounts
          Columns      = columns
          ColumnsByKey = columnsByKey }

    let cachedKindOfRows
        (nullability: Map<string, bool>)
        (kind: Kind)
        (rows: StaticRow list)
        : CachedKind option =
        if List.isEmpty kind.Attributes then None
        else
            let attrs = kind.Attributes
            let attrCount = List.length attrs
            let perColumn =
                Array.init attrCount (fun _ -> System.Collections.Generic.List<CachedValue>())
            for row in rows do
                attrs
                |> List.iteri (fun idx a ->
                    let raw =
                        Map.tryFind a.Name row.Values
                        |> Option.defaultValue ""
                    perColumn.[idx].Add (CachedValue.ofRaw a.Type raw))
            Some (cachedKindFromPerColumn nullability kind (int64 (List.length rows)) perColumn)

    /// Positional sibling of `cachedKindOfRows` — derive a kind's evidence
    /// straight from the in-flight quantum carrier, skipping the IR rebuild
    /// entirely (no per-row Map mint, no row-identity synthesis; evidence
    /// never needed either). `Cells.[i]` is positional against
    /// `Kind.rowBasis`, which IS `kind.Attributes` order, so the cell for
    /// attribute `i` reads by index; a short row's missing tail reads as
    /// the empty raw (the same total-row default the by-name lookup takes
    /// for an absent key). Same exactness contract and the same `""` ≡ NULL
    /// sentinel note as the named-row entry; equal to it over
    /// `StaticRow.ofQuantum`-materialized rows BY CONSTRUCTION of the
    /// shared assembly tail (pinned in the pure pool).
    let cachedKindOfQuanta
        (nullability: Map<string, bool>)
        (kind: Kind)
        (quanta: RowQuantum list)
        : CachedKind option =
        if List.isEmpty kind.Attributes then None
        else
            let attrs = kind.Attributes
            let attrCount = List.length attrs
            let perColumn =
                Array.init attrCount (fun _ -> System.Collections.Generic.List<CachedValue>())
            for q in quanta do
                attrs
                |> List.iteri (fun idx a ->
                    let raw = if idx < q.Cells.Length then q.Cells.[idx] else ""
                    perColumn.[idx].Add (CachedValue.ofRaw a.Type raw))
            Some (cachedKindFromPerColumn nullability kind (int64 (List.length quanta)) perColumn)

    /// The drained-reader sibling of the two entries above (PL-8/S03):
    /// derive a kind's evidence from per-column `CachedValue` lists an
    /// adapter already materialized off ONE row stream — the unsampled
    /// live discovery's single-scan arm, where the aggregate scan's exact
    /// RowCount/NullCounts are a pure function of the full stream. Same
    /// assembly tail (`cachedKindFromPerColumn`), so the three entries
    /// cannot drift. Attribute-less kinds yield `None` (the live
    /// discovery's own refusal shape).
    let cachedKindOfColumns
        (nullability: Map<string, bool>)
        (kind: Kind)
        (rowCount: int64)
        (perColumn: System.Collections.Generic.List<CachedValue>[])
        : CachedKind option =
        if List.isEmpty kind.Attributes then None
        else Some (cachedKindFromPerColumn nullability kind rowCount perColumn)

    /// Find a column within a kind by attribute SsKey. Returns
    /// `None` when the attribute exists in catalog but the cache
    /// has no column for it (e.g., column dropped from deployed
    /// schema; reflection silent). O(log C) via the precomputed
    /// `ColumnsByKey` index per slice 6b Big-O audit.
    let tryFindColumn
        (kindKey: SsKey)
        (attributeKey: SsKey)
        (cache: EvidenceCache)
        : CachedColumn option =
        cache
        |> tryFindKind kindKey
        |> Option.bind (fun k -> Map.tryFind attributeKey k.ColumnsByKey)

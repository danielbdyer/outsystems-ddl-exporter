namespace Projection.Core

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

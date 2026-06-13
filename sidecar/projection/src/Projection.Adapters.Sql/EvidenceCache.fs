namespace Projection.Adapters.Sql

open System
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core

/// **EvidenceCache** — chapter B.3 slice 6 architectural pivot. V2's
/// LiveProfiler historically issued per-attribute / per-Reference /
/// per-Index SQL probes accumulating to thousands of round-trips at
/// production scale (300 tables × ~10 attrs × multiple probes). The
/// no-overfetching premise: pull row data once per kind into a
/// typed in-memory cache, derive ALL Profile evidence (and FK
/// orphans, composite uniqueness, distributions) in pure F# from
/// the cache.
///
/// Per principal-PO direction (2026-05-19 mid-slice-6 refinement):
/// in-memory F# typed-row substrate; full-scan default;
/// FK probes fold into the cache (cross-table in-memory derivation).
///
/// **Per V2 algebraic discipline.** EvidenceCache is a DataIntent
/// surface (pure observation of deployed reality). The cache holds
/// raw values; derivation functions produce Profile axes WITHOUT
/// re-touching SQL. Per A35 stream-realization pattern: data flows
/// in via streaming reader; aggregates fold over the stream.
///
/// **Column-oriented storage.** Statistical aggregates over a
/// single column (Min, Max, Mean, GROUP BY) iterate that column's
/// values; row-oriented would force projections per row. Per V2's
/// PhysicalSchema.Rows precedent at chapter 3.1.
///
/// **Scope this slice (B.3.6 MVP):**
///   - Cache types: `CachedValue` / `CachedColumn` / `CachedKind` /
///     `EvidenceCache`
///   - `LiveProfiler.captureEvidenceCache` discovery primitive
///     (full-scan; one SELECT * per kind + one INFORMATION_SCHEMA
///     reflection + one COUNT_BIG aggregate per kind for exact
///     RowCount/NullCount)
///   - Pure-F# derivations: `Cache.deriveColumnProfiles` +
///     `Cache.deriveAttributeRealities` +
///     `Cache.deriveNumericDistributions`
///   - `LiveProfiler.attachFromCache` synchronous compose
///   - `LiveProfiler.attach` reworked to capture-then-derive
///
/// **Deferred to slice 6b (named follow-up):**
///   - `Cache.deriveCategoricalDistributions` (per-column GROUP BY
///     equivalent via List.groupBy)
///   - `Cache.deriveCompositeUniqueCandidates` (multi-column
///     GROUP BY via List.groupBy on tuple keys)
///   - `Cache.deriveForeignKeyRealities` (cross-table Set.difference
///     on source-FK / target-PK)
///   - `Cache.deriveForeignKeyOrphanSamples` (cross-table sample)
///   - Sampling policy + per-kind size caps (slice 7)
///   - String/Text per-type evidence variant (slice 7 or 8)

/// Typed value cell in the cache. Each variant covers a SQL Server
/// column type family; `NullValue` represents an observed NULL
/// distinct from "value not yet read." The optional inside each
/// non-null variant is structurally redundant with `NullValue` and
/// reserved for future-proofing (e.g., partial parses); slice 6
/// callers never construct `IntValue None`.
type CachedValue =
    | IntValue     of int64
    | DecimalValue of decimal
    | StringValue  of string
    | DateValue    of DateTimeOffset
    | BinaryValue  of byte array
    | NullValue


[<RequireQualifiedAccess>]
module CachedValue =

    /// Project a SQL reader column value into a CachedValue. Reader
    /// returns `obj`; null check first, then type-dispatch. Unknown
    /// types fall back to `StringValue (ToString())` so the cache
    /// never throws at extraction time.
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
    /// catalog order). Slice 6 ships full-scan default, so
    /// `Columns.[i].Values.Length = RowCount` for all i; slice 7
    /// adds sampling caps.
    Columns    : CachedColumn list
    /// Pre-indexed column lookup by attribute SsKey. Built once at
    /// `discoverKind` time so `Cache.derive*` consumers can do
    /// O(log C) column lookups instead of O(C) `List.tryFind` scans
    /// (slice 6b Big-O audit). Discipline: ColumnsByKey MUST
    /// correspond exactly to Columns; constructed-correctly by the
    /// discovery primitive.
    ColumnsByKey : Map<SsKey, CachedColumn>
}


/// The evidence cache for one catalog. Keyed by kind SsKey for
/// O(log n) lookup during cross-table derivations (FK orphans
/// require both source and target kinds).
type EvidenceCache = {
    Kinds : Map<SsKey, CachedKind>
}


/// Operator-supplied profile-capture options. Slice B.3.7 surfaces:
///   - `MaxRowsPerKind` — optional sampling cap. `None` = full-scan
///     (slice 6/6b default); `Some N` = `SELECT TOP (N)` capped at
///     extraction time. Sampling is **deterministic** when the kind
///     has a single-column PK (the cache stream uses `ORDER BY
///     <pk>` for repeatable extraction).
///   - `EnvironmentTag` — operator-supplied label for the
///     environment being profiled (dev / qa / uat / prod). Plumbed
///     through to `ProbeStatus.Outcome` if/when adapters need
///     environment-aware diagnostics; for slice 7's scope, used by
///     the multi-env orchestrator to label profiles before merge.
///
/// Per `DECISIONS 2026-05-18 (slice 5.4.δ.profiling)` — sampling
/// policy is operator intent, lives in the orchestrator/options,
/// not in Profile IR. `SqlProfilerOptions` is the orchestrator
/// surface that carries the operator's choice into the adapter.
type SqlProfilerOptions = {
    MaxRowsPerKind  : int option
    EnvironmentTag  : string option
}

[<RequireQualifiedAccess>]
module SqlProfilerOptions =

    /// Default options: full-scan (no sampling cap), no environment
    /// tag. Slice 6/6b shipped with this implicit shape; slice 7
    /// makes it explicit so operators can opt into sampling.
    let defaults : SqlProfilerOptions = {
        MaxRowsPerKind  = None
        EnvironmentTag  = None
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

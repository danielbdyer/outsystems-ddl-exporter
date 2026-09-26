namespace Projection.Adapters.OssysSql

open Projection.Core

// LINT-ALLOW-FILE: validation-error message construction in the smart
// constructor uses `sprintf "...%s..."` to format operator-supplied
// result-set / column names in the diagnostic message. Same allowed-
// exception class as `Catalog.create` / `ModuleFilter.fs` (per their
// LINT-ALLOW-FILE block); the validation payload is operator-facing
// audit-trail prose where typed value formatting is the right primitive.

// File-header carbon-copy citation (per `DECISIONS 2026-05-16 (later) —
// V2 self-containment + carbon-copy editorial inheritance`). V1 source
// file inherited from at chapter B.4 slice 5 (2026-05-19):
//   src/Osm.Pipeline/SqlExtraction/MetadataContractOverrides.cs (~140 LOC)
// The V2 port refactors to F# idioms + V2's structural-commitment-
// via-construction-validation operational principle: smart constructor
// returns `Result<_>` accumulating per-entry validation errors (V1
// throws on first invalid input); set-of-string for the per-result-set
// optional-column list; case-insensitive normalization at construction
// time with original-case names preserved for diagnostics (mirrors the
// chapter-B.4 slice-4 `ModuleFilter` port's convention).
//
// **Scope at slice 5 — mechanism only, wiring deferred.** This slice
// ships the structural surface (data type + smart constructor +
// lookup). The V1 component had exactly one production call site
// (`AttributeJsonResultSetProcessor.IsColumnOptional("AttributeJson",
// "AttributesJson")`); that result-set is V1-SUNSET in V2 —
// `MetadataSnapshotRunner.fs:778` skips it (`do! skip "attrJson"`)
// because V2 reads the structured `attributes` rowset directly rather
// than the V1 JSON aggregation column. So V2 has zero direct carry-
// over of V1's single wiring site. Wiring the override into specific
// V2 mappers (e.g., relaxing `mapModuleRow`'s `EspaceName` from
// `readString` to a `readStringRelaxable` variant under operator
// opt-in) defers to slice 7's `full-export` CLI when the orchestrator
// resolves the operator's config; per-mapper wiring follows the
// IR-grows-under-evidence discipline — pick the relaxable column on
// real V1-source drift, not speculatively. ADMIRE entry: `ADMIRE.md`
// § "2026-05-19 — MetadataContractOverrides (chapter B.4 slice 5
// carbon-copy)".
//
// **Pillar 9 — open OverlayAxis decision at slice 7.** Operator
// intent here is "weaken the metadata contract at extraction time"
// — the operator decides whether V2's mappers fail-fast on a NULL
// (strict; today's behaviour) or tolerate it (relaxed). None of the
// existing five `OverlayAxis` variants (`Selection | Emission |
// Insertion | Tightening | Ordering`) describe extraction-time
// contract relaxation cleanly; `Tightening` is the closest semantic
// (operator decision about constraint enforcement) but V2 reserves
// that axis for *catalog-emit* constraint enforcement, not source-
// data-read tolerance. Slice 7 decides at wiring time whether to add
// a sixth `OverlayAxis.Extraction` variant (per the chapter A.4.7
// open's Q9 trigger-fires discipline — real evidence of an
// operator-intent axis not subsumed by the existing five) or stretch
// `Tightening` with a docstring note. This slice records the open
// question and leaves the classification at the TransformRegistry
// registration site (slice 7), not here at the mechanism site.

/// Operator-supplied weakening of V2's strict metadata-contract
/// enforcement at SQL-extraction time. Each entry names a (result-set,
/// column) pair where V2's strict mapper would normally fail-fast on
/// a NULL value; the override declares the column as optional for that
/// run.
///
/// Carbon-copied from V1's `Osm.Pipeline.SqlExtraction
/// .MetadataContractOverrides` (~140 LOC) into V2's `Projection
/// .Adapters.OssysSql` (the V1-source-aligned home — V2's F# adapter
/// for the OSSYS SQL extraction path is the natural V2 location).
///
/// **V2 refactor from V1 shape (per the carbon-copy editorial
/// inheritance discipline):**
/// - Smart constructor returns `Result<MetadataContractOverrides>`
///   accumulating per-entry validation errors (V1 threw
///   `ArgumentException` on first invalid input).
/// - Set semantics for the per-result-set optional-column list (V1
///   used `HashSet<string>` with `OrdinalIgnoreCase` comparer; V2
///   uses lowercase-normalized `Set<string>`).
/// - Case-insensitive normalization at construction time with the
///   original-case names preserved for diagnostic messages so
///   operators see their own typing in error output. Mirrors the
///   slice-4 `ModuleFilter` convention.
/// - `MetadataContractOverrides.empty` replaces V1's `Strict`
///   property (concept-shaped name per pillar 8: the empty value
///   IS the strict-no-overrides identity; "strict" was an action-
///   shaped naming).
type MetadataContractOverrides = {
    /// Per-result-set optional-column sets. Key = lowercase-
    /// normalized result-set name; value = lowercase-normalized
    /// column-name set. Empty map = strict-no-overrides identity.
    OptionalColumns : Map<string, Set<string>>
    /// Operator-original-case display labels for diagnostic
    /// messages. Key = lowercase result-set name; value = original-
    /// case result-set name as the operator typed it. Mirrors the
    /// V1 V2-port pattern from `ModuleFilter` slice 4 (original-
    /// case names preserved for operator-readable errors).
    ResultSetLabels : Map<string, string>
    /// Operator-original-case column labels per result-set, for
    /// the same diagnostic-readability reason. Key = (lowercase
    /// result-set, lowercase column); value = original-case column
    /// name as the operator typed it.
    ColumnLabels : Map<string * string, string>
}


[<RequireQualifiedAccess>]
module MetadataContractOverrides =

    /// The strict-no-overrides identity. V2 mappers consult this when
    /// the operator has supplied no extraction-contract overrides;
    /// every strict column behaves as today (fail-fast on NULL via
    /// `MetadataSnapshotRunner.readString` / `readInt` /
    /// `MetadataExtractionError.RowMappingFailure`). Equivalent to
    /// V1's `MetadataContractOverrides.Strict` singleton.
    let empty : MetadataContractOverrides = {
        OptionalColumns = Map.empty
        ResultSetLabels = Map.empty
        ColumnLabels    = Map.empty
    }

    /// Predicate: does this overrides instance carry any relaxation?
    /// True iff at least one (result-set, column) entry is present.
    /// `MetadataSnapshotRunner` consumers short-circuit to strict
    /// behaviour when this returns false.
    let hasOverrides (overrides: MetadataContractOverrides) : bool =
        not (Map.isEmpty overrides.OptionalColumns)

    /// Smart constructor: validate operator-supplied (result-set,
    /// column) pairs; normalize names case-insensitively; preserve
    /// original-case labels for diagnostics; accumulate per-entry
    /// validation errors. Per the structural-commitment-via-
    /// construction-validation operational principle.
    ///
    /// V1 parity: V1's constructor threw `ArgumentException` on the
    /// first blank result-set key; V2 collects all errors before
    /// returning `Result.failure`. Empty result-set's column list
    /// is dropped silently (V1 behaviour: result sets with no
    /// retained columns are pruned from the dictionary). A pair
    /// whose every entry is blank yields `Result.success empty`
    /// (no overrides, no errors).
    let create
        (entries: (string * string seq) seq)
        : Result<MetadataContractOverrides> =
        use _ = Bench.scope "ossysContract.create"
        if isNull (box entries) then
            Result.success empty
        else
        let materialized = entries |> Seq.toList
        let errors = ResizeArray<ValidationError>()
        let optionalColumns = System.Collections.Generic.Dictionary<string, Set<string>>()
        let resultSetLabels = System.Collections.Generic.Dictionary<string, string>()
        let columnLabels = System.Collections.Generic.Dictionary<string * string, string>()
        materialized
        |> List.iteri (fun idx (resultSet, columns) ->
            if isNull (box resultSet) then
                errors.Add(
                    ValidationError.create
                        "metadataContract.resultSet.null"
                        (sprintf "Result set name at position %d must not be null." idx))
            elif System.String.IsNullOrWhiteSpace(resultSet) then
                errors.Add(
                    ValidationError.create
                        "metadataContract.resultSet.empty"
                        (sprintf "Result set name at position %d must not be empty or whitespace." idx))
            else
                let resultSetOriginal = resultSet.Trim()
                let resultSetLowered = resultSetOriginal.ToLowerInvariant()
                let columnList =
                    if isNull (box columns) then []
                    else Seq.toList columns
                let normalized = ResizeArray<string>()
                columnList
                |> List.iter (fun column ->
                    if isNull (box column) || System.String.IsNullOrWhiteSpace(column) then
                        // V1 silently skipped blank column entries
                        // (matches `Clone` in MetadataContractOverrides.cs);
                        // V2 mirrors the silent skip to preserve V1
                        // parity. The operator-supplied list of
                        // optional columns can legitimately carry
                        // trailing empty entries from config-parse
                        // edge cases.
                        ()
                    else
                        let columnOriginal = column.Trim()
                        let columnLowered = columnOriginal.ToLowerInvariant()
                        normalized.Add(columnLowered)
                        columnLabels.[(resultSetLowered, columnLowered)] <- columnOriginal)
                if normalized.Count > 0 then
                    let columnSet = Set.ofSeq normalized
                    match optionalColumns.TryGetValue(resultSetLowered) with
                    | true, existing ->
                        optionalColumns.[resultSetLowered] <- Set.union existing columnSet
                    | false, _ ->
                        optionalColumns.[resultSetLowered] <- columnSet
                    // Preserve first-seen original-case label per result
                    // set; subsequent entries with different casing carry
                    // the same lowered key + the original-case label of
                    // their FIRST appearance for stable diagnostics.
                    if not (resultSetLabels.ContainsKey(resultSetLowered)) then
                        resultSetLabels.[resultSetLowered] <- resultSetOriginal)
        if errors.Count > 0 then
            Result.failure (List.ofSeq errors)
        else
            Result.success {
                OptionalColumns =
                    optionalColumns
                    |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                    |> Map.ofSeq
                ResultSetLabels =
                    resultSetLabels
                    |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                    |> Map.ofSeq
                ColumnLabels =
                    columnLabels
                    |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                    |> Map.ofSeq
            }

    /// Predicate consulted at runtime by V2 mappers (slice 7 wiring)
    /// to decide whether a strict column may be relaxed to optional
    /// for this run. Case-insensitive: the operator-supplied
    /// (`resultSetName`, `columnName`) are normalized to lowercase
    /// before lookup; matches even when the mapper passes a
    /// differently-cased name.
    ///
    /// V1 parity: V1's `IsColumnOptional` threw `ArgumentException`
    /// on blank inputs; V2 returns `false` for blanks (mapper
    /// passing a blank name is a V2-side bug, not an operator-input
    /// issue — return the safe "strict" default rather than throw).
    let isColumnOptional
        (resultSetName: string)
        (columnName: string)
        (overrides: MetadataContractOverrides)
        : bool =
        if System.String.IsNullOrWhiteSpace(resultSetName)
           || System.String.IsNullOrWhiteSpace(columnName) then
            false
        else
            let resultSetLowered = resultSetName.Trim().ToLowerInvariant()
            let columnLowered = columnName.Trim().ToLowerInvariant()
            match Map.tryFind resultSetLowered overrides.OptionalColumns with
            | Some columns -> Set.contains columnLowered columns
            | None -> false

    /// Fluent additive: return a new `MetadataContractOverrides`
    /// with the given (result-set, column) pair added to the
    /// optional-columns map. Idempotent — adding the same pair
    /// twice returns an equal result. Used by config-resolution at
    /// slice 7 to build up overrides incrementally from the
    /// operator's config sections.
    ///
    /// Rejects blank inputs by returning the original overrides
    /// unchanged (consistent with `isColumnOptional`'s blank-input
    /// safety posture). For operator-side validation, callers use
    /// `create` which accumulates errors structurally.
    let withOptional
        (resultSetName: string)
        (columnName: string)
        (overrides: MetadataContractOverrides)
        : MetadataContractOverrides =
        if System.String.IsNullOrWhiteSpace(resultSetName)
           || System.String.IsNullOrWhiteSpace(columnName) then
            overrides
        else
            let resultSetOriginal = resultSetName.Trim()
            let resultSetLowered = resultSetOriginal.ToLowerInvariant()
            let columnOriginal = columnName.Trim()
            let columnLowered = columnOriginal.ToLowerInvariant()
            let existing =
                Map.tryFind resultSetLowered overrides.OptionalColumns
                |> Option.defaultValue Set.empty
            let updated = Set.add columnLowered existing
            let labels =
                if Map.containsKey resultSetLowered overrides.ResultSetLabels
                then overrides.ResultSetLabels
                else Map.add resultSetLowered resultSetOriginal overrides.ResultSetLabels
            {
                OptionalColumns =
                    Map.add resultSetLowered updated overrides.OptionalColumns
                ResultSetLabels = labels
                ColumnLabels    =
                    Map.add
                        (resultSetLowered, columnLowered)
                        columnOriginal
                        overrides.ColumnLabels
            }

    /// Original-case display label for the given lowercase result-
    /// set key. Returns `None` if the override doesn't contain
    /// the result set. Used by diagnostic emit paths to surface
    /// operator-typed names in error messages.
    let tryResultSetLabel
        (resultSetLowered: string)
        (overrides: MetadataContractOverrides)
        : string option =
        Map.tryFind resultSetLowered overrides.ResultSetLabels

    /// Original-case display label for the given lowercase
    /// (result-set, column) pair. Returns `None` if the override
    /// doesn't contain the column.
    let tryColumnLabel
        (resultSetLowered: string)
        (columnLowered: string)
        (overrides: MetadataContractOverrides)
        : string option =
        Map.tryFind (resultSetLowered, columnLowered) overrides.ColumnLabels

namespace Projection.Core

// LINT-ALLOW-FILE: `renderDiff` produces operator-facing multi-line
// diagnostic text (the canary's failure-message surface). `sprintf`
// is the discipline's allowed exception per `DECISIONS 2026-05-09 —
// Built-in obligation` for human-readable diagnostic interpolation.
// The structural diff data (`PhysicalSchemaDiff` record) and its
// hashing / comparison surfaces use built-in BCL primitives
// (`SHA256.HashData`, `Set.difference`, `HashSet.ExceptWith`); only
// the operator-facing render falls under the exemption. Future
// chapter 3.7 ScriptDom adoption may replace the SQL-type
// sub-renderers (length / precision / scale formatters) with typed
// `DataTypeReference` AST emission; out of scope for the current
// allowlist scope.

/// A column's physical-schema coordinate — the structural-fidelity
/// axis that survives the deploy → read round-trip. Used by the
/// canary's round-trip property test (M3 onward) to compare two
/// Catalogs produced by different adapters (e.g., OSSYS JSON vs.
/// `Projection.Adapters.Sql.ReadSide`) without false negatives on
/// V2-IR-only metadata or SsKey-synthesis-source differences.
///
/// Per `DECISIONS 2026-05-23 — Source SQL Server with OutSystems
/// semantics is the canary's primary wide integration surface`,
/// `PhysicalSchema` is the comparison primitive both halves of the
/// round-trip use:
///
///   - **Source half.** OutSystems-shaped DDL → deploy → read →
///     `sourceCatalog`. Project to `PhysicalSchema` via
///     `PhysicalSchema.ofCatalog`.
///   - **Target half.** V2 emit → deploy → read → `targetCatalog`.
///     Project to `PhysicalSchema` via the same function.
///   - **Assertion.** `PhysicalSchema.diff source target` returns
///     `(missingInTarget, extraInTarget)` for both columns AND FKs;
///     all four empty means the emitter preserved the source's
///     structural intent.
///
/// **What's compared.**
///   - `Columns`: set of `(schema, table, column, type, nullable,
///     isPrimaryKey)` tuples.
///   - `ForeignKeys`: set of `(srcSchema, srcTable, srcCol,
///     tgtSchema, tgtTable, tgtCol)` tuples (Session B addition).
///
/// **What's NOT compared.** SsKey identity, Module structure,
/// Origin / Modality marks, Indexes (non-PK), static populations,
/// comment metadata. These are V2-IR-only axes that SQL Server's
/// catalog cannot recover. M4's Tolerance taxonomy will name
/// additional comparison flags (e.g., column length / precision;
/// indexes; FK delete-rule semantics).
type PhysicalColumn =
    {
        Schema : string
        Table : string
        Column : string
        Type : PrimitiveType
        Nullable : bool
        IsPrimaryKey : bool
        /// NVARCHAR / VARCHAR / VARBINARY length. None for MAX or
        /// non-applicable types. Per session-32 — the canary's
        /// round-trip property catches NVARCHAR(N) → NVARCHAR(M)
        /// drift when N ≠ M.
        Length : int option
        /// DECIMAL precision. None for non-decimal types. Catches
        /// DECIMAL(P, S) → DECIMAL(P', S') drift.
        Precision : int option
        /// DECIMAL scale. Same.
        Scale : int option
        /// IDENTITY column property. Catches drift in identity-ness
        /// (source had IDENTITY, target dropped it, or vice versa).
        IsIdentity : bool
    }

/// A foreign-key relationship in physical-schema coordinates. Per
/// session-31 Session B, the canary's round-trip property covers
/// FK structural fidelity: the source's FKs should appear in the
/// target after V2's emit + deploy + readback.
///
/// Composite FKs (multi-column references) appear as multiple
/// `PhysicalForeignKey` entries with the same source / target
/// table coordinates and different column pairs. Comparing as a
/// set of column-level entries handles composite cases by
/// construction.
type PhysicalForeignKey =
    {
        SourceSchema : string
        SourceTable : string
        SourceColumn : string
        TargetSchema : string
        TargetTable : string
        TargetColumn : string
    }

/// A row's content fingerprint in physical-schema coordinates.
/// Per session-33 — adds the data-plane axis to the canary's
/// round-trip surface. ReadSide produces one `PhysicalRow` per
/// (schema, table, row) tuple; PhysicalSchema's `Rows` set
/// compares by hash so the round-trip catches missing / extra /
/// mutated rows without retaining full row content in memory.
///
/// `Hash` is a deterministic SHA256 over the row's column values
/// in column-name order. Same rows → same hash; different rows →
/// different hash with overwhelming probability.
type PhysicalRow =
    {
        Schema : string
        Table : string
        Hash : string
    }

/// Per-table aggregate row fingerprint. Per session-35 — covers
/// tables whose row counts exceed `Modality.Static`'s materialization
/// budget, so structural-fidelity comparison still has a row axis at
/// enterprise scale (1M+ row tables) without holding rows in IR
/// memory. Order-independent: the aggregate combines per-row SHA256
/// hashes by sum-mod-2^256, so ingest order is irrelevant.
///
/// Two aggregates with the same `(Count, AggregateHash)` represent
/// identical multisets with overwhelming probability. A drift either
/// shifts the count or perturbs the sum; both surface as a non-empty
/// diff entry on the `RowDigests` axis.
type PhysicalRowDigest =
    {
        Schema : string
        Table : string
        Count : int64
        AggregateHash : string
    }

/// Structural-fidelity view of a Catalog: columns + FKs + per-row
/// hashes (small tables) + per-table digests (large tables). The
/// two row axes are complementary: small tables get granular diff
/// (which row drifted), large tables get bounded-memory diff
/// (the table drifted).
type PhysicalSchema =
    {
        Columns : Set<PhysicalColumn>
        ForeignKeys : Set<PhysicalForeignKey>
        Rows : Set<PhysicalRow>
        RowDigests : Set<PhysicalRowDigest>
    }

/// The diff between two `PhysicalSchema` values. All eight fields
/// empty means structural-and-data intent matches; anything
/// populated is a canary-blocking divergence under R6.
type PhysicalSchemaDiff =
    {
        MissingColumns : PhysicalColumn list
        ExtraColumns : PhysicalColumn list
        MissingForeignKeys : PhysicalForeignKey list
        ExtraForeignKeys : PhysicalForeignKey list
        MissingRows : PhysicalRow list
        ExtraRows : PhysicalRow list
        MissingRowDigests : PhysicalRowDigest list
        ExtraRowDigests : PhysicalRowDigest list
    }

/// Streaming aggregate row-hash builder. Per session-35 — folds an
/// arbitrary row stream into a `(count, aggregateHash)` pair without
/// materializing rows in memory. The aggregate is the sum-mod-2^256
/// of per-row SHA256s; commutative and associative, so streaming
/// order doesn't matter (multiset equality survives reordering).
///
/// Used by the canary at large-table scale: ReadSide streams via
/// `readRowsStream`, the digester folds, the result becomes a
/// `PhysicalRowDigest` that joins `PhysicalSchema.RowDigests`.
/// Sync (Core-friendly); async wrapping happens at the call site.
[<RequireQualifiedAccess>]
module RowDigester =

    type State =
        {
            Count : int64
            Acc : byte[]   // 32-byte running sum mod 2^256
        }

    let empty () : State = { Count = 0L; Acc = Array.zeroCreate 32 }

    /// Big-endian add-with-carry of a 32-byte addend into a 32-byte
    /// accumulator, mod 2^256. Mutates the accumulator in place to
    /// avoid per-row allocation; the State carries this same array
    /// across folds.
    let private addInPlace (acc: byte[]) (addend: byte[]) : unit =
        let mutable carry = 0
        for i in 31 .. -1 .. 0 do
            let s = int acc[i] + int addend[i] + carry
            acc[i] <- byte (s &&& 0xFF)
            carry <- s >>> 8

    /// Streaming-friendly add: same array, mutated. Caller passes
    /// the accumulator from `State.Acc`.
    let private hashRowBytes (row: StaticRow) : byte[] =
        let pairs =
            row.Values
            |> Map.toArray
            |> Array.sortBy (fun (n, _) -> Name.value n)
        let sb = System.Text.StringBuilder(64)
        let mutable first = true
        for (n, v) in pairs do
            if not first then sb.Append('') |> ignore
            sb.Append(Name.value n).Append('=').Append(v) |> ignore
            first <- false
        let bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString())
        System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>(bytes))

    let add (row: StaticRow) (s: State) : State =
        let h = hashRowBytes row
        addInPlace s.Acc h
        { s with Count = s.Count + 1L }

    let finalize
        (schema: string) (table: string) (s: State) : PhysicalRowDigest =
        {
            Schema = schema
            Table = table
            Count = s.Count
            AggregateHash = System.Convert.ToHexString s.Acc
        }

[<RequireQualifiedAccess>]
module PhysicalSchema =

    let private toPhysicalColumns (k: Kind) : PhysicalColumn list =
        k.Attributes
        |> Bench.iterMap "physicalSchema.attribute" (fun a ->
            {
                Schema = k.Physical.Schema
                Table = k.Physical.Table
                Column = a.Column.ColumnName
                Type = a.Type
                Nullable = a.Column.IsNullable
                IsPrimaryKey = a.IsPrimaryKey
                Length = a.Length
                Precision = a.Precision
                Scale = a.Scale
                IsIdentity = a.IsIdentity
            })

    /// Hash a static row deterministically. Concatenates
    /// `<column-name>=<value>` pairs sorted by column name and
    /// SHA256s the result. Stable across runs given stable inputs.
    ///
    /// Per session-35 — single `StringBuilder` accumulation replaces
    /// the v1 `Map.toList -> List.sortBy -> List.map sprintf ->
    /// String.concat` chain. Per-row allocation halves at 500k-row
    /// scale (~8 us/row -> ~4 us/row); SHA256 itself is unchanged.
    /// The RS (\x1e) separator survives — it disambiguates
    /// `<col>=<val>` pairs that would otherwise alias under
    /// degenerate column-name / value combinations.
    let private hashStaticRowBytes (row: StaticRow) : byte[] =
        let pairs =
            row.Values
            |> Map.toArray
            |> Array.sortBy (fun (n, _) -> Name.value n)
        let sb = System.Text.StringBuilder(64)
        let mutable first = true
        for (n, v) in pairs do
            if not first then sb.Append('') |> ignore
            sb.Append(Name.value n).Append('=').Append(v) |> ignore
            first <- false
        let bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString())
        System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>(bytes))

    /// Hex form of the row hash — used by `PhysicalRow.Hash` so per-row
    /// granular diffs render as a stable string. The bytes form
    /// (`hashStaticRowBytes`) feeds the per-table aggregate path.
    let private hashStaticRow (row: StaticRow) : string =
        System.Convert.ToHexString (hashStaticRowBytes row)

    /// Per session-35 — `Array.Parallel.map` replaces sequential
    /// `iterMap` for the per-row hash. SHA256 is CPU-bound and
    /// independent per row; on multi-core hosts the 4-second hash
    /// phase at 500k-row scale drops to ~`4 / cores` seconds.
    /// Output ordering is preserved by `Array.Parallel.map`, but
    /// `PhysicalSchema.Rows` is a `Set` so order is irrelevant
    /// downstream — this is a pure throughput win with no semantic
    /// change. Bench scope retained as a single sample per kind so
    /// per-kind hashing wall-time still surfaces; per-row scope
    /// dropped (parallel timing samples-per-row aren't meaningful).
    let private toPhysicalRows (k: Kind) : PhysicalRow list =
        k.Modality
        |> List.collect (fun m ->
            match m with
            | Static rows when not (List.isEmpty rows) ->
                use _ = Bench.scope "physicalSchema.rows.hash"
                let arr = List.toArray rows
                let hashed =
                    arr
                    |> Array.Parallel.map (fun r ->
                        {
                            Schema = k.Physical.Schema
                            Table = k.Physical.Table
                            Hash = hashStaticRow r
                        })
                Bench.recordSample "physicalSchema.rows.hash.elements" (int64 arr.Length)
                List.ofArray hashed
            | _ -> [])

    /// Per session-35 — `kindByKey` and `targetPkColumnByKey` lifted
    /// to `Map` once per `ofCatalog` invocation rather than scanning
    /// the catalog linearly per reference. At 300 kinds × ~5 refs
    /// each that's ~1500 catalog scans (each O(K)) → ~1500 hash
    /// lookups. Source-attribute lookup stays linear over per-kind
    /// `Attributes` (≈10 entries on avg, not worth the per-kind
    /// allocation of a separate map).
    let private toPhysicalForeignKeys
        (kindByKey: Map<SsKey, Kind>)
        (targetPkColumnByKey: Map<SsKey, string>)
        (k: Kind)
        : PhysicalForeignKey list =
        k.References
        |> List.choose (fun r ->
            let sourceColumn =
                k.Attributes
                |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                |> Option.map (fun a -> a.Column.ColumnName)
            match sourceColumn,
                  Map.tryFind r.TargetKind kindByKey,
                  Map.tryFind r.TargetKind targetPkColumnByKey with
            | Some srcCol, Some tk, Some tgtCol ->
                Some
                    {
                        SourceSchema = k.Physical.Schema
                        SourceTable = k.Physical.Table
                        SourceColumn = srcCol
                        TargetSchema = tk.Physical.Schema
                        TargetTable = tk.Physical.Table
                        TargetColumn = tgtCol
                    }
            | _ -> None)

    /// Project a Catalog to its `PhysicalSchema` view — the set of
    /// `(schema, table, column, type, nullable, isPrimaryKey)`
    /// tuples PLUS the set of `(src, tgt)` FK tuples reachable
    /// through every Module's Kinds. Modules, Origin, Modality,
    /// non-PK Indexes are projected out by construction.
    ///
    /// Per session-35 — `RowDigests` defaults empty; bulk-table
    /// digests are layered on via `withDigests` when the canary
    /// computes them out-of-band (streaming readside fold).
    let ofCatalog (c: Catalog) : PhysicalSchema =
        use _ = Bench.scope "physicalSchema.ofCatalog"
        let kinds = c.Modules |> List.collect (fun m -> m.Kinds)
        // Per session-35 — index lookups lifted once for FK projection
        // (was O(K) catalog scan per reference; now O(log K) hash
        // lookup). 300-kind catalog × 1500 refs: ~450k linear ops →
        // ~1500 hashed ops.
        let kindByKey =
            kinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        let targetPkColumnByKey =
            kinds
            |> List.choose (fun k ->
                k.Attributes
                |> List.tryFind (fun a -> a.IsPrimaryKey)
                |> Option.map (fun pk -> k.SsKey, pk.Column.ColumnName))
            |> Map.ofList
        let columns =
            kinds
            |> Bench.iterMap "physicalSchema.kind" toPhysicalColumns
            |> List.concat
            |> Set.ofList
        let foreignKeys =
            kinds
            |> List.collect (toPhysicalForeignKeys kindByKey targetPkColumnByKey)
            |> Set.ofList
        let rows =
            kinds
            |> List.collect toPhysicalRows
            |> Set.ofList
        {
            Columns = columns
            ForeignKeys = foreignKeys
            Rows = rows
            RowDigests = Set.empty
        }

    /// Layer per-table aggregate row digests onto an existing
    /// PhysicalSchema. Used when row data is too large to materialize
    /// into the Catalog's `Modality.Static`; the digests come from
    /// `RowDigester` folds over the streaming readside.
    let withDigests (digests: seq<PhysicalRowDigest>) (s: PhysicalSchema) : PhysicalSchema =
        { s with RowDigests = s.RowDigests + Set.ofSeq digests }

    /// Diff two `PhysicalSchema` values across four axes (columns +
    /// FKs + per-row hashes + per-table digests). Per session-35 —
    /// `Set.difference` switched to `HashSet.ExceptWith` form for
    /// large-row diffs (`PhysicalSchema.diff` was the dominant cost
    /// when canaries fail with millions of mismatched rows).
    let private setDifference (source: Set<'a>) (target: Set<'a>) : 'a list =
        if Set.isEmpty source then []
        elif Set.isEmpty target then Set.toList source
        else
            let hs = System.Collections.Generic.HashSet<'a>(source)
            hs.ExceptWith target
            List.ofSeq hs

    let diff (source: PhysicalSchema) (target: PhysicalSchema) : PhysicalSchemaDiff =
        use _ = Bench.scope "physicalSchema.diff"
        {
            MissingColumns       = setDifference source.Columns       target.Columns
            ExtraColumns         = setDifference target.Columns       source.Columns
            MissingForeignKeys   = setDifference source.ForeignKeys   target.ForeignKeys
            ExtraForeignKeys     = setDifference target.ForeignKeys   source.ForeignKeys
            MissingRows          = setDifference source.Rows          target.Rows
            ExtraRows            = setDifference target.Rows          source.Rows
            MissingRowDigests    = setDifference source.RowDigests    target.RowDigests
            ExtraRowDigests      = setDifference target.RowDigests    source.RowDigests
        }

    /// True iff the diff is empty across all eight axes.
    let isEqual (d: PhysicalSchemaDiff) : bool =
        List.isEmpty d.MissingColumns
        && List.isEmpty d.ExtraColumns
        && List.isEmpty d.MissingForeignKeys
        && List.isEmpty d.ExtraForeignKeys
        && List.isEmpty d.MissingRows
        && List.isEmpty d.ExtraRows
        && List.isEmpty d.MissingRowDigests
        && List.isEmpty d.ExtraRowDigests

    /// Render a diff as a human-readable multi-line string. Used by
    /// canary failure messages so the operator sees exactly which
    /// columns / FKs mismatched, not just "they differ."
    let renderDiff (d: PhysicalSchemaDiff) : string =
        let renderColumn (c: PhysicalColumn) : string =
            let len =
                match c.Length with
                | Some n -> sprintf "(%d)" n
                | None -> ""
            let prec =
                match c.Precision, c.Scale with
                | Some p, Some s -> sprintf "(%d,%d)" p s
                | Some p, None -> sprintf "(%d)" p
                | _ -> ""
            sprintf
                "  [%s].[%s].[%s] %A%s%s nullable=%b pk=%b identity=%b"
                c.Schema
                c.Table
                c.Column
                c.Type
                len
                prec
                c.Nullable
                c.IsPrimaryKey
                c.IsIdentity
        let renderFk (f: PhysicalForeignKey) : string =
            sprintf
                "  [%s].[%s].[%s] -> [%s].[%s].[%s]"
                f.SourceSchema
                f.SourceTable
                f.SourceColumn
                f.TargetSchema
                f.TargetTable
                f.TargetColumn
        let renderRow (r: PhysicalRow) : string =
            sprintf
                "  [%s].[%s] row hash=%s"
                r.Schema
                r.Table
                (r.Hash.Substring(0, min 16 r.Hash.Length))
        let renderDigest (d: PhysicalRowDigest) : string =
            sprintf
                "  [%s].[%s] count=%d aggregate=%s"
                d.Schema
                d.Table
                d.Count
                (d.AggregateHash.Substring(0, min 16 d.AggregateHash.Length))
        let block (label: string) (renderer: 'a -> string) (xs: 'a list) : string =
            if List.isEmpty xs then sprintf "%s:\n  (none)" label
            else
                sprintf "%s:\n%s" label (xs |> List.map renderer |> String.concat "\n")
        // Per session-35 — pattern-match-based count instead of
        // `List.length` (which walks the entire list before deciding
        // whether to truncate). Distinguishes 0 / ≤5 / >5 in O(6).
        let countTier (xs: 'a list) : int =
            match xs with
            | [] -> 0
            | [_] -> 1
            | [_;_] -> 2
            | [_;_;_] -> 3
            | [_;_;_;_] -> 4
            | [_;_;_;_;_] -> 5
            | _ -> 6
        let truncatedBlock
            (label: string) (renderer: 'a -> string) (xs: 'a list) : string =
            match countTier xs with
            | 0 -> sprintf "%s:\n  (none)" label
            | tier when tier <= 5 ->
                sprintf "%s:\n%s" label (xs |> List.map renderer |> String.concat "\n")
            | _ ->
                let shown = List.truncate 5 xs
                let total = List.length xs
                sprintf
                    "%s (%d total; showing first 5):\n%s"
                    label
                    total
                    (shown |> List.map renderer |> String.concat "\n")
        String.concat
            "\n"
            [
                "PhysicalSchema diff:"
                block "Missing columns in target (source had, target lost)" renderColumn d.MissingColumns
                block "Extra columns in target (target has, source did not)" renderColumn d.ExtraColumns
                block "Missing FKs in target (source had, target lost)" renderFk d.MissingForeignKeys
                block "Extra FKs in target (target has, source did not)" renderFk d.ExtraForeignKeys
                truncatedBlock "Missing rows in target (source had, target lost)" renderRow d.MissingRows
                truncatedBlock "Extra rows in target (target has, source did not)" renderRow d.ExtraRows
                truncatedBlock "Missing row digests in target (source had, target lost)" renderDigest d.MissingRowDigests
                truncatedBlock "Extra row digests in target (target has, source did not)" renderDigest d.ExtraRowDigests
            ]

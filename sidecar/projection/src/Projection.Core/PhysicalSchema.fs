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
///     tgtSchema, tgtTable, tgtCol, isTrusted)` tuples (Session B addition;
///     `isTrusted` is THE VECTOR Wave 1 / M1's Decision-axis trust sub-axis).
///
/// **What's NOT compared.** SsKey identity, Module structure,
/// Origin / Modality marks, static populations. Non-PK index
/// *structure* IS compared as of E1 (see `PhysicalIndex` / the
/// `Indexes` axis); index *options* (filter / included columns /
/// storage flags) remain a named residual
/// (`ToleratedDivergence.IndexOptionsUnreflected`). These excluded
/// axes are V2-IR-only or option-level details SQL Server's catalog
/// does not recover symmetrically.
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
        /// DEFAULT-constraint expression in normalized form (Wave-1
        /// slice 1.2). `None` when the column has no DEFAULT. Catches
        /// the canary's blindness to a dropped/changed DEFAULT clause
        /// through the emit → deploy → read round-trip. Normalized via
        /// `PhysicalSchema.normalizeDefault` (strip matched outer parens
        /// SQL Server adds) so the emitter-IR side
        /// (`SqlLiteral.toString`) and the ReadSide side
        /// (`sys.default_constraints.definition`) compare equal — the
        /// named DEFAULT round-trip tolerance (A37-family).
        Default : string option
        /// Computed-column expression in normalized form (Wave-1 slice
        /// 1.3, L3-S7). `None` for non-computed columns. `IsPersisted`
        /// rides the string (`"<expr>|persisted"` / `"<expr>"`) so the
        /// round-trip catches both a dropped computation and a
        /// persisted↔non-persisted drift. Normalized via
        /// `PhysicalSchema.normalizeDefault` (same paren-stripping).
        Computed : string option
    }

/// A schema annotation that lives at table or catalog scope rather than
/// column scope — Wave-1 slice 1.3's uniform carrier for the four
/// non-column hollow-canary features (triggers / CHECK constraints /
/// sequences / extended properties). One axis with a `Kind`
/// discriminator keeps the `PhysicalSchemaDiff` to a single Missing/Extra
/// pair instead of four, while preserving per-feature attributability via
/// `Kind`. Compared as a value: a dropped / added / changed annotation
/// surfaces as a set-difference entry.
///
/// `Owner` is the qualified object the annotation hangs on
/// (`[schema].[table]` for triggers/checks/ext-props; `[schema].[name]`
/// for sequences). `Name` is the annotation's own identifier; `Payload`
/// is the normalized definition / value that must round-trip.
type PhysicalAnnotationKind =
    | TriggerAnnotation
    | CheckAnnotation
    | SequenceAnnotation
    | ExtendedPropertyAnnotation

type PhysicalAnnotation =
    {
        Kind : PhysicalAnnotationKind
        Owner : string
        Name : string
        Payload : string
    }

/// A non-PK index in physical-schema coordinates (E1 / debrief G3).
/// Compares the index's *structural identity* — owner, name, uniqueness,
/// and ordered key columns — which is exactly the surface `ReadSide`
/// reconstructs (`readIndexes`: `i.name`, `i.is_unique`, key columns +
/// `is_descending_key` in `key_ordinal` order; PKs excluded). Both halves
/// of the canary read back through the same `ReadSide`, so this axis stays
/// symmetric and surfaces a *genuine* index drop/reshape (a UNIQUE index V2
/// failed to emit, a key-column reorder) without false positives.
///
/// **Deliberately NOT compared:** included columns, the filter predicate,
/// and the storage options (FILLFACTOR / lock flags / compression) — these
/// are recovered by neither side today, so they're a named residual
/// (`ToleratedDivergence.IndexOptionsUnreflected`), not a silent drop.
type PhysicalIndex =
    {
        Schema : string
        Table : string
        Name : string
        IsUnique : bool
        /// Ordered key columns, encoded `[col:ASC][col2:DESC]`. Order is
        /// load-bearing — a different key order is a different index.
        KeyColumns : string
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
///
/// **`IsTrusted` — the Decision-axis trust sub-axis (THE VECTOR Wave 1 /
/// M1).** `true` for a normally-enforced FK; `false` for a `WITH NOCHECK`
/// (untrusted) FK. The decision arrives two ways, both reflected here so the
/// general `diff` comparator observes a trust divergence:
///   - the source `Reference.IsConstraintTrusted` (a deployed source FK that
///     was already `WITH NOCHECK`, recovered at `ReadSide.fs:1171` from
///     `sys.foreign_keys.is_not_trusted`), AND
///   - a registered `EnforceConstraint (ScriptWithNoCheck _)` intervention,
///     carried in `DecisionOverlay.NoCheckFk` and applied by `ofCatalogWith`.
/// Before M1 this field did not exist, so `PhysicalSchema.diff` was
/// structurally blind to FK trust — the over-claim the retired
/// `ToleratedDivergence.FkTrustUnreflected` named.
type PhysicalForeignKey =
    {
        SourceSchema : string
        SourceTable : string
        SourceColumn : string
        TargetSchema : string
        TargetTable : string
        TargetColumn : string
        /// `true` ⇒ enforced/trusted; `false` ⇒ `WITH NOCHECK` (untrusted).
        /// See the type docstring — the Decision-axis trust sub-axis.
        IsTrusted : bool
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

/// A binding from a deployed physical coordinate to its logical
/// name. Slice D.1.c addition — closes the chapter-D logical-name-
/// emission arc by giving the canary's PhysicalSchema diff a fifth
/// axis that carries the V2.LogicalName extended-property identity.
///
/// **Semantics.** `Column = None` is a table-level binding (the kind's
/// logical name); `Column = Some col` is a column-level binding (the
/// attribute's logical name). `Table` carries the deployed physical
/// table name; `LogicalName` carries the logical name (recovered
/// from `sys.extended_properties` via slice D.1.b's ReadSide hydration
/// in roundtrip flows, or from `Kind.Name` / `Attribute.Name` in
/// direct projection flows).
///
/// **Triangle property** (slice D.1.c's verification target).
/// Source-side binding `(Schema, Table = OSUSR_*, Column = None,
/// LogicalName = X)` and target-side binding `(Schema, Table = X,
/// Column = None, LogicalName = X)` represent the same kind under
/// the substitution-then-recovery chain: source's Table is the
/// OSSYS-shape, target's Table equals its LogicalName (V2 substituted
/// the logical name into the physical-realization slot), and both
/// carry the same LogicalName (the recovery preserved logical
/// identity end-to-end). Per-target check: `binding.Table =
/// binding.LogicalName` ∧ (for column-level) `binding.Column =
/// Some binding.LogicalName`.
type LogicalNameBinding =
    {
        Schema : string
        Table : string
        /// `Some col` for column-level bindings; `None` for
        /// table-level (kind-level) bindings.
        Column : string option
        LogicalName : string
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
        /// Slice D.1.c — logical-name bindings (the kind's / attribute's
        /// `Name` projected alongside the deployed physical coordinate).
        /// Populated from `Kind.Name` + `Attribute.Name` in `ofCatalog`;
        /// recovered from `sys.extended_properties` via slice D.1.b's
        /// ReadSide hydration in roundtrip flows.
        LogicalNameBindings : Set<LogicalNameBinding>
        /// Wave-1 slice 1.3 — table/catalog-scoped annotations (triggers,
        /// CHECK constraints, sequences, extended properties). Un-hollows
        /// the canary on L3-S4 / S5 / S8 / S9. Populated from `Kind.Triggers`
        /// / `Kind.ColumnChecks` / `Catalog.Sequences` / `*.ExtendedProperties`
        /// in `ofCatalog`; recovered from `sys.triggers` / `sys.check_constraints`
        /// / `sys.sequences` / `sys.extended_properties` by ReadSide.
        Annotations : Set<PhysicalAnnotation>
        /// E1 (debrief G3) — non-PK indexes (owner + name + uniqueness +
        /// ordered key columns). Populated from `Kind.Indexes` in `ofCatalog`;
        /// recovered from `sys.indexes` ⋈ `sys.index_columns` by ReadSide's
        /// `readIndexes` + `attachIndexes`. Retires the prior
        /// `Tolerance.IndexOptionsUnreflected` (index *structure* is now compared;
        /// index *options* remain a named residual).
        Indexes : Set<PhysicalIndex>
    }

/// The diff between two `PhysicalSchema` values. All ten fields
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
        /// Slice D.1.c — logical-name bindings that appear in source
        /// but not target (Missing) / target but not source (Extra).
        /// Set-difference on the binding's full record (Schema + Table
        /// + Column + LogicalName); the triangle property (separate
        /// predicate) projects out the Table to compare on logical
        /// identity alone.
        MissingLogicalNameBindings : LogicalNameBinding list
        ExtraLogicalNameBindings : LogicalNameBinding list
        /// Wave-1 slice 1.3 — annotations (triggers / checks / sequences /
        /// extended properties) in source-not-target (Missing) /
        /// target-not-source (Extra).
        MissingAnnotations : PhysicalAnnotation list
        ExtraAnnotations : PhysicalAnnotation list
        /// E1 — non-PK indexes in source-not-target (Missing) /
        /// target-not-source (Extra). A populated entry is a genuine index
        /// drop / reshape on the round-trip.
        MissingIndexes : PhysicalIndex list
        ExtraIndexes : PhysicalIndex list
    }

/// The canonical per-row content-hash recipes (both canary paths and
/// the Q-track hash through here). The streaming sum-mod-2^256
/// aggregate fold was DELETED 2026-06-12 (CONSTELLATION_BACKLOG card
/// F7, plane N10; zero call sites — the dead-algebra precedent) with
/// its rebuild recipe recorded here. **The named trigger fired at the
/// fidelity chapter open (DECISIONS 2026-07-15; T17 candidate): the
/// row-fidelity ladder's aggregate rung is the awaited second
/// consumer, and the fold is REBUILT below as `RowDigestFold` — over
/// quanta via `hashQuantumBytes`, exactly as the recipe named.** (The
/// old `PhysicalSchema.RowDigests` axis stays deleted; the fidelity
/// comparator consumes the fold directly, per-kind, and never
/// materializes an axis.)
[<RequireQualifiedAccess>]
module RowDigester =

    /// THE canonical per-row content hash: sort `Values` by column name,
    /// build the `<name>=<value>` string joined by the RS (\x1e)
    /// separator (disambiguating pairs that would otherwise alias under
    /// degenerate name/value combinations), UTF8, SHA256 → bytes. Both
    /// the streaming aggregate path (`add`, below) and the granular
    /// per-row-hex path (`PhysicalSchema.hashStaticRow`) hash through this
    /// one recipe — it was duplicated byte-for-byte as
    /// `PhysicalSchema.hashStaticRowBytes` (CONSTELLATION_BACKLOG plane
    /// N1, collapsed 2026-06-11). Per session-35 the single
    /// `StringBuilder` accumulation replaced a `sortBy -> map sprintf ->
    /// String.concat` chain (~8 → ~4 us/row at 500k scale).
    ///
    /// WP-3 (F11) NULL encoding: a NULL cell OMITS its pair entirely —
    /// the same rule the SQL-side plane (`ServerDigest`, FOR XML) already
    /// applies — while an empty string contributes `name=`. NULL and `''`
    /// therefore hash DISTINCTLY; rows without NULL cells keep their
    /// pre-WP-3 bytes. Nothing persists these hashes across runs (the
    /// 2026-07-16 survey: every consumer compares intra-run; consent
    /// fingerprints and the estate store use independent recipes), so the
    /// encoding change carries no re-bless / re-record cost.
    let hashRowBytes (row: StaticRow) : byte[] =
        let pairs =
            row.Values
            |> Map.toArray
            |> Array.sortBy (fun (n, _) -> Name.value n)
        let sb = System.Text.StringBuilder(64)
        let mutable first = true
        for (n, v) in pairs do
            match v with
            | None -> ()   // NULL omits its attribute (the ServerDigest rule)
            | Some v ->
                if not first then sb.Append('') |> ignore
                sb.Append(Name.value n).Append('=').Append(v) |> ignore
                first <- false
        let bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString())
        System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>(bytes))

    /// Hash a `RowQuantum` byte-identically to `hashRowBytes` over the
    /// equivalent TOTAL `StaticRow`: walk the basis's name-sorted
    /// permutation (no per-row sort), build `<name>=<value>` joined by the
    /// RS () separator, UTF8, SHA256. The byte-identity with
    /// `hashRowBytes` is the Q-track's load-bearing invariant — it keeps
    /// the canary's row hashes stable when the read carrier becomes
    /// positional (CONSTELLATION_BACKLOG Q1; witness `Q1: hashQuantumBytes
    /// over a quantum equals hashRowBytes over the equivalent row`).
    let hashQuantumBytes (basis: RowBasis) (q: RowQuantum) : byte[] =
        let names = RowBasis.names basis
        let order = RowBasis.nameSortedOrder basis
        let sb = System.Text.StringBuilder(64)
        let mutable first = true
        for i in order do
            match q.Cells.[i] with
            | ValueNone -> ()   // NULL omits its attribute (the ServerDigest rule)
            | ValueSome v ->
                if not first then sb.Append('') |> ignore
                sb.Append(Name.value names.[i]).Append('=').Append(v) |> ignore
                first <- false
        let bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString())
        System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>(bytes))

/// The streaming, ORDER-INDEPENDENT aggregate digest of a table's rows —
/// the 2026-06-12-deleted fold, rebuilt at its named trigger (the fidelity
/// chapter open, DECISIONS 2026-07-15; T17's ladder rung L1). One digest +
/// count per table at bounded memory: `addRow`/`addQuantum` fold each row's
/// canonical SHA256 (`RowDigester`'s one recipe) into a 256-bit unsigned sum
/// mod 2²⁵⁶, so the fold is COMMUTATIVE with `empty` as identity — two sides
/// of a comparison need NO ordering agreement at the aggregate rung, and a
/// mismatch sends the ladder to its ordered per-row drill-down (rung L2).
/// The quantum path hashes against a `RowBasis`, so a physical-rendition
/// stream folded under a renamed (logical) basis digests identically to the
/// logical rendition's own stream — byte-identity stays well-defined across
/// the physical→logical gap (T17's rows-axis triangle).
[<RequireQualifiedAccess>]
module RowDigestFold =

    /// The accumulator: the 256-bit running sum (little-endian, 32 bytes) +
    /// the row count. Opaque — mint through `empty`, grow through `add*`,
    /// read through `finalize`.
    type State = private { Sum : byte[]; Count : int64 }

    /// The identity: a zero sum over zero rows.
    let empty : State = { Sum = Array.zeroCreate 32; Count = 0L }

    /// Fold one canonical per-row hash into the sum, mod 2²⁵⁶ (byte-wise
    /// add with carry; the final carry out of byte 31 drops — the modulus).
    /// Copy-on-fold keeps `State` a value (a shared `empty` is never mutated).
    let private addHash (state: State) (hash: byte[]) : State =
        let sum = Array.copy state.Sum
        let mutable carry = 0
        for i in 0 .. 31 do
            let v = int sum.[i] + int hash.[i] + carry
            sum.[i] <- byte (v &&& 0xFF)
            carry <- v >>> 8
        { Sum = sum; Count = state.Count + 1L }

    /// Fold one IR-grain row (the Map carrier).
    let addRow (state: State) (row: StaticRow) : State =
        addHash state (RowDigester.hashRowBytes row)

    /// Fold one positional row against its stream's basis (the scale path —
    /// no per-row sort; the basis's name-sorted permutation reproduces the
    /// Map-sorted bytes).
    let addQuantum (basis: RowBasis) (state: State) (q: RowQuantum) : State =
        addHash state (RowDigester.hashQuantumBytes basis q)

    /// The finalized per-table digest: the aggregate sum (hex) + the count.
    /// Two tables' row multisets are equal only if their digests AND counts
    /// agree (the count disambiguates sums that alias at different sizes).
    type TableDigest = { Aggregate : string; Count : int64 }

    let finalize (state: State) : TableDigest =
        { Aggregate = System.Convert.ToHexString state.Sum; Count = state.Count }

[<RequireQualifiedAccess>]
module PhysicalSchema =

    /// Slice D.1.c — project one kind's logical-name bindings:
    /// one table-level entry (Column = None) carrying `Kind.Name`,
    /// plus one column-level entry per attribute carrying
    /// `Attribute.Name`. Mirrors the V2.LogicalName extended-property
    /// emission shape (slice D.1.b) so the readside-recovered
    /// catalog and the in-memory catalog produce the same bindings.
    let private toLogicalNameBindings (k: Kind) : LogicalNameBinding list =
        let schemaStr = SchemaName.value k.Physical.Schema
        let tableStr = TableName.value k.Physical.Table
        let tableBinding =
            {
                Schema = schemaStr
                Table = tableStr
                Column = None
                LogicalName = Name.value k.Name
            }
        let columnBindings =
            k.Attributes
            |> List.map (fun a ->
                {
                    Schema = schemaStr
                    Table = tableStr
                    Column = Some (ColumnRealization.columnNameText a.Column)
                    LogicalName = Name.value a.Name
                })
        tableBinding :: columnBindings

    /// Normalize a DEFAULT-constraint expression for round-trip
    /// comparison (Wave-1 slice 1.2). SQL Server canonicalizes a
    /// `DEFAULT 0` clause to `((0))` in `sys.default_constraints
    /// .definition`, while V2's emitter renders `SqlLiteral.toString`
    /// (`0`). Stripping matched outer parens + trimming whitespace
    /// brings both to a common form. This is a NAMED tolerance: it
    /// erases SQL Server's redundant-paren canonicalization only —
    /// the inner expression must still match exactly, so a changed or
    /// dropped DEFAULT is still caught. Idempotent; stops at the first
    /// unmatched paren so `((0)+1)` is not over-stripped.
    let normalizeDefault (expr: string) : string =
        let rec strip (s: string) =
            let t = s.Trim()
            if t.Length >= 2 && t.[0] = '(' && t.[t.Length - 1] = ')' then
                // Only strip when the leading '(' matches the trailing ')'
                // (i.e. the whole string is parenthesized), not when they
                // are two independent groups like `(a)+(b)`.
                let mutable depth = 0
                let mutable matchedAtEnd = true
                for i in 0 .. t.Length - 1 do
                    if t.[i] = '(' then depth <- depth + 1
                    elif t.[i] = ')' then
                        depth <- depth - 1
                        if depth = 0 && i < t.Length - 1 then matchedAtEnd <- false
                if matchedAtEnd then strip (t.Substring(1, t.Length - 2))
                else t
            else t
        strip expr

    /// Encode a computed-column config into the normalized comparison
    /// string the `PhysicalColumn.Computed` axis carries (Wave-1 slice 1.3).
    /// `IsPersisted` rides the string so a persisted↔non-persisted drift
    /// surfaces. Single definition site (consumed by both producers —
    /// `ofCatalog` here and `PhysicalSchemaReader`) so the encoding cannot
    /// drift between the two halves of the adjunction.
    let encodeComputed (cc: ComputedColumnConfig) : string =
        let expr = normalizeDefault cc.Expression
        if cc.IsPersisted then System.String.Concat(expr, "|persisted") else expr

    let private toPhysicalColumns (k: Kind) : PhysicalColumn list =
        let schemaStr = SchemaName.value k.Physical.Schema
        let tableStr = TableName.value k.Physical.Table
        k.Attributes
        |> Bench.iterMap "physicalSchema.attribute" (fun a ->
            {
                Schema = schemaStr
                Table = tableStr
                Column = ColumnRealization.columnNameText a.Column
                Type = a.Type
                Nullable = a.Column.IsNullable
                IsPrimaryKey = a.IsPrimaryKey
                Length = a.Length
                Precision = a.Precision
                Scale = a.Scale
                IsIdentity = a.IsIdentity
                Default =
                    a.DefaultValue
                    |> Option.map (fun lit -> normalizeDefault (SqlLiteral.toString lit))
                Computed = a.Computed |> Option.map encodeComputed
            })

    /// Wave-1 slice 1.3 — project a Kind's table-scoped annotations
    /// (triggers + CHECK constraints) into the uniform annotation axis.
    /// Trigger payload is a normalized digest of the definition body
    /// (whitespace-collapsed, lowercased) so the round-trip catches a
    /// dropped/changed trigger without over-asserting on SQL Server's
    /// re-formatting of the stored definition (a named A37-family
    /// tolerance). CHECK payload is the normalized (paren-stripped)
    /// expression.
    let private normalizeBody (s: string) : string =
        // Collapse all runs of whitespace to a single space, trim, lower.
        let collapsed =
            s.Split([| ' '; '\t'; '\r'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> String.concat " "
        collapsed.ToLowerInvariant()

    let private toKindAnnotations (k: Kind) : PhysicalAnnotation list =
        let owner = System.String.Concat("[", TableId.schemaText k.Physical, "].[", TableId.tableText k.Physical, "]")
        let triggers =
            k.Triggers
            |> List.map (fun t ->
                {
                    Kind = TriggerAnnotation
                    Owner = owner
                    Name = Name.value t.Name
                    // Disabled-state rides the payload so a disable/enable
                    // drift surfaces; body digest is normalized.
                    Payload =
                        (if t.IsDisabled then "disabled|" else "enabled|")
                        + normalizeBody t.Definition
                })
        let checks =
            k.ColumnChecks
            |> List.map (fun c ->
                {
                    Kind = CheckAnnotation
                    Owner = owner
                    // CHECK name is optional (SQL Server auto-names); when
                    // absent the expression IS the identity, so key on it.
                    Name = c.Name |> Option.map Name.value |> Option.defaultValue ""
                    Payload = normalizeDefault c.Definition
                })
        triggers @ checks

    /// E1 (debrief G3) — project a Kind's non-PK indexes into the index
    /// axis. Resolves each `IndexColumn`'s attribute SsKey to its physical
    /// column name (the same coordinate `toPhysicalColumns` keys on) and
    /// encodes the ordered key list so a reorder surfaces as a divergence.
    ///
    /// THE VECTOR Wave 1 / M1 — `IsUnique` is additive over the overlay: an
    /// index is UNIQUE iff the catalog already declared it unique OR a
    /// registered `UniqueIndex` intervention decided `EnforceUnique`
    /// (`DecisionOverlay.EnforceUnique`). This mirrors the emitter's own rule
    /// at `SsdtDdlEmitter.indexStatements` (`isUnique || overlay.EnforceUnique`)
    /// so the source projection and the read-back agree on the promotion (the
    /// read leg recovers `sys.indexes.is_unique`), routing the unique-promotion
    /// decision through the general comparator (retires
    /// `ToleratedDivergence.UniquePromotionUnreflected`).
    let private toPhysicalIndexes (overlay: DecisionOverlay) (k: Kind) : PhysicalIndex list =
        let schemaStr = SchemaName.value k.Physical.Schema
        let tableStr = TableName.value k.Physical.Table
        let colNameByKey =
            k.Attributes
            |> List.map (fun a -> a.SsKey, ColumnRealization.columnNameText a.Column)
            |> Map.ofList
        // The deployed index name is the EMITTED name (`IndexNaming` — the
        // same derivation `SsdtDdlEmitter` renders), not the source-side
        // `Index.Name`: the read-back leg recovers `sys.indexes.name`, so
        // the expectation must project the name the emission introduced or
        // every round-trip diff reports phantom index drift.
        let emittedNames = IndexNaming.emittedNames overlay k
        k.Indexes
        |> List.map (fun idx ->
            let keyColumns =
                idx.Columns
                |> List.map (fun ic ->
                    let colName = Map.tryFind ic.Attribute colNameByKey |> Option.defaultValue "<unresolved>"
                    let dir = match ic.Direction with | Ascending -> "ASC" | Descending -> "DESC"
                    System.String.Concat("[", colName, ":", dir, "]"))
                |> String.concat ""
            {
                Schema = schemaStr
                Table = tableStr
                Name = Map.find idx.SsKey emittedNames
                IsUnique = IndexUniqueness.isUnique idx.Uniqueness || Set.contains idx.SsKey overlay.EnforceUnique
                KeyColumns = keyColumns
            })

    /// Project the Catalog's sequences into annotations. Sequence shape
    /// (start / increment / min / max / cycle / cache) rides the payload
    /// so any shape drift surfaces.
    let private toSequenceAnnotations (c: Catalog) : PhysicalAnnotation list =
        c.Sequences
        |> List.map (fun s ->
            let optDec (d: decimal option) = d |> Option.map string |> Option.defaultValue "-"
            let cache =
                match s.CacheMode with
                | Cache -> System.String.Concat("cache:", (s.CacheSize |> Option.map string |> Option.defaultValue "?"))
                | NoCache -> "nocache"
                | Unspecified -> "cache:default"
            {
                Kind = SequenceAnnotation
                Owner = System.String.Concat("[", s.Schema, "].[", Name.value s.Name, "]")
                Name = Name.value s.Name
                Payload =
                    String.concat "|"
                        [ s.DataType.ToLowerInvariant()
                          "start:" + optDec s.StartValue
                          "incr:" + optDec s.Increment
                          "min:" + optDec s.Minimum
                          "max:" + optDec s.Maximum
                          (if s.IsCycleEnabled then "cycle" else "nocycle")
                          cache ]
            })

    /// Project extended properties (module / kind / attribute) into
    /// annotations. The `V2.LogicalName` property is EXCLUDED — it is
    /// already covered by the `LogicalNameBindings` axis, so including it
    /// here would double-count and (worse) make the canary assert on the
    /// emitter's own round-trip scaffolding.
    let private toExtendedPropertyAnnotations (k: Kind) : PhysicalAnnotation list =
        let tableOwner = System.String.Concat("[", TableId.schemaText k.Physical, "].[", TableId.tableText k.Physical, "]")
        // WP5 / C1 — exclude BOTH the renamed (`Projection.LogicalName`) and
        // the legacy (`V2.LogicalName`) identity property during the dual-read
        // window, so neither phantom-diffs against the other.
        let isLogicalName (ep: ExtendedProperty) =
            ep.Name = "Projection.LogicalName" || ep.Name = "V2.LogicalName"
        let kindEps =
            k.ExtendedProperties
            |> List.filter (not << isLogicalName)
            |> List.map (fun ep ->
                {
                    Kind = ExtendedPropertyAnnotation
                    Owner = tableOwner
                    Name = ep.Name
                    Payload = ep.Value |> Option.defaultValue ""
                })
        let attrEps =
            k.Attributes
            |> List.collect (fun a ->
                a.ExtendedProperties
                |> List.filter (not << isLogicalName)
                |> List.map (fun ep ->
                    {
                        Kind = ExtendedPropertyAnnotation
                        Owner = System.String.Concat(tableOwner, ".[", ColumnRealization.columnNameText a.Column, "]")
                        Name = ep.Name
                        Payload = ep.Value |> Option.defaultValue ""
                    }))
        kindEps @ attrEps

    /// Hex form of the row hash — used by `PhysicalRow.Hash` so per-row
    /// granular diffs render as a stable string, over the one canonical
    /// recipe (`RowDigester.hashRowBytes`).
    let private hashStaticRow (row: StaticRow) : string =
        System.Convert.ToHexString (RowDigester.hashRowBytes row)

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
                let schemaStr = SchemaName.value k.Physical.Schema
                let tableStr = TableName.value k.Physical.Table
                let arr = List.toArray rows
                let hashed =
                    arr
                    |> Array.Parallel.map (fun r ->
                        {
                            Schema = schemaStr
                            Table = tableStr
                            Hash = hashStaticRow r
                        })
                Bench.recordSample "physicalSchema.rows.hash.elements" (int64 arr.Length)
                List.ofArray hashed
            | _ -> [])

    /// Per session-35 — `kindByKey` and `targetPkColumnsByKey` lifted
    /// to `Map` once per `ofCatalog` invocation rather than scanning
    /// the catalog linearly per reference. At 300 kinds × ~5 refs
    /// each that's ~1500 catalog scans (each O(K)) → ~1500 hash
    /// lookups. Source-attribute lookup stays linear over per-kind
    /// `Attributes` (≈10 entries on avg, not worth the per-kind
    /// allocation of a separate map).
    let private toPhysicalForeignKeys
        (overlay: DecisionOverlay)
        (kindByKey: Map<SsKey, Kind>)
        (targetPkColumnsByKey: Map<SsKey, string list>)
        (k: Kind)
        : PhysicalForeignKey list =
        k.References
        |> List.choose (fun r ->
            let sourceColumn =
                k.Attributes
                |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
            match sourceColumn,
                  Map.tryFind r.TargetKind kindByKey,
                  Map.tryFind r.TargetKind targetPkColumnsByKey with
            | Some srcCol, Some tk, Some (tgtPkFirst :: tgtPkRest) ->
                // NM-28 — the target's PK is the FULL ordered list
                // `tgtPkFirst :: tgtPkRest`. V2's `Reference` IR carries ONE
                // source column (single-column per chapter 5.0), so only the
                // first leg can be paired; a composite target PK
                // (`tgtPkRest <> []`) leaves the later legs UNREFLECTED. This is
                // the named, closed tolerance `ToleratedDivergence
                // .CompositePkFkUnreflected` (NOT a silent first-element pick) —
                // retiring it needs a composite-FK IR carrying the source legs.
                ignore tgtPkRest
                Some
                    {
                        SourceSchema = SchemaName.value k.Physical.Schema
                        SourceTable = TableName.value k.Physical.Table
                        SourceColumn = srcCol
                        TargetSchema = SchemaName.value tk.Physical.Schema
                        TargetTable = TableName.value tk.Physical.Table
                        TargetColumn = tgtPkFirst
                        // THE VECTOR Wave 1 / M1 — the Decision-axis trust
                        // sub-axis. Untrusted iff the source FK is itself
                        // `WITH NOCHECK` (`r.IsConstraintTrusted = false`,
                        // recovered at `ReadSide.fs:1171`) OR a registered
                        // intervention decided NOCHECK (`overlay.NoCheckFk`).
                        // Mirrors the emitter's NOCHECK predicate
                        // (`SsdtDdlEmitter.untrustedFkAlters`) so the source
                        // projection and the read-back agree.
                        IsTrusted = Reference.isConstraintTrusted r && not (Set.contains r.SsKey overlay.NoCheckFk)
                    }
            | _ ->
                // Dropped: the source attribute is unresolvable, the target
                // kind is absent from the catalog, OR the target has NO primary
                // key (empty PK list). The first two are structural-integrity
                // gaps `Catalog.create` validates upstream; the no-PK-target
                // case has no FK to reflect by construction (a SQL FK must
                // reference a key). Surfacing these as a Core diagnostic needs a
                // diagnostics channel on `PhysicalSchema` (today a pure
                // set-of-tuples value) — FLAGGED as a larger change (NM-28b),
                // not landed here.
                None)

    /// Project a Catalog to its `PhysicalSchema` view under a
    /// `DecisionOverlay` — the set of `(schema, table, column, type, nullable,
    /// isPrimaryKey)` tuples PLUS the set of `(src, tgt, isTrusted)` FK tuples
    /// reachable through every Module's Kinds. Modules, Origin, Modality are
    /// projected out by construction.
    ///
    /// **THE VECTOR Wave 1 / M1 — the overlay-aware core.** The overlay
    /// reflects the two Decision sub-axes the catalog does not itself bake in:
    /// FK trust (`NoCheckFk` → `PhysicalForeignKey.IsTrusted`) and unique
    /// promotion (`EnforceUnique` → `PhysicalIndex.IsUnique`). (Nullability
    /// tightening is already baked into the catalog before projection, so it
    /// round-trips without overlay threading.) At `DecisionOverlay.empty` this
    /// is **byte-identical** to the pre-M1 projection — the T1 goldens
    /// (`GoldenEmissionTests`) and `AdjunctionLawTests` are the guard. Mirrors
    /// the `SsdtDdlEmitter.statements` / `statementsWith` precedent: the
    /// emitter consults the same overlay at emission, so source projection and
    /// read-back agree on the recovered decision.
    let ofCatalogWith (overlay: DecisionOverlay) (c: Catalog) : PhysicalSchema =
        use _ = Bench.scope "physicalSchema.ofCatalog"
        let kinds = c.Modules |> List.collect (fun m -> m.Kinds)
        // Per session-35 — index lookups lifted once for FK projection
        // (was O(K) catalog scan per reference; now O(log K) hash
        // lookup). 300-kind catalog × 1500 refs: ~450k linear ops →
        // ~1500 hashed ops.
        let kindByKey =
            kinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        // NM-28 — the FULL ordered PK column list per kind (declaration order),
        // not just the first PK column. `toPhysicalForeignKeys` reflects only
        // the first leg (single-column `Reference` IR) but now sees the whole
        // list, so the composite case is named (`CompositePkFkUnreflected`)
        // rather than silently collapsed at the map-build site.
        let targetPkColumnsByKey =
            kinds
            |> List.choose (fun k ->
                match
                    k.Attributes
                    |> List.filter (fun a -> a.IsPrimaryKey)
                    |> List.map (fun pk -> ColumnRealization.columnNameText pk.Column)
                with
                | []      -> None
                | pkCols  -> Some (k.SsKey, pkCols))
            |> Map.ofList
        let columns =
            kinds
            |> Bench.iterMap "physicalSchema.kind" toPhysicalColumns
            |> List.concat
            |> Set.ofList
        let foreignKeys =
            kinds
            |> List.collect (toPhysicalForeignKeys overlay kindByKey targetPkColumnsByKey)
            |> Set.ofList
        let rows =
            kinds
            |> List.collect toPhysicalRows
            |> Set.ofList
        let logicalNameBindings =
            kinds
            |> List.collect toLogicalNameBindings
            |> Set.ofList
        let annotations =
            (kinds |> List.collect toKindAnnotations)
            @ (kinds |> List.collect toExtendedPropertyAnnotations)
            @ toSequenceAnnotations c
            |> Set.ofList
        let indexes =
            kinds
            |> List.collect (toPhysicalIndexes overlay)
            |> Set.ofList
        {
            Columns = columns
            ForeignKeys = foreignKeys
            Rows = rows
            LogicalNameBindings = logicalNameBindings
            Annotations = annotations
            Indexes = indexes
        }

    /// Project a Catalog to its `PhysicalSchema` view with **no** decision
    /// overlay — the `DecisionOverlay.empty` default of `ofCatalogWith`,
    /// byte-identical to the pre-M1 projection and the function ~30 call sites
    /// already use. The read-back leg (`ofCatalog` over a `ReadSide`-recovered
    /// catalog) uses this form: the recovered `Reference.IsConstraintTrusted` /
    /// `sys.indexes.is_unique` already carry the deployed decision, so no
    /// overlay is needed to observe it. The source leg uses `ofCatalogWith` to
    /// reflect a registered (not-yet-deployed) decision. Do NOT widen this
    /// signature — `ofCatalogWith` is the overlay-aware entry point.
    let ofCatalog (c: Catalog) : PhysicalSchema =
        ofCatalogWith DecisionOverlay.empty c

    /// Diff two `PhysicalSchema` values across its axes (columns +
    /// FKs + per-row hashes + bindings + annotations + indexes). Per session-35 —
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
            MissingColumns             = setDifference source.Columns             target.Columns
            ExtraColumns               = setDifference target.Columns             source.Columns
            MissingForeignKeys         = setDifference source.ForeignKeys         target.ForeignKeys
            ExtraForeignKeys           = setDifference target.ForeignKeys         source.ForeignKeys
            MissingRows                = setDifference source.Rows                target.Rows
            ExtraRows                  = setDifference target.Rows                source.Rows
            MissingLogicalNameBindings = setDifference source.LogicalNameBindings target.LogicalNameBindings
            ExtraLogicalNameBindings   = setDifference target.LogicalNameBindings source.LogicalNameBindings
            MissingAnnotations         = setDifference source.Annotations         target.Annotations
            ExtraAnnotations           = setDifference target.Annotations         source.Annotations
            MissingIndexes             = setDifference source.Indexes             target.Indexes
            ExtraIndexes               = setDifference target.Indexes             source.Indexes
        }

    /// True iff the diff is empty across all ten axes.
    let isEqual (d: PhysicalSchemaDiff) : bool =
        List.isEmpty d.MissingColumns
        && List.isEmpty d.ExtraColumns
        && List.isEmpty d.MissingForeignKeys
        && List.isEmpty d.ExtraForeignKeys
        && List.isEmpty d.MissingRows
        && List.isEmpty d.ExtraRows
        && List.isEmpty d.MissingLogicalNameBindings
        && List.isEmpty d.ExtraLogicalNameBindings
        && List.isEmpty d.MissingAnnotations
        && List.isEmpty d.ExtraAnnotations
        && List.isEmpty d.MissingIndexes
        && List.isEmpty d.ExtraIndexes

    /// Schema-structural equality: columns + FKs + logical-name bindings +
    /// annotations match, **ignoring row data** (`Missing`/`ExtraRows`).
    /// The right predicate for a *schema* migration's
    /// verification — `migrate A B` must make B' reproduce B's **structure**;
    /// the rows B' carries are the **preserved/migrated data** (the whole point
    /// of a differential over a drop+recreate), not part of the schema target B
    /// (which is a definition, not a data set). The full `isEqual` (data
    /// included) stays the predicate for the static-seed round-trip canaries.
    let isSchemaEqual (d: PhysicalSchemaDiff) : bool =
        List.isEmpty d.MissingColumns
        && List.isEmpty d.ExtraColumns
        && List.isEmpty d.MissingForeignKeys
        && List.isEmpty d.ExtraForeignKeys
        && List.isEmpty d.MissingLogicalNameBindings
        && List.isEmpty d.ExtraLogicalNameBindings
        && List.isEmpty d.MissingAnnotations
        && List.isEmpty d.ExtraAnnotations
        && List.isEmpty d.MissingIndexes
        && List.isEmpty d.ExtraIndexes

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
                "  [%s].[%s].[%s] -> [%s].[%s].[%s] trusted=%b"
                f.SourceSchema
                f.SourceTable
                f.SourceColumn
                f.TargetSchema
                f.TargetTable
                f.TargetColumn
                f.IsTrusted
        let renderRow (r: PhysicalRow) : string =
            sprintf
                "  [%s].[%s] row hash=%s"
                r.Schema
                r.Table
                (r.Hash.Substring(0, min 16 r.Hash.Length))
        let renderBinding (b: LogicalNameBinding) : string =
            match b.Column with
            | None ->
                sprintf
                    "  [%s].[%s] (table) logical=%s"
                    b.Schema b.Table b.LogicalName
            | Some col ->
                sprintf
                    "  [%s].[%s].[%s] logical=%s"
                    b.Schema b.Table col b.LogicalName
        let renderAnnotation (a: PhysicalAnnotation) : string =
            let kind =
                match a.Kind with
                | TriggerAnnotation -> "trigger"
                | CheckAnnotation -> "check"
                | SequenceAnnotation -> "sequence"
                | ExtendedPropertyAnnotation -> "extprop"
            sprintf "  %s %s on %s = %s" kind a.Name a.Owner
                (a.Payload.Substring(0, min 60 a.Payload.Length))
        let renderIndex (i: PhysicalIndex) : string =
            sprintf "  %sindex %s on [%s].[%s] %s"
                (if i.IsUnique then "unique " else "") i.Name i.Schema i.Table i.KeyColumns
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
                truncatedBlock "Missing logical-name bindings in target (source had, target lost)" renderBinding d.MissingLogicalNameBindings
                truncatedBlock "Extra logical-name bindings in target (target has, source did not)" renderBinding d.ExtraLogicalNameBindings
                truncatedBlock "Missing annotations in target (triggers/checks/sequences/extprops source had, target lost)" renderAnnotation d.MissingAnnotations
                truncatedBlock "Extra annotations in target (target has, source did not)" renderAnnotation d.ExtraAnnotations
                truncatedBlock "Missing indexes in target (source had, target lost)" renderIndex d.MissingIndexes
                truncatedBlock "Extra indexes in target (target has, source did not)" renderIndex d.ExtraIndexes
            ]

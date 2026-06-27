namespace Projection.Core

// LINT-ALLOW-FILE: validation-error message construction in
// `Module.create` / `Catalog.create` / `Reference.attach` smart
// constructors uses `sprintf "...%A..."` to interpolate typed
// `SsKey` values. The `%A` formatter is F#'s closed-DU structural
// pretty-printer — the canonical "stringify a typed value" surface.
// Per `DECISIONS 2026-05-09 — Built-in obligation`, this is the
// allowed exception (no BCL alternative emits closed-DU
// pretty-printed text). Audit-deferred Tier-2 #14 names typed
// `Outcome.toDiagnosticString` per DU as the eventual home; the
// same shape would absorb these. Until then, `sprintf "%A"` IS
// the typed pretty-print.

/// Presentation name for a catalog node. Names are policy-transformed (A15)
/// and never participate in identity (A2). The DU prevents accidental
/// confusion with `SsKey`.
type Name = Name of string

/// Construction and inspection helpers for `Name`.
[<RequireQualifiedAccess>]
module Name =

    let private nameEmpty =
        ValidationError.create "name.empty" "A name cannot be blank."

    /// Build a `Name`. Rejects blank input.
    let create (value: string) : Result<Name> =
        if System.String.IsNullOrWhiteSpace value then Result.failureOf nameEmpty
        else Result.success (Name value)

    /// Project the underlying string. Presentation only.
    let value (Name v) : string = v


/// A9: closed three-way origin discriminant. Widen only when evidence forces
/// it (a fourth source category would mean another reader and another set
/// of axioms about its provenance).
///
/// **Slice 4.6 — algebraic variant names (decouple V1 vocabulary from Core).**
/// The variants name *what the kind's provenance IS* in V2's algebra, not
/// the V1 product surface that observes it: `Native` (originated within the
/// platform), `ExternalIndirect` (external, exposed through an intermediating
/// step — V1's Integration-Studio IS-functor is one such producer),
/// `ExternalDirect` (external, exposed by a direct reader). The V1→V2
/// translation (`CatalogReader.parseOrigin*`) reads V1's `isExternal` flag +
/// EspaceKind; V1's product names never enter the Core DU.
type Origin =
    /// Kind originated within the platform (V1: `isExternal = false`).
    | Native
    /// External kind exposed through an intermediating step (V1: external +
    /// IS-functor / Integration-Studio extension).
    | ExternalIndirect
    /// External kind exposed by a direct reader, no intermediating step.
    | ExternalDirect

/// Typed structural display for `Origin`. Tag-only variants;
/// renders to the variant name.
[<RequireQualifiedAccess>]
module Origin =
    let toStructured (o: Origin) : StructuredString =
        match o with
        | Native -> StructuredString.tag "Native"
        | ExternalIndirect -> StructuredString.tag "ExternalIndirect"
        | ExternalDirect -> StructuredString.tag "ExternalDirect"

    let toDiagnosticString (o: Origin) : string =
        toStructured o |> StructuredString.render


/// One row of a Static kind's population. Populations live in the catalog
/// per A7; the unfold pass lifts them into type-level metadata for Pi.
/// `Identifier` is the row's stable SsKey; `Values` carries cell values
/// keyed by attribute name. Cell values are kept as strings here — the
/// projection's type-correspondence policy (A13) determines how they are
/// rendered on the surface.
type StaticRow = {
    Identifier : SsKey
    Values     : Map<Name, string>
}

/// The positional row carrier for data in flight (CONSTELLATION §9.5;
/// CONSTELLATION_BACKLOG Q-track). `Cells` are in ATTRIBUTE order against
/// a per-stream `RowBasis`; the typed Map vocabulary lives at the
/// stream's header, not in every element. `[<Struct>]` per the §9.7
/// promotion the H3 carrier-cost measurement fired (carrier build
/// 4.77 µs/row = 42% of stream wall); a single-field struct over a
/// `string[]` reference copies one word — no large-struct hazard.
[<Struct>]
type RowQuantum = { Cells : string[] }

/// The per-stream column basis a `RowQuantum` is positional against. Two
/// facts, established once per stream and never per row: the column
/// `Names` in attribute order, and `NameSortedPerm` — the permutation of
/// column indices that visits them in `Name`-sorted order, so the content
/// hash reproduces `RowDigester.hashRowBytes`'s Map-sorted bytes WITHOUT
/// re-sorting each row. Private record + smart constructor (the house
/// derive-macro, §9.8.9): `NameSortedPerm` is, by construction, always a
/// valid permutation of `[0 .. Names.Length-1]`.
///
/// **Totality precondition.** A `RowQuantum` hashed or rebuilt against a
/// basis must be TOTAL over it — every column present (in-flight
/// ReadSide-origin rows always are; NULL → ""). The omit-vs-NULL
/// distinction the IR-grain `StaticRow` Map carries is deliberately NOT
/// representable here; it stays at the IR grain.
type RowBasis = private { Names : Name[]; NameSortedPerm : int[] }

[<RequireQualifiedAccess>]
module RowBasis =

    /// Build the basis from a kind's attribute names, in attribute order.
    let ofNames (names: Name list) : RowBasis =
        let arr = List.toArray names
        // Stable sort of indices by the column name's string value —
        // matches `hashRowBytes`'s `Map.toArray |> Array.sortBy Name.value`.
        let perm = Array.init arr.Length id |> Array.sortBy (fun i -> Name.value arr.[i])
        { Names = arr; NameSortedPerm = perm }

    let names (b: RowBasis) : Name[] = b.Names

    let length (b: RowBasis) : int = b.Names.Length

    /// The column indices in `Name`-sorted order — the content hash walks
    /// this, never a per-row sort.
    let nameSortedOrder (b: RowBasis) : int[] = b.NameSortedPerm

    /// Ordinal of `name` in the basis, if present. Resolved once per
    /// stream/kind by consumers (never per row) — the quantum's by-key
    /// access path (Q3).
    let tryOrdinal (name: Name) (b: RowBasis) : int option =
        b.Names |> Array.tryFindIndex (fun n -> n = name)

    /// Rename basis columns through a source→sink Name re-key map — the
    /// basis-level realization of a column rename (Q3): under a positional
    /// carrier a rename is a HEADER operation done once per stream; the
    /// quanta are untouched. Names absent from the map pass through; the
    /// name-sorted permutation is recomputed (renames can reorder names).
    /// Empty map → the same basis (the no-rename stream is byte-identical).
    let rename (map: Map<Name, Name>) (b: RowBasis) : RowBasis =
        if Map.isEmpty map then b
        else
            ofNames
                [ for n in b.Names -> Map.tryFind n map |> Option.defaultValue n ]

[<RequireQualifiedAccess>]
module RowQuantum =

    /// Project a (total) `StaticRow` onto the basis: cell i is the value
    /// for basis column i, or "" if absent (the `readRowsStream`
    /// NULL → "" convention). For an in-flight total row every column is
    /// present, so no cell defaults.
    let ofStaticRow (basis: RowBasis) (row: StaticRow) : RowQuantum =
        { Cells =
            RowBasis.names basis
            |> Array.map (fun n -> Map.tryFind n row.Values |> Option.defaultValue "") }

    /// Rebuild the value Map from a quantum (the boundary back to the IR
    /// grain — `StaticRow` reconstruction at the buffered `readRows`
    /// path). Total over the basis by construction.
    let toValues (basis: RowBasis) (q: RowQuantum) : Map<Name, string> =
        Array.zip (RowBasis.names basis) q.Cells |> Map.ofArray

    /// STAGED by-name accessor: resolve the ordinal once per kind/stream,
    /// index per row (Q3 — the quantum counterpart of `Map.tryFind name
    /// row.Values |> Option.defaultValue ""`; a name absent from the basis
    /// reads as the empty raw, exactly as an absent Map key does).
    let cellGetter (basis: RowBasis) (name: Name) : (RowQuantum -> string) =
        match RowBasis.tryOrdinal name basis with
        | Some ix -> fun q -> q.Cells.[ix]
        | None -> fun _ -> ""

[<RequireQualifiedAccess>]
module StaticRow =

    /// The Map-carried row's by-name accessor, empty-raw default — named
    /// once so the carrier-generic consumers (Q3: the capture ladder, the
    /// cell projections) read identically over both grains.
    let valueOrEmpty (name: Name) (row: StaticRow) : string =
        Map.tryFind name row.Values |> Option.defaultValue ""

    /// The IR-grain boundary: rebuild a `StaticRow` from an in-flight
    /// quantum (Q2 — `RowQuantum.ofStaticRow`'s inverse over a total row;
    /// witness `R4: ofQuantum ∘ toQuantum = id`). The caller supplies the
    /// row identity — quanta deliberately carry none (identity at row
    /// grain is the PK cell through the basis); the IR grain still does.
    let ofQuantum (basis: RowBasis) (identifier: SsKey) (q: RowQuantum) : StaticRow =
        { Identifier = identifier
          Values = RowQuantum.toValues basis q }


/// SQL Server "extended property" — a named string annotation attached to
/// a schema object. V1 source: `sys.extended_properties` (carried as
/// `ExtendedPropertyDocument` in V1's JSON projection). Chapter A.0' slice
/// ζ — IR fidelity lift (L3-S9 extended-properties sub-axiom). Mirrors
/// V1's `Osm.Domain.Model.ExtendedProperty` (Name + nullable Value).
type ExtendedProperty = {
    Name  : string
    Value : string option
}

/// Construction helper for `ExtendedProperty`. Empty `Value` strings
/// normalize to `None` per V1 parity (`ExtendedProperty.Create` collapses
/// `{ Length: 0 }` to null).
[<RequireQualifiedAccess>]
module ExtendedProperty =

    let private nameEmpty =
        ValidationError.create
            "extendedProperty.name.empty"
            "Extended property name cannot be blank."

    /// Smart constructor (A39). Rejects blank names; normalizes empty
    /// values to `None`. Pillar-9 classification: DataIntent — the
    /// value is observed source-schema metadata, no operator opinion.
    let create (name: string) (value: string option) : Result<ExtendedProperty> =
        if System.String.IsNullOrWhiteSpace name then Result.failureOf nameEmpty
        else
            let normalized =
                match value with
                | Some v when System.String.IsNullOrEmpty v -> None
                | other -> other
            Result.success { Name = name; Value = normalized }


/// SQL Server CHECK constraint at the table level. V1 source:
/// `AttributeOnDiskCheckConstraint` (Name + Definition + IsNotTrusted).
/// Chapter A.0' slice ε — IR fidelity lift (L3-S8 CHECK sub-axiom).
/// Lives at `Kind.ColumnChecks` (table-scoped; a CHECK may span multiple
/// columns per the SQL Server semantic).
type ColumnCheck = {
    SsKey        : SsKey
    Name         : Name option
    Definition   : string
    IsNotTrusted : bool
}

[<RequireQualifiedAccess>]
module ColumnCheck =

    let private definitionEmpty =
        ValidationError.create
            "columnCheck.definition.empty"
            "CHECK constraint definition cannot be blank."

    /// Smart constructor (A39). Rejects blank definitions; the name
    /// is optional (V1 surfaces unnamed CHECK constraints).
    let create
        (ssKey: SsKey)
        (name: Name option)
        (definition: string)
        (isNotTrusted: bool)
        : Result<ColumnCheck> =
        if System.String.IsNullOrWhiteSpace definition then
            Result.failureOf definitionEmpty
        else
            Result.success
                { SsKey        = ssKey
                  Name         = name
                  Definition   = definition.Trim()
                  IsNotTrusted = isNotTrusted }


/// SQL Server computed-column configuration. Carries the expression and
/// whether the computation is persisted. Chapter A.0' slice ε — IR
/// fidelity lift (L3-S7 computed-column sub-axiom). V1 source: V1's
/// model does not currently carry computed-column metadata at the
/// JSON-projection boundary; the V2 IR field is positioned for a
/// future DACPAC or rowset slice. Until populated, attributes carry
/// `Computed = None`.
type ComputedColumnConfig = {
    Expression  : string
    IsPersisted : bool
}

[<RequireQualifiedAccess>]
module ComputedColumnConfig =

    let private expressionEmpty =
        ValidationError.create
            "computedColumn.expression.empty"
            "Computed-column expression cannot be blank."

    let create (expression: string) (isPersisted: bool) : Result<ComputedColumnConfig> =
        if System.String.IsNullOrWhiteSpace expression then
            Result.failureOf expressionEmpty
        else
            Result.success
                { Expression  = expression.Trim()
                  IsPersisted = isPersisted }


/// SQL Server DML trigger. V1 source: V1's `TriggerModel` (Name +
/// IsDisabled + Definition) plus V1's JSON entity-level `triggers[]`
/// array. Chapter A.0' slice γ — IR fidelity lift (L3-S4 triggers
/// sub-axiom). Lives at `Kind.Triggers` per the domain semantic — a
/// trigger is owned by the table it fires on; the chapter open's
/// "Catalog.Triggers" planning shorthand is corrected to Kind-scoped
/// per pillar 8 (concept-shaped: a trigger IS a property of a kind).
type Trigger = {
    SsKey      : SsKey
    Name       : Name
    IsDisabled : bool
    Definition : string
}

[<RequireQualifiedAccess>]
module Trigger =

    let private definitionEmpty =
        ValidationError.create
            "trigger.definition.empty"
            "Trigger definition cannot be blank."

    /// Smart constructor (A39). Rejects blank definitions; V1's
    /// `TriggerModel.Create` enforces the same invariant.
    let create
        (ssKey: SsKey)
        (name: Name)
        (isDisabled: bool)
        (definition: string)
        : Result<Trigger> =
        if System.String.IsNullOrWhiteSpace definition then
            Result.failureOf definitionEmpty
        else
            Result.success
                { SsKey      = ssKey
                  Name       = name
                  IsDisabled = isDisabled
                  Definition = definition }


/// SQL Server SEQUENCE schema object. V1 source: V1's `SequenceModel`
/// (rich shape: schema/name/dataType/start/increment/min/max/cycle/cache).
/// Chapter A.0' slice δ — IR fidelity lift (L3-S5 sequences sub-axiom).
/// Top-level Catalog object (sequences are schema-scoped, not table-
/// scoped); `Catalog.Sequences : Sequence list`.
type SequenceCacheMode =
    /// No cache directive — server-default behavior.
    | Unspecified
    /// `CACHE n` cache size set.
    | Cache
    /// `NO CACHE` — caching disabled.
    | NoCache

type Sequence = {
    SsKey          : SsKey
    Name           : Name
    Schema         : string
    DataType       : string
    StartValue     : decimal option
    Increment      : decimal option
    Minimum        : decimal option
    Maximum        : decimal option
    IsCycleEnabled : bool
    CacheMode      : SequenceCacheMode
    CacheSize      : int option
}

[<RequireQualifiedAccess>]
module Sequence =

    let private schemaEmpty =
        ValidationError.create "sequence.schema.empty"
            "Sequence schema cannot be blank."
    let private dataTypeEmpty =
        ValidationError.create "sequence.dataType.empty"
            "Sequence data type cannot be blank."

    let create
        (ssKey: SsKey)
        (name: Name)
        (schema: string)
        (dataType: string)
        (startValue: decimal option)
        (increment: decimal option)
        (minimum: decimal option)
        (maximum: decimal option)
        (isCycleEnabled: bool)
        (cacheMode: SequenceCacheMode)
        (cacheSize: int option)
        : Result<Sequence> =
        if System.String.IsNullOrWhiteSpace schema then Result.failureOf schemaEmpty
        elif System.String.IsNullOrWhiteSpace dataType then Result.failureOf dataTypeEmpty
        else
            Result.success
                { SsKey          = ssKey
                  Name           = name
                  Schema         = schema.Trim()
                  DataType       = dataType.Trim()
                  StartValue     = startValue
                  Increment      = increment
                  Minimum        = minimum
                  Maximum        = maximum
                  IsCycleEnabled = isCycleEnabled
                  CacheMode      = cacheMode
                  CacheSize      = cacheSize }


/// SQL Server system-versioned temporal-table configuration. V1 source:
/// `TemporalTableMetadata` (HistorySchema/HistoryTable/PeriodStart/
/// PeriodEnd/Retention). Chapter A.0' slice η — IR fidelity lift,
/// closed-DU expansion (`ModalityMark.Temporal of TemporalConfig`).
/// The V1 model is rich; the V2 IR carries the operationally-relevant
/// subset: the period columns + the history-table coordinates + the
/// retention policy.
type TemporalRetentionUnit =
    | Days
    | Weeks
    | Months
    | Years

type TemporalRetention =
    /// `HISTORY_RETENTION_PERIOD = INFINITE`.
    | Infinite
    /// `HISTORY_RETENTION_PERIOD = <n> <unit>`.
    | Limited of value: int * unit: TemporalRetentionUnit

type TemporalConfig = {
    HistorySchema : string option
    HistoryTable  : string option
    PeriodStart   : Name option
    PeriodEnd     : Name option
    Retention     : TemporalRetention
}


/// Modality marks attached to a kind. A kind may carry multiple marks; the
/// representation is a list rather than a flag set so that payloaded marks
/// (Static) coexist cleanly with payload-free marks (TenantScoped,
/// SoftDeletable).
type ModalityMark =
    /// Schema-resident populations (A7).
    | Static of populations: StaticRow list
    /// Multi-tenant kind; rows are partitioned by the policy-defined tenant
    /// discriminator. The discriminator column itself is policy.
    | TenantScoped
    /// Logical deletes are represented in-row, not by physical removal.
    | SoftDeletable
    /// Stewardship marker (chapter 3.2 slice 4): the kind is owned by
    /// the V1 OutSystems platform rather than developer-authored.
    /// Sourced from V1's `ossys_Entity.Is_System` column (visible
    /// only through the rowset path; the JSON path drops the bit).
    /// Future consumers — emitters that exclude system tables from
    /// CREATE TABLE, passes that elide system-owned entities from
    /// FK reflow — walk `kind.Modality |> List.contains SystemOwned`.
    /// Payload-free per the codified ModalityMark pattern; mirrors
    /// `TenantScoped` / `SoftDeletable`.
    | SystemOwned
    /// SQL Server system-versioned temporal table. Chapter A.0' slice
    /// η — closed-DU widening. The payload carries the history-table
    /// coordinates + period columns + retention policy; emitters that
    /// understand temporal tables walk `kind.Modality` looking for
    /// the variant. Adheres to the closed-DU empirical-test
    /// discipline: pattern-match sites surface at compile time only.
    | Temporal of TemporalConfig

/// Typed structural display for `ModalityMark`. The `Static`
/// variant carries the population count (not the rows themselves)
/// — the count is the operator-relevant signal; the rows are
/// queried separately.
[<RequireQualifiedAccess>]
module ModalityMark =
    let toStructured (m: ModalityMark) : StructuredString =
        match m with
        | Static populations ->
            StructuredString.create "Static"
                [ "populations", Inv.int32 (List.length populations) ]
        | TenantScoped -> StructuredString.tag "TenantScoped"
        | SoftDeletable -> StructuredString.tag "SoftDeletable"
        | SystemOwned -> StructuredString.tag "SystemOwned"
        | Temporal config ->
            let retentionTag =
                match config.Retention with
                | Infinite -> "Infinite"
                | Limited _ -> "Limited"
            StructuredString.create "Temporal"
                [ "retention", retentionTag ]

    let toDiagnosticString (m: ModalityMark) : string =
        toStructured m |> StructuredString.render


/// Per-kind physical realization. Per session-36 audit (Agents 1, 2,
/// 3 multi-axis): unified with the schema-coordinate value object
/// `TableId` (Coordinates.fs). Same shape (`{ Schema: string; Table:
/// string }`); existing `kind.Physical.Schema` / `.Table` field
/// access unchanged. Construction now flows through `TableId.create`
/// for non-blank invariants. Future: typed `SchemaName` / `TableName`
/// VOs once a consumer pays for the explicit projection.
type PhysicalRealization = TableId


/// Per-attribute (column-level) physical realization. Decoupled from
/// `Attribute` so that policy and unfold passes can rewrite physical
/// metadata without touching logical structure.
type ColumnRealization = {
    ColumnName : ColumnName
    IsNullable : bool
    /// F1 (audit 2026-06-17) — the column's SQL Server collation, when the
    /// source declares a non-default one (`sys.columns.collation_name`, read by
    /// the OSSYS rowset adapter). `None` means "no collation opinion" — the
    /// emitter writes no `COLLATE` clause and the column inherits the database
    /// default, byte-identical to pre-F1 output. `Some name` is carried
    /// faithfully through to the emitted `COLLATE <name>` so a fresh deploy no
    /// longer silently loses the team's chosen collation. The JSON source does
    /// not expose collation, so that path stays `None`.
    Collation  : string option
    /// F10 (audit 2026-06-17) — the IDENTITY `(seed, increment)` for an identity
    /// column. `None` means "OS-native autonumber" — the emitter writes
    /// `IDENTITY(1, 1)`, the faithful default for an OutSystems autonumber and
    /// byte-identical to the prior hardcode. `Some (s, i)` lets a
    /// reflected/external identity column carry a non-default seed so it is not
    /// silently normalized to `(1, 1)`. The read side does not yet populate a
    /// non-default seed (see the F10 disposition); this makes the emission
    /// IR-driven and the IR able to express it.
    Identity   : (int64 * int64) option
}

/// Smart constructors and projections for `ColumnRealization`. Lifted
/// 2026-06-02 (slice 5b) — `ColumnName` is now the typed VO; consumers
/// reading the column-name string unwrap via `ColumnRealization.columnNameText`
/// or `ColumnName.value`.
[<RequireQualifiedAccess>]
module ColumnRealization =

    /// Boundary helper — pre-unwrapped column-name text. Use at adapter /
    /// emitter / diagnostic-formatting boundaries (SQL identifier
    /// encoding, sprintf "%s", map lookups keyed on string).
    let columnNameText (c: ColumnRealization) : string = ColumnName.value c.ColumnName

    /// Does this column's physical name equal `name` under SQL Server's
    /// default-collation semantics (case-insensitive)? The one name for the
    /// column-identifier comparison — a raw `=` is the latent bug
    /// (`CONSTELLATION_BACKLOG.md` plane N3): SQL treats `CustomerId` and
    /// `CUSTOMERID` as one column, so a case-sensitive lookup silently
    /// fails to resolve an operator's differently-cased ref. (Pre-existing
    /// adopters of this policy, not yet migrated to this name:
    /// `Policy.fs:82`, `OssysRowsetReader.fs:325`.)
    let columnNameEquals (name: string) (c: ColumnRealization) : bool =
        System.String.Equals(columnNameText c, name, System.StringComparison.OrdinalIgnoreCase)

    /// Build a `ColumnRealization` from a raw column-name string.
    /// Validates non-blank + ≤128-char identifier limit via
    /// `ColumnName.create`; returns `Result`. Use at adapter
    /// construction sites (SQL reads, JSON codec) where the input
    /// is a raw string.
    let create (columnName: string) (isNullable: bool) : Result<ColumnRealization> =
        ColumnName.create columnName
        |> Result.map (fun cn -> { ColumnName = cn; IsNullable = isNullable; Collation = None; Identity = None })

    /// Build a `ColumnRealization` from an already-validated `ColumnName`.
    /// Total — no validation needed since the input is already typed.
    let fromTyped (columnName: ColumnName) (isNullable: bool) : ColumnRealization =
        { ColumnName = columnName; IsNullable = isNullable; Collation = None; Identity = None }

    /// F1 (audit 2026-06-17) — carry a source-declared collation onto an
    /// already-built `ColumnRealization`. The adapter read path uses this when
    /// `sys.columns.collation_name` is present; `None` is the identity (no
    /// collation opinion). Keeps `create`/`fromTyped` at their 2-arg shape so
    /// the ~300 callers that have no collation evidence are untouched.
    let withCollation (collation: string option) (c: ColumnRealization) : ColumnRealization =
        { c with Collation = collation }

    /// F10 (audit 2026-06-17) — carry a non-default IDENTITY `(seed, increment)`
    /// onto an already-built `ColumnRealization`. `None` is the OS-native
    /// default `(1, 1)`. The reflected/external-table read populates this when a
    /// seed read is wired (the named follow-on); sibling to `withCollation`.
    let withIdentity (identity: (int64 * int64) option) (c: ColumnRealization) : ColumnRealization =
        { c with Identity = identity }


/// Reference action at the target side. Mirrored from the standard
/// foreign-key DELETE/UPDATE cascade vocabulary.
type ReferenceAction =
    | NoAction
    | Cascade
    | SetNull
    | Restrict


/// A scalar attribute on a kind.
///
/// `IsPrimaryKey` flags whether this attribute participates in its kind's
/// primary key. Composite primary keys are expressed by flagging multiple
/// attributes on the same kind. This field was added under the discipline
/// "IR grows under evidence" (see DECISIONS.md): the V1 admire pass on
/// `EntitySeedDeterminizer` (see ADMIRE.md) needs PK columns to drive
/// row-order normalization, and the eventual `Projection.Targets.SSDT`
/// FK emitter needs PK columns to resolve target-side references.
///
/// `IsMandatory` flags whether the source model declares this attribute
/// as logically mandatory — V1's OutSystems model carries this as a
/// metadata flag distinct from physical NOT NULL. The flag drives
/// `NullabilityRules`'s mandatory signal hierarchy. Added under
/// "IR grows under evidence" (DECISIONS.md, 2026-05-10): the
/// `NullabilityEvaluator` end-to-end test (session 6) surfaced the gap;
/// V2 closes it here so the V1 mandatory branches can fire.
type Attribute = {
    SsKey        : SsKey
    Name         : Name
    Type         : PrimitiveType
    Column       : ColumnRealization
    IsPrimaryKey : bool
    IsMandatory  : bool
    /// NVARCHAR / VARCHAR / CHAR / NCHAR / VARBINARY / BINARY:
    /// the declared length. `None` for MAX (open-ended) or for
    /// types where length is not applicable (Integer / Boolean
    /// / DateTime / etc.). Per session-32 — ReadSide populates
    /// from `INFORMATION_SCHEMA.COLUMNS.CHARACTER_MAXIMUM_LENGTH`;
    /// `-1` from SQL Server (the MAX marker) maps to `None`.
    Length       : int option
    /// DECIMAL / NUMERIC: the declared precision. `None` for
    /// non-decimal types. ReadSide populates from
    /// `INFORMATION_SCHEMA.COLUMNS.NUMERIC_PRECISION`.
    Precision    : int option
    /// DECIMAL / NUMERIC: the declared scale. `None` for non-
    /// decimal types. ReadSide populates from
    /// `INFORMATION_SCHEMA.COLUMNS.NUMERIC_SCALE`.
    Scale        : int option
    /// IDENTITY column property (`INT NOT NULL IDENTITY(1,1)` →
    /// `IsIdentity = true`). Per session-32 — V2 IR carries the
    /// boolean; seed and increment values are deferred (always
    /// emit `IDENTITY(1,1)` when set, which matches the
    /// OutSystems convention). ReadSide reads from
    /// `sys.columns.is_identity` (1 → true).
    IsIdentity   : bool
    /// Operator-visible docstring carried from the V1 source
    /// `ossys_EntityAttr.Description` (rowset path) or the
    /// `description` JSON property (JSON path). `None` when the
    /// source omits the field. Chapter A.0' slice α — IR fidelity
    /// lift (L3-S9 descriptions sub-axiom). Emission lands when a
    /// consumer demands it (extended-properties DDL is chapter
    /// 4.1.A slice 8 territory; the `CommentMetadataUnreflected`
    /// Tolerance variant retires when both the IR carries AND the
    /// emitter emits).
    Description  : string option
    /// V1 lifecycle flag carried from `ossys_EntityAttr.Is_Active`
    /// (rowset path) or the `isActive` JSON property (JSON path).
    /// V1's SQL coerces missing/null source values to `true` per
    /// `outsystems_metadata_rowsets.sql:94, 116, 239`; the V2 adapter
    /// mirrors that semantic (absent JSON → `true`). Chapter A.0'
    /// slice β — IR fidelity lift (L3-S9 IsActive sub-axiom). The
    /// pre-slice-β session-21 adapter-boundary filter dropped
    /// `IsActive=false` attributes silently; slice β retires that
    /// disposition per pillar-9 harvest-dichotomy (filtering is
    /// `OperatorIntent`, mis-placed at the adapter which carries
    /// only `DataIntent`). Carriage-only in this slice — any
    /// Selection-axis filter pass that re-applies an inactive-records
    /// drop policy lands when a consumer demands it.
    IsActive     : bool
    /// SQL Server DEFAULT constraint expression for this column.
    /// `None` when the source omits a default. Chapter A.0' slice ε
    /// — IR fidelity lift (L3-S6 DEFAULT sub-axiom). Sourced from
    /// V1's JSON `attribute.default` (currently always `null` in
    /// V1 fixtures; the IR field is positioned for when V1 begins
    /// projecting defaults or when DACPAC adoption surfaces them).
    /// Carried as a typed `SqlLiteral` per pillar 1 (data-structure-
    /// oriented; the IR consumes typed values, not raw strings).
    DefaultValue : SqlLiteral option
    /// Named DEFAULT constraint identity. `None` when V2 emits an
    /// auto-named DEFAULT (SQL Server generates `DF_<table>_<column>_<hash>`);
    /// `Some name` round-trips V1's deployed-target constraint name
    /// (V1 source: `AttributeOnDiskDefaultConstraint.Name`). Slice
    /// 5.3.α.column-axis-deferral-closeout (matrix row 53 partial cash-out).
    /// Carriage-only: the realization layer's `ColumnDef.DefaultName`
    /// already accepts the optional identifier; SsdtDdlEmitter threads
    /// the value through without further transformation. The
    /// `IsNotTrusted` axis (row 53 full envelope) deferred-with-trigger
    /// pending V2 emission of WITH NOCHECK CHECK CONSTRAINT for defaults.
    DefaultName  : Name option
    /// SQL Server computed-column configuration. `None` for
    /// non-computed columns. Chapter A.0' slice ε — IR fidelity lift
    /// (L3-S7 computed-column sub-axiom). V1's source JSON does not
    /// currently carry computed-column metadata; positioned for a
    /// future DACPAC or rowset slice.
    Computed     : ComputedColumnConfig option
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this column. Empty when the source carries none. Chapter A.0'
    /// slice ζ — IR fidelity lift (L3-S9 extended-properties
    /// sub-axiom; attribute level).
    ExtendedProperties : ExtendedProperty list
    /// Prior attribute name carried from V1's `ossys_EntityAttr.OriginalName`
    /// (JSON `originalName`). Set when an attribute has been renamed in
    /// the model; `None` when no rename history exists. Carriage-only —
    /// any rename-aware consumer (refactor-log emitter, V1-parity
    /// migration paths) lands when triggered. Chapter 4.9 slice β —
    /// retires one of two A.0'-deferred-out-of-scope concepts under
    /// explicit principal-PO direction (`CHAPTER_4_9_OPEN.md` §Why).
    OriginalName : string option
    /// User-specified external SQL Server database type for external
    /// entities (V1's `ossys_EntityAttr.ExternalColumnType`; JSON
    /// `external_dbType`). `None` for OS-native entities and for
    /// external entities where V1 omits the override. V2's
    /// `PrimitiveType` abstraction (A13) remains canonical for
    /// emission; this field is fidelity carriage for round-trip
    /// reconstruction and future external-entity DDL paths.
    /// Chapter 4.9 slice β.
    ExternalDatabaseType : string option
    /// Concrete SQL Server storage type when source evidence names it.
    /// `Type` (the semantic `PrimitiveType`) is always present; this
    /// field carries the *concrete realization* the OSSYS adapter
    /// resolves from `ossys_EntityAttr.Type` (`rtLongInteger` →
    /// `BigInt`, `rtDateTime` → `DateTime`, ...) or from an
    /// `external_dbType` override. `None` for sources that carry only
    /// the semantic category (test fixtures; `ReadSide`'s structural
    /// reflection); emitters then fall back to the `PrimitiveType` →
    /// `SqlDataTypeOption` mapping. Invariant:
    /// `SqlStorageType.toPrimitiveType storage = Type` whenever
    /// `Some storage`.
    SqlStorage : SqlStorageType option
    /// Service-Studio authored attribute order, carried from the real
    /// `ossys_Entity_Attr.Order_Num` column (rowset path) or the
    /// `order` JSON property (JSON path). `None` for hand-built
    /// catalogs and for the `ReadSide` reflection path (deployed
    /// schema carries no OutSystems authored order). WP8 / NM-72 —
    /// emission column order is `(PK first, then Order ascending,
    /// then SsKey as a stable tiebreak)`; `None` falls back to the
    /// existing PK-first / SsKey order so determinism (T1) holds for
    /// every source. The ordering is applied at `CanonicalizeIdentity`
    /// (the pass that already owns the canonical attribute order),
    /// inherited uniformly by the SSDT, dacpac, and data-lane emitters
    /// which all iterate `Kind.Attributes` in list order.
    Order : int option
}


/// The constraint-state of a `Reference` (M4 — THE VECTOR §6 Kind II).
/// Collapses the `(HasDbConstraint, IsConstraintTrusted)` `bool × bool`
/// quadrant — a 4-state space encoding a 3-state semantic reality — into a
/// closed DU. The illegal quadrant `(HasDbConstraint = false,
/// IsConstraintTrusted = false)` ("untrusted without a constraint", the G14
/// debrief finding) typechecked but is semantically impossible: trust is only
/// meaningful when a DB constraint exists (`¬trusted ⟹ hasDbConstraint`). The
/// DU forbids it by construction — promoting the prior *runtime* invariant
/// (`Reference.isConstraintStateConsistent` + the aggregate-root rejection) to
/// a *type theorem*. Mirrors `IndexUniqueness` (Slice 2a), the sibling
/// quadrant→DU collapse on the same record family, and follows the archetype
/// `CapabilityProfile.of` precedent (closed DU → derived projection →
/// round-trip law).
///
/// **Ordering carries semantics**: a trusted constraint is "stronger than" an
/// untrusted one is "stronger than" no constraint. Strategy / emission
/// surfaces pattern-match the variant directly; sites that need the boolean
/// projection use `ConstraintState.hasDbConstraint` / `isConstraintTrusted`.
/// The codec keeps the legacy boolean pair on the wire (`toLegacyBooleans` on
/// write, `ofLegacyBooleans` on read) so serialized catalogs round-trip
/// byte-identically — no store migration (the `IndexUniqueness` wire precedent).
///
/// `[<RequireQualifiedAccess>]` — the `TrustedConstraint` case name otherwise
/// collides with `ProbeOutcome.TrustedConstraint`; qualification also makes the
/// variants unforgeable at every match site.
[<RequireQualifiedAccess>]
type ConstraintState =
    /// No backing SQL Server FK constraint — the reference is logical-only (an
    /// OutSystems-model FK with no `FOREIGN KEY` clause). Trust is N/A
    /// (vacuously trusted). Boolean projection: `(false, true)`. The
    /// `Reference.create` default (V1's COALESCE-to-0 `HasFK`).
    | NoDbConstraint
    /// A real, TRUSTED FK constraint — created normally, or re-validated after
    /// a `WITH NOCHECK` insert. Boolean projection: `(true, true)`. The V1
    /// default for a reflected constraint.
    | TrustedConstraint
    /// A real but UNTRUSTED FK constraint (`WITH NOCHECK` — created over
    /// existing unvalidated rows). Boolean projection: `(true, false)`; fires
    /// the post-CREATE-TABLE NOCHECK alter (`SsdtDdlEmitter.untrustedFkAlters`).
    /// Carries from V1 `#FkReality.IsNoCheck = 1`.
    | UntrustedConstraint

[<RequireQualifiedAccess>]
module ConstraintState =

    /// True iff a real DB constraint backs the reference (`Trusted` /
    /// `Untrusted`). Sites that only need the boolean "is there a constraint?"
    /// projection use this without pattern-matching.
    let hasDbConstraint (s: ConstraintState) : bool =
        match s with
        | ConstraintState.TrustedConstraint | ConstraintState.UntrustedConstraint -> true
        | ConstraintState.NoDbConstraint -> false

    /// True iff the constraint is trusted. `NoDbConstraint` is *vacuously*
    /// trusted (trust is N/A absent a constraint) — the canonical `(false,
    /// true)` projection.
    let isConstraintTrusted (s: ConstraintState) : bool =
        match s with
        | ConstraintState.UntrustedConstraint -> false
        | ConstraintState.NoDbConstraint | ConstraintState.TrustedConstraint -> true

    /// Build a `ConstraintState` from the legacy `(hasDbConstraint,
    /// isConstraintTrusted)` boolean pair. Adapters reading external formats
    /// (V1 JSON, SQL reflection rows, the codec) project their booleans through
    /// this helper to reach the typed surface. The illegal combination
    /// `(false, false)` ("untrusted without a constraint") normalizes to
    /// `NoDbConstraint` — a constraint-less reference is vacuously trusted (the
    /// prior `Reference.withConstraintState` normalization, now total and at the
    /// type level).
    let ofLegacyBooleans (hasDbConstraint: bool) (isConstraintTrusted: bool) : ConstraintState =
        match hasDbConstraint, isConstraintTrusted with
        | false, _     -> ConstraintState.NoDbConstraint
        | true,  true  -> ConstraintState.TrustedConstraint
        | true,  false -> ConstraintState.UntrustedConstraint

    /// The legacy `(hasDbConstraint, isConstraintTrusted)` boolean pair — the
    /// derived projection the codec writes to the wire. Round-trip law:
    /// `ofLegacyBooleans ∘ toLegacyBooleans = id` on every variant (witnessed in
    /// `ReferenceConstraintStateTests`).
    let toLegacyBooleans (s: ConstraintState) : bool * bool =
        hasDbConstraint s, isConstraintTrusted s


/// A directional reference (A10). Symmetry, if needed by a target surface,
/// is introduced by the symmetric-closure pass and the resulting reference
/// carries a `Derived` SsKey with reason `"inverse"`.
///
/// **`IsUserFk` field added at chapter 4.2 slice ζ** — per
/// `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §5: identifies references
/// whose target is the platform user kind, so data-emission siblings
/// (chapter 4.1.B MigrationDependenciesEmitter + BootstrapEmitter)
/// can rewrite `CreatedBy` / `UpdatedBy` column values via the
/// chapter 4.2 `UserRemapContext`. The OSSYS adapter resolves the
/// flag at construction (per the V1 reference
/// `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys`); test
/// fixtures and non-user-FK adapter sites default to `false`.
type Reference = {
    SsKey           : SsKey
    Name            : Name
    SourceAttribute : SsKey
    TargetKind      : SsKey
    OnDelete        : ReferenceAction
    /// True iff this reference's `TargetKind` resolves to the
    /// platform user kind (the OSSYS-native users entity in V1's
    /// model). Set by the OSSYS adapter when it can identify the
    /// user kind via the platform's `extension_id` lookup; false
    /// otherwise. Slice η emitters consume this flag to gate User-
    /// FK column rewriting at row-emission time.
    IsUserFk        : bool
    /// Optional ON UPDATE referential action. `None` = unstated (V1
    /// default; SQL Server emits no ON UPDATE clause, server-default
    /// NO ACTION applies). `Some action` = operator-supplied explicit
    /// action carried from V1's `#FkReality.UpdateAction` column. The
    /// SSDT emitter consumes via ScriptDom's
    /// `ForeignKeyConstraintDefinition.UpdateAction`.
    ///
    /// Slice 5.13.fk-features-emit (matrix row 58 cash-out).
    OnUpdate        : ReferenceAction option
    /// The constraint-state of this reference (M4 — THE VECTOR §6 Kind II):
    /// the `ConstraintState` DU that replaced the prior `(HasDbConstraint,
    /// IsConstraintTrusted)` boolean pair (chapter 4.6 slice α IR lift +
    /// slice 5.13.fk-features-emit row 59). `NoDbConstraint` = logical-only
    /// FK (V1's COALESCE-to-0 `HasFK`); `TrustedConstraint` = a real trusted
    /// constraint (the V1 default); `UntrustedConstraint` = `WITH NOCHECK`
    /// (from V1 `#FkReality.IsNoCheck = 1`), firing the post-CREATE-TABLE
    /// NOCHECK alter. The illegal "untrusted without a constraint" quadrant is
    /// unrepresentable. Boolean-projection sites use `Reference.hasDbConstraint`
    /// / `Reference.isConstraintTrusted`.
    ConstraintState : ConstraintState
}


/// Per-column sort direction within an `Index`. Chapter 4.9 slice γ
/// — retires the third A.0'-deferred concept (`IndexColumnDirection`).
/// SQL Server emits the keyword DESC after a column name when the
/// direction is `Descending`; `Ascending` is the default and
/// ScriptDom's `SortOrder.NotSpecified` carries it (matching V1's
/// `IndexScriptBuilder` convention which sets `SortOrder` only on
/// descending columns).
type IndexColumnDirection =
    | Ascending
    | Descending

/// One key column within an `Index.Columns` ordered list. Carries
/// the participating `Attribute` SsKey + the per-column sort
/// direction. Chapter 4.9 slice γ — record-modification of the prior
/// `SsKey list` shape. Included columns (covering indexes) stay on
/// `Index.IncludedColumns : SsKey list` — non-key columns carry no
/// direction in SQL Server.
type IndexColumn = {
    Attribute : SsKey
    Direction : IndexColumnDirection
}

[<RequireQualifiedAccess>]
module IndexColumn =

    /// Build one `IndexColumn` with the given attribute + direction.
    /// Slice 5.13.shim-retirement (2026-05-18) — lifted from the
    /// test-side `IRBuilders.mkIndexColumn` so production code paths
    /// (and the test surface uniformly) name the IndexColumn shape
    /// through the production module.
    let create (attribute: SsKey) (direction: IndexColumnDirection) : IndexColumn =
        { Attribute = attribute; Direction = direction }

    /// Build an all-Ascending `IndexColumn list` from a list of
    /// attribute SsKeys. The common shape for consumers that don't
    /// care about per-column sort direction (most indexes; V1
    /// defaults SortOrder to ASC and DESC requires an explicit
    /// override). Slice 5.13.shim-retirement (2026-05-18) — lifted
    /// from the test-side `IRBuilders.mkIndexColumns`.
    let ascendingList (attributes: SsKey list) : IndexColumn list =
        attributes |> List.map (fun a -> create a Ascending)


/// SQL Server `DATA_COMPRESSION` levels for an index (or partition
/// range). Mirrors ScriptDom's `DataCompressionLevel` enum modulo
/// the columnstore variants (which V1 doesn't surface and V2 has no
/// fixture evidence for; lift trigger: an actual columnstore-bearing
/// index surfaces in production). Slice 5.13.index-features-emit
/// (matrix row 56).
[<RequireQualifiedAccess>]
type DataCompressionLevel =
    | None
    | Row
    | Page

/// SQL Server index storage placement — the dataspace an index resides
/// on. Closed-DU mirroring V1's `IndexDataSpace.cs` aggregate
/// (`Name : string`, `Type : DataSpaceType`). Per matrix row 56 cash-
/// out shape: closed-DU `DataSpace = Filegroup of name | PartitionScheme
/// of name × columns`. Slice A.4.7'-prelude.row56-dataspace (LR7
/// closure).
///
/// **Variants.**
///   - `Filegroup name` — index resides on a named filegroup
///     (`PRIMARY`, `INDEX_FG`, etc.). Emitted as `ON [name]`.
///     V1 source: `sys.data_spaces.type_desc = 'ROWS_FILEGROUP'`.
///   - `PartitionScheme (name, columns)` — index uses a partition
///     scheme keyed by named partition columns. Emitted as
///     `ON [name]([col1], [col2], ...)`. Columns reference table
///     columns (typically the partition key); V1 carries them as
///     names (not SsKeys) from the `sys.index_columns` reflection
///     where `partition_ordinal > 0`. V2 mirrors as `string list`
///     at the IR layer; emitter resolves to ScriptDom identifiers.
///     V1 source: `sys.data_spaces.type_desc = 'PARTITION_SCHEME'`
///     + `OUTER APPLY sys.index_columns WHERE partition_ordinal > 0`.
///
/// `Index.DataSpace = None` (the default) means no explicit `ON`
/// clause — SQL Server inherits the table-level dataspace
/// (typically `PRIMARY` filegroup). This matches V1's behavior
/// when no dataspace is specified.
[<RequireQualifiedAccess>]
type DataSpace =
    | Filegroup of name: string
    | PartitionScheme of name: string * columns: string list

/// Uniqueness discriminator for indexes (Slice 2a, 2026-06-02). Replaces
/// the prior `(IsUnique : bool, IsPrimaryKey : bool)` boolean tuple, which
/// was a 4-state space encoding a 3-state semantic reality: the
/// `(IsUnique = false, IsPrimaryKey = true)` quadrant typechecked but is
/// semantically impossible (a primary-key index is unique by definition).
/// The DU forbids that quadrant by construction.
///
/// **Ordering carries semantics**: a PK is "stronger than" a unique
/// index is "stronger than" a non-unique index. Strategy/diagnostic
/// surfaces can pattern-match the variant directly; sites that just need
/// the boolean projection use `IndexUniqueness.isUnique` /
/// `isPrimaryKey` helpers.
type IndexUniqueness =
    /// Ordinary non-unique index — duplicates allowed.
    | NotUnique
    /// Uniqueness-enforcing index (not the primary key). Source treats
    /// duplicates in the keyed columns as a constraint violation.
    | Unique
    /// The kind's primary-key index. V1 treats PK as a unique index;
    /// V2 distinguishes them structurally because emission +
    /// diagnostics differ (PK is implicitly unique; uniqueness is
    /// expressed via the constraint shape).
    | PrimaryKey

[<RequireQualifiedAccess>]
module IndexUniqueness =

    /// True for `Unique` or `PrimaryKey` (PK is unique by definition).
    /// Sites that only need the boolean "does this index enforce
    /// uniqueness?" projection use this helper without pattern-matching.
    let isUnique (u: IndexUniqueness) : bool =
        match u with
        | Unique | PrimaryKey -> true
        | NotUnique -> false

    /// True iff this is the kind's primary-key index.
    let isPrimaryKey (u: IndexUniqueness) : bool =
        match u with
        | PrimaryKey -> true
        | _ -> false

    /// Build an `IndexUniqueness` from the legacy `(isUnique, isPrimaryKey)`
    /// boolean pair. Adapters reading from external formats (V1 JSON, SQL
    /// reflection rows) project their booleans through this helper to
    /// reach the typed surface. The legacy "illegal" combination
    /// `(isUnique = false, isPrimaryKey = true)` is treated as
    /// `PrimaryKey` (PK is unique by definition; the legacy `IsUnique`
    /// boolean was redundant when `IsPrimaryKey = true`).
    let ofLegacyBooleans (isUnique: bool) (isPrimaryKey: bool) : IndexUniqueness =
        match isUnique, isPrimaryKey with
        | _,     true  -> PrimaryKey
        | true,  false -> Unique
        | false, false -> NotUnique


/// A schema-level index on a kind. Carries identity, name, the
/// participating attribute SsKeys (in declaration order; composite
/// indexes have multiple), and `Uniqueness` (the 3-state
/// `IndexUniqueness` DU — Slice 2a, 2026-06-02; replaces the prior
/// `(IsUnique : bool, IsPrimaryKey : bool)` boolean tuple).
///
/// Added under "IR grows under evidence" (DECISIONS.md, 2026-05-10):
/// the V1 `UniqueIndexDecisionOrchestrator` admire (ADMIRE.md
/// 2026-05-10) requires per-index decisions; the synthetic milestone
/// covered PK + FK only and didn't model unique indexes. V2's
/// `UniqueIndexRules` + `UniqueIndexPass` (session 7 commit 5) consume
/// this field; emitters that render `CREATE UNIQUE INDEX` walk it.
///
/// V1 `Index.Columns` includes both key columns and "included"
/// (non-key) columns; V2's `Columns` carries only key columns. The
/// V1↔V2 adapter (when it lands) drops included columns at the
/// boundary per the 2026-05-10 vestigial-fields convention.
type Index = {
    SsKey        : SsKey
    Name         : Name
    /// Key columns in declaration order. Each entry carries the
    /// attribute SsKey + per-column sort direction. Chapter 4.9
    /// slice γ — record-modification from `SsKey list` to
    /// `IndexColumn list`.
    Columns      : IndexColumn list
    /// Uniqueness discriminator (Slice 2a, 2026-06-02): `NotUnique` /
    /// `Unique` / `PrimaryKey`. Replaces the prior
    /// `(IsUnique : bool, IsPrimaryKey : bool)` boolean tuple; see
    /// `IndexUniqueness` for the rationale.
    Uniqueness   : IndexUniqueness
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this index. Empty when the source carries none. Chapter A.0'
    /// slice ζ — IR fidelity lift (L3-S9 extended-properties
    /// sub-axiom; index level).
    ExtendedProperties : ExtendedProperty list
    /// SQL Server filter predicate for filtered indexes. Carried as
    /// the raw V1 filter-definition string (mirrors V1's
    /// `IndexOnDiskMetadata.FilterDefinition`); parsed at emit time
    /// via `TSql160Parser.ParseExpression` into ScriptDom's
    /// `BooleanExpression`. `None` for unfiltered indexes (V1
    /// default).
    ///
    /// Chapter 4.5 slice α — IR fidelity lift retiring chapter 4.4's
    /// `HasFilteredIndex` always-false PredicateName variant. Source:
    /// V1's JSON `index.filterDefinition` projection (rowset path
    /// reads `sys.indexes.filter_definition`).
    Filter       : string option
    /// SQL Server INCLUDE columns for covering indexes. V1's
    /// `IndexColumnModel.IsIncluded = true` entries land here at the
    /// adapter boundary (the V2-pre-chapter-4.5 adapter dropped them
    /// per the documented ADMIRE divergence; slice β retires the drop).
    /// Empty for indexes without included columns (V1 default).
    ///
    /// Chapter 4.5 slice β — IR fidelity lift retiring chapter 4.4's
    /// `HasIncludedIndexColumns` always-false PredicateName variant.
    /// Source: V1's JSON `index.columns[]` entries with
    /// `isIncluded: true` (rowset path: index-columns rowset).
    /// Ordered by V1 `ordinal` field at the adapter boundary
    /// (same shape as `Columns` key-column ordering).
    IncludedColumns : SsKey list
    /// True iff this index is OutSystems-platform-auto-generated (V1
    /// `IndexModel.IsPlatformAuto`). Auto indexes are emitted by the
    /// V1 platform's index-creation logic; V2 inherits via the
    /// adapter. Used by future emitters to gate whether to include
    /// platform-auto indexes in the SSDT bundle (operator-toggle).
    ///
    /// Chapter 4.6 slice β — IR fidelity lift retiring one of four
    /// A.0' deferred concepts (OriginalName / ExternalDatabaseType /
    /// IndexColumnDirection / IsPlatformAuto). Source: V1's JSON
    /// `index.isPlatformAuto` projection.
    IsPlatformAuto : bool
    /// SQL Server `FILLFACTOR` index option (per-index allocation
    /// density 1-100). `None` = server default. Mirrors V1's
    /// `IndexOnDiskMetadata.FillFactor`. Chapter 4.8 slice β.
    FillFactor : int option
    /// SQL Server `PAD_INDEX` option. `false` (V1 default) = OFF;
    /// `true` = ON (apply FILLFACTOR to non-leaf intermediate pages).
    /// Mirrors V1's `IndexOnDiskMetadata.IsPadded`. Chapter 4.8 slice β.
    IsPadded : bool
    /// SQL Server `ALLOW_ROW_LOCKS` option. `true` (V1 default) = ON.
    /// Mirrors V1's `IndexOnDiskMetadata.AllowRowLocks`. Chapter 4.8 slice β.
    AllowRowLocks : bool
    /// SQL Server `ALLOW_PAGE_LOCKS` option. `true` (V1 default) = ON.
    /// Mirrors V1's `IndexOnDiskMetadata.AllowPageLocks`. Chapter 4.8 slice β.
    AllowPageLocks : bool
    /// SQL Server `STATISTICS_NORECOMPUTE` option. `false` (V1 default)
    /// = OFF (auto-update enabled). Mirrors V1's
    /// `IndexOnDiskMetadata.NoRecomputeStatistics`. Chapter 4.8 slice β.
    NoRecomputeStatistics : bool
    /// SQL Server `IGNORE_DUP_KEY` option. `false` (V1 default) =
    /// OFF (a duplicate-key insert fails the entire statement); `true`
    /// = ON (the duplicate row is silently skipped, the statement
    /// succeeds for the other rows). Mirrors V1's
    /// `IndexOnDiskMetadata.IgnoreDuplicateKey`. Emitted as
    /// `IGNORE_DUP_KEY = ON` in the CREATE INDEX `WITH (…)` clause.
    /// Slice 5.13.index-features-emit (matrix row 55).
    IgnoreDuplicateKey : bool
    /// SQL Server index disable state. `false` (V1 default) = enabled
    /// (the index participates in query plans and is maintained on
    /// data changes); `true` = disabled (the index is preserved in
    /// metadata but its B-tree is dropped — restored only by a
    /// REBUILD). Mirrors V1's `IndexOnDiskMetadata.IsDisabled`.
    /// Emitted as a post-CREATE-INDEX `ALTER INDEX [name] ON [table]
    /// DISABLE` statement (ScriptDom's `AlterIndexStatement` with
    /// `AlterIndexType.Disable`), since the disable state is not a
    /// CREATE-INDEX clause. Slice 5.13.index-features-emit (matrix
    /// row 55).
    IsDisabled : bool
    /// SQL Server `DATA_COMPRESSION` option. `None` (V1 default) =
    /// no explicit DATA_COMPRESSION clause emitted (server inherits
    /// table-level or partition-level setting). `Some level` =
    /// explicit `DATA_COMPRESSION = NONE | ROW | PAGE` in the
    /// CREATE INDEX `WITH (…)` clause. Mirrors V1's
    /// `IndexOnDiskMetadata.DataCompression` (single-value form;
    /// per-partition-range compression deferred to a follow-up
    /// slice when partitioned indexes surface in fixture data).
    /// Slice 5.13.index-features-emit (matrix row 56).
    DataCompression : DataCompressionLevel option
    /// SQL Server index dataspace placement. `None` (the default) =
    /// no explicit `ON` clause; SQL Server inherits the table-level
    /// dataspace (typically `PRIMARY`). `Some (Filegroup name)` =
    /// emit `ON [name]` (V1's `IndexDataSpace.Type = ROWS_FILEGROUP`
    /// shape). `Some (PartitionScheme (name, cols))` = emit
    /// `ON [name]([col1], [col2], …)` (V1's `Type = PARTITION_SCHEME`
    /// shape). Closed-DU per matrix row 56 cash-out (LR7 closure).
    /// Slice A.4.7'-prelude.row56-dataspace.
    DataSpace : DataSpace option
}


/// A kind: the schema-level entity type. Carries identity, name, origin,
/// modality marks, physical realization, attributes, references, and
/// indexes (A8).
type Kind = {
    SsKey       : SsKey
    Name        : Name
    Origin      : Origin
    Modality    : ModalityMark list
    Physical    : PhysicalRealization
    Attributes  : Attribute list
    References  : Reference list
    Indexes     : Index list
    /// Operator-visible docstring carried from the V1 source
    /// `ossys_Entity.Description` (rowset path) or the `description`
    /// JSON property (JSON path). `None` when the source omits the
    /// field. Chapter A.0' slice α — IR fidelity lift (L3-S9
    /// descriptions sub-axiom). Sibling to `Attribute.Description`;
    /// same operational semantics (carriage-only at this slice).
    Description : string option
    /// V1 lifecycle flag carried from `ossys_Entity.Is_Active`
    /// (rowset path) or the entity-level `isActive` JSON property
    /// (JSON path). Same default-true semantics as
    /// `Attribute.IsActive`. Chapter A.0' slice β — IR fidelity
    /// lift; retires the session-21 entity-level adapter-boundary
    /// filter at `parseKind`. Sibling to `Module.IsActive` and
    /// `Attribute.IsActive`; downstream emitters decide.
    IsActive    : bool
    /// SQL Server DML triggers attached to this kind. V1 source:
    /// JSON entity-level `triggers[]` array (carries name +
    /// isDisabled + definition). Chapter A.0' slice γ — IR fidelity
    /// lift (L3-S4 triggers sub-axiom). Empty when the source
    /// projects no triggers.
    Triggers    : Trigger list
    /// SQL Server CHECK constraints at the table level. V1 source:
    /// V1's `AttributeOnDiskCheckConstraint` (column-level CHECK
    /// constraints are exposed via the on-disk metadata channel,
    /// not the JSON-projection boundary today). Chapter A.0' slice ε
    /// — IR fidelity lift (L3-S8 CHECK sub-axiom). Kind-scoped
    /// because a CHECK can span multiple columns (SQL Server
    /// semantic). Empty when the source projects no CHECK
    /// constraints.
    ColumnChecks : ColumnCheck list
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this kind. V1 source: V1's JSON entity-level
    /// `extendedProperties[]` array. Chapter A.0' slice ζ — IR
    /// fidelity lift (L3-S9 extended-properties sub-axiom; entity
    /// level).
    ExtendedProperties : ExtendedProperty list
}


/// A coproduct cell of the catalog (A11). Modules are disjoint by SsKey;
/// the projection respects the decomposition (T2).
type Module = {
    SsKey    : SsKey
    Name     : Name
    Kinds    : Kind list
    /// V1 lifecycle flag carried from `ossys_Espace.Is_Active`
    /// (rowset path; `ModuleRow.IsActive`) or the module-level
    /// `isActive` JSON property (JSON path). Same default-true
    /// semantics as `Kind.IsActive` / `Attribute.IsActive`.
    /// Chapter A.0' slice β — IR fidelity lift; retires the
    /// `parseRowsetBundle` module-level filter that previously
    /// dropped `IsActive=false` modules silently. The JSON path's
    /// `parseDocument` did not previously filter modules (Subagent
    /// #3's O2 finding on `module.isActive: false`); slice β adds
    /// JSON-path module-level read for parity.
    IsActive : bool
    /// SQL Server `sys.extended_properties` annotations attached to
    /// this module's schema. V1 does not currently project module-
    /// level extended properties; positioned for future expansion.
    /// Chapter A.0' slice ζ — IR fidelity lift (L3-S9 extended-
    /// properties sub-axiom; module level).
    ExtendedProperties : ExtendedProperty list
}


/// The whole catalog: a coproduct over modules.
type Catalog = {
    Modules : Module list
    /// SQL Server SEQUENCE schema objects. Top-level Catalog field
    /// because sequences are schema-scoped, not table-scoped (they
    /// can be referenced by multiple tables or by application code).
    /// Chapter A.0' slice δ — IR fidelity lift (L3-S5 sequences
    /// sub-axiom). V1's JSON projection does not currently surface
    /// sequences at the catalog boundary; populated empty until a
    /// future source (DACPAC adapter; expanded V1 rowsets) provides
    /// the evidence.
    Sequences : Sequence list
}


/// Slice 5.13.fk-features-emit (smart-constructor closure) — production-
/// side smart constructors for the IR aggregate records that lacked
/// them. The pattern mirrors the test-side `IRBuilders.mkX` helpers
/// (`tests/Projection.Tests/IRBuilders.fs`) lifted to production. Each
/// constructor takes the minimum-evidence positional arguments; all
/// optional axes default to their no-evidence form. Field extensions
/// land on the constructor body only — literal-construction sites
/// continue working via record-update syntax (`{ Attribute.create … with
/// IsActive = false }`).
///
/// Rationale (per user direction; A39 codification): when an IR record
/// is extension-prone (i.e., chapter-A.0' lifts repeatedly add fields),
/// every additional field forces a sweep across every literal site.
/// The smart constructor absorbs the field at one location; consumers
/// stay stable. Sibling to `Module.create` + `Catalog.create` (the
/// pre-existing pattern); siblings to the existing `Name.create` +
/// `ColumnCheck.create` + `Trigger.create` + `Sequence.create`.

[<RequireQualifiedAccess>]
module Attribute =

    /// Build an `Attribute` with minimum-evidence defaults. Required:
    /// `ssKey`, `name`, `ptype`. Optional axes default to:
    ///   - `Column = { ColumnName = Name.value name; IsNullable = false }`
    ///   - `IsPrimaryKey = false`; `IsMandatory = false`
    ///   - `Length = None`; `Precision = None`; `Scale = None`
    ///   - `IsIdentity = false`
    ///   - `Description = None`; `IsActive = true` (V1 default)
    ///   - `DefaultValue = None`; `DefaultName = None`; `Computed = None`
    ///   - `ExtendedProperties = []`
    ///   - `OriginalName = None`; `ExternalDatabaseType = None`
    ///   - `SqlStorage = None` (semantic fallback; emitters use `Type`)
    ///   - `Order = None` (no authored Service-Studio order; PK-first /
    ///     SsKey canonical fallback)
    ///
    /// Consumers override via record-update: `{ Attribute.create k n
    /// Integer with IsPrimaryKey = true; IsMandatory = true }`.
    let create (ssKey: SsKey) (name: Name) (ptype: PrimitiveType) : Attribute =
        {
            SsKey                = ssKey
            Name                 = name
            Type                 = ptype
            // Slice 5b (lift): default ColumnName synthesized from Name.
            //
            // NM-15 — this was the ONLY non-total smart constructor in the file
            // (every sibling returns `Result`); it `failwithf`'d on an over-128-
            // char logical name. The throw was both unnecessary and hazardous:
            // the dominant production caller is `CatalogCodec.readAttribute`,
            // which writes `{ Attribute.create k n t with Column = <deserialized> }`
            // — F# still EVALUATES this throwing default before the `with` block
            // discards it, so a long-named attribute with a perfectly valid
            // explicit Column threw anyway. And the docstring's "loud rather than
            // silent invalid SQL" defense was unfounded: the column-derivation
            // naming site (`LogicalColumnEmission.substituteAttribute`) does NOT
            // throw on a long logical name — it leaves the physical name as-is.
            //
            // The default now applies `IdentifierBudget.fit` — the same
            // deterministic truncate-+-SHA-suffix discipline the SSDT emitters
            // use for over-budget GENERATED names (PK_/FK_). The result is always
            // a VALID ≤128-char identifier (never silent-invalid SQL), `fit` is a
            // total pure function, and an explicit `with Column = …` override
            // still replaces it. The constructor is now total, matching every
            // sibling.
            Column               =
                { ColumnName = ColumnName.create (IdentifierBudget.fit (Name.value name)) |> Result.value
                  IsNullable = false
                  Collation = None
                  Identity = None }
            IsPrimaryKey         = false
            IsMandatory          = false
            Length               = None
            Precision            = None
            Scale                = None
            IsIdentity           = false
            Description          = None
            IsActive             = true
            DefaultValue         = None
            DefaultName          = None
            Computed             = None
            ExtendedProperties   = []
            OriginalName         = None
            ExternalDatabaseType = None
            SqlStorage           = None
            // WP8 / NM-72 — authored Service-Studio order. `None` by
            // default: hand-built and minimum-evidence attributes carry
            // no authored order and fall back to the PK-first / SsKey
            // canonical order. The OSSYS rowset / JSON paths override
            // via `{ Attribute.create … with Order = Some n }`.
            Order                = None
        }

    /// NM-14 — the `(Type, SqlStorage)` agreement invariant. When an
    /// attribute carries concrete storage evidence (`SqlStorage = Some
    /// storage`), that storage's semantic projection MUST equal the
    /// attribute's semantic `Type`: `SqlStorageType.toPrimitiveType
    /// storage = Type`. Both halves of an attribute's typing then stay
    /// consistent — every type-driven decision reads `Type`, the emitter
    /// reads `SqlStorage`, and the two never disagree (an adapter that
    /// set `Type = Text; SqlStorage = Some BigInt` would emit `BIGINT`
    /// while every semantic decision treated the column as text). The
    /// `None` case is vacuously consistent: no concrete evidence, the
    /// emitter falls back to the `Type → SqlDataTypeOption` mapping.
    /// Asserted at construction by `Catalog.create`, mirroring the
    /// reference constraint-state quadrant
    /// (`Reference.isConstraintStateConsistent`, NM-12).
    let isStorageTypeConsistent (a: Attribute) : bool =
        match a.SqlStorage with
        | Some storage -> SqlStorageType.toPrimitiveType storage = a.Type
        | None -> true


[<RequireQualifiedAccess>]
module Reference =

    /// Build a `Reference` with minimum-evidence defaults. Required:
    /// `ssKey`, `name`, `sourceAttribute`, `targetKind`. Optional axes
    /// default to:
    ///   - `OnDelete = NoAction` (SQL Server's server-default behavior)
    ///   - `IsUserFk = false`
    ///   - `HasDbConstraint = false` (V1's COALESCE-to-0 default;
    ///     adapters opt into `true` when reflection observes the
    ///     constraint)
    ///   - `OnUpdate = None` (unstated → SQL Server emits no ON UPDATE
    ///     clause; server default NO ACTION applies)
    ///   - `IsConstraintTrusted = true` (V1 default; FKs are TRUSTED
    ///     unless `#FkReality.IsNoCheck = 1` flips this)
    ///
    /// Consumers override via record-update: `{ Reference.create k n s
    /// t with OnDelete = Cascade; HasDbConstraint = true }`.
    let create
        (ssKey: SsKey)
        (name: Name)
        (sourceAttribute: SsKey)
        (targetKind: SsKey)
        : Reference =
        {
            SsKey               = ssKey
            Name                = name
            SourceAttribute     = sourceAttribute
            TargetKind          = targetKind
            OnDelete            = NoAction
            IsUserFk            = false
            OnUpdate            = None
            ConstraintState     = ConstraintState.NoDbConstraint
        }

    /// Boolean projection: is this reference backed by a real DB constraint?
    /// (M4 — the derived `(HasDbConstraint, _)` accessor over the
    /// `ConstraintState` DU; sites that read the old field now call this.)
    let hasDbConstraint (r: Reference) : bool =
        ConstraintState.hasDbConstraint r.ConstraintState

    /// Boolean projection: is this reference's constraint trusted?
    /// (M4 — the derived `(_, IsConstraintTrusted)` accessor; `NoDbConstraint`
    /// projects to vacuously-trusted `true`.)
    let isConstraintTrusted (r: Reference) : bool =
        ConstraintState.isConstraintTrusted r.ConstraintState

    /// G14 — the constraint-state invariant, now a **theorem** rather than a
    /// runtime check: `¬trusted ⟹ hasDbConstraint` holds for every
    /// `ConstraintState` variant by construction (the illegal "untrusted
    /// without a constraint" quadrant is unrepresentable since M4). Retained as
    /// a total predicate (always `true`) so the witness in
    /// `ReferenceConstraintStateTests` and the aggregate-root proof read
    /// explicitly rather than vacuously.
    let isConstraintStateConsistent (r: Reference) : bool =
        match r.ConstraintState with
        | ConstraintState.NoDbConstraint | ConstraintState.TrustedConstraint | ConstraintState.UntrustedConstraint -> true

    /// The sanctioned way to set the constraint-state from the legacy
    /// `(hasDbConstraint, isConstraintTrusted)` boolean pair external evidence
    /// supplies (V1 JSON / ReadSide / codec). Delegates to
    /// `ConstraintState.ofLegacyBooleans`, which normalizes the illegal
    /// `(false, false)` quadrant to `NoDbConstraint` (vacuous trust). Since M4
    /// the illegal quadrant is unrepresentable at the type, so this is total by
    /// construction (witnessed in `ReferenceConstraintStateTests`).
    let withConstraintState (hasDbConstraint: bool) (isConstraintTrusted: bool) (r: Reference) : Reference =
        { r with ConstraintState = ConstraintState.ofLegacyBooleans hasDbConstraint isConstraintTrusted }

    /// The SsKey derivation reason the symmetric-closure pass stamps on
    /// synthesized inverse references. Owned here — the compile-order floor of
    /// the reference vocabulary — so the deployability predicate below and the
    /// pass share one definition; `SymmetricClosure.inverseReason` aliases it.
    /// (DECISIONS 2026-06-12 — reconciliation slice 1; closed to the
    /// `DerivationReason` DU 2026-06-27, recon #14.)
    let inverseDerivationReason : DerivationReason = DerivationReason.Inverse

    /// True iff this reference is a symmetric-closure inverse — a
    /// pass-synthesized edge carrying `DerivedFrom(_, Inverse)`. Total match over
    /// the closed `DerivationReason` set.
    let isInverse (r: Reference) : bool =
        match r.SsKey with
        | DerivedFrom (_, DerivationReason.Inverse) -> true
        | _ -> false

    /// The single definition site for "deployable reference"
    /// (DECISIONS 2026-06-12 — reconciliation slice 1). A
    /// symmetric-closure inverse is a navigation/ordering edge only:
    /// it never participates in FK tightening decisions or
    /// constraint-emission surfaces (its `fkDef` resolution would
    /// script a second FK on the TARGET'S PK column — duplicate
    /// `FK_*` names and PK-to-PK type mismatches). Constraint-
    /// modeling surfaces filter by this predicate; navigation
    /// surfaces (topological order, centrality, bounded context)
    /// keep the full closure.
    let isDeployable (r: Reference) : bool =
        not (isInverse r)


[<RequireQualifiedAccess>]
module Index =

    /// Build an `Index` with minimum-evidence defaults. Required:
    /// `ssKey`, `name`, `columns`. Optional axes default to:
    ///   - `IsUnique = false`; `IsPrimaryKey = false`
    ///   - `ExtendedProperties = []`
    ///   - `Filter = None`; `IncludedColumns = []`
    ///   - `IsPlatformAuto = false`
    ///   - `FillFactor = None`; `IsPadded = false`
    ///   - `AllowRowLocks = true`; `AllowPageLocks = true` (V1 defaults)
    ///   - `NoRecomputeStatistics = false`
    ///   - `IgnoreDuplicateKey = false` (V1 default)
    ///   - `IsDisabled = false` (V1 default)
    ///   - `DataCompression = None` (V1 default: no explicit
    ///     DATA_COMPRESSION clause)
    ///   - `DataSpace = None` (V1 default: index inherits table-level
    ///     dataspace; no explicit `ON` clause)
    ///
    /// Consumers override via record-update.
    let create
        (ssKey: SsKey)
        (name: Name)
        (columns: IndexColumn list)
        : Index =
        {
            SsKey                 = ssKey
            Name                  = name
            Columns               = columns
            Uniqueness            = NotUnique
            ExtendedProperties    = []
            Filter                = None
            IncludedColumns       = []
            IsPlatformAuto        = false
            FillFactor            = None
            IsPadded              = false
            AllowRowLocks         = true
            AllowPageLocks        = true
            NoRecomputeStatistics = false
            IgnoreDuplicateKey    = false
            IsDisabled            = false
            DataCompression       = None
            DataSpace             = None
        }

    /// Build an `Index` from a `SsKey list` of key columns
    /// (interpreted as all-Ascending). Convenience for the common
    /// case where consumers don't care about per-column sort
    /// direction. Equivalent to `Index.create ssKey name
    /// (IndexColumn.ascendingList attributes)`. Slice
    /// 5.13.shim-retirement (2026-05-18) — lifted from the test-side
    /// `IRBuilders.mkIndex`.
    let ofKeyColumns
        (ssKey: SsKey)
        (name: Name)
        (attributes: SsKey list)
        : Index =
        create ssKey name (IndexColumn.ascendingList attributes)


/// Identity-based equality and lookup helpers for catalog nodes (A4).
/// The default F# record `=` compares all fields, which is the right
/// operator for "did this pass change anything?" tests; these helpers are
/// the right operator for "is this the same node, structurally?" — that is,
/// for catalog-level identity.
[<RequireQualifiedAccess>]
module Kind =

    /// Build a `Kind` with minimum-evidence defaults. Required: `ssKey`,
    /// `name`, `physical`, `attributes`. Optional axes default to:
    ///   - `Origin = Native`
    ///   - `Modality = []`; `References = []`; `Indexes = []`
    ///   - `Description = None`; `IsActive = true`
    ///   - `Triggers = []`; `ColumnChecks = []`
    ///   - `ExtendedProperties = []`
    ///
    /// Consumers override via record-update.
    let create
        (ssKey: SsKey)
        (name: Name)
        (physical: PhysicalRealization)
        (attributes: Attribute list)
        : Kind =
        use _ = Bench.scope "ir.kind.create"
        {
            SsKey              = ssKey
            Name               = name
            Origin             = Native
            Modality           = []
            Physical           = physical
            Attributes         = attributes
            References         = []
            Indexes            = []
            Description        = None
            IsActive           = true
            Triggers           = []
            ColumnChecks       = []
            ExtendedProperties = []
        }

    /// True when two kinds share the same SsKey, regardless of names,
    /// attribute orderings, modality marks, or any other field. Encodes
    /// A4 as a function: structural equality of kinds is by SsKey only.
    let byIdentity (a: Kind) (b: Kind) : bool = a.SsKey = b.SsKey

    /// The attributes flagged `IsPrimaryKey` on this kind, in the order
    /// they appear. May be empty for kinds without a declared PK; may
    /// contain multiple entries for composite-key kinds.
    let primaryKey (k: Kind) : Attribute list =
        k.Attributes |> List.filter (fun a -> a.IsPrimaryKey)

    /// The kind's row basis: its attribute Names in attribute order — the
    /// per-stream header every in-flight `RowQuantum` read from this kind
    /// is positional against (Q2). Established once per stream, never per
    /// row.
    let rowBasis (k: Kind) : RowBasis =
        RowBasis.ofNames (k.Attributes |> List.map (fun a -> a.Name))

    /// The static-population rows attached to this kind via
    /// `Modality.Static`, or `[]` if the kind carries no static
    /// modality. Per A7 (static populations live in the catalog) +
    /// the modality projection in `ModalityMark`. Two-consumer
    /// extraction (StaticSeedsEmitter MERGE realization +
    /// StaticPopulationEmitter typed-stream realization); the
    /// projection lives in Core so both data-emission realizations
    /// share one source-of-truth.
    let staticPopulations (k: Kind) : StaticRow list =
        k.Modality
        |> List.tryPick (function
            | Static rows -> Some rows
            | _           -> None)
        |> Option.defaultValue []

    /// Per-Kind attribute-index cache. Per slice
    /// A.4.7'-prelude.perf-sweep-2 (`PERF_OPPORTUNITIES.md` Rank 2):
    /// `tryFindAttribute` is called per FK column resolution in
    /// `StaticSeedsEmitter.deferredColumns`, `MigrationDependenciesEmitter`,
    /// `UserFkReflowPass`; at 300-table × 10-attrs/kind production
    /// scale the prior `List.tryFind` was O(n²) across the emit
    /// pipeline. The `ConditionalWeakTable` keys per-Kind-instance
    /// (F# records are reference types under the hood); the index is
    /// built on first lookup and reused for every subsequent lookup
    /// against the same instance.
    let private attributeIndexCache =
        System.Runtime.CompilerServices.ConditionalWeakTable<Kind, Map<SsKey, Attribute>>()

    let attributeIndex (k: Kind) : Map<SsKey, Attribute> =
        match attributeIndexCache.TryGetValue(k) with
        | true, idx -> idx
        | false, _ ->
            let idx = k.Attributes |> List.map (fun a -> a.SsKey, a) |> Map.ofList
            // `GetValue` is the thread-safe idempotent add (vs `Add`
            // which throws on duplicate key); use it so concurrent
            // callers from a future parallel-pass driver see one
            // index per Kind.
            attributeIndexCache.GetValue(
                k,
                System.Runtime.CompilerServices.ConditionalWeakTable<Kind, Map<SsKey, Attribute>>.CreateValueCallback(fun _ -> idx))

    /// Find an attribute on the kind by SsKey (per A4 — identity-keyed
    /// lookup, never by name). Returns `None` if absent. Lifted to
    /// Core at chapter 4.1.B slice ε per the slice-δ improvement
    /// surface (#5): `StaticSeedsEmitter.deferredColumns` and
    /// `MigrationDependenciesEmitter`'s reference-resolution path
    /// both look up source attributes by SsKey through this lens; a
    /// third consumer at chapter 4.2's `UserFkReflowPass` is on the
    /// horizon. Two-consumer threshold met.
    let tryFindAttribute (ssKey: SsKey) (k: Kind) : Attribute option =
        Map.tryFind ssKey (attributeIndex k)


[<RequireQualifiedAccess>]
module Module =

    /// Find a kind in this module by SsKey. Returns `None` if absent. A4:
    /// lookup is by identity, never by name.
    let tryFindKind (ssKey: SsKey) (m: Module) : Kind option =
        m.Kinds |> List.tryFind (fun k -> k.SsKey = ssKey)

    /// Smart constructor enforcing the per-module aggregate invariants.
    /// Per session-36 audit (Agent 3 #10/#11): `Module` is an
    /// aggregate boundary; the per-module invariant is "Kind SsKeys
    /// disjoint within the module." Production-side construction
    /// (adapters, passes producing transformed catalogs) flows
    /// through `create`; passes that map over an existing catalog's
    /// modules and preserve invariants by construction can use
    /// record-literal updates (`{ m with Kinds = ... }`) since the
    /// invariant cannot be violated by a non-introducing transform.
    let create
        (ssKey: SsKey)
        (name: Name)
        (kinds: Kind list)
        (isActive: bool)
        (extendedProperties: ExtendedProperty list)
        : Result<Module> =
        use _ = Bench.scope "ir.module.create"
        // LR1 (slice 5.13.module-non-empty-invariant, matrix row 42):
        // per-module non-empty Kind invariant. V1's `ModuleModel.Create`
        // enforces this; V2 lifts the same axis per A39 (aggregate-root
        // smart-constructor invariants) + `DECISIONS 2026-05-18 (slice
        // 5.2.α.module)` path (a). Prevents a ghost-module class of bug
        // in transformation passes — a module with zero kinds is
        // semantically meaningless at every consumer (emitter / pass /
        // diagnostic) but was silently constructible.
        let errors =
            Validation.nonEmpty
                "module.kinds.empty"
                (sprintf "Module %A must contain at least one Kind." ssKey)
                kinds
            @ Validation.duplicateKeyErrors
                "module.kinds.duplicateKey"
                (fun (dupKey: SsKey) ->
                    sprintf
                        "Module %A has duplicate Kind SsKey %A; A11 (coproduct cell) requires disjoint kinds."
                        ssKey
                        dupKey)
                (fun (k: Kind) -> k.SsKey)
                kinds
        if List.isEmpty errors then
            Result.success
                { SsKey              = ssKey
                  Name               = name
                  Kinds              = kinds
                  IsActive           = isActive
                  ExtendedProperties = extendedProperties }
        else
            Result.failure errors


[<RequireQualifiedAccess>]
module Catalog =

    // ===========================================================================
    // Catalog traversal primitives (chapter-Cluster-B compression; 2026-05-22).
    //
    // Cross-cutting workhorse functions for "walk every Kind in the catalog"
    // patterns. Replaces 5+ inline `c.Modules |> List.collect (fun m -> m.Kinds)
    // |> ...` boilerplate sites with named primitives. Pairs with the existing
    // `CatalogTraversal.mapKinds` in `LineageBuffer.fs` (which carries Lineage
    // emission); these primitives are pure (no Lineage carrier). The existing
    // `allKinds` (defined later in this module with Bench instrumentation per
    // the iterator-logging discipline) is the public scan accessor; the
    // primitives below add fold / iter / map / update shapes for the
    // recurring traversal patterns that `allKinds` alone doesn't capture.
    //
    // **Naming convention:** `*Kinds` for kind-level traversals;
    // `*ModulesKinds` if the owning Module is needed alongside the Kind.
    // ===========================================================================

    /// Every (Module, Kind) pair in the catalog. Used by traversals
    /// that need to know which Module owns each Kind (e.g.,
    /// `kindOwnershipIndex` builds a `SsKey -> Module` map).
    let allModulesKinds (c: Catalog) : (Module * Kind) list =
        c.Modules |> List.collect (fun m -> m.Kinds |> List.map (fun k -> (m, k)))

    /// The Cartesian product `(Kind × Context)` of the catalog's kinds
    /// with each kind's `extract`-derived contexts (attributes /
    /// references / indexes / any kind-owned typed list), ordered
    /// deterministically by `(Kind.SsKey, sortKey context)` so T1
    /// byte-determinism holds without per-call sorting at consumers.
    /// The canonical iteration primitive for `Composition.fanOut`-style
    /// pass drivers (four Tightening passes — Nullability / UniqueIndex
    /// / ForeignKey / CategoricalUniqueness — share this exact shape;
    /// the named primitive prevents per-pass open-coding).
    let kindContexts
        (extract: Kind -> 'ctx list)
        (sortKey: 'ctx -> SsKey)
        (c: Catalog)
        : (Kind * 'ctx) list =
        c.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.sortBy (fun k -> k.SsKey)
        |> List.collect (fun k ->
            extract k
            |> List.sortBy sortKey
            |> List.map (fun ctx -> k, ctx))

    /// Fold over every Kind in the catalog with access to the owning
    /// Module. Used to build SsKey-indexed maps and accumulate
    /// per-kind state in one pass.
    let foldKinds (f: Module -> Kind -> 'acc -> 'acc) (initial: 'acc) (c: Catalog) : 'acc =
        c.Modules |> List.fold (fun a m -> m.Kinds |> List.fold (fun a' k -> f m k a') a) initial

    /// Iterate over every Kind with side effects (rare in pure-core
    /// code; used by benchmarks and audit consumers).
    let iterKinds (f: Module -> Kind -> unit) (c: Catalog) : unit =
        c.Modules |> List.iter (fun m -> m.Kinds |> List.iter (f m))

    /// Map every Kind through a pure transformation (Module ownership
    /// preserved). Sibling to `CatalogTraversal.mapKinds` (which
    /// emits Lineage); this form is for callers that don't need
    /// trail emission.
    // NB: hand-rolled rather than lensed because `module Catalog` lives
    // inside `Catalog.fs`, which compiles BEFORE `Optics.fs` (the IR types
    // it focuses must precede it). Lensifying this primitive would require
    // splitting `module Catalog` operations into a separate post-Optics file
    // — deferred to a future "Catalog traversal extraction" slice.
    let mapKinds (f: Kind -> Kind) (c: Catalog) : Catalog =
        { c with Modules = c.Modules |> List.map (fun m -> { m with Kinds = m.Kinds |> List.map f }) }

    /// Update Kinds matching a predicate; non-matching kinds pass
    /// through unchanged. Compresses the recurring
    /// `Catalog -> Modules.map(fun m -> {m with Kinds = m.Kinds.map(if pred then update else id)})`
    /// pattern in pass drivers that touch a single Kind's fields.
    let updateKindsWhere (predicate: Kind -> bool) (updater: Kind -> Kind) (c: Catalog) : Catalog =
        mapKinds (fun k -> if predicate k then updater k else k) c

    /// Remove every `Static` modality mark, preserving all other marks.
    /// The one definition site for the "4.4 trap" strip: `ReadSide.read`
    /// marks every row-carrying reconstructed kind `Static` (lifting live
    /// rows for the per-row PhysicalSchema canary), and `LiveProfiler`
    /// skips Static kinds — so profiling over a ReadSide-derived catalog
    /// must strip the mark first. Strips ONLY `Static`: erasing the other
    /// marks (TenantScoped / SoftDeletable / SystemOwned / Temporal) here
    /// would silently widen the erasure (`CONSTELLATION_BACKLOG.md` plane
    /// N2 — the Preflight over-erasure this definition site closed).
    let stripStaticPopulations (c: Catalog) : Catalog =
        mapKinds
            (fun k -> { k with Modality = k.Modality |> List.filter (function Static _ -> false | _ -> true) })
            c

    /// Per-Catalog kind-index cache. Per slice
    /// A.4.7'-prelude.perf-sweep-2 (`PERF_OPPORTUNITIES.md` Rank 1 —
    /// highest single-finding leverage): `tryFindKind` is the hottest
    /// cross-cutting lookup in V2 — emitters resolve FK targets,
    /// passes look up referenced kinds, the cycle resolver walks the
    /// FK graph. The prior `List.tryPick` was O(modules ×
    /// kinds_per_module) per call; at 300-table × per-FK-reference
    /// scale that compounded across the full pipeline. The
    /// `ConditionalWeakTable` keys per-Catalog-instance; the index
    /// is built on first lookup and reused thereafter. Both
    /// `kindIndex` and `kindOwnershipIndex` populate at the same
    /// site (one pass over modules) so a single first-lookup pays
    /// for both lookups.
    let private kindIndexCache =
        System.Runtime.CompilerServices.ConditionalWeakTable<Catalog, Map<SsKey, Kind>>()

    let private kindOwnershipIndexCache =
        System.Runtime.CompilerServices.ConditionalWeakTable<Catalog, Map<SsKey, Module>>()

    let kindIndex (c: Catalog) : Map<SsKey, Kind> =
        match kindIndexCache.TryGetValue(c) with
        | true, idx -> idx
        | false, _ ->
            let idx = foldKinds (fun _ k acc -> Map.add k.SsKey k acc) Map.empty c
            kindIndexCache.GetValue(
                c,
                System.Runtime.CompilerServices.ConditionalWeakTable<Catalog, Map<SsKey, Kind>>.CreateValueCallback(fun _ -> idx))

    let kindOwnershipIndex (c: Catalog) : Map<SsKey, Module> =
        match kindOwnershipIndexCache.TryGetValue(c) with
        | true, idx -> idx
        | false, _ ->
            let idx = foldKinds (fun m k acc -> Map.add k.SsKey m acc) Map.empty c
            kindOwnershipIndexCache.GetValue(
                c,
                System.Runtime.CompilerServices.ConditionalWeakTable<Catalog, Map<SsKey, Module>>.CreateValueCallback(fun _ -> idx))

    /// Find a kind anywhere in the catalog by SsKey. Returns `None` if
    /// absent. A4: lookup is by identity, never by name.
    let tryFindKind (ssKey: SsKey) (c: Catalog) : Kind option =
        Map.tryFind ssKey (kindIndex c)

    /// Find the module that owns a given kind by SsKey.
    let tryFindOwningModule (ssKey: SsKey) (c: Catalog) : Module option =
        Map.tryFind ssKey (kindOwnershipIndex c)

    /// Enumerate all kinds across all modules.
    let allKinds (c: Catalog) : Kind list =
        use _ = Bench.scope "ir.catalog.scan.allKinds"
        c.Modules |> List.collect (fun m -> m.Kinds)

    /// A flat `SsKey -> Name` index over every named entity — kinds, attributes,
    /// references, indexes, sequences. The shared legibility primitive: an
    /// operator-facing surface keyed by `SsKey` resolves through this rather than
    /// `SsKey.rootOriginal` (a bare GUID for an `OssysOriginal` key), so a real
    /// OSSYS estate reads by `Name`, not hex. Built once per surface; the diff
    /// (`Comparison`), the run/apply narration (`RunFaces`), and the transfer
    /// report all share it. A4 holds — this is a terminal DISPLAY projection, never
    /// a lookup-by-name; identity stays the `SsKey`.
    let nameIndex (c: Catalog) : Map<SsKey, string> =
        seq {
            for k in allKinds c do
                yield k.SsKey, Name.value k.Name
                for a in k.Attributes do yield a.SsKey, Name.value a.Name
                for r in k.References  do yield r.SsKey, Name.value r.Name
                for i in k.Indexes     do yield i.SsKey, Name.value i.Name
            for s in c.Sequences do yield s.SsKey, Name.value s.Name
        }
        |> Map.ofSeq

    /// The display name of an entity by `SsKey` — its `Name` via `nameIndex`, or
    /// the `rootOriginal` projection as the honest fallback for a key absent from
    /// the catalog. For a single lookup; build a `nameIndex` once for many.
    let displayName (c: Catalog) (key: SsKey) : string =
        Map.tryFind key (nameIndex c) |> Option.defaultValue (SsKey.rootOriginal key)

    /// Smart constructor enforcing the catalog-wide aggregate
    /// invariants. Per session-36 audit (Agent 3 #10/#12, Agent 1 #19):
    ///   1. Module SsKeys are disjoint (A11).
    ///   2. Kind SsKeys are disjoint across all modules.
    ///   3. Every `Reference.SourceAttribute` exists on its owning
    ///      `Kind.Attributes`.
    ///   4. Every `Reference.TargetKind` exists somewhere in the
    ///      catalog (no dangling FKs).
    ///   5. Every `Index.Columns` entry exists on its owning
    ///      `Kind.Attributes`.
    ///
    /// `tryFindKind`, `RawTextEmitter.fkDef`, and
    /// `PhysicalSchema.toPhysicalForeignKeys` previously each
    /// re-validated #3/#4 by silently dropping bad references. Per
    /// the discipline "invariants live with the type, not in the
    /// consumer" — flowing through `create` makes #1–#5 impossible
    /// to violate. Aggregates errors so a consumer sees every
    /// violation in one Result.
    let create (modules: Module list) (sequences: Sequence list) : Result<Catalog> =
        use _ = Bench.scope "ir.catalog.create"
        // Chapter-Cluster-B compression (2026-05-22): the three
        // duplicate-key partitions (modules / kinds / sequences) share
        // the same algorithmic shape and now go through
        // `Validation.duplicateKeyErrors`. The reference / index
        // dangling-key checks have a richer per-error shape (per-Kind
        // attrKeys context) and stay inline.

        let moduleDupes =
            modules
            |> Validation.duplicateKeyErrors
                "catalog.modules.duplicateKey"
                (sprintf
                    "Catalog has duplicate Module SsKey %A; A11 requires disjoint modules.")
                (fun m -> m.SsKey)

        let allKindList =
            modules |> List.collect (fun m -> m.Kinds)

        let kindDupes =
            allKindList
            |> Validation.duplicateKeyErrors
                "catalog.kinds.duplicateKey"
                (sprintf
                    "Catalog has Kind SsKey %A duplicated across modules; A4 requires Kind identity to be globally unique.")
                (fun k -> k.SsKey)

        let kindKeySet =
            allKindList |> List.map (fun k -> k.SsKey) |> Set.ofList

        // Wave-0 slice 0.4 (2026-05-30): single walk over `allKindList`
        // computing each kind's `attrKeys` exactly once and accumulating
        // the reference- and index-dangling errors into separate buffers.
        // Behaviour is identical to the prior two `List.collect` passes
        // (the final order stays `referenceErrors @ indexErrors`); the
        // change halves the kind-walk count and removes the duplicate
        // per-kind `attrKeys` Set construction on the `Catalog.create`
        // hot path (bench label `ir.catalog.create`). Verified
        // order-preserving by the existing pure `CatalogTests`
        // dangling-key suite.
        let referenceErrors, indexErrors, attributeErrors =
            let refAcc = ResizeArray<ValidationError>()
            let idxAcc = ResizeArray<ValidationError>()
            let attrAcc = ResizeArray<ValidationError>()
            for k in allKindList do
                let attrKeys =
                    k.Attributes |> List.map (fun a -> a.SsKey) |> Set.ofList
                for a in k.Attributes do
                    // NM-14 — the `(Type, SqlStorage)` agreement invariant.
                    // When an attribute carries concrete storage evidence
                    // (`SqlStorage = Some storage`), `SqlStorageType.toPrimitiveType
                    // storage` MUST equal the semantic `Type`. Otherwise the
                    // emitter would render the concrete storage (e.g. BIGINT)
                    // while every type-driven decision read the disagreeing
                    // semantic category (e.g. Text). Mirrors the NM-12 reference
                    // quadrant reject: the disagreeing pair cannot enter the IR.
                    if not (Attribute.isStorageTypeConsistent a) then
                        attrAcc.Add(
                            ValidationError.create
                                "catalog.attribute.storageTypeMismatch"
                                (sprintf
                                    "Attribute %A on Kind %A has SqlStorage %A whose semantic projection (%A) disagrees with its Type %A; the concrete storage evidence must agree with the semantic type (SqlStorageType.toPrimitiveType storage = Type)."
                                    a.SsKey
                                    k.SsKey
                                    a.SqlStorage
                                    (a.SqlStorage |> Option.map SqlStorageType.toPrimitiveType)
                                    a.Type))
                for r in k.References do
                    if not (Set.contains r.SourceAttribute attrKeys) then
                        refAcc.Add(
                            ValidationError.create
                                "catalog.reference.danglingSource"
                                (sprintf
                                    "Reference %A on Kind %A has SourceAttribute %A absent from the kind's Attributes."
                                    r.SsKey k.SsKey r.SourceAttribute))
                    if not (Set.contains r.TargetKind kindKeySet) then
                        refAcc.Add(
                            ValidationError.create
                                "catalog.reference.danglingTarget"
                                (sprintf
                                    "Reference %A on Kind %A has TargetKind %A absent from the catalog."
                                    r.SsKey k.SsKey r.TargetKind))
                    // NM-12 — G14: the illegal trust quadrant
                    // (HasDbConstraint=false ∧ IsConstraintTrusted=false) is now
                    // UNREPRESENTABLE — `Reference.ConstraintState` is a closed
                    // 3-variant DU (M4 — THE VECTOR §6 Kind II), so the prior
                    // runtime rejection here became dead code and was retired.
                    // The invariant `¬trusted ⟹ hasDbConstraint` is a type
                    // theorem (`Reference.isConstraintStateConsistent` is now
                    // total `true`); the witness moved to the round-trip law in
                    // `ReferenceConstraintStateTests`.
                for idx in k.Indexes do
                    for col in idx.Columns do
                        if not (Set.contains col.Attribute attrKeys) then
                            idxAcc.Add(
                                ValidationError.create
                                    "catalog.index.danglingColumn"
                                    (sprintf
                                        "Index %A on Kind %A references column SsKey %A absent from the kind's Attributes."
                                        idx.SsKey k.SsKey col.Attribute))
            List.ofSeq refAcc, List.ofSeq idxAcc, List.ofSeq attrAcc

        // Sequence SsKey disjointness (chapter A.0' slice δ). Sequences
        // are top-level Catalog objects; their SsKeys must be unique
        // across the catalog by A4. Disjointness from Kind SsKeys is
        // not currently enforced — sequences and kinds are different
        // schema-object kinds (SEQUENCE vs TABLE) and use disjoint
        // SsKey-synthesis prefixes (`OS_SEQ_*` vs `OS_KIND_*`), so
        // collisions are not structurally possible.
        let sequenceDupes =
            sequences
            |> Validation.duplicateKeyErrors
                "catalog.sequences.duplicateKey"
                (sprintf
                    "Catalog has duplicate Sequence SsKey %A; A4 requires unique sequence identity.")
                (fun s -> s.SsKey)

        let allErrors =
            moduleDupes @ kindDupes @ referenceErrors @ indexErrors @ attributeErrors @ sequenceDupes

        if List.isEmpty allErrors then
            Result.success { Modules = modules; Sequences = sequences }
        else
            Result.failure allErrors

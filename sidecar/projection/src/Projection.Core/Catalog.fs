namespace Projection.Core

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
type Origin =
    /// OutSystems-native kind originated within the platform.
    | OsNative
    /// External kind exposed via Integration Studio's IS-functor.
    | ExternalViaIntegrationStudio
    /// External kind exposed by direct reader, no IS step.
    | ExternalDirect


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


/// Primitive scalar types. Concrete mapping to a target surface scalar is
/// policy (A13); the IR holds the abstract type only.
type PrimitiveType =
    | Integer
    | Decimal
    | Text
    | Boolean
    | DateTime
    | Date
    | Time
    | Binary
    | Guid


/// Per-kind physical realization. Starts narrow; widen when evidence forces
/// (e.g., view-backed kinds, cross-server external kinds).
type PhysicalRealization = {
    Schema : string
    Table  : string
}


/// Per-attribute (column-level) physical realization. Decoupled from
/// `Attribute` so that policy and unfold passes can rewrite physical
/// metadata without touching logical structure.
type ColumnRealization = {
    ColumnName : string
    IsNullable : bool
}


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
}


/// A directional reference (A10). Symmetry, if needed by a target surface,
/// is introduced by the symmetric-closure pass and the resulting reference
/// carries a `Derived` SsKey with reason `"inverse"`.
type Reference = {
    SsKey           : SsKey
    Name            : Name
    SourceAttribute : SsKey
    TargetKind      : SsKey
    OnDelete        : ReferenceAction
}


/// A schema-level index on a kind. Carries identity, name, the
/// participating attribute SsKeys (in declaration order; composite
/// indexes have multiple), `IsUnique` (does the source treat this index
/// as a uniqueness constraint), and `IsPrimaryKey` (is this the kind's
/// primary-key index — V1 treats the PK as a unique index, but V2
/// distinguishes them at the structural level).
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
    Columns      : SsKey list
    IsUnique     : bool
    IsPrimaryKey : bool
}


/// A kind: the schema-level entity type. Carries identity, name, origin,
/// modality marks, physical realization, attributes, references, and
/// indexes (A8).
type Kind = {
    SsKey      : SsKey
    Name       : Name
    Origin     : Origin
    Modality   : ModalityMark list
    Physical   : PhysicalRealization
    Attributes : Attribute list
    References : Reference list
    Indexes    : Index list
}


/// A coproduct cell of the catalog (A11). Modules are disjoint by SsKey;
/// the projection respects the decomposition (T2).
type Module = {
    SsKey : SsKey
    Name  : Name
    Kinds : Kind list
}


/// The whole catalog: a coproduct over modules.
type Catalog = {
    Modules : Module list
}


/// Identity-based equality and lookup helpers for catalog nodes (A4).
/// The default F# record `=` compares all fields, which is the right
/// operator for "did this pass change anything?" tests; these helpers are
/// the right operator for "is this the same node, structurally?" — that is,
/// for catalog-level identity.
[<RequireQualifiedAccess>]
module Kind =

    /// True when two kinds share the same SsKey, regardless of names,
    /// attribute orderings, modality marks, or any other field. Encodes
    /// A4 as a function: structural equality of kinds is by SsKey only.
    let byIdentity (a: Kind) (b: Kind) : bool = a.SsKey = b.SsKey

    /// The attributes flagged `IsPrimaryKey` on this kind, in the order
    /// they appear. May be empty for kinds without a declared PK; may
    /// contain multiple entries for composite-key kinds.
    let primaryKey (k: Kind) : Attribute list =
        k.Attributes |> List.filter (fun a -> a.IsPrimaryKey)


[<RequireQualifiedAccess>]
module Module =

    /// Find a kind in this module by SsKey. Returns `None` if absent. A4:
    /// lookup is by identity, never by name.
    let tryFindKind (ssKey: SsKey) (m: Module) : Kind option =
        m.Kinds |> List.tryFind (fun k -> k.SsKey = ssKey)


[<RequireQualifiedAccess>]
module Catalog =

    /// Find a kind anywhere in the catalog by SsKey. Returns `None` if
    /// absent. A4: lookup is by identity, never by name.
    let tryFindKind (ssKey: SsKey) (c: Catalog) : Kind option =
        c.Modules |> List.tryPick (Module.tryFindKind ssKey)

    /// Find the module that owns a given kind by SsKey.
    let tryFindOwningModule (ssKey: SsKey) (c: Catalog) : Module option =
        c.Modules |> List.tryFind (fun m ->
            m.Kinds |> List.exists (fun k -> k.SsKey = ssKey))

    /// Enumerate all kinds across all modules.
    let allKinds (c: Catalog) : Kind list =
        c.Modules |> List.collect (fun m -> m.Kinds)

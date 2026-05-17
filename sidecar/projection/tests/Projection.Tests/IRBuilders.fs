module Projection.Tests.IRBuilders

open Projection.Core

/// Fixture builders for V2 IR records. Centralises the "empty / sensible
/// default" form so future slices that add fields to `Attribute` / `Kind` /
/// `Module` / `Catalog` / `Index` update one site instead of ~150 record
/// literals across the test surface.
///
/// **Why this exists.** Chapter A.0' slices α/β/γ+δ+ε+ζ+η each added new
/// fields to the IR (Description, IsActive, Triggers, Sequences,
/// DefaultValue, Computed, ColumnChecks, ExtendedProperties at four levels,
/// ModalityMark.Temporal). Pre-existing tests construct records from
/// scratch — every new field forces a mechanical edit across the entire
/// test surface. The builders below absorb new fields with the slice that
/// adds them; downstream tests use `{ mkAttribute ... with FieldOfInterest
/// = ... }` and stay stable across IR growth.
///
/// **Discipline (pillar 8 — domain-first naming).** Builder names answer
/// "what does this REPRESENT" (an `Attribute`, a `Kind`, etc.), not "what
/// does this DO" (no `Helper` / `Util` / `Builder` suffix on the modules
/// themselves; the module name `IRBuilders` is the file-level concept —
/// the *family of builders*).
///
/// **Discipline (pillar 9 — DataIntent default).** All builder defaults
/// represent DataIntent zero-evidence values: empty collections, `None`
/// options, V1-default-true for IsActive. Tests opting into specific
/// values (e.g., `IsActive = false`) override via record-update.

/// Build an `Attribute` with minimum-evidence defaults. Override fields
/// via record-update syntax (`{ mkAttribute key name ptype with ... }`).
let mkAttribute (ssKey: SsKey) (name: Name) (ptype: PrimitiveType) : Attribute =
    {
        SsKey              = ssKey
        Name               = name
        Type               = ptype
        Column             = { ColumnName = Name.value name; IsNullable = false }
        IsPrimaryKey       = false
        IsMandatory        = false
        Length             = None
        Precision          = None
        Scale              = None
        IsIdentity         = false
        Description        = None
        IsActive           = true
        DefaultValue       = None
        Computed           = None
        ExtendedProperties = []
        OriginalName       = None
        ExternalDatabaseType = None
    }

/// Build a `Kind` with the given attributes and minimum-evidence defaults.
let mkKind
    (ssKey: SsKey)
    (name: Name)
    (physical: PhysicalRealization)
    (attributes: Attribute list)
    : Kind =
    {
        SsKey              = ssKey
        Name               = name
        Origin             = OsNative
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

/// Build a `Module` with the given kinds and minimum-evidence defaults.
let mkModule (ssKey: SsKey) (name: Name) (kinds: Kind list) : Module =
    {
        SsKey              = ssKey
        Name               = name
        Kinds              = kinds
        IsActive           = true
        ExtendedProperties = []
    }

/// Build one `IndexColumn` with the given direction. Chapter 4.9
/// slice γ — used by tests that exercise DESC indexes; most tests
/// build all-Ascending via `mkIndexColumns`.
let mkIndexColumn (attribute: SsKey) (direction: IndexColumnDirection) : IndexColumn =
    { Attribute = attribute; Direction = direction }

/// Build an all-Ascending `IndexColumn list` from a list of attribute
/// keys. The common shape for tests that don't care about direction.
/// Chapter 4.9 slice γ.
let mkIndexColumns (attributes: SsKey list) : IndexColumn list =
    attributes |> List.map (fun a -> { Attribute = a; Direction = Ascending })

/// Build an `Index` with minimum-evidence defaults. Accepts the
/// attribute keys directly; defaults to all-Ascending. Tests opting
/// into DESC direction override `Columns` via record-update with
/// `mkIndexColumn` entries.
let mkIndex
    (ssKey: SsKey)
    (name: Name)
    (columns: SsKey list)
    : Index =
    {
        SsKey              = ssKey
        Name               = name
        Columns            = mkIndexColumns columns
        IsUnique           = false
        IsPrimaryKey       = false
        ExtendedProperties = []
        Filter             = None
        IncludedColumns    = []
        IsPlatformAuto     = false
        FillFactor         = None
        IsPadded           = false
        AllowRowLocks      = true
        AllowPageLocks     = true
        NoRecomputeStatistics = false
    }

/// Build a `Reference` with minimum-evidence defaults. Required: ssKey,
/// name, sourceAttribute, targetKind. Defaults: OnDelete = NoAction,
/// IsUserFk = false, HasDbConstraint = false (V1's COALESCE-to-0 default).
let mkReference
    (ssKey: SsKey)
    (name: Name)
    (sourceAttribute: SsKey)
    (targetKind: SsKey)
    : Reference =
    {
        SsKey           = ssKey
        Name            = name
        SourceAttribute = sourceAttribute
        TargetKind      = targetKind
        OnDelete        = NoAction
        IsUserFk        = false
        HasDbConstraint = false
    }

/// Build a `Catalog` with the given modules and no sequences. For
/// invariant-checking construction use `Catalog.create modules sequences`;
/// `mkCatalog` is the no-invariant-check shorthand for test fixtures that
/// have already validated structurally elsewhere.
let mkCatalog (modules: Module list) : Catalog =
    { Modules = modules; Sequences = [] }

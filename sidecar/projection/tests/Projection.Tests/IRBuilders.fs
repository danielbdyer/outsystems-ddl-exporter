module Projection.Tests.IRBuilders

open Projection.Core

/// Test-fixture shape-adapters and skip-invariant-check builders for V2 IR
/// records. Production-side smart constructors (`Attribute.create`,
/// `Reference.create`, `Kind.create`, `Index.create`) absorb field
/// extensions at one site per the A39 codification (slice
/// 5.13.smart-constructor-lift, 2026-05-18). Slice
/// 5.13.shim-retirement (2026-05-18) migrated qualified-call sites
/// (`IRBuilders.mkAttribute …`) to direct `Attribute.create …` calls.
/// The shim wrappers stay for unqualified-call sites (files that
/// `open Projection.Tests.IRBuilders` and use `mkX` bare); full
/// soft-retirement triggers when those sites migrate (a separate
/// hygiene slice deferred-with-trigger).
///
/// **Two surfaces remain:**
///   - **Pure delegation shims** (`mkAttribute`, `mkKind`, `mkReference`)
///     — 1-line wrappers around the production smart constructors.
///     Pillar-9 framing: zero-cost adapters serving the unqualified-
///     usage call sites. Retire when those call sites migrate.
///   - **Shape adapters + skip-invariant builders** (`mkIndex`,
///     `mkIndexColumn`, `mkIndexColumns`, `mkModule`, `mkCatalog`)
///     — provide test-side semantics the production smart constructors
///     don't expose (SsKey→IndexColumn list conversion; skip-Result
///     shorthand). These stay.
///
/// **Discipline (pillar 8 — domain-first naming).** Builder names answer
/// "what does this REPRESENT" (`mkIndexColumns` ⇒ a list of index
/// columns), not "what does this DO". The module name `IRBuilders` is
/// the file-level concept — the *family of test-fixture adapters*.
///
/// **Discipline (pillar 9 — DataIntent default).** All builder defaults
/// represent DataIntent zero-evidence values: empty collections, `None`
/// options, V1-default-true for IsActive. Tests opting into specific
/// values (e.g., `IsActive = false`) override via record-update.

/// Build an `Attribute` with minimum-evidence defaults. Delegates to
/// the production-side `Attribute.create` smart constructor.
let mkAttribute (ssKey: SsKey) (name: Name) (ptype: PrimitiveType) : Attribute =
    Attribute.create ssKey name ptype

/// Build a `Kind` with the given attributes and minimum-evidence
/// defaults. Delegates to the production-side `Kind.create` smart
/// constructor.
let mkKind
    (ssKey: SsKey)
    (name: Name)
    (physical: PhysicalRealization)
    (attributes: Attribute list)
    : Kind =
    Kind.create ssKey name physical attributes

/// Build a `Reference` with minimum-evidence defaults. Delegates to
/// the production-side `Reference.create` smart constructor.
let mkReference
    (ssKey: SsKey)
    (name: Name)
    (sourceAttribute: SsKey)
    (targetKind: SsKey)
    : Reference =
    Reference.create ssKey name sourceAttribute targetKind

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
/// attribute keys directly; defaults to all-Ascending. Delegates to
/// the production-side `Index.create` smart constructor (slice
/// 5.13.fk-features-emit).
let mkIndex
    (ssKey: SsKey)
    (name: Name)
    (columns: SsKey list)
    : Index =
    Index.create ssKey name (mkIndexColumns columns)

/// Build a `Catalog` with the given modules and no sequences. For
/// invariant-checking construction use `Catalog.create modules sequences`;
/// `mkCatalog` is the no-invariant-check shorthand for test fixtures that
/// have already validated structurally elsewhere.
let mkCatalog (modules: Module list) : Catalog =
    { Modules = modules; Sequences = [] }

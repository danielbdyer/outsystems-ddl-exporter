module Projection.Tests.IRBuilders

open Projection.Core

/// Test-fixture skip-Result builders for V2 aggregate-root records
/// whose production smart constructors return `Result<_>` for
/// invariant-checking. The test surface uses these conveniences when
/// the fixture has already validated structurally elsewhere; the
/// fixture's intent is "construct a known-good value," not "exercise
/// the validator" (validator exercises live in dedicated
/// invariant-violation tests).
///
/// **Scope after slice 5.13.shim-retirement (2026-05-18):** the
/// pure-delegation shims (`mkAttribute / mkKind / mkReference`) and
/// the index-column shape adapters (`mkIndexColumn / mkIndexColumns /
/// Index.ofKeyColumns`) retired — production-side `Attribute.create /
/// Kind.create / Reference.create` are direct construction;
/// `IndexColumn.create / IndexColumn.ascendingList / Index.create /
/// Index.ofKeyColumns` lifted to Core (per A39 + the pillar 8
/// ubiquitous-language posture). Only the two skip-Result test
/// conveniences remain in this module — the production constructors
/// return `Result<_>` which test fixtures unwrap; the helpers below
/// inline that unwrap at the canonical fixture-construction shape.
///
/// **Discipline (pillar 9 — DataIntent default).** Both builders'
/// defaults represent DataIntent zero-evidence values: empty
/// ExtendedProperties, V1-default-true for IsActive, empty
/// Sequences. Tests opting into specific values override via
/// record-update on the returned value.

/// Build a `Module` with the given kinds and minimum-evidence
/// defaults (`IsActive = true`, `ExtendedProperties = []`). The
/// production smart constructor `Module.create` enforces the
/// non-empty-Kinds invariant (LR1; slice 5.13.module-non-empty-
/// invariant); fixtures use this helper when the kinds are
/// constructed inline and known well-formed.
let mkModule (ssKey: SsKey) (name: Name) (kinds: Kind list) : Module =
    {
        SsKey              = ssKey
        Name               = name
        Kinds              = kinds
        IsActive           = true
        ExtendedProperties = []
    }

/// Build a `Catalog` with the given modules and no sequences. The
/// production smart constructor `Catalog.create` validates the
/// referential-integrity invariants (A39); fixtures use this helper
/// when the modules are constructed inline and known well-formed.
let mkCatalog (modules: Module list) : Catalog =
    { Modules = modules; Sequences = [] }

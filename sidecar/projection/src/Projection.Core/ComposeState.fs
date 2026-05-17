namespace Projection.Core

/// Aggregate state carrier for registry-driven Compose traversal.
/// Per chapter A.4.7' axis 1 (`CHAPTER_A_4_7_PRIME_OPEN.md`):
/// resolves the heterogeneous-output-types blocker named in
/// `CHAPTER_A_4_7_CLOSE.md` forward signal #3 by accumulating every
/// pass's output evidence in one record so the registry fold is
/// well-typed at `ComposeState -> Lineage<Diagnostics<ComposeState>>`.
/// Catalog-rewriting passes update `Catalog`; decision-set passes
/// write into the appropriate Option field. Emitters consume the
/// final state.
///
/// Per A39 preserved: the components carry their own smart-
/// constructor invariants (`Catalog.create` / `NullabilityRules.emptyDecisionSet`
/// etc.); ComposeState is opt-in evidence accumulation, not an
/// aggregate root that re-validates.
type ComposeState = {
    Catalog : Catalog
    TopologicalOrder : TopologicalOrder option
    NullabilityDecisions : NullabilityDecisionSet option
    UniqueIndexDecisions : UniqueIndexDecisionSet option
    ForeignKeyDecisions : ForeignKeyDecisionSet option
    CategoricalUniquenessDecisions : CategoricalUniquenessDecisionSet option
    UserRemap : UserRemapContext option
}

[<RequireQualifiedAccess>]
module ComposeState =

    /// Initial state from a parsed Catalog; every decision-set field
    /// is `None`. Slice β onward populates as registered passes fire.
    let initial (catalog: Catalog) : ComposeState =
        { Catalog = catalog
          TopologicalOrder = None
          NullabilityDecisions = None
          UniqueIndexDecisions = None
          ForeignKeyDecisions = None
          CategoricalUniquenessDecisions = None
          UserRemap = None }

    let withCatalog (catalog: Catalog) (state: ComposeState) : ComposeState =
        { state with Catalog = catalog }

    let withTopologicalOrder (value: TopologicalOrder) (state: ComposeState) : ComposeState =
        { state with TopologicalOrder = Some value }

    let withNullabilityDecisions (value: NullabilityDecisionSet) (state: ComposeState) : ComposeState =
        { state with NullabilityDecisions = Some value }

    let withUniqueIndexDecisions (value: UniqueIndexDecisionSet) (state: ComposeState) : ComposeState =
        { state with UniqueIndexDecisions = Some value }

    let withForeignKeyDecisions (value: ForeignKeyDecisionSet) (state: ComposeState) : ComposeState =
        { state with ForeignKeyDecisions = Some value }

    let withCategoricalUniquenessDecisions
        (value: CategoricalUniquenessDecisionSet)
        (state: ComposeState)
        : ComposeState =
        { state with CategoricalUniquenessDecisions = Some value }

    let withUserRemap (value: UserRemapContext) (state: ComposeState) : ComposeState =
        { state with UserRemap = Some value }

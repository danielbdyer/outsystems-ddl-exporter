namespace Projection.Core

/// Chapter A.4.7 slice ε — strategy `RegisteredTransformMetadata`
/// surface for the registered-intervention pass strategies. Each
/// pass strategy (NullabilityRules, UniqueIndexRules, ForeignKeyRules,
/// CategoricalUniquenessRules, CycleResolution) is metadata-only —
/// strategies aren't independently callable (they're invoked via
/// `Composition.fanOut` from inside the host pass module), so the
/// `RegisteredTransform<'In, 'Out>` typed shell with `Run` field
/// doesn't fit.
///
/// **Compile-order rationale.** Strategy outcome types
/// (`NullabilityOutcome`, `UniqueIndexOutcome`, etc.) are referenced
/// by `Lineage.fs`'s `AnnotationDetail` DU, so the strategy modules
/// must compile before `Lineage.fs` + `TransformRegistry.fs`. The
/// strategy modules themselves therefore cannot embed
/// `let registeredMetadata` directly — the `RegisteredTransformMetadata`
/// type isn't in scope at that compile point. This file lives after
/// `TransformRegistry.fs` and packages the strategy registrations as
/// per-strategy values in one collection.
///
/// **Registration granularity.** Per the chapter A.4.7 open Q7 + the
/// chapter-open's anti-scope clause: registry classification is at
/// the strategy-decision level (not sub-strategy fanOut level).
/// `<Strategy>.evaluate` is the conceptual unit; the per-rule
/// classifications (per-`KeepReason` variant within a strategy) are
/// intra-strategy detail not exposed structurally. The Sites list
/// captures the strategy's classification + harvest-discipline
/// rationale.
[<RequireQualifiedAccess>]
module StrategyRegistrations =

    /// Nullability tightening strategy. Consumed by `NullabilityPass`
    /// via `Composition.fanOut`. Operator-supplied policy
    /// (`Policy.Nullability`) drives the decisions; lands as
    /// Tightening-axis overlay.
    let nullabilityRules : RegisteredTransformMetadata =
        { Name = "nullabilityRules"
          Domain = Data
          StageBinding = Pass
          Sites =
            [ { SiteName = "evaluate"
                Classification = OperatorIntent Tightening
                Rationale = "Per-attribute NOT NULL tightening decision (NullabilityOutcome) per operator-supplied Tightening policy (Cautious / Aggressive / Disabled). Profile evidence (null-count probes) drives the empirical decision; lands as Tightening-axis overlay via the host pass NullabilityPass." } ]
          Status = Active }

    /// Unique-index promotion strategy. Consumed by `UniqueIndexPass`
    /// via `Composition.fanOut`. Operator-supplied policy
    /// (`Policy.UniqueIndex`) drives the decisions; lands as
    /// Tightening-axis overlay.
    let uniqueIndexRules : RegisteredTransformMetadata =
        { Name = "uniqueIndexRules"
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "evaluate"
                Classification = OperatorIntent Tightening
                Rationale = "Per-index UNIQUE promotion decision (UniqueIndexOutcome) per operator-supplied Tightening policy. Profile evidence (duplicate-row probes) drives the empirical decision; lands as Tightening-axis overlay via the host pass UniqueIndexPass." } ]
          Status = Active }

    /// Foreign-key enforcement strategy. Consumed by `ForeignKeyPass`
    /// via `Composition.fanOut`. Operator-supplied policy
    /// (`Policy.ForeignKey`) drives the decisions; lands as
    /// Tightening-axis overlay.
    let foreignKeyRules : RegisteredTransformMetadata =
        { Name = "foreignKeyRules"
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "evaluate"
                Classification = OperatorIntent Tightening
                Rationale = "Per-reference FK enforcement decision (ForeignKeyOutcome) per operator-supplied Tightening policy. Profile evidence (orphan-row probes) drives the empirical decision; lands as Tightening-axis overlay via the host pass ForeignKeyPass." } ]
          Status = Active }

    /// Categorical-uniqueness promotion strategy. Consumed by
    /// `CategoricalUniquenessPass` via `Composition.fanOut`.
    /// Operator-supplied policy (`Policy.CategoricalUniqueness`)
    /// drives the decisions; lands as Tightening-axis overlay.
    let categoricalUniquenessRules : RegisteredTransformMetadata =
        { Name = "categoricalUniquenessRules"
          Domain = Data
          StageBinding = Pass
          Sites =
            [ { SiteName = "evaluate"
                Classification = OperatorIntent Tightening
                Rationale = "Per-attribute categorical-uniqueness promotion decision (CategoricalUniquenessOutcome) per operator-supplied Tightening policy. Profile evidence (cardinality + uniqueness probes) drives the empirical decision; lands as Tightening-axis overlay via the host pass CategoricalUniquenessPass." } ]
          Status = Active }

    /// Cycle-resolution strategy. Consumed by `TopologicalOrderPass`
    /// when the graph contains cycles. The asymmetric-2-cycle
    /// resolver chooses which `Weak` precedence edges to remove to
    /// resolve cycles; the choice is algorithm-internal (no operator
    /// opinion at the per-cycle level), so the site classifies as
    /// `DataIntent`. The broader Ordering operator-intent surface
    /// (SelfLoopPolicy) is captured separately at
    /// `TopologicalOrderPass.registered`'s `selfLoopHandling` site.
    let cycleResolution : RegisteredTransformMetadata =
        { Name = "cycleResolution"
          Domain = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName = "asymmetric2CycleStrategy"
                Classification = DataIntent
                Rationale = "Asymmetric-2-cycle resolver: when a 2-cycle has exactly one Weak precedence edge, remove it to resolve. The choice is algorithm-internal (the resolver's heuristic); no operator opinion at the per-cycle level. Operator-supplied SelfLoopPolicy is a separate concern handled at TopologicalOrderPass.registered's selfLoopHandling site (OperatorIntent Ordering — Q9-trigger-fires worked example)." } ]
          Status = Active }

    /// All five strategy registrations in one list. Slice ζ's
    /// `Compose.run` traversal will fold this list into
    /// `TransformRegistry.all` alongside the pass / adapter / emitter
    /// registrations.
    let all : RegisteredTransformMetadata list =
        [ nullabilityRules
          uniqueIndexRules
          foreignKeyRules
          categoricalUniquenessRules
          cycleResolution ]

namespace Projection.Core

/// Operator-intent axis (chapter A.4.7 slice α; per `DECISIONS 2026-05-15
/// (late) — Pillar 9: harvest-dichotomy classification`).
///
/// Four of the variants (`Selection | Emission | Insertion | Tightening`)
/// correspond exactly to `Projection.Core.Policy.fs`'s DU axes; `Policy`
/// IS operator intent reified at those axes. The fifth variant `Ordering`
/// is the chapter A.4.7 open's Q9-trigger-fires worked example —
/// `TopologicalOrderPass.SelfLoopPolicy` is operator-supplied (not
/// topology-derived) but doesn't fit `Selection | Emission | Insertion |
/// Tightening` (it controls ordering-policy semantics). Per Q9 reserved
/// expansion: "with an opportunity to expand in case we truly find a
/// fifth axis is warranted by real evidence." The structural equivalence
/// weakens from `OverlayAxis = Policy DU axes exactly` to `OverlayAxis ⊃
/// Policy DU axes` until consumer pressure forces the deferred-with-
/// trigger `Policy.fs ↔ OverlayAxis` structural collapse refactor (per
/// chapter A.4.7 open Out-of-scope clause).
///
/// **Closed DU; further expansion requires the same trigger-fires
/// discipline** (`DECISIONS 2026-05-13 — Active deferrals index`; real
/// evidence of an operator-intent axis not subsumed by the existing
/// five).
type OverlayAxis =
    /// Which kinds appear in the catalog (filtering, masking, inactive-
    /// records disposition). `VisibilityMask` is the canonical site.
    | Selection
    /// What physical form a kind takes in emitted output (rename specs,
    /// presentation morphisms). `TableRename` is the canonical site.
    | Emission
    /// What content the catalog gains beyond source evidence (static-row
    /// seeds, synthesized references). `NormalizeStaticPopulations`
    /// touches this conceptually but at the structural/normalization
    /// level rather than the operator-intent level.
    | Insertion
    /// What invariants the catalog enforces beyond source evidence
    /// (NOT NULL strengthening, UNIQUE enforcement, categorical
    /// uniqueness). `NullabilityPass` / `UniqueIndexPass` /
    /// `ForeignKeyPass` / `CategoricalUniquenessPass` are the canonical
    /// sites.
    | Tightening
    /// How nodes are ordered when topology under-determines the choice
    /// (self-loop disposition, cycle-resolution policy). Chapter A.4.7
    /// open's Q9-trigger-fires worked example;
    /// `TopologicalOrderPass.SelfLoopPolicy` is the named real-evidence
    /// trigger. The registry-level site `SelfLoopHandling` lands at
    /// slice ε with `Classification = OperatorIntent Ordering`.
    | Ordering

/// Harvest-dichotomy classification (pillar 9; `DECISIONS 2026-05-15
/// (late)`). Every transformation site in V2 reads under one of two
/// classifications:
///
/// - **`DataIntent`** — preserves data intention; reachable from
///   `Project(catalog, Policy.empty, profile)` without operator opinion;
///   lands in the **skeleton**. Profile-driven *observations* (null
///   counts, FK orphan rows, distribution percentiles) are `DataIntent`
///   evidence; the skeleton consumes them.
///
/// - **`OperatorIntent of OverlayAxis`** — expresses operator-supplied
///   intent through one of the five overlay axes; lands as **registered
///   overlay** with explicit stage binding (in the full chapter A.4.7
///   structure) and `LineageEvent` emission carrying the classification.
///
/// Slice α (this commit) ships the type + carries it on `LineageEvent`;
/// each pass self-classifies per the harvest-discipline analysis prose
/// codified in the pass's module docstring. The full structural
/// enforcement seam (`TransformRegistry` + `Compose.run` traversal +
/// bidirectional property tests) lands at chapter A.4.7 slices β
/// onward; pillar 9 manifests in code via this type first.
///
/// **Skeleton-overlay drift caught at slice θ.** The chapter A.4.7
/// skeleton-purity property test asserts `Compose.runWithSkeleton`
/// emits zero `OperatorIntent` `LineageEvent`s; misclassifying an
/// operator-intent pass as `DataIntent` leaks operator intent into the
/// skeleton and the property fails. Slice α's per-pass classifications
/// are the first opportunity to get the classification right.
type Classification =
    | DataIntent
    | OperatorIntent of OverlayAxis


/// Chapter C slice C.4 — operator-facing **feature-toggle groupings**
/// of registered transformations. Distinct from `OverlayAxis` (which
/// names *whose intent* a transform expresses): `TransformGroup` names
/// *which named preset* the transform belongs to so the operator can
/// flip several related transforms on/off as a unit
/// (`Policy.TransformGroups : Map<TransformGroup, bool>`).
///
/// **Closed DU; preset seed (no operator-defined custom groups)** per
/// `DECISIONS 2026-05-19 (chapter B.4 mid-chapter strategic
/// exploration)` decision 3. Variants land under the closed-DU
/// expansion empirical-test discipline (`DECISIONS 2026-05-13`): a real
/// operator-pull for an unrepresented grouping triggers a new variant
/// + a DECISIONS entry naming the trigger. Today's seed list is the
/// minimum set of pass-chain groupings with concrete operator-toggle
/// pull:
///
///   - **`Tightening`** — the four tightening passes
///     (`NullabilityPass`, `UniqueIndexPass`, `ForeignKeyPass`,
///     `CategoricalUniquenessPass`). Operator may toggle the entire
///     tightening surface off without uninstalling per-intervention
///     config.
///   - **`UserReflow`** — `UserFkReflowPass`. Operator may disable
///     user-FK reflow when user-migration is out of scope for the
///     run (e.g., schema-only canary; non-user-touching deploys).
///
/// Per pillar 9: TransformGroup is an `OperatorIntent`-flavored
/// concept (operator-supplied feature-toggle); the binder + filter
/// live in `Projection.Pipeline` so the Core's registry types stay
/// `DataIntent`-pure (the registry record itself doesn't carry tags —
/// the tag map lives at the Pipeline-realization layer alongside the
/// chain it filters).
///
/// `[<RequireQualifiedAccess>]` because `Tightening` collides with
/// `OverlayAxis.Tightening` — call sites disambiguate as
/// `TransformGroup.Tightening` vs `Tightening` (the OverlayAxis case;
/// unqualified resolution remains).
[<RequireQualifiedAccess>]
type TransformGroup =
    /// The four tightening passes. Operator toggles the entire
    /// tightening surface off without uninstalling per-intervention
    /// config.
    | Tightening
    /// `UserFkReflowPass`. Operator disables user-FK reflow when
    /// user-migration is out of scope for the run.
    | UserReflow

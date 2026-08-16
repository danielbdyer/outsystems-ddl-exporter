namespace Projection.Core

/// Operator-intent axis (chapter A.4.7 slice α; per `DECISIONS 2026-05-15
/// (late) — Pillar 9: harvest-dichotomy classification`).
///
/// THE AXIS VOCABULARY IS THE OPERATOR OUTCOME SPACE (A50, the alignment
/// program align-I.2): conflict detection, by-axis registry queries, the
/// manifest's applied-transforms enumeration, and the policy DSL all
/// quantify over this DU. Its relation to `Projection.Core.Policy.fs`'s
/// decision channels is the TOTAL map `PolicyAxis.overlayAxisOf` below —
/// every Policy channel has exactly one designated axis; `Ordering` is
/// the one axis with an EMPTY Policy preimage (its operator lever is
/// `TopologicalOrderPass.SelfLoopPolicy` config, outside `Policy`). The
/// sixth variant `Identity` landed at align-I.2: `Policy` had grown
/// `UserMatching` (chapter 4.2) and `BridgeRetarget` (the 2026-07 bridge
/// arc) — operator-supplied identity-resolution channels shoehorned into
/// `Selection` against its own contract — and the 2026-05-16 collapse
/// deferral's trigger had FIRED unnoticed at `PolicyExpr.eval`'s
/// `Override` arm (DECISIONS 2026-08-16, the alignment program opens:
/// resolution = path (a)-lite, this vocabulary stays canonical, no
/// structural type fusion).
///
/// **Closed DU; further expansion requires the same trigger-fires
/// discipline** (`DECISIONS 2026-05-13 — Active deferrals index`; real
/// evidence of an operator-intent axis not subsumed by the existing
/// six). SERIALIZATION ORDER IS LOAD-BEARING (T1): the DU tag order
/// sorts durable applied-transforms enumerations — new variants APPEND,
/// never insert.
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
    /// How cross-environment identity resolution reroutes: user-FK value
    /// remapping (`Policy.UserMatching` — ByEmail/BySsKey/ManualOverride/
    /// FallbackToSystemUser) and bridge-entity FK retargeting
    /// (`Policy.BridgeRetarget`). The alignment program's align-I.2
    /// variant (A50's worked example): both channels are operator-supplied
    /// identity decisions, neither affects WHICH kinds appear —
    /// `UserFkReflowPass` / `BridgeRetargetPass` are the canonical sites
    /// (reclassified from `Selection` at align-I.3). APPENDED after
    /// `Ordering` per the T1 serialization-order rule above.
    | Identity

/// Operations on `OverlayAxis` — the canonical string codec for the five
/// axes. `name` is the single source of truth (the closed-DU case name);
/// `tryParse` is its inverse, total over the known tokens and `None` for an
/// unrecognized one (fail-closed, mirroring `ToleratedDivergence.name`/
/// `tryParse`). The durable provenance store (`LifecycleStore`) serializes the
/// per-artifact overlay enumeration through this codec, so the round-trip
/// `name >> tryParse = Some` is the persistence law.
[<RequireQualifiedAccess>]
module OverlayAxis =

    /// Canonical token for an overlay axis. Exhaustive match: a new variant
    /// fires FS0025 here under `TreatWarningsAsErrors`, forcing a token.
    let name (a: OverlayAxis) : string =
        match a with
        | Selection  -> "Selection"
        | Emission   -> "Emission"
        | Insertion  -> "Insertion"
        | Tightening -> "Tightening"
        | Ordering   -> "Ordering"
        | Identity   -> "Identity"

    /// Every known overlay axis (the closed set the round-trip ranges over).
    let allKnown : OverlayAxis list =
        [ Selection; Emission; Insertion; Tightening; Ordering; Identity ]

    /// Parse a token to its axis, or `None` for an unrecognized token.
    /// Derived from `name` so `name >> tryParse` is the identity on every
    /// known variant.
    let tryParse (token: string) : OverlayAxis option =
        allKnown |> List.tryFind (fun a -> name a = token)


/// The `Policy` record's decision channels, ENUMERATED (A50; align-I.2).
/// `Policy` is a record — six fields — so before this DU its channel set
/// was not a value anywhere: nothing could quantify over "every decision
/// channel the operator has," which is exactly how two channels
/// (`UserMatching`, `BridgeRetarget`) landed with no axis designation.
/// `[<RequireQualifiedAccess>]` because four case names deliberately
/// mirror `OverlayAxis` cases.
///
/// **Expansion discipline, fifth step (added at align-I.2):** a new
/// `Policy` field lands in the SAME commit with its `PolicyAxis` case,
/// its `all` entry, and its `overlayAxisOf` arm — the A50 totality
/// property pins the map, and `PolicyExpr.eval`'s Override arm projects
/// through the preimage, so an undesignated channel cannot compile into
/// the DSL.
[<RequireQualifiedAccess>]
type PolicyAxis =
    | Selection
    | Emission
    | Insertion
    | Tightening
    | UserMatching
    | BridgeRetarget

/// Companion enumeration + the A50 total designation map (the
/// `TransformGroup.all` / `WriteSignoff.allModes` single-source shape).
[<RequireQualifiedAccess>]
module PolicyAxis =

    /// Every Policy decision channel — the one list the A50 totality
    /// property, the DSL's Override projection, and the schema-facing
    /// enumerations range over. Order mirrors the `Policy` record's
    /// field order (documentation only; nothing durable sorts by it).
    let all : PolicyAxis list =
        [ PolicyAxis.Selection
          PolicyAxis.Emission
          PolicyAxis.Insertion
          PolicyAxis.Tightening
          PolicyAxis.UserMatching
          PolicyAxis.BridgeRetarget ]

    /// **A50 — the total designation.** Every Policy decision channel has
    /// exactly one `OverlayAxis` home; a new `PolicyAxis` case fails to
    /// compile until designated here. The two identity-resolution
    /// channels designate `Identity` (the align-I.2 variant); the four
    /// founding channels designate their namesakes.
    let overlayAxisOf (a: PolicyAxis) : OverlayAxis =
        match a with
        | PolicyAxis.Selection      -> Selection
        | PolicyAxis.Emission       -> Emission
        | PolicyAxis.Insertion      -> Insertion
        | PolicyAxis.Tightening     -> Tightening
        | PolicyAxis.UserMatching   -> Identity
        | PolicyAxis.BridgeRetarget -> Identity

    /// The DERIVED inverse of the designation: which Policy channels an
    /// overlay axis governs. `Ordering`'s preimage is EMPTY — its
    /// operator lever (`TopologicalOrderPass.SelfLoopPolicy`) lives
    /// outside `Policy` — which makes "Override(Ordering) projects
    /// nothing" a THEOREM of the map rather than a hand-coded silent
    /// case (the audit's a3-F1 fix). Never hand-write this list.
    let preimageOf (axis: OverlayAxis) : PolicyAxis list =
        all |> List.filter (fun a -> overlayAxisOf a = axis)


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
    /// `BridgeRetargetPass`. Opt-in (default off): the operator enables
    /// bridge retargeting for the run only when `overrides.bridgeRetargets`
    /// is declared and greenlit.
    | BridgeRetarget

/// Companion enumeration + config-name projection — the SINGLE list both the
/// config parser (`TransformGroupsBinding.parseGroupName`) and the generated
/// config schema derive their known-group vocabulary from, so "what parses"
/// and "what the schema advertises" cannot disagree (the
/// `WriteSignoff.allModes` precedent). `configName` is a total match: a new
/// group fails to compile until it is named here, and `all` is the one list
/// to extend.
[<RequireQualifiedAccess>]
module TransformGroup =

    let all : TransformGroup list =
        [ TransformGroup.Tightening
          TransformGroup.UserReflow
          TransformGroup.BridgeRetarget ]

    /// The operator-writable config name — the DU case name verbatim.
    let configName (g: TransformGroup) : string =
        match g with
        | TransformGroup.Tightening     -> "Tightening"
        | TransformGroup.UserReflow     -> "UserReflow"
        | TransformGroup.BridgeRetarget -> "BridgeRetarget"

namespace Projection.Core

/// Typed combinator language for composing policies (H-016).
///
/// `PolicyExpr` is a DSL for constructing and composing `Policy` values
/// without direct record construction. The canonical evaluation map
/// `PolicyExpr.eval` is a structure-preserving homomorphism: it maps the
/// expression algebra to the five-axis `Policy` product.
///
/// **Algebra.**
///   - `Atom p`            — lift a concrete policy.
///   - `And (a, b)`        — restrict the Selection axis (intersection);
///                           all other axes take b's value.
///   - `Or (a, b)`         — expand the Selection axis (union);
///                           all other axes take b's value.
///   - `Seq (a, b)`        — sequential composition: b overrides a on every
///                           axis; Tightening interventions accumulate (left
///                           then right), reflecting the registry's additive
///                           nature.
///   - `Override (axis, e)` — evaluate e, extract only the named axis;
///                           all other axes stay at `Policy.empty`.
///
/// **Identity.** `identity = Atom Policy.empty` is the left identity of
/// `Seq`: `eval (Seq identity e) = eval e` for all expressions e. The
/// right identity does NOT hold for axes with non-trivial Policy.empty
/// values (e.g. Selection.IncludeAll overwrites a narrower selection).
/// `simplify` elides identity-atom sequences.
type PolicyExpr =
    /// Lift a concrete policy into the expression language.
    | Atom     of Policy
    /// Restrict the Selection axis: retain only kinds admitted by both sides.
    /// All other axes: right side wins. Tightening interventions accumulate.
    | And      of PolicyExpr * PolicyExpr
    /// Expand the Selection axis: retain kinds admitted by either side.
    /// All other axes: right side wins. Tightening interventions accumulate.
    | Or       of PolicyExpr * PolicyExpr
    /// Sequential composition: right side overrides left on all axes.
    /// Tightening interventions accumulate (left then right).
    | Seq      of PolicyExpr * PolicyExpr
    /// Merge composition (HORIZON Cluster F follow-up): right wins on
    /// **non-default** axes only — left-side non-default values are
    /// preserved when the right side carries the axis at its
    /// `Policy.empty` default. Tightening interventions accumulate
    /// (left then right). The HORIZON H-054 "applyDelta union for
    /// independent axes" semantics: commutative on disjoint-axis
    /// policies, associative across all axes.
    | Merge    of PolicyExpr * PolicyExpr
    /// Apply only the named axis from the child expression; all other
    /// axes remain at `Policy.empty`. `Override (Ordering, _)` produces
    /// `Policy.empty` because `Ordering` has no corresponding Policy axis.
    | Override of OverlayAxis * PolicyExpr


/// Construction and evaluation for `PolicyExpr` (H-016).
[<RequireQualifiedAccess>]
module PolicyExpr =

    /// Left identity of `Seq`. `eval (Seq identity e) = eval e`.
    let identity : PolicyExpr = Atom Policy.empty

    /// Lift a concrete policy into the expression language.
    let ofPolicy (p: Policy) : PolicyExpr = Atom p

    /// **`fromPolicy = ofPolicy`** — the inverse direction of the natural
    /// transformation between `PolicyExpr` and `Policy`. Together with
    /// `eval`, satisfies the round-trip law: `eval (fromPolicy p) = p`
    /// for every `Policy` value. This is the second leg of H-060's
    /// natural-transformation contract (H-060 ↔ H-016).
    ///
    /// The implementation is the same `Atom` lift as `ofPolicy`; the
    /// distinct name makes the round-trip direction explicit at call
    /// sites and documents the law's symmetry. The two-leg pairing
    /// `(fromPolicy, eval)` IS the natural transformation.
    let fromPolicy (p: Policy) : PolicyExpr = Atom p

    // -----------------------------------------------------------------
    // Selection-axis lattice helpers
    // -----------------------------------------------------------------

    /// Intersection of two selection policies (And semantics).
    /// `IncludeAll ∩ x = x`; `IncludeOnly A ∩ IncludeOnly B = IncludeOnly (A ∩ B)`;
    /// `ExcludeOnly A ∩ ExcludeOnly B = ExcludeOnly (A ∪ B)` (more excluded = tighter).
    let private intersectSelection (a: SelectionPolicy) (b: SelectionPolicy) : SelectionPolicy =
        match a, b with
        | IncludeAll,    x            -> x
        | x,             IncludeAll   -> x
        | IncludeOnly ka, IncludeOnly kb -> IncludeOnly (Set.intersect ka kb)
        | ExcludeOnly ka, ExcludeOnly kb -> ExcludeOnly (Set.union ka kb)
        | IncludeOnly ka, ExcludeOnly kb -> IncludeOnly (Set.difference ka kb)
        | ExcludeOnly ka, IncludeOnly kb -> IncludeOnly (Set.difference kb ka)

    /// Union of two selection policies (Or semantics).
    /// `IncludeAll ∪ x = IncludeAll`; `IncludeOnly A ∪ IncludeOnly B = IncludeOnly (A ∪ B)`;
    /// `ExcludeOnly A ∪ ExcludeOnly B = ExcludeOnly (A ∩ B)` (fewer excluded = wider).
    let private unionSelection (a: SelectionPolicy) (b: SelectionPolicy) : SelectionPolicy =
        match a, b with
        | IncludeAll,    _            -> IncludeAll
        | _,             IncludeAll   -> IncludeAll
        | IncludeOnly ka, IncludeOnly kb -> IncludeOnly (Set.union ka kb)
        | ExcludeOnly ka, ExcludeOnly kb -> ExcludeOnly (Set.intersect ka kb)
        | IncludeOnly ka, ExcludeOnly kb -> ExcludeOnly (Set.difference kb ka)
        | ExcludeOnly ka, IncludeOnly kb -> ExcludeOnly (Set.difference ka kb)

    /// Sequential merge of two evaluated policies. `b` wins on all axes
    /// except Tightening and BridgeRetarget, which accumulate (left then
    /// right) — both are intervention registries. This is the primitive
    /// composition used by `Seq`, `And`, and `Or`.
    let private mergePolicy (a: Policy) (b: Policy) : Policy =
        { Selection      = b.Selection
          Emission       = b.Emission
          Insertion      = b.Insertion
          Tightening     = { Interventions = a.Tightening.Interventions @ b.Tightening.Interventions }
          UserMatching   = b.UserMatching
          BridgeRetarget = { BridgeRetargetPolicy.Plans = a.BridgeRetarget.Plans @ b.BridgeRetarget.Plans } }

    /// Evaluate a `PolicyExpr` to a concrete `Policy`.
    ///
    /// Structure-preserving properties:
    ///   - `eval (Atom p) = p`
    ///   - `eval (Seq a b)` is `mergePolicy (eval a) (eval b)`
    ///   - `eval (And a b)` restricts selection of the merge
    ///   - `eval (Or a b)` expands selection of the merge
    ///   - `eval identity = Policy.empty`
    ///   - `eval (Seq identity e) = eval e` (left identity)
    let rec eval (expr: PolicyExpr) : Policy =
        match expr with
        | Atom p ->
            p
        | And (a, b) ->
            let pa = eval a
            let pb = eval b
            { mergePolicy pa pb with
                Selection = intersectSelection pa.Selection pb.Selection }
        | Or (a, b) ->
            let pa = eval a
            let pb = eval b
            { mergePolicy pa pb with
                Selection = unionSelection pa.Selection pb.Selection }
        | Seq (a, b) ->
            mergePolicy (eval a) (eval b)
        | Merge (a, b) ->
            Policy.merge (eval a) (eval b)
        | Override (axis, child) ->
            let p = eval child
            match axis with
            | Selection  -> { Policy.empty with Selection  = p.Selection  }
            | Emission   -> { Policy.empty with Emission   = p.Emission   }
            | Insertion  -> { Policy.empty with Insertion  = p.Insertion  }
            | Tightening -> { Policy.empty with Tightening = p.Tightening }
            | Ordering   -> Policy.empty  // Ordering has no corresponding Policy axis

    /// Structural simplification. Eliminates `Seq(identity, e)` (left
    /// identity). Recurses into all sub-expressions. Does not alter
    /// semantics: `eval (simplify e) = eval e`.
    ///
    /// **Why only left identity?** `Seq` uses right-wins on all axes except
    /// Tightening, so `Seq(e, identity)` overwrites e's non-empty axes with
    /// `Policy.empty`'s values — it does NOT equal `e` in general. Only the
    /// left identity `Seq(identity, e) = e` holds structurally.
    let rec simplify (expr: PolicyExpr) : PolicyExpr =
        match expr with
        | Atom _           -> expr
        | And  (a, b)      -> And (simplify a, simplify b)
        | Or   (a, b)      -> Or  (simplify a, simplify b)
        | Seq  (a, b)      ->
            match simplify a, simplify b with
            | Atom p, sb when p = Policy.empty -> sb   // left identity only
            | sa, sb                           -> Seq (sa, sb)
        | Merge (a, b)     ->
            // Merge is two-sided identity: empty on either side
            // elides. The unconditional `Policy.merge` makes both
            // directions of the identity law sound.
            match simplify a, simplify b with
            | Atom p, sb when p = Policy.empty -> sb
            | sa, Atom p when p = Policy.empty -> sa
            | sa, sb                           -> Merge (sa, sb)
        | Override (axis, child) ->
            Override (axis, simplify child)

    /// Produce a `PolicyExpr` that evaluates to `eval b`, showing the
    /// "after" state. Returns `identity` when both expressions evaluate
    /// to the same policy, otherwise `Atom (eval b)`.
    ///
    /// For axis-by-axis structural diffs, use `PolicyDiff.compare` (Pipeline).
    let diff (a: PolicyExpr) (b: PolicyExpr) : PolicyExpr =
        let pb = eval b
        if eval a = pb then identity
        else Atom pb

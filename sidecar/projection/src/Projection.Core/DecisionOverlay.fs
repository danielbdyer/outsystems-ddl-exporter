namespace Projection.Core

/// Wave-2 slice 2.1 — the emitter-consumable projection of the three
/// tightening decision sets in `ComposeState`. Collapses the
/// `Option<NullabilityDecisionSet>` / `Option<UniqueIndexDecisionSet>` /
/// `Option<ForeignKeyDecisionSet>` evidence into flat `Set<SsKey>` lookups
/// the SSDT emitter consults at emission time.
///
/// **A18-safe (the load-bearing classification).** A `DecisionOverlay`
/// carries *decisions* — facts derived from evidence (Catalog × Profile)
/// under intent (Policy) by the passes. By the time a decision reaches the
/// emitter the operator's intent has been discharged into evidence: a
/// decision is the fact "this attribute was decided NOT NULL under the
/// registered intervention given the observed null count." The emitter
/// consumes facts, not intent — so threading `DecisionOverlay` as a curried
/// prefix argument (slice 2.2) keeps the `Emitter` port `Catalog`-only
/// (A18-amended holds). `empty` is the observable identity:
/// `ofComposeState (ComposeState.initial c) = empty`, so emission with no
/// registered interventions is byte-identical to today (A42 candidate).
type DecisionOverlay =
    {
        /// AttributeKeys decided `NullabilityOutcome.EnforceNotNull`.
        EnforceNotNull : Set<SsKey>
        /// AttributeKeys decided `KeepNullable OperatorOverride` — the
        /// operator's EXPLICIT posture relaxes the emitted nullability
        /// below the source's declaration (DECISIONS 2026-07-15, the
        /// estate A6 amendment: the one lawful loosening; the reopen
        /// probe retires it). Evidence-reasoned KeepNullable outcomes
        /// (NoTighteningSignal / RelaxedUnderEvidence) never land here —
        /// evidence never loosens source truth.
        KeepNullable : Set<SsKey>
        /// IndexKeys decided `UniqueIndexOutcome.EnforceUnique`.
        EnforceUnique : Set<SsKey>
        /// ReferenceKeys decided `ForeignKeyOutcome.DoNotEnforce` — drop the
        /// inline FK constraint at emission.
        DropFk : Set<SsKey>
        /// ReferenceKeys decided `EnforceConstraint (ScriptWithNoCheck _)` or
        /// `EnforceConstraint NoCheckWithoutEvidence` — emit the FK but
        /// `WITH NOCHECK` (untrusted).
        NoCheckFk : Set<SsKey>
        /// ReferenceKeys decided to RETARGET through a bridge attribute — each maps
        /// to the target ATTRIBUTE key (on the bridge kind) the FK resolves through
        /// INSTEAD of the original parent's primary key. The child FK cell value is
        /// unchanged; only the constraint target moves. The emitter renders the FK
        /// against the bridge attribute's kind + column, and `PhysicalSchema`
        /// reflects the same, so the round-trip comparator stays consistent — the
        /// exact discipline `DropFk` / `NoCheckFk` follow. `Map.empty` ⇒ no retarget
        /// (byte-identical). Policy-derived (A18): a retarget is a decided fact,
        /// kept OUT of the source IR and threaded as overlay.
        RetargetFk : Map<SsKey, SsKey>
    }

[<RequireQualifiedAccess>]
module DecisionOverlay =

    /// The empty overlay — no tightening decided. Observable identity:
    /// `ofComposeState (ComposeState.initial c) = empty`.
    let empty : DecisionOverlay =
        {
            EnforceNotNull = Set.empty
            KeepNullable = Set.empty
            EnforceUnique = Set.empty
            DropFk = Set.empty
            NoCheckFk = Set.empty
            RetargetFk = Map.empty
        }

    let private nullabilityKeys (decisions: NullabilityDecisionSet option) : Set<SsKey> =
        match decisions with
        | None -> Set.empty
        | Some s ->
            s.Decisions
            |> List.choose (fun d ->
                match d.Outcome with
                | NullabilityOutcome.EnforceNotNull _ -> Some d.AttributeKey
                | _ -> None)
            |> Set.ofList

    /// The operator's explicit keep-nullable posture — `OperatorOverride`
    /// outcomes ONLY (the estate A6 amendment). The evidence-reasoned
    /// KeepNullable variants stay out by construction: a relaxation is an
    /// operator's named act, never an evidence inference.
    let private keepNullableKeys (decisions: NullabilityDecisionSet option) : Set<SsKey> =
        match decisions with
        | None -> Set.empty
        | Some s ->
            s.Decisions
            |> List.choose (fun d ->
                match d.Outcome with
                | NullabilityOutcome.KeepNullable OperatorOverride -> Some d.AttributeKey
                | _ -> None)
            |> Set.ofList

    let private uniqueKeys (decisions: UniqueIndexDecisionSet option) : Set<SsKey> =
        match decisions with
        | None -> Set.empty
        | Some s ->
            s.Decisions
            |> List.choose (fun d ->
                match d.Outcome with
                | UniqueIndexOutcome.EnforceUnique _ -> Some d.IndexKey
                | _ -> None)
            |> Set.ofList

    let private isDropFk (o: ForeignKeyOutcome) : bool =
        match o with
        | ForeignKeyOutcome.DoNotEnforce _ -> true
        | _ -> false

    let private isNoCheckFk (o: ForeignKeyOutcome) : bool =
        match o with
        | ForeignKeyOutcome.EnforceConstraint (ScriptWithNoCheck _) -> true
        | ForeignKeyOutcome.EnforceConstraint NoCheckWithoutEvidence -> true
        | _ -> false

    let private fkKeys
        (predicate: ForeignKeyOutcome -> bool)
        (decisions: ForeignKeyDecisionSet option)
        : Set<SsKey> =
        match decisions with
        | None -> Set.empty
        | Some s ->
            s.Decisions
            |> List.choose (fun d -> if predicate d.Outcome then Some d.ReferenceKey else None)
            |> Set.ofList

    /// Project the three tightening decision sets in a ComposeState into the
    /// overlay. `None` fields contribute `Set.empty` (the
    /// observable-identity path); `DoNotEnforce` and `ScriptWithNoCheck`
    /// partition the FK decisions across `DropFk` / `NoCheckFk`.
    let ofComposeState (state: ComposeState) : DecisionOverlay =
        {
            EnforceNotNull = nullabilityKeys state.NullabilityDecisions
            KeepNullable = keepNullableKeys state.NullabilityDecisions
            EnforceUnique = uniqueKeys state.UniqueIndexDecisions
            DropFk = fkKeys isDropFk state.ForeignKeyDecisions
            NoCheckFk = fkKeys isNoCheckFk state.ForeignKeyDecisions
            RetargetFk = state.BridgeRetargets |> Option.defaultValue Map.empty
        }

namespace Projection.Core.Passes

open Projection.Core

/// The symmetric-closure pass — A10's commitment in code. References in
/// the catalog are directional (from a source kind's attribute to a
/// target kind); the surface may want bidirectional navigation
/// ("Customer.Orders" alongside "Order.Customer"). This pass walks the
/// catalog and, for every directional reference, attaches an inverse
/// reference on the target kind pointing back at the source. Inverses
/// carry `Derived(originalRefKey, "inverse")` SsKeys per A5 — the
/// derivation is deterministic, machine-checkable, and recoverable.
///
/// Idempotent: re-running the pass does not double-add inverses (the
/// scan ignores references whose SsKey is already a `Derived(..., "inverse")`).
///
/// SsKey discipline: identity is never written on the source side of an
/// inverse — derived keys are only ever produced by `SsKey.derived`,
/// never freshly constructed.
[<RequireQualifiedAccess>]
module SymmetricClosure =

    /// Pass version. Bump when the inverse's `Name` derivation rule or
    /// the `OnDelete` default changes.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "symmetricClosure"

    /// The reserved derivation reason for inverse references. The string
    /// is registered in DECISIONS.md (2026-05-06 — SsKey as a sum type).
    [<Literal>]
    let inverseReason : string = "inverse"

    /// Does this reference's SsKey indicate it is itself an inverse from
    /// a previous run of the pass? Used to keep the pass idempotent.
    let private isInverseRef (r: Reference) : bool =
        match r.SsKey with
        | DerivedFrom (_, reason) when reason = inverseReason -> true
        | _ -> false

    let private deriveInverseKey (r: Reference) : SsKey =
        // A5 enforced by SsKey.derivedFrom; the deterministic formula is
        // (original-key, "inverse").
        match SsKey.derivedFrom r.SsKey inverseReason with
        | Ok k  -> k
        | Error _ ->
            // Per chapter 3.5 deep audit (2026-05-09):
            // `SsKey.derivedFrom` only fails on blank `reason`;
            // `inverseReason` is the `[<Literal>]` constant
            // `"inverse"`, so this branch is unreachable by F#
            // type-system construction. The legacy
            // implementation built a debug string from the
            // (unreachable-by-construction) error codes via
            // `String.concat ", "` + interpolated string —
            // both string-concatenation primitives. Defensive:
            // bare static phrase to `invalidOp`; the BCL
            // `InvalidOperationException` carries enough context
            // (call stack + the static phrase) for postmortem
            // diagnosis. The unreachable error-codes detail
            // gains nothing at the cost of two concatenation
            // primitives.
            invalidOp "symmetricClosure: SsKey.derivedFrom rejected the reserved 'inverse' reason; this branch is structurally unreachable."

    /// Build the inverse reference if the target kind has a primary key.
    /// Returns `None` (with a documented skip-reason) when the target
    /// lacks a PK or when the target is absent from the catalog.
    let private buildInverse (sourceKind: Kind) (r: Reference) (target: Kind) : Reference option =
        match Kind.primaryKey target with
        | []         -> None
        | pkAttr :: _ ->
            // For composite PKs the synthetic milestone takes the first
            // PK attribute. Composite-FK semantics arrive when a real V1
            // fixture has them; the migration path is documented in the
            // EntityDependencySorter admire entry.
            Some
                { SsKey           = deriveInverseKey r
                  Name            = sourceKind.Name
                  SourceAttribute = pkAttr.SsKey
                  TargetKind      = sourceKind.SsKey
                  OnDelete        = NoAction
                  // Inverse references inherit the original's User-FK
                  // status — if the original is a User-FK (CreatedBy
                  // → users), its inverse (users → entity that
                  // created it) carries the same flag for consumer
                  // gating at emission time.
                  IsUserFk        = r.IsUserFk }

    let private hasInverseAlready (refs: Reference list) (key: SsKey) : bool =
        refs |> List.exists (fun r -> r.SsKey = key)

    /// Pillar 9 (chapter A.4.7 slice α): symmetric closure derives
    /// inverse references from the existing graph topology — no
    /// operator opinion enters. Skipped events name a topology-derived
    /// reason (target absent / no PK). Lands in the skeleton.
    let private classification : Classification = DataIntent

    let private createdEvent (key: SsKey) : LineageEvent =
        { PassName       = passName
          PassVersion    = version
          SsKey          = key
          TransformKind  = Created
          Classification = classification }

    let private skippedEvent (key: SsKey) (reason: SymmetricClosureSkipReason) : LineageEvent =
        // Chapter-3.6 slice-γ: typed `SymmetricClosureSkipReason`
        // payload replaces the prior "skipped: ..." prose strings.
        // The two skip cases (`TargetKindAbsent`,
        // `TargetHasNoPrimaryKey`) classified in `classifyStep` flow
        // through structurally to audit consumers.
        { PassName       = passName
          PassVersion    = version
          SsKey          = key
          TransformKind  = Annotated (ClosureSkipped reason)
          Classification = classification }

    /// Run the pass. For every directional reference whose target is
    /// resolvable in the catalog and has at least one primary-key
    /// attribute, attach an inverse reference on the target kind.
    /// Emits one `Created` lineage event per inverse added; one
    /// `Annotated` event per skip (target absent or no PK), so the trail
    /// is auditable.
    /// One step of the symmetric-closure fold: classify a single
    /// (source, reference) pair into either a skip event, a no-op,
    /// or a `(createdEvent, inverse)` pair to fold into the
    /// inverses-by-target map.
    type private Step =
        /// No event, no map update (idempotence cases).
        | NoOp
        /// Add a skip-annotated event; no map update.
        | Skip of LineageEvent
        /// Add a created event; add the inverse to the target's
        /// inverse list in the accumulator map.
        | Created of LineageEvent * targetKey: SsKey * inverse: Reference

    let private classifyStep
        (kindByKey: Map<SsKey, Kind>)
        (sourceKind: Kind)
        (r: Reference)
        : Step =
        if isInverseRef r then NoOp
        else
            let inverseKey = deriveInverseKey r
            match Map.tryFind r.TargetKind kindByKey with
            | None ->
                Skip (skippedEvent r.SsKey TargetKindAbsent)
            | Some target ->
                if hasInverseAlready target.References inverseKey then NoOp
                else
                    match buildInverse sourceKind r target with
                    | None ->
                        Skip (skippedEvent r.SsKey TargetHasNoPrimaryKey)
                    | Some inverse ->
                        Created (createdEvent inverse.SsKey, target.SsKey, inverse)

    /// Run the pass. For every directional reference whose target is
    /// resolvable in the catalog and has at least one primary-key
    /// attribute, attach an inverse reference on the target kind.
    /// Emits one `Created` lineage event per inverse added; one
    /// `Annotated` event per skip (target absent or no PK), so the trail
    /// is auditable.
    ///
    /// Pure F# fold — no `let mutable`. The classifier `classifyStep`
    /// reduces each `(sourceKind, reference)` pair to a `Step` DU
    /// variant; the fold accumulator `(events: LineageEvent list,
    /// inversesByTarget: Map<SsKey, Reference list>)` carries the
    /// closure-construction state immutably.
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `SymmetricClosure.registered.Run`
    let private run (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.symmetricClosure"
        let allKinds = Catalog.allKinds c
        let kindByKey =
            allKinds
            |> List.map (fun k -> k.SsKey, k)
            |> Map.ofList

        // Flatten `(sourceKind, reference)` pairs as the fold's input
        // sequence. Order preserved: all references from the first
        // kind, then all from the second, etc.
        let pairs =
            seq {
                for sourceKind in allKinds do
                    for r in sourceKind.References do
                        yield sourceKind, r
            }

        // Fold into `(eventsRev, inversesByTarget)`. Events accumulate
        // reversed (cons-and-reverse pattern); reversed once at the end.
        let initial : LineageEvent list * Map<SsKey, Reference list> =
            [], Map.empty
        let eventsRev, inversesByTarget =
            pairs
            |> Seq.fold
                (fun (eventsAcc, mapAcc) (sourceKind, r) ->
                    match classifyStep kindByKey sourceKind r with
                    | NoOp -> eventsAcc, mapAcc
                    | Skip ev -> ev :: eventsAcc, mapAcc
                    | Created (ev, targetKey, inverse) ->
                        let current =
                            Map.tryFind targetKey mapAcc
                            |> Option.defaultValue []
                        let mapAcc' =
                            Map.add targetKey (inverse :: current) mapAcc
                        ev :: eventsAcc, mapAcc')
                initial
        let events = List.rev eventsRev

        let withInverses =
            { Modules =
                c.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                match Map.tryFind k.SsKey inversesByTarget with
                                | None       -> k
                                | Some toAdd ->
                                    // Reverse so inverses appear in the
                                    // order they were discovered (the
                                    // accumulator builds in reverse
                                    // because of `inverse :: current`).
                                    { k with References = k.References @ List.rev toAdd }) })
              Sequences = c.Sequences }

        Lineage.ofValueAndEvents events withInverses

    /// Chapter A.4.7 slice γ. Single `DataIntent` site: derives
    /// inverse references from existing graph topology + PK presence.
    /// Skipped events carry topology-derived reasons (target absent /
    /// no PK); no operator opinion enters.
    let registered : RegisteredTransform<Catalog, Catalog> =
        { Name = passName
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "synthesizeInverse"
                Classification = classification
                Rationale = "For each directional reference, synthesize the inverse on the target kind if the target is resolvable and has a primary key. Topology-derived; no operator opinion enters." } ]
          Run = fun c -> run c |> Lineage.map Diagnostics.ofValue
          Status = Active }

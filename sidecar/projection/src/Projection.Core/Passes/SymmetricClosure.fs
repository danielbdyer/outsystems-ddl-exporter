namespace Projection.Core.Passes

// LINT-ALLOW-FILE-MUTATION: Pass-driver event accumulation + inversesByTarget map for symmetric-
//   closure construction. Reified through Lineage.ofValueAndEvents.

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
        | Success k  -> k
        | Failure es ->
            // SsKey.derivedFrom only fails on blank reason; "inverse" is
            // a compile-time literal, so this branch is unreachable. Fail
            // loudly if the invariant is ever violated.
            let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
            invalidOp $"symmetricClosure: SsKey.derivedFrom rejected reserved reason ({codes})"

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
                  OnDelete        = NoAction }

    let private hasInverseAlready (refs: Reference list) (key: SsKey) : bool =
        refs |> List.exists (fun r -> r.SsKey = key)

    let private createdEvent (key: SsKey) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = key
          TransformKind = Created }

    let private skippedEvent (key: SsKey) (detail: string) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = key
          TransformKind = Annotated detail }

    /// Run the pass. For every directional reference whose target is
    /// resolvable in the catalog and has at least one primary-key
    /// attribute, attach an inverse reference on the target kind.
    /// Emits one `Created` lineage event per inverse added; one
    /// `Annotated` event per skip (target absent or no PK), so the trail
    /// is auditable.
    let run (c: Catalog) : Lineage<Catalog> =
        let allKinds = Catalog.allKinds c
        let kindByKey =
            allKinds
            |> List.map (fun k -> k.SsKey, k)
            |> Map.ofList

        let events = ResizeArray<LineageEvent>()
        let mutable inversesByTarget : Map<SsKey, Reference list> = Map.empty

        for sourceKind in allKinds do
            for r in sourceKind.References do
                if isInverseRef r then
                    // Idempotence: don't compute the inverse-of-an-inverse.
                    ()
                else
                    let inverseKey = deriveInverseKey r
                    match Map.tryFind r.TargetKind kindByKey with
                    | None ->
                        events.Add(skippedEvent r.SsKey "skipped: target kind absent")
                    | Some target ->
                        if hasInverseAlready target.References inverseKey then
                            // Idempotence at the second-run level: an
                            // inverse with this SsKey already lives on
                            // the target.
                            ()
                        else
                            match buildInverse sourceKind r target with
                            | None ->
                                events.Add(skippedEvent r.SsKey "skipped: target has no primary key")
                            | Some inverse ->
                                events.Add(createdEvent inverse.SsKey)
                                let current =
                                    Map.tryFind target.SsKey inversesByTarget
                                    |> Option.defaultValue []
                                inversesByTarget <-
                                    Map.add target.SsKey (inverse :: current) inversesByTarget

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
                                    // mutable map accumulates in reverse
                                    // because of `inverse :: current`).
                                    { k with References = k.References @ List.rev toAdd }) }) }

        Lineage.ofValueAndEvents (List.ofSeq events) withInverses

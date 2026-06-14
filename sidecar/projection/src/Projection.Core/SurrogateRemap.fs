namespace Projection.Core

// LINT-ALLOW-FILE: validation-error message construction in
// `SurrogateRemapContext.capture` uses `sprintf` to format the
// offending kind identity + source surrogate into the diagnostic
// message. Same allowed-exception class as `UserRemap.fs` /
// `Catalog.create` (per their LINT-ALLOW-FILE blocks); the validation
// payload is operator-facing audit-trail prose where typed-value
// formatting is the right primitive.

/// Surrogate-remap is the generic, any-kind identity evidence the
/// pipeline threads anywhere FK values have to cross identity spaces.
/// Two consumers ship today — the Transfer realization (re-point at
/// load time) and the static-artifact emit path (re-point at MERGE-
/// rendering time, the `full-export` consumer) — and the carrier types
/// + apply primitives live here, separate from any one acquisition
/// method. `Reconciliation` (sink-row matching, transfer-flavored) and
/// `UserFkReflowPass` (operator user-matching strategy, V1-shaped) are
/// two acquisition surfaces; they produce evidence in this file's
/// `SurrogateRemapContext` shape, consumed at every emit/realization
/// site through `SurrogateRemap.remapRowFks`.

/// A surrogate primary-key value as it exists in the `Source` substrate's
/// identity space, carried in the `RawValueCodec` raw-string form (the
/// same form `StaticRow.Values` uses). The orientation marker prevents
/// passing a `SourceKey` where an `AssignedKey` is expected — the
/// surrogate-remap analog of `SourceUserId` / `TargetUserId`.
type SourceKey = SourceKey of string

/// A surrogate primary-key value as assigned by the target identity
/// space — sink-minted at insert time in the Transfer flow, sink-pre-
/// existing for a reconciled match, or operator-supplied in a static-
/// artifact remap. Sibling to `SourceKey`; consumed by every FK re-point.
type AssignedKey = AssignedKey of string

[<RequireQualifiedAccess>]
module SourceKey =
    let ofString (raw: string) : SourceKey = SourceKey raw
    let value (SourceKey v) : string = v

[<RequireQualifiedAccess>]
module AssignedKey =
    let ofString (raw: string) : AssignedKey = AssignedKey raw
    let value (AssignedKey v) : string = v

/// How a kind's surrogate primary key is established in the target
/// identity space. Derived from the kind's PK structure (`IsIdentity`)
/// when the disposition is structural — `DataIntent`; operator-chosen
/// when the operator names which kinds reconcile to pre-existing target
/// identities.
///
///   - `AssignedBySink` — the target mints the surrogate (the kind's PK
///     is an IDENTITY/autonumber column). Under DML-only write rights the
///     source key cannot be force-inserted, so it must be captured and
///     remapped, and every FK pointing at this kind re-pointed.
///   - `PreservedFromSource` — the source key is written directly (a
///     business / non-identity PK). No remap; the FK value stays correct
///     because the referenced key is preserved.
///   - `ReconciledByRule` — the referenced rows already exist in the
///     target identity space. Match the Source surrogate to the *pre-
///     existing* target surrogate by an operator ruleset, then re-point
///     FKs. Operator-chosen — NOT derivable from `IsIdentity`; `ofKind`
///     never returns it.
[<RequireQualifiedAccess>]
type IdentityDisposition =
    | AssignedBySink
    | PreservedFromSource
    | ReconciledByRule

[<RequireQualifiedAccess>]
module IdentityDisposition =

    /// Classify a kind from its primary-key structure. A kind whose
    /// primary key carries the IDENTITY property is `AssignedBySink`;
    /// otherwise the source key is `PreservedFromSource`. Pure over
    /// `Catalog` evidence — the `IsPrimaryKey` / `IsIdentity` flags
    /// already ride on every `Attribute`.
    let ofKind (kind: Kind) : IdentityDisposition =
        let pkIsIdentity =
            kind.Attributes
            |> List.exists (fun a -> a.IsPrimaryKey && a.IsIdentity)
        if pkIsIdentity then IdentityDisposition.AssignedBySink
        else IdentityDisposition.PreservedFromSource

    /// NM-26 — the SINGLE-SOURCED `SET IDENTITY_INSERT` bracketing
    /// predicate shared by every data emitter (`StaticPopulationEmitter`,
    /// `StaticSeedsEmitter`, `MigrationDependenciesEmitter`).
    ///
    /// **Why this is `IsIdentity` over `IsPrimaryKey && IsIdentity`.**
    /// The disposition split (`AssignedBySink` ⇔ PK-is-IDENTITY) answers
    /// a DIFFERENT question — "does the sink mint the surrogate, so a
    /// remap is needed?" — and is correct for THAT. But the SQL Server
    /// requirement for `SET IDENTITY_INSERT [t] ON` is narrower in
    /// meaning and broader in scope: it is required whenever an `INSERT`
    /// supplies an explicit value for the table's IDENTITY column,
    /// *whether or not that column is the primary key*. A table has at
    /// most one IDENTITY column; the emitters insert ALL columns from
    /// the same static-population rows (`orderedColumnNames`), so a kind
    /// with a NON-PK identity column (a natural/business PK plus an
    /// OutSystems autonumber surrogate) still writes into the IDENTITY
    /// column and SQL Server rejects the `INSERT`/`MERGE` without the
    /// bracket. `AssignedBySink` misses exactly that kind. Routing every
    /// emitter through this predicate makes the three siblings agree by
    /// construction (T11) and closes the deploy-rejection hazard NM-25
    /// named for the PK case, here for the non-PK case.
    let needsIdentityInsert (kind: Kind) : bool =
        kind.Attributes |> List.exists (fun a -> a.IsIdentity)


/// Per-kind Source→target surrogate mapping. Outer key: the kind's
/// `SsKey`. Inner: `SourceKey` → `AssignedKey`. Acquired by Transfer's
/// reconcile-against-sink, or by `UserFkReflowPass`'s operator-matching,
/// or operator-supplied at emit time; consumed wherever FK values cross
/// identity spaces.
///
/// **Invariant** (smart-constructor `capture`): within a kind, a
/// `SourceKey` maps to at most one `AssignedKey`. A second `capture` of
/// the same source surrogate is a double-bind and is rejected.
type SurrogateRemapContext =
    {
        Assignments : Map<SsKey, Map<SourceKey, AssignedKey>>
    }


[<RequireQualifiedAccess>]
module SurrogateRemapContext =

    let private duplicateSource (kind: SsKey) (source: SourceKey) =
        ValidationError.create
            "surrogateRemap.duplicateSource"
            (sprintf
                "SurrogateRemapContext invariant violation: source surrogate '%s' captured twice for kind %s."
                (SourceKey.value source)
                (SsKey.rootOriginal kind))

    let empty : SurrogateRemapContext =
        { Assignments = Map.empty }

    let capture
        (kind: SsKey)
        (source: SourceKey)
        (assigned: AssignedKey)
        (ctx: SurrogateRemapContext)
        : Result<SurrogateRemapContext> =
        let inner =
            ctx.Assignments
            |> Map.tryFind kind
            |> Option.defaultValue Map.empty
        match Map.tryFind source inner with
        | Some _ -> Result.failureOf (duplicateSource kind source)
        | None ->
            let inner' = Map.add source assigned inner
            Result.success
                { ctx with Assignments = Map.add kind inner' ctx.Assignments }

    let tryFindAssigned
        (kind: SsKey)
        (source: SourceKey)
        (ctx: SurrogateRemapContext)
        : AssignedKey option =
        ctx.Assignments
        |> Map.tryFind kind
        |> Option.bind (Map.tryFind source)

    let assignmentCount (ctx: SurrogateRemapContext) : int =
        ctx.Assignments
        |> Map.toSeq
        |> Seq.sumBy (fun (_, inner) -> Map.count inner)

    let kindCount (ctx: SurrogateRemapContext) : int =
        Map.count ctx.Assignments

    /// True iff the context carries no captures — the neutral element
    /// that consumers use to skip remap application entirely.
    let isEmpty (ctx: SurrogateRemapContext) : bool =
        Map.isEmpty ctx.Assignments


/// One FK row reference the remap could not resolve: the column targets
/// a kind in the remap set, but the referenced Source surrogate has no
/// matched target identity. Consumers drop the row and surface this
/// diagnostic (skip-and-diagnose).
type UnresolvedReference =
    {
        Column           : Name
        Target           : SsKey
        UnresolvedSource : SourceKey
    }

/// The outcome of applying a `SurrogateRemapContext` to one kind's rows
/// at a re-point site: the rows kept (with target-FK values re-pointed
/// to the assigned surrogate) and the references dropped because the
/// remap had no matched identity.
type RemappedRows =
    {
        Rows    : StaticRow list
        Skipped : UnresolvedReference list
    }

/// `RemappedRows`' sibling at the in-flight quantum grain (Q3): the quanta
/// kept (FK cells re-pointed, copy-on-write) and the references dropped
/// because the remap had no matched identity.
type RemappedQuanta =
    {
        Rows    : RowQuantum list
        Skipped : UnresolvedReference list
    }


[<RequireQualifiedAccess>]
module SurrogateRemap =

    /// The FK columns on `kind` whose target is in the given set, keyed
    /// by column `Name` (the key `StaticRow.Values` uses) → target's
    /// `SsKey` (the key `SurrogateRemapContext` uses). Pure over the
    /// kind's FK graph; the set parameter names *which* targets matter,
    /// so the same function serves the transfer realization (set =
    /// reconciled kinds) and the static-artifact emit path (set =
    /// operator-supplied remap's kinds).
    let fkColumnsTargeting (targets: Set<SsKey>) (kind: Kind) : Map<Name, SsKey> =
        kind.References
        |> List.choose (fun r ->
            if Set.contains r.TargetKind targets then
                Kind.tryFindAttribute r.SourceAttribute kind
                |> Option.map (fun a -> a.Name, r.TargetKind)
            else None)
        |> Map.ofList

    /// Apply a surrogate re-point to one kind's rows through an INJECTED
    /// lookup (A40 harmonization — same algorithm, the assignment store is
    /// the parameterized axis: the pure `SurrogateRemapContext` for
    /// plan-side remaps, the realization layer's packed store for the
    /// estate-scale capture path). For each row, every `fkTargets` column
    /// carrying a non-NULL Source surrogate is resolved: a hit re-points
    /// the value to the assigned surrogate; a miss drops the row
    /// (skip-and-diagnose). A NULL / absent FK is left untouched. Pure and
    /// order-preserving (T1 determinism).
    let remapRowFksWith
        (tryFindAssigned: SsKey -> string -> string option)
        (fkTargets: Map<Name, SsKey>)
        (rows: StaticRow list)
        : RemappedRows =
        let mutable kept : StaticRow list = []
        let mutable skipped : UnresolvedReference list = []
        for row in rows do
            let resolved =
                fkTargets
                |> Map.fold
                    (fun acc col target ->
                        match acc with
                        | Error _ -> acc
                        | Ok values ->
                            match Map.tryFind col values with
                            | None -> Ok values
                            | Some v when v = "" -> Ok values
                            | Some v ->
                                match tryFindAssigned target v with
                                | Some assigned -> Ok (Map.add col assigned values)
                                | None ->
                                    Error { Column = col; Target = target; UnresolvedSource = SourceKey.ofString v })
                    (Ok row.Values)
            match resolved with
            | Ok values  -> kept <- { row with Values = values } :: kept
            | Error uref -> skipped <- uref :: skipped
        { Rows    = List.rev kept
          Skipped = List.rev skipped }

    /// Resolve `fkColumnsTargeting`'s Name-keyed targets onto a basis as
    /// ordinals, ONCE per kind/stream (Q3) — never per row. Emitted in
    /// Name order (the order `remapRowFksWith`'s Map fold visits), so the
    /// quantum remap reports the same first-unresolved column a Map-carried
    /// remap would. A column absent from the basis carries no value on
    /// this stream and is dropped from the target set.
    let fkOrdinalsTargeting
        (basis: RowBasis)
        (fkTargets: Map<Name, SsKey>)
        : (int * Name * SsKey) list =
        fkTargets
        |> Map.toList
        |> List.choose (fun (col, target) ->
            RowBasis.tryOrdinal col basis
            |> Option.map (fun ix -> ix, col, target))

    /// `remapRowFksWith` at the quantum grain (A40 — same algorithm, the
    /// row carrier is the parameterized axis). FK targets arrive as basis
    /// ordinals (`fkOrdinalsTargeting`); a re-point copies the cells array
    /// once per changed row (copy-on-write — quanta are immutable values);
    /// an unresolvable non-NULL FK drops the row (skip-and-diagnose),
    /// exactly as the Map-carried remap does. Pure and order-preserving
    /// (T1 determinism).
    let remapQuantumFksWith
        (tryFindAssigned: SsKey -> string -> string option)
        (fkTargets: (int * Name * SsKey) list)
        (rows: RowQuantum list)
        : RemappedQuanta =
        if List.isEmpty fkTargets then { Rows = rows; Skipped = [] }
        else
            let mutable kept : RowQuantum list = []
            let mutable skipped : UnresolvedReference list = []
            for q in rows do
                let mutable cells = q.Cells
                let mutable copied = false
                let mutable failure : UnresolvedReference option = None
                for (ix, col, target) in fkTargets do
                    if Option.isNone failure then
                        let v = cells.[ix]
                        if v <> "" then
                            match tryFindAssigned target v with
                            | Some assigned ->
                                if not copied then
                                    cells <- Array.copy cells
                                    copied <- true
                                cells.[ix] <- assigned
                            | None ->
                                failure <-
                                    Some
                                        { Column = col
                                          Target = target
                                          UnresolvedSource = SourceKey.ofString v }
                match failure with
                | None -> kept <- { Cells = cells } :: kept
                | Some uref -> skipped <- uref :: skipped
            { Rows    = List.rev kept
              Skipped = List.rev skipped }

    /// Apply a `SurrogateRemapContext` to one kind's rows — the
    /// context-backed projection of `remapRowFksWith`.
    let remapRowFks
        (fkTargets: Map<Name, SsKey>)
        (remap: SurrogateRemapContext)
        (rows: StaticRow list)
        : RemappedRows =
        remapRowFksWith
            (fun target v ->
                SurrogateRemapContext.tryFindAssigned target (SourceKey.ofString v) remap
                |> Option.map AssignedKey.value)
            fkTargets rows

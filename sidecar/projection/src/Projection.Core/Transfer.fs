namespace Projection.Core

// LINT-ALLOW-FILE: validation-error message construction in
// `SurrogateRemapContext.capture` uses `sprintf` to format the
// offending kind identity + source surrogate into the diagnostic
// message. Same allowed-exception class as `UserRemap.fs` /
// `Catalog.create` (per their LINT-ALLOW-FILE blocks); the validation
// payload is operator-facing audit-trail prose where typed-value
// formatting is the right primitive.

/// Vocabulary of the bidirectional control plane (Transfer prescope —
/// `PRESCOPE_TRANSFER.md`). The pipeline mediates between a logical
/// `Catalog` (the schema contract) and physical database substrates.
/// **Projection** lowers the Catalog (and rows) onto a substrate; its
/// named peer **Ingestion** lifts a substrate back into the Catalog (and
/// rows). A **Transfer** composes an Ingestion from a `Source` substrate
/// with a Projection onto a `Sink` substrate, governed by one shared
/// Catalog contract. No direction is canonical: today's export and a
/// staging→UAT load are both Transfers — only the bound adapters differ.

/// The flow-relative role a database substrate plays in a Transfer. A
/// substrate is never intrinsically a source or a sink; it is bound to a
/// role per flow (staging is a `Sink` during export, a `Source` during a
/// staging→UAT load). Mirrors the canary's `Source_`/`Target_` ephemeral
/// databases — role, not identity.
[<RequireQualifiedAccess>]
type SubstrateRole =
    | Source
    | Sink

/// A surrogate primary-key value as it exists in the `Source` substrate's
/// identity space, carried in the `RawValueCodec` raw-string form (the
/// same form `StaticRow.Values` uses). The orientation marker prevents
/// passing a `SourceKey` where an `AssignedKey` is expected — the
/// surrogate-remap analog of `SourceUserId` / `TargetUserId`.
type SourceKey = SourceKey of string

/// A surrogate primary-key value as assigned by the `Sink` substrate when
/// the row was inserted (e.g. an OutSystems autonumber `Id`). Sibling to
/// `SourceKey`; captured during phase-1 insert and consulted during the
/// phase-2 FK re-point.
type AssignedKey = AssignedKey of string

[<RequireQualifiedAccess>]
module SourceKey =
    let ofString (raw: string) : SourceKey = SourceKey raw
    let value (SourceKey v) : string = v

[<RequireQualifiedAccess>]
module AssignedKey =
    let ofString (raw: string) : AssignedKey = AssignedKey raw
    let value (AssignedKey v) : string = v

/// How a kind's surrogate primary key is established in the `Sink`
/// substrate during a Transfer. Derived from the kind's structure, not
/// from operator opinion (`DataIntent`).
///
///   - `AssignedBySink` — the sink assigns the surrogate (the kind's PK
///     is an IDENTITY/autonumber column). Under DML-only sink rights the
///     source key cannot be force-inserted, so it must be captured and
///     remapped, and every FK pointing at this kind re-pointed in phase 2.
///   - `PreservedFromSource` — the source key is written directly (a
///     business / non-identity PK). No remap; the original FK value stays
///     correct because the referenced key is preserved.
///   - `ReconciledByRule` — the referenced rows already exist in the Sink
///     (dominantly Users). Match the Source surrogate to the *pre-existing*
///     Sink surrogate by an operator ruleset *before* insert, then re-point
///     FKs. Neither preservation (the source key is wrong in the Sink) nor
///     sink-assignment (the Sink mints no new key). Operator-chosen — NOT
///     derivable from `IsIdentity`; `ofKind` never returns it.
[<RequireQualifiedAccess>]
type IdentityDisposition =
    | AssignedBySink
    | PreservedFromSource
    | ReconciledByRule

[<RequireQualifiedAccess>]
module IdentityDisposition =

    /// Classify a kind from its primary-key structure. A kind whose
    /// primary key carries the IDENTITY property is `AssignedBySink`
    /// (the sink mints the surrogate); otherwise the source key is
    /// `PreservedFromSource`. Pure over `Catalog` evidence — the
    /// `IsPrimaryKey` / `IsIdentity` flags already ride on every
    /// `Attribute`.
    let ofKind (kind: Kind) : IdentityDisposition =
        let pkIsIdentity =
            kind.Attributes
            |> List.exists (fun a -> a.IsPrimaryKey && a.IsIdentity)
        if pkIsIdentity then IdentityDisposition.AssignedBySink
        else IdentityDisposition.PreservedFromSource


/// Surrogate-remap evidence for a Transfer — the per-kind generalization
/// of `UserRemapContext`. Where `UserRemapContext` maps one kind's User
/// FK across environments by pre-known matching, this captures, per kind,
/// the `Source` surrogate → `Sink`-assigned surrogate mapping *as rows are
/// inserted* (phase 1), so phase-2 FK re-pointing can resolve every FK
/// value against the keys the sink actually assigned.
///
/// **Invariant** (smart-constructor `capture`): within a kind, a
/// `SourceKey` maps to at most one `AssignedKey`. A second `capture` of
/// the same source surrogate is a phase-1 double-insert and is rejected.
type SurrogateRemapContext =
    {
        /// Per-kind `Source`→`Sink` surrogate mapping. Outer key: the
        /// kind's `SsKey`. Inner: source surrogate → sink-assigned
        /// surrogate. A kind is absent until its first `capture`.
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

    /// The empty context — no surrogates captured yet. The neutral input
    /// at the start of a Transfer's phase-1 insert.
    let empty : SurrogateRemapContext =
        { Assignments = Map.empty }

    /// Record one sink-assigned surrogate for a source surrogate of a
    /// kind. Rejects a second capture of the same `SourceKey` for the
    /// kind (the phase-1 double-insert invariant). Construction-time
    /// validation per the structural-commitment-via-construction-
    /// validation operational principle (`AXIOMS.md`).
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

    /// Resolve the sink-assigned surrogate for a source surrogate of a
    /// kind. `None` when the kind has no captures or the source
    /// surrogate was never inserted (the phase-2 orphan-FK signal — the
    /// referenced row was not part of the Transfer).
    let tryFindAssigned
        (kind: SsKey)
        (source: SourceKey)
        (ctx: SurrogateRemapContext)
        : AssignedKey option =
        ctx.Assignments
        |> Map.tryFind kind
        |> Option.bind (Map.tryFind source)

    /// Total number of captured surrogate assignments across all kinds.
    let assignmentCount (ctx: SurrogateRemapContext) : int =
        ctx.Assignments
        |> Map.toSeq
        |> Seq.sumBy (fun (_, inner) -> Map.count inner)

    /// Number of distinct kinds that have at least one captured
    /// assignment.
    let kindCount (ctx: SurrogateRemapContext) : int =
        Map.count ctx.Assignments


// ---------------------------------------------------------------------------
// The connection apparatus (§4.1) — a multi-environment, role-bound,
// concurrency-aware connection set with credentials resolved out-of-band
// (D9: a `ConnectionRef` names where the secret lives, never the secret).
// ---------------------------------------------------------------------------

/// A logical environment identity. The multi-environment dimension the V1
/// corporate remote carries (the four named environments) plus an open
/// `Named` escape hatch.
[<RequireQualifiedAccess>]
type Environment =
    | Dev
    | Test
    | Uat
    | Prod
    | Named of string

[<RequireQualifiedAccess>]
module Environment =
    let name (e: Environment) : string =
        match e with
        | Environment.Dev     -> "DEV"
        | Environment.Test    -> "TEST"
        | Environment.Uat     -> "UAT"
        | Environment.Prod    -> "PROD"
        | Environment.Named n -> n

/// A *reference* to where a substrate's credentials live — never the
/// secret (D9). Either an environment-variable name or a file path the
/// operator supplies out of band.
[<RequireQualifiedAccess>]
type ConnectionRef =
    | EnvVar of name: string
    | File of path: string

/// An `Environment` bound to the `SubstrateRole` it plays in a Transfer,
/// with its out-of-band `ConnectionRef`. The thing you open.
type Substrate =
    {
        Environment   : Environment
        Role          : SubstrateRole
        ConnectionRef : ConnectionRef
    }

/// The connection set a Transfer binds: which substrate is the data
/// `Source`, which is the write `Sink`, and which substrates are profiled
/// for identity reference. For a `ReconciledByRule` Transfer the Sink is
/// profiled too — "the Sink is not write-only" — which is why a reconcile
/// needs ≥2 concurrent connections.
type TransferConnections =
    {
        Source              : Substrate
        Sink                : Substrate
        ProfiledForIdentity : Substrate list
    }

[<RequireQualifiedAccess>]
module TransferConnections =

    let private roleMismatch (which: string) (expected: SubstrateRole) (got: SubstrateRole) : ValidationError =
        ValidationError.create
            "transfer.connections.roleMismatch"
            (sprintf "%s substrate must carry SubstrateRole.%A, got %A." which expected got)

    /// Bind a Source + Sink into a `TransferConnections`, validating that
    /// each substrate carries the matching role. The Source is always
    /// profiled for identity; the Sink is added to the profiled set only
    /// when the Transfer carries a `ReconciledByRule` kind (`reconcile`).
    let create (source: Substrate) (sink: Substrate) (reconcile: bool) : Result<TransferConnections> =
        let errs =
            [ if source.Role <> SubstrateRole.Source then roleMismatch "Source" SubstrateRole.Source source.Role
              if sink.Role <> SubstrateRole.Sink then roleMismatch "Sink" SubstrateRole.Sink sink.Role ]
        match errs with
        | [] ->
            let profiled = if reconcile then [ source; sink ] else [ source ]
            Result.success { Source = source; Sink = sink; ProfiledForIdentity = profiled }
        | es -> Result.failure es

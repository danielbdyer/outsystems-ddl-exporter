namespace Projection.Core

// LINT-ALLOW-FILE: boundary-validation diagnostic prose. The substrate-role guard renders an
//   operator-facing `must carry SubstrateRole.X` message via `sprintf`; the
//   structural Transfer vocabulary is fully typed. Per
//   `DECISIONS 2026-05-09 — Built-in obligation`.

/// Vocabulary of the bidirectional control plane (Transfer prescope —
/// `PRESCOPE_TRANSFER.md`). The pipeline mediates between a logical
/// `Catalog` (the schema contract) and physical database substrates.
/// **Projection** lowers the Catalog (and rows) onto a substrate; its
/// named peer **Ingestion** lifts a substrate back into the Catalog
/// (and rows). A **Transfer** composes an Ingestion from a `Source`
/// substrate with a Projection onto a `Sink` substrate, governed by
/// one shared Catalog contract. No direction is canonical: today's
/// export and a staging→UAT load are both Transfers — only the bound
/// adapters differ.
///
/// This file carries the Transfer-specific **apparatus**: substrate
/// roles, the environment + connection-reference vocabulary, and the
/// `TransferConnections` set. The generic identity-domain primitives
/// (`SurrogateRemapContext`, `IdentityDisposition`, `SourceKey` /
/// `AssignedKey`) live in `SurrogateRemap.fs` so emitters can consume
/// the remap evidence without taking a conceptual dependency on the
/// Transfer flow.

/// The flow-relative role a database substrate plays in a Transfer. A
/// substrate is never intrinsically a source or a sink; it is bound to
/// a role per flow (staging is a `Sink` during export, a `Source`
/// during a staging→UAT load). Mirrors the canary's `Source_`/`Target_`
/// ephemeral databases — role, not identity.
[<RequireQualifiedAccess>]
type SubstrateRole =
    | Source
    | Sink


// ---------------------------------------------------------------------------
// The connection apparatus (§4.1) — a multi-environment, role-bound,
// concurrency-aware connection set with credentials resolved out-of-band
// (D9: a `ConnectionRef` names where the secret lives, never the secret).
// ---------------------------------------------------------------------------

/// A logical environment identity. The multi-environment dimension the V1
/// managed-environment remote carries. The **active cutover rotation is Dev → Qa → Uat**
/// (the operator's three live stages); `Prod` is the eventual target, carried
/// for forward-readiness but **not yet in rotation**. `Named` is the open
/// escape hatch for a one-off stage.
[<RequireQualifiedAccess>]
type Environment =
    | Dev
    | Qa
    | Uat
    | Prod
    | Named of string

[<RequireQualifiedAccess>]
module Environment =
    let name (e: Environment) : string =
        match e with
        | Environment.Dev     -> "DEV"
        | Environment.Qa      -> "QA"
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

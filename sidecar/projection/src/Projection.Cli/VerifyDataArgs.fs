module Projection.Cli.VerifyDataArgs

open Argu

/// Argu closed-DU surface for `projection verify-data` (slice 4.4 — the
/// post-deploy data-integrity gate). Compares two deployments of the same
/// schema contract on exact per-table row counts + per-column null counts —
/// the data-fidelity complement to the canary's structural equivalence.
/// D9 out-of-band credentials: each connection points at *where* its secret
/// lives (env var or file), never the secret value. Read-only: verify-data
/// never writes, so there is no execute gate. The schema contract is derived
/// from the `--before-conn` deployment via `ReadSide.read`.
type VerifyDataArg =
    | [<Mandatory>] Before_Conn of spec: string
    | [<Mandatory>] After_Conn of spec: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Before_Conn _ ->
                "Baseline deployment connection — env:<VAR_NAME> | file:<PATH>. "
                + "The schema contract is read from this deployment; both sides are "
                + "profiled against it. D9: this flag points at where the secret lives."
            | After_Conn _ ->
                "Candidate deployment connection — env:<VAR_NAME> | file:<PATH>. "
                + "Row-count + null-count divergences from the baseline are reported. "
                + "D9: this flag points at where the secret lives."

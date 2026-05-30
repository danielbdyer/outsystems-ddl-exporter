module Projection.Cli.TransferArgs

open Argu

/// Argu closed-DU surface for `projection transfer` (Phase 11 Slice D —
/// the bidirectional data-load CLI verb). D9 out-of-band credentials:
/// each connection is specified by *where* the secret lives (env var or
/// file), never by the secret value itself. Default mode is the safe
/// `DryRun` preview; `--execute` flips to writing, gated behind the
/// `PROJECTION_ALLOW_EXECUTE=1` environment variable (R6 — V2 owns no
/// production write path until the gate is lowered). The spec-parsing
/// + catalog-resolution logic lives in `Projection.Pipeline.TransferSpec`
/// so the test pool reaches it without depending on the CLI executable.
type TransferArg =
    | [<Mandatory>] Source_Conn of spec: string
    | [<Mandatory>] Sink_Conn of spec: string
    | Reconcile of spec: string
    | Execute
    | Allow_Cdc

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Source_Conn _ ->
                "Source substrate connection — env:<VAR_NAME> | file:<PATH>. "
                + "D9: the secret lives out of band; this flag points at where it lives."
            | Sink_Conn _ ->
                "Sink substrate connection — env:<VAR_NAME> | file:<PATH>. "
                + "D9: the secret lives out of band; this flag points at where it lives."
            | Reconcile _ ->
                "Reconcile a kind to a pre-existing Sink identity by matching a "
                + "column (ReconciledByRule). Format: <table>:<match-column>. "
                + "Repeatable — one entry per reconciled kind. Source rows whose "
                + "match-column has no Sink counterpart are dropped and diagnosed."
            | Execute ->
                "Actually write to the Sink. Default is a DryRun preview. "
                + "Requires PROJECTION_ALLOW_EXECUTE=1 in the environment "
                + "(the R6 gate); --execute without it is refused."
            | Allow_Cdc ->
                "Permit --execute against a Sink that has CDC-tracked tables. Default: an Execute run pre-flights the Sink for sys.tables.is_tracked_by_cdc and REFUSES if any are tracked (writing would generate unintended CDC capture during a UAT-preview). Pass this flag to override that refusal."

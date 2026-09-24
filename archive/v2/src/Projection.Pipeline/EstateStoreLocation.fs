namespace Projection.Pipeline

open System

/// The ONE resolution rule for the durable estate-store root — extracted from
/// `EstateEvidenceStore` (which still re-exports it) when the bridge-staging
/// cache became a second consumer that compiles EARLIER in the project order.
/// The rule is unchanged (the `Run.storeDir` R1d precedent):
/// `PROJECTION_ESTATE_DIR` when set; else the ledger dir's `estate/` child;
/// else the store is DISABLED — the run is live-only, and every consumer says
/// so by name (never a silent degradation).
[<RequireQualifiedAccess>]
module EstateStoreLocation =

    /// The pure resolution over the two variables' values (testable without
    /// process-global environment mutation).
    let storeDirFrom (estateDir: string option) (ledgerDir: string option) : string option =
        match estateDir with
        | Some d when not (String.IsNullOrWhiteSpace d) -> Some d
        | _ ->
            match ledgerDir with
            | Some l when not (String.IsNullOrWhiteSpace l) -> Some (System.IO.Path.Combine(l, "estate"))
            | _ -> None

    /// The boundary read: resolve from the process environment.
    let storeDir () : string option =
        storeDirFrom
            (Option.ofObj (Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"))
            (Option.ofObj (Environment.GetEnvironmentVariable "PROJECTION_LEDGER_DIR"))

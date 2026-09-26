namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.OssysSql

/// The ONE sanctioned scoped OSSYS-catalog read for a cross-environment
/// comparison (`check environments` / `check estate` / `check shape`). It
/// composes the two module-selection seams the full-export and peer-transfer
/// paths already share — nothing new, just NAMED once so every comparison
/// face routes through the same door:
///
///   1. **Query-time pushdown** — `SnapshotScopeBinding.fromModel` narrows the
///      OSSYS rowsets server-side (only the `model.modules` estate crosses the
///      wire), exactly as `Compose.readConfigModel` / `PeerTransfer.
///      acquireContractsWith` do.
///   2. **Semantic backstop** — `ModuleFilter.apply` (via
///      `ModuleFilterBinding.fromConfig`) narrows the read catalog in memory.
///      Double enforcement is V1's own precedent (the pushdown ≡ filter
///      equivalence law).
///
/// The opt-in gate is byte-identical to those paths: an empty `model.modules`
/// makes BOTH seams the show-me-everything identity, so an unconfigured scope
/// reads the whole estate exactly as before.
///
/// Governance (THE_CONFIG_CONTROL_PLANE §6, S3): the comparison faces MUST NOT
/// call `Source.read (Source.ofOssys …)` directly — that reads the WHOLE OSSYS
/// estate (clone / deleted / test / system eSpaces included), which is not the
/// cutover surface the operator declared. This module is the only sanctioned
/// path; a source-scan test enforces it (`EstateScopeGovernanceTests`).
[<RequireQualifiedAccess>]
module ScopedRead =

    /// The OSSYS `Source` scoped by the model's query-time pushdown. Retained
    /// as a `Source` (not just a catalog) so the caller keeps the read + the
    /// data-plane `profile` capability under ONE scope — the estate profiles
    /// each environment's live data within the same declared module surface.
    let scopedOssysSource (model: Config.ModelSection) (conn: string) : Source.Source =
        Source.ofOssysWith (SnapshotScopeBinding.fromModel model) conn

    /// Apply the in-memory `ModuleFilter` backstop to an already-read catalog
    /// under the `model` scope. The sibling of `Compose.applyModuleFilter` that
    /// takes the `ModelSection` directly (so a catalog resolved OUT of the OSSYS
    /// read path — e.g. an authored `--against model` — is scoped the same way).
    /// An empty `model.modules` is the identity; a bad module name is the
    /// structured `moduleFilter.*` refusal (fail-loud, never a silent empty).
    let applyScope (model: Config.ModelSection) (catalog: Catalog) : Result<Catalog> =
        ModuleFilterBinding.fromConfig model
        |> Result.bind (fun opts -> ModuleFilter.apply opts catalog)

    /// Read one environment's OSSYS catalog scoped to `model.modules`: the
    /// pushdown-scoped read, then the `ModuleFilter` backstop. The single
    /// sanctioned scoped catalog read for a cross-environment comparison.
    let readOssysScoped (model: Config.ModelSection) (conn: string) : Task<Result<Catalog>> =
        task {
            match! Source.read (scopedOssysSource model conn) with
            | Error es -> return Result.failure es
            | Ok catalog -> return applyScope model catalog
        }

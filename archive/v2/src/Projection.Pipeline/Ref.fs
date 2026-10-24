namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core

/// The keystone connector — the **revision algebra**. A `Ref` is a typed
/// reference that **resolves** to an operand, dispatching through the right
/// port: `Source` for external refs (file / json / live), the `Run`-store for
/// a `@runId`. This is the system's git-revision: with it, every verb becomes
/// `verb <ref>…` and they compose (`diff model.json @run-9`,
/// `migrate @run-9 live://uat`) — because the operands are resolved uniformly.
///
/// It SUPPORTS diff / migrate / explain-by-runId without completing any: it is
/// the polymorphic *input* those verbs share, built atop the two preconditions
/// (`Source`, the addressable `Run`).
module Ref =

    type Ref =
        | File of path: string
        | Json of json: string
        | RunArtifact of runId: string
        | Live of conn: string
        | Ossys of conn: string

    /// Parse a reference string — the revision syntax (cf. a git revision:
    /// `HEAD` / `<sha>` / `<path>`). `@<id>` is a stored run; `live:<conn>` a
    /// live connection; `json:<…>` an inline model; anything else is a file.
    let parse (s: string) : Ref =
        if s.StartsWith("@") then RunArtifact(s.Substring(1))
        elif s.StartsWith("live:") then Live(s.Substring(5))
        elif s.StartsWith("ossys:") then Ossys(s.Substring(6))
        elif s.StartsWith("json:") then Json(s.Substring(5))
        else File s

    /// Human-readable identity of a ref (for diff/explain headers, logs).
    let identity (r: Ref) : string =
        match r with
        | File p -> "file:" + p  // LINT-ALLOW: terminal Ref-identity tag (file:/@/live:/ossys: prefix); the value IS a string identity, no use-case-specific AST applies
        | Json _ -> "json:inline"
        | RunArtifact id -> "@" + id  // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary
        | Live c -> "live:" + c  // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary
        | Ossys c -> "ossys:" + c  // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary

    /// Both operands are OSSYS-sourced (`ossys:`) ⇒ a cross-environment compare
    /// is espace-SAFE by identity (native GUID), and the caller should normalize
    /// to the logical shape (`Readiness.toLogicalShape`) to drop the
    /// realization-name artifacts `CatalogDiff` compares (CROSS_ENVIRONMENT_READINESS.md).
    let bothOssys (a: Ref) (b: Ref) : bool =
        match a, b with Ossys _, Ossys _ -> true | _ -> false

    /// Both operands are physical `live:` reads ⇒ a cross-environment compare is
    /// espace-UNSAFE: `ReadSide` synthesizes SsKeys from the physical name, so the
    /// same entity in two OutSystems environments will not align (the `compare`/
    /// `diff` run faces surface this as a named advisory, never a silent result).
    let bothLive (a: Ref) (b: Ref) : bool =
        match a, b with Live _, Live _ -> true | _ -> false

    let private fail (code: string) (msg: string) : Result<'a> =
        Result.failure [ ValidationError.create code msg ]

    /// Resolve a reference to its `Catalog` operand. External refs flow through
    /// `Source`; a `@runId` loads the stored `Run` and re-reads its captured
    /// `model.json` artifact (the Run's tree), so a runId resolves to the same
    /// Catalog type as a file — that uniformity is the point.
    let resolveCatalog (r: Ref) : Task<Result<Catalog>> =
        task {
            match r with
            | File path -> return! Source.read (Source.ofFile path)
            | Json json -> return! Source.read (Source.ofJson json)
            | RunArtifact runId ->
                match Run.configuredDir () with
                | None -> return fail "ref.noRunsDir" "set PROJECTION_RUNS_DIR to resolve @runId references"
                | Some dir ->
                    match Run.load dir runId with
                    | None -> return fail "ref.runNotFound" (sprintf "run %s not found in the store" runId)
                    | Some run ->
                        match Map.tryFind "model.json" run.Artifacts with
                        | Some modelJson -> return! Source.read (Source.ofJson modelJson)
                        | None -> return fail "ref.noModelArtifact" (sprintf "run %s captured no model.json artifact" runId)
            | Live conn ->
                // The live adapter (`Source.ofLive`) reads the deployed catalog
                // back via ReadSide over the connection; `env:VAR` resolves the
                // connection string from the environment.
                return! Source.read (Source.ofLive conn)
            | Ossys conn ->
                // The OSSYS model-read adapter (`Source.ofOssys`) reads the model
                // from the OutSystems metamodel — native GUID (`OssysOriginal`)
                // SsKey at kind AND attribute grain, the espace-safe identity for
                // cross-environment readiness (CROSS_ENVIRONMENT_READINESS.md).
                return! Source.read (Source.ofOssys conn)
        }

    /// Resolve a reference to its capability-typed `Source` — the catalog read
    /// PLUS, for a live env, the profile-acquisition verb (`AcquireProfile`).
    /// `resolveCatalog` reads only the catalog; consumers that also need the
    /// data evidence (e.g. `compare`'s dealbreaker section) resolve the Source
    /// and call `Source.profile`. A `@runId` / file / json source carries no
    /// profile (a static model has no observed data) — `AcquireProfile = None`,
    /// so the dealbreaker section stays honestly advisory-silent for them.
    let resolveSource (r: Ref) : Task<Result<Source.Source>> =
        task {
            match r with
            | File path -> return Result.success (Source.ofFile path)
            | Json json -> return Result.success (Source.ofJson json)
            | Live conn -> return Result.success (Source.ofLive conn)
            | Ossys conn -> return Result.success (Source.ofOssys conn)
            | RunArtifact runId ->
                match Run.configuredDir () with
                | None -> return fail "ref.noRunsDir" "set PROJECTION_RUNS_DIR to resolve @runId references"
                | Some dir ->
                    match Run.load dir runId with
                    | None -> return fail "ref.runNotFound" (sprintf "run %s not found in the store" runId)
                    | Some run ->
                        match Map.tryFind "model.json" run.Artifacts with
                        | Some modelJson -> return Result.success (Source.ofJson modelJson)
                        | None -> return fail "ref.noModelArtifact" (sprintf "run %s captured no model.json artifact" runId)
        }

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
        /// A witnessed sink state (the data-sink chapter, S7):
        /// `sink:<env>[@<syncId>]` — the environment name `projection sync`
        /// stamped, optionally pinned to a sync ordinal (latest otherwise).
        | Sink of env: string * syncId: int option

    /// Parse a reference string — the revision syntax (cf. a git revision:
    /// `HEAD` / `<sha>` / `<path>`). `@<id>` is a stored run; `live:<conn>` a
    /// live connection; `json:<…>` an inline model; `sink:<env>[@<syncId>]` a
    /// witnessed sink state; anything else is a file.
    let parse (s: string) : Ref =
        if s.StartsWith("@") then RunArtifact(s.Substring(1))
        elif s.StartsWith("live:") then Live(s.Substring(5))
        elif s.StartsWith("ossys:") then Ossys(s.Substring(6))
        elif s.StartsWith("json:") then Json(s.Substring(5))
        elif s.StartsWith("sink:") then
            // `sink:<env>@<n>` pins an edition; a non-numeric tail stays part
            // of the LABEL (labels are opaque operator strings), so a
            // malformed pin fails downstream as the named `sink.envUnknown`
            // naming the whole text — total parse, never a silent misroute.
            let body = s.Substring(5)
            match body.LastIndexOf '@' with
            | -1 -> Sink(body, None)
            | i ->
                match System.Int32.TryParse(body.Substring(i + 1)) with
                | true, n -> Sink(body.Substring(0, i), Some n)
                | false, _ -> Sink(body, None)
        else File s

    /// Human-readable identity of a ref (for diff/explain headers, logs).
    let identity (r: Ref) : string =
        match r with
        | File p -> "file:" + p  // LINT-ALLOW: terminal Ref-identity tag (file:/@/live:/ossys: prefix); the value IS a string identity, no use-case-specific AST applies
        | Json _ -> "json:inline"
        | RunArtifact id -> "@" + id  // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary
        | Live c -> "live:" + c  // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary
        | Ossys c -> "ossys:" + c  // LINT-ALLOW: terminal Ref-identity tag; string identity at the boundary
        | Sink (env, syncId) -> SinkRead.identityOf env syncId

    /// The operand carries NATIVE (OssysOriginal GUID) identity, stable across
    /// OutSystems environments — so a compare keyed on it is espace-SAFE
    /// (CROSS_ENVIRONMENT_READINESS.md). True for an `ossys:` live model read
    /// and for a `sink:` witnessed state (the sink persists the same rowsets
    /// the ossys read parses — identical identity by construction, K2). A
    /// file/json model or a `@runId` is not GUARANTEED native (authored models
    /// vary), and a `live:` physical read synthesizes SsKeys — both stay out.
    let espaceSafe (r: Ref) : bool =
        match r with
        | Ossys _ | Sink _ -> true
        | File _ | Json _ | RunArtifact _ | Live _ -> false

    /// Both operands carry espace-safe native identity ⇒ a cross-environment
    /// compare is meaningful, and the caller should normalize to the logical
    /// shape (`Readiness.toLogicalShape`) to drop the realization-name
    /// artifacts `CatalogDiff` compares. Generalizes the former `bothOssys`
    /// (S7: a sink ref is espace-safe too, so `diff ossys:A sink:B` and
    /// `diff sink:e@1 sink:e@2` normalize the same way).
    let bothEspaceSafe (a: Ref) (b: Ref) : bool =
        espaceSafe a && espaceSafe b

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
            | Sink (env, syncId) ->
                // The witnessed-state adapter (`Source.ofSink`) resolves the
                // environment name against the sink store's manifests and
                // replays snapshot → bundle → parse — offline-true, native
                // GUID identity (the data-sink chapter, S7).
                return! Source.read (Source.ofSink env syncId)
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
            | Sink (env, syncId) -> return Result.success (Source.ofSink env syncId)
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

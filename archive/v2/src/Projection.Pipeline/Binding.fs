namespace Projection.Pipeline

open Projection.Core

/// The closed vocabulary of config-binder axes — the `pipeline.<axis>.<code>`
/// error namespace made TOTAL and greppable (recon Finding #2, the XL realization).
/// Replaces the 7 free-string axis literals that the binders' `bindError` sites
/// each re-typed: a typo, or a new binder that forgets its namespace, is now a
/// compile error (the `prefix` match is exhaustive) rather than a silent divergent
/// refusal code.
///
/// **Why this and not a "binder registry to fold over" (the recon's other XL
/// sketch).** The binders are HETEROGENEOUS — each yields a distinct Core type
/// (`EmissionFolders` / `TighteningPolicy` / `TransformGroups` / `InsertionPolicy`
/// / …) — and the orchestrator already combines them with an applicative
/// `validation { let! … and! … }` whose single funnel (`Pipeline.bindShapingTriple`)
/// is convergence-by-construction per its own docstring. A homogeneous registry to
/// fold would force boxing/existentials across those distinct output types — a
/// type-safety regression. So the axis vocabulary is the part of the XL that is a
/// real improvement; the fold is not.
[<RequireQualifiedAccess>]
type ConfigAxis =
    | EmissionFolders
    | InsertionPolicy
    | MigrationDependencies
    | SpecialCircumstances
    | Tightening
    | TransformGroups
    | RenameBinding

[<RequireQualifiedAccess>]
module ConfigAxis =

    /// The `pipeline.<prefix>.` namespace segment for an axis. Exhaustive — a new
    /// `ConfigAxis` case lights up here as a compile error until it is named, so
    /// the `pipeline.*` refusal-code vocabulary stays total and greppable.
    let prefix (axis: ConfigAxis) : string =
        match axis with
        | ConfigAxis.EmissionFolders       -> "emissionFolders"
        | ConfigAxis.InsertionPolicy       -> "insertionPolicy"
        | ConfigAxis.MigrationDependencies -> "migrationDependencies"
        | ConfigAxis.SpecialCircumstances  -> "specialCircumstances"
        | ConfigAxis.Tightening            -> "tightening"
        | ConfigAxis.TransformGroups       -> "transformGroups"
        | ConfigAxis.RenameBinding         -> "renameBinding"

/// The shared config-binder kernel (recon Finding #2). Every `*Binding` module is
/// the same morphism — Config-shape (raw strings) → `Result<Core-shape (value
/// objects)>`, aggregating *named* refusals — and three pieces of that morphism had
/// been copy-pasted across them:
///
///   - the `pipeline.<axis>.<code>` error constructor (`bindError` / `bindingError`
///     × 7),
///   - the logical `(module, entity)` → kind `SsKey` resolution-or-refusal (× 4,
///     `tryKindByLogical |> Some key | None -> failureOf <error>`, differing only
///     in the refusal),
///   - the closed-DU-name parser ("match the case names verbatim; else a
///     `Known: a | b` refusal") (× 2).
///
/// They live here once. Each binder keeps its public `fromConfig` shape and its
/// own axis + messages, so the operator-facing codes and copy stay byte-identical
/// (the `*BindingTests` / `ConfigTests` assert them unchanged).
///
/// Lives after `CatalogResolution` (the pure catalog-coordinate lookup) and before
/// the binders in the compile order.
[<RequireQualifiedAccess>]
module Binding =

    /// The namespaced binder-error constructor — `pipeline.<axis>.<code>` over the
    /// closed `ConfigAxis`. Replaces the per-binder `bindError` / `bindingError`
    /// copies; each binder binds it once as
    /// `let private bindError = Binding.error ConfigAxis.<Axis>` and its call sites
    /// are unchanged.
    let error (axis: ConfigAxis) (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.%s.%s" (ConfigAxis.prefix axis) code) message

    /// Resolve a logical `(module, entity)` pair to its kind's `SsKey`, or the
    /// caller's named refusal. Replaces the copies of the resolution-or-refusal
    /// skeleton that differ only in the error. The caller supplies the
    /// already-built `unresolved` error so the message stays axis-specific (the
    /// success path is cheap; the error is only surfaced on a miss).
    let requireKindByLogical
        (catalog: Catalog)
        (moduleName: string)
        (entityName: string)
        (unresolved: ValidationError)
        : Result<SsKey> =
        match CatalogResolution.tryKindByLogical catalog moduleName entityName with
        | Some key -> Result.success key
        | None     -> Result.failureOf unresolved

    /// Parse a textual value against a closed set of `(case-name, value)` pairs
    /// (the closed-DU's case names verbatim), or a named refusal listing the known
    /// names. Replaces `InsertionPolicyBinding.fromString` /
    /// `TransformGroupsBinding.parseGroupName`. `fieldDesc` is the operator-facing
    /// config-path phrase (e.g. `"policy.insertion value"`); `typeName` is the DU's
    /// name (e.g. `"InsertionPolicy"`). The message is
    /// `"<fieldDesc> '<raw>' is not a recognized <typeName>. Known: a | b | c."`.
    let ofClosedName
        (axis: ConfigAxis)
        (code: string)
        (fieldDesc: string)
        (typeName: string)
        (known: (string * 'a) list)
        (raw: string)
        : Result<'a> =
        match known |> List.tryFind (fun (name, _) -> name = raw) with
        | Some (_, value) -> Result.success value
        | None ->
            let knownNames = known |> List.map fst |> String.concat " | "  // LINT-ALLOW: terminal operator-diagnostic listing the known config-axis names at the Binding error boundary; String.concat is the BCL primitive, no typed-AST applies to a free-text refusal hint
            Result.failureOf (
                error axis code
                    (sprintf "%s '%s' is not a recognized %s. Known: %s." fieldDesc raw typeName knownNames))

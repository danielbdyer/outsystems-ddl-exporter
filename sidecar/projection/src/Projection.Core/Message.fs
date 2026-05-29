namespace Projection.Core

/// Operator-facing diagnostic prose templates. Each combinator names
/// the *shape* of a recurring message — the constant framing the
/// codebase repeats across many sites, with per-variant detail flowing
/// in as a typed parameter. The named combinator centralizes the
/// wording so operators see consistent prose and changes to the
/// framing land at one site.
///
/// Per pillar 8 (domain-first naming): each combinator names the
/// algebraic message shape (`foreignKeyNotCreated` names a constraint-
/// decision-with-reason; `unmatchedSourceUser` names a per-identity
/// match failure). The detail string carries the variant-specific
/// rationale; the combinator carries the prefix/framing.
///
/// Per the LINT-ALLOW substantive-rationale discipline: the
/// `sprintf` / string-concatenation primitives at the *terminal*
/// boundary inside these combinators are the typed-message-building
/// equivalent of `SqlLiteral.toString` / `ScriptDomGenerate.toText`
/// — the named site for terminal text composition, not a tactical
/// shortcut.
[<RequireQualifiedAccess>]
module Message =

    /// "Foreign-key constraint was not created. <detail>" — the
    /// recurring framing for the six `ForeignKeyKeepReason` variants
    /// (PolicyDisabled / DataHasOrphans / CrossSchemaBlocked /
    /// CrossCatalogBlocked / DeleteRuleIgnored / EvidenceMissing).
    /// Each variant's per-reason detail flows in as the suffix; the
    /// constant prefix lives here so the wording is consistent across
    /// the six call sites in `ForeignKeyPass.opportunityEntry`.
    let foreignKeyNotCreated (detail: string) : string =
        // LINT-ALLOW: terminal prose-construction boundary; this
        // combinator IS the named site for the FK-decision message
        // shape (mirror of `SqlLiteral.toString` for the terminal-
        // text boundary discipline). The detail flows in as a typed
        // parameter; the prefix is the algebraic invariant.
        "Foreign-key constraint was not created. " + detail

    /// "source user <id>: <detail>" — the recurring framing for the
    /// five `RemapDiagnostic` variants in `UserFkReflowPass.unmatched
    /// Entry`. Each variant supplies its own detail describing why the
    /// source user could not match a target identity; this combinator
    /// owns the constant prefix that names the identity.
    let unmatchedSourceUser (sourceUserValue: int) (detail: string) : string =
        // LINT-ALLOW: terminal prose-construction boundary; the
        // numeric source-user identity is interpolated via `sprintf
        // "%d"`, the same integer-to-prose primitive `Render.format
        // SqlLiteral` uses at its typed-value-to-raw boundary. The
        // detail flows in as the per-variant suffix.
        sprintf "source user %d: %s" sourceUserValue detail

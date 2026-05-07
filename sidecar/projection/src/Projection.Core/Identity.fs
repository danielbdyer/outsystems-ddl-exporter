namespace Projection.Core

/// Stable identity for catalog nodes (A1, A2). The single-case DU at the
/// type level (A4) makes `SsKey` and `string` non-interchangeable: the
/// compiler rejects accidental string-as-key uses.
///
/// `Original` carries an SS_KEY supplied by the source (Catalog Reader).
/// `Derived` is constructed by passes that must introduce new nodes (e.g.,
/// the symmetric-closure pass adds inverse references). The original/
/// derived distinction is in the type system so later passes can pattern-
/// match on whether a key was synthesized (A5).
///
/// Reserved derivation reasons are enumerated in `DECISIONS.md`. The
/// current set:
///   "inverse" — symmetric-closure pass; adds the inverse of a reference.
type SsKey =
    | Original of string
    | Derived of original: SsKey * reason: string

/// Construction and inspection helpers for `SsKey`.
[<RequireQualifiedAccess>]
module SsKey =

    let private originalEmpty =
        ValidationError.create "sskey.empty" "An SS_KEY cannot be blank."

    let private reasonEmpty =
        ValidationError.create "sskey.derivedReason.empty" "A derivation reason cannot be blank."

    /// Build an `Original` SsKey from a source-supplied identifier. Rejects
    /// blank input with `ValidationError "sskey.empty"`.
    let original (value: string) : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace value then Result.failureOf originalEmpty
        else Result.success (Original value)

    /// Build a `Derived` SsKey from a parent identity and a documented reason.
    /// Rejects blank reasons with `ValidationError "sskey.derivedReason.empty"`.
    let derived (parent: SsKey) (reason: string) : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace reason then Result.failureOf reasonEmpty
        else Result.success (Derived (parent, reason))

    /// Walk back to the originating string of a key. Useful for
    /// diagnostics; never used for structural keying.
    let rec rootOriginal (key: SsKey) : string =
        match key with
        | Original v        -> v
        | Derived (p, _)    -> rootOriginal p

    /// True if the key was introduced by a pass.
    let isDerived (key: SsKey) : bool =
        match key with
        | Derived _ -> true
        | Original _ -> false

    /// Sequence of derivation reasons from the root outward, oldest first.
    /// Empty for `Original` keys.
    let rec derivationReasons (key: SsKey) : string list =
        match key with
        | Original _        -> []
        | Derived (p, r)    -> derivationReasons p @ [r]

namespace Projection.Core

/// Stable identity for catalog nodes (A1, A2, A4). The four-variant DU
/// stratifies the JSON-projection-lossiness bound documented at the
/// bottom of `AXIOMS.md` A1 (session 23 amendment). Per `CHAPTER_3_PRESCOPE_
/// ARTIFACTBYKIND_REFACTOR.md` §3, the variant is not presentation; it
/// is structural, and downstream code pattern-matches on it to decide
/// whether A1 holds unconditionally (`OssysOriginal`, `DerivedFrom`
/// rooted in `OssysOriginal` / `V1Mapped`) or is bounded
/// (`Synthesized`).
///
/// Variants:
///   - `OssysOriginal of System.Guid` — SSKey GUID supplied directly by
///     an OSSYS rowsets adapter (chapter 3.2). A1 holds unconditionally;
///     this value survives source rename.
///   - `Synthesized of source * basisParts` — JSON-path-synthesized
///     identity. `source` names the synthesis convention (e.g.,
///     `"OS_KIND"`); `basisParts` is the **typed segment list** the
///     adapter built from name fields (e.g., `["AppCore"; "User"]`).
///     **Chapter 3.6 slice-δ widened from `basis: string` to
///     `basisParts: string list`** — typed segments survive structurally
///     through the DU; `String.concat "_"` lives only at the terminal
///     `rootOriginal` display projection. A1 remains **bounded** for
///     this variant — a source-side rename produces a different `SsKey`.
///   - `DerivedFrom of parent * reason` — pass-introduced identity (e.g.,
///     the symmetric-closure pass adding inverse references). A1
///     preservation inherits from the root via `rootOriginal`.
///   - `V1Mapped of v1Sskey * v2Namespace` — cross-version threading: a
///     V1 entity's SSKey GUID mapped deterministically into V2's
///     identity space. A1 holds within V2; the namespace tag makes the
///     cross-version origin pattern-matchable.
///
/// **Equality is per-variant; cross-variant equality is always false.**
/// `OssysOriginal g =? V1Mapped (g, _)` is `false` even if the GUIDs
/// numerically collide — variant tag preserves provenance.
/// `Synthesized` equality is structural over `(source, basisParts)`:
/// list element equality + order. `Synthesized ("OS_KIND",
/// ["AppCore_User"])` and `Synthesized ("OS_KIND", ["AppCore"; "User"])`
/// are NOT equal even though they render to the same identifier — the
/// build-path's segmentation choice is part of the structural identity.
///
/// Reserved derivation reasons are enumerated in `DECISIONS.md`:
///   - `"inverse"` — symmetric-closure pass; adds the inverse of a
///     reference.
type SsKey =
    | OssysOriginal of System.Guid
    | Synthesized of source: string * basisParts: string list
    | DerivedFrom of parent: SsKey * reason: string
    | V1Mapped of v1Sskey: System.Guid * v2Namespace: System.Guid

/// Construction and inspection helpers for `SsKey`.
[<RequireQualifiedAccess>]
module SsKey =

    let private synthSourceEmpty =
        ValidationError.create "sskey.synth.source.empty"
            "Synthesis source cannot be blank."

    let private synthBasisEmpty =
        ValidationError.create "sskey.synth.basis.empty"
            "Synthesis basis cannot be blank."

    let private reasonEmpty =
        ValidationError.create "sskey.derivedReason.empty"
            "A derivation reason cannot be blank."

    /// Build an `OssysOriginal` SsKey from a source-supplied GUID. Total:
    /// a `System.Guid` is well-formed by construction. Used by the
    /// `SnapshotRowsets` adapter variant (chapter 3.2).
    let ossysOriginal (g: System.Guid) : SsKey = OssysOriginal g

    /// Build a `Synthesized` SsKey from a synthesis convention and a
    /// single basis string. Wraps the basis as a single-element typed
    /// segment list `[basis]`. Used by adapters whose basis is already
    /// a flat name (`SsKey.synthesized "OS_MOD" moduleName`).
    /// Both `source` and `basis` must be non-blank.
    let synthesized (source: string) (basis: string) : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace source then
            Result.failureOf synthSourceEmpty
        elif System.String.IsNullOrWhiteSpace basis then
            Result.failureOf synthBasisEmpty
        else
            Result.success (Synthesized (source, [basis]))

    /// Build a `Synthesized` SsKey from a synthesis convention and a
    /// typed basis-component list. **Chapter 3.6 slice-δ:** the typed
    /// `string list` flows through the `Synthesized` DU directly; no
    /// `String.concat "_"` at the build path. The list IS the
    /// structure; the separator (`"_"`) is parameterized only at the
    /// terminal `rootOriginal` display projection. Each component must
    /// be non-blank; rejects on first blank.
    let synthesizedComposite
        (source: string)
        (basisParts: string list)
        : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace source then
            Result.failureOf synthSourceEmpty
        elif basisParts |> List.exists System.String.IsNullOrWhiteSpace then
            Result.failureOf synthBasisEmpty
        else
            Result.success (Synthesized (source, basisParts))

    /// Build a `DerivedFrom` SsKey from a parent identity and a
    /// documented reason. Rejects blank reasons with
    /// `ValidationError "sskey.derivedReason.empty"`. Used by passes
    /// that introduce new nodes (e.g., `SymmetricClosure`).
    let derivedFrom (parent: SsKey) (reason: string) : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace reason then
            Result.failureOf reasonEmpty
        else
            Result.success (DerivedFrom (parent, reason))

    /// Build a `V1Mapped` SsKey from a V1 GUID and the V2 namespace tag.
    /// Total: GUID arithmetic is. The `v2Namespace` GUID is a stable
    /// V2-side namespace identifier; the actual UUIDv5 derivation lives
    /// in adapter code (chapter 4.2 User FK reflow), this constructor
    /// just records the result.
    let fromV1 (v1: System.Guid) (v2Namespace: System.Guid) : SsKey =
        V1Mapped (v1, v2Namespace)


    /// Walk back to the originating identifier of a key, surfaced as a
    /// string for diagnostics. Variant-specific surfacing:
    ///   - `OssysOriginal g` → `g.ToString "N"` (32-char hex, no dashes)
    ///   - `Synthesized (source, basisParts)` →
    ///     `<source>_<basisParts joined with _>` (preserves the
    ///     pre-stratification full-identifier form that emitter
    ///     comments and differential tests depend on; consumers needing
    ///     the structural segments should pattern-match on the
    ///     `Synthesized (source, parts)` value directly)
    ///   - `DerivedFrom (parent, _)` → recurse on parent
    ///   - `V1Mapped (v1, _)` → `v1.ToString "N"`
    /// Never used for structural keying — A4 equality is by full DU
    /// value, not by `rootOriginal`.
    let rec rootOriginal (key: SsKey) : string =
        match key with
        | OssysOriginal g -> g.ToString "N"
        | Synthesized (source, basisParts) ->
            // Chapter 3.6 slice-δ: typed `basisParts: string list`
            // flows through the `Synthesized` DU; this is the
            // **single** terminal-text-emission boundary where the
            // identifier string is built. Consumers that need the
            // structural segments pattern-match on `Synthesized
            // (source, parts)` directly. The separator `"_"` is
            // parameterized at this one site; nowhere else.
            String.concat "_" (source :: basisParts)  // LINT-ALLOW: terminal diagnostic projection; typed `Synthesized (s, parts)` available via pattern-match for structural consumers
        | DerivedFrom (parent, _) -> rootOriginal parent
        | V1Mapped (v1, _) -> v1.ToString "N"

    /// True if the key was introduced by a pass (i.e., `DerivedFrom`).
    /// `OssysOriginal`, `Synthesized`, and `V1Mapped` are leaf identities
    /// — not derivations — so this returns false for them.
    let isDerived (key: SsKey) : bool =
        match key with
        | DerivedFrom _ -> true
        | OssysOriginal _
        | Synthesized _
        | V1Mapped _ -> false

    /// Sequence of derivation reasons from the root outward, oldest first.
    /// Empty for leaf identities (`OssysOriginal`, `Synthesized`,
    /// `V1Mapped`).
    let rec derivationReasons (key: SsKey) : string list =
        match key with
        | OssysOriginal _
        | Synthesized _
        | V1Mapped _ -> []
        | DerivedFrom (parent, reason) ->
            derivationReasons parent @ [ reason ]

    /// Canonical display projection for an `SsKey` — the flat string an
    /// emitter writes when it needs to surface identity to the operator.
    /// The root original is the leftmost segment; `[derived]` suffix
    /// surfaces derivation provenance when present. Sibling accessor to
    /// `rootOriginal` / `isDerived` (compose them here so consumers don't
    /// re-derive the format). Three emitters (JSON, Distributions, SSDT-
    /// adjacent) share this projection; the named accessor keeps them
    /// from re-implementing it locally.
    let display (key: SsKey) : string =
        let root = rootOriginal key
        if isDerived key then sprintf "%s [derived]" root else root

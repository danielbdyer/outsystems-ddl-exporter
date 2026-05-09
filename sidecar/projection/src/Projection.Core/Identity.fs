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
///   - `Synthesized of source * basis` — JSON-path-synthesized identity.
///     `source` names the synthesis convention (e.g., `"OS_KIND"`);
///     `basis` is the concatenated name fields the adapter built. A1 is
///     **bounded** for this variant — a source-side rename produces a
///     different `SsKey`.
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
///
/// Reserved derivation reasons are enumerated in `DECISIONS.md`:
///   - `"inverse"` — symmetric-closure pass; adds the inverse of a
///     reference.
type SsKey =
    | OssysOriginal of System.Guid
    | Synthesized of source: string * basis: string
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

    let private legacyValueEmpty =
        ValidationError.create "sskey.empty" "An SS_KEY cannot be blank."

    /// Build an `OssysOriginal` SsKey from a source-supplied GUID. Total:
    /// a `System.Guid` is well-formed by construction. Used by the
    /// `SnapshotRowsets` adapter variant (chapter 3.2).
    let ossysOriginal (g: System.Guid) : SsKey = OssysOriginal g

    /// Build a `Synthesized` SsKey from a synthesis convention and basis.
    /// Both must be non-blank. Used by adapters that synthesize identity
    /// from name fields (e.g., the OSSYS JSON adapter at
    /// `Projection.Adapters.Osm.CatalogReader`).
    let synthesized (source: string) (basis: string) : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace source then
            Result.failureOf synthSourceEmpty
        elif System.String.IsNullOrWhiteSpace basis then
            Result.failureOf synthBasisEmpty
        else
            Result.success (Synthesized (source, basis))

    /// Build a `Synthesized` SsKey from a synthesis convention and a
    /// typed basis-component list. Joins via `String.concat "_"` —
    /// the canonical separator the OSSYS adapter conventions use
    /// (e.g., `"OS_KIND" + ["AppCore"; "User"]` → `Synthesized
    /// ("OS_KIND", "AppCore_User")`). Per the no-string-concatenation
    /// discipline (`DECISIONS 2026-05-09`), adapters compose typed
    /// component lists rather than `sprintf "%s_%s"`. Pairs
    /// structurally with the legacy `original` parser's prefix-match
    /// over `knownSynthSources`: build via this constructor; parse
    /// back via `original`. Each component must be non-blank;
    /// rejects on first blank.
    let synthesizedComposite
        (source: string)
        (basisParts: string list)
        : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace source then
            Result.failureOf synthSourceEmpty
        elif basisParts |> List.exists System.String.IsNullOrWhiteSpace then
            Result.failureOf synthBasisEmpty
        else
            // **Per-site analysis (chapter 3.5 deep audit, hard
            // line)**: `Synthesized of source * basis` carries
            // `basis : string` — V2's chosen DU shape. The build
            // path (here) and parse path (`SsKey.original`'s
            // prefix-match at line ~119) form a structural pair;
            // both use `_` as the canonical separator.
            // Alternatives considered:
            //   - **Refactor `Synthesized` to `of source * basis-
            //     parts: string list`**: would carry typed list
            //     through the SsKey DU; eliminates the joiner.
            //     Breaking change — affects A4 structural equality,
            //     A1 rename-survival semantics, ~50+ test fixtures
            //     using `Synthesized (s, b)` patterns. Deferred to
            //     chapter 4+ as a structural-commitment refactor.
            //   - **`String.Join("_", basisParts)`** (BCL): same
            //     family as `String.concat`; renamed primitive.
            //   - **Adopted: `String.concat "_"`** — F#'s typed
            //     collection joiner; the typed `string list` IS
            //     the structure; the separator is parameterized.
            //     The boundary IS the SsKey DU's `basis` field;
            //     the joiner is the build half of the round-trip.
            let basis = basisParts |> String.concat "_"  // LINT-ALLOW: round-trip pair with `SsKey.original` parser; SsKey-DU shape refactor deferred to chapter 4
            Result.success (Synthesized (source, basis))

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

    /// Synthesis-source prefixes recognized by the back-compat
    /// `original` shim. When a pre-stratification caller passes
    /// `"OS_KIND_AppCore_User"`, the shim splits on the first matching
    /// prefix so the result matches what the migrated adapter emits
    /// directly via `synthesized "OS_KIND" "AppCore_User"`. Anything not
    /// matching a known prefix falls back to the `LEGACY` marker.
    let private knownSynthSources : string list =
        [ "OS_KIND"; "OS_MOD"; "OS_ATTR"; "OS_REF"; "OS_IDX"; "OS_ROW" ]

    /// **Back-compat shim.** Pre-stratification call sites (~50+ test
    /// fixtures) pass a single string identifier; this function
    /// preserves their surface by detecting a known synthesis prefix
    /// (`OS_KIND_…`, `OS_MOD_…`, etc.) and producing the matching
    /// `Synthesized (source, basis)` value. Strings without a known
    /// prefix get the marker source `"LEGACY"`. New call sites should
    /// prefer `synthesized` directly per the chapter-3 cross-cutting
    /// prescope §3 stratification.
    ///
    /// Rejects blank input with `ValidationError "sskey.empty"`.
    let original (value: string) : Result<SsKey> =
        if System.String.IsNullOrWhiteSpace value then
            Result.failureOf legacyValueEmpty
        else
            // Per-site analysis (chapter 3.5 deep audit): the legacy
            // implementation built `<src>_` via `String.Concat` to
            // pass to `value.StartsWith`. The defensive / paradigmatic
            // alternative — structural multi-check via BCL primitives
            // alone — eliminates the string allocation entirely:
            //   1. `value.StartsWith(src, OrdinalIgnoreCase)` — BCL
            //      string-prefix match, no allocation.
            //   2. `value.Length > src.Length` — BCL length check.
            //   3. `value.[src.Length] = '_'` — BCL char indexer.
            // The three checks compose without producing any
            // intermediate string. Same observable behavior as the
            // sentinel form; data-structure-oriented (no string-
            // concatenation primitive used).
            let prefixMatch =
                knownSynthSources
                |> List.tryFind (fun src ->
                    value.StartsWith(src, System.StringComparison.Ordinal)
                    && value.Length > src.Length
                    && value.[src.Length] = '_')
            match prefixMatch with
            | Some src ->
                let basis = value.Substring(src.Length + 1)
                if System.String.IsNullOrWhiteSpace basis then
                    Result.success (Synthesized ("LEGACY", value))
                else
                    Result.success (Synthesized (src, basis))
            | None ->
                Result.success (Synthesized ("LEGACY", value))

    /// **Back-compat shim.** Forwards to `derivedFrom`. Existing call
    /// sites in `SymmetricClosure` migrate to the new name; this shim
    /// stays through chapter 3 to absorb adapter call sites that have
    /// not yet migrated.
    let derived (parent: SsKey) (reason: string) : Result<SsKey> =
        derivedFrom parent reason

    /// Walk back to the originating identifier of a key, surfaced as a
    /// string for diagnostics. Variant-specific surfacing:
    ///   - `OssysOriginal g` → `g.ToString "N"` (32-char hex, no dashes)
    ///   - `Synthesized (source, basis)` → `<source>_<basis>` (preserves
    ///     the pre-stratification full-identifier form that emitter
    ///     comments and differential tests depend on; consumers needing
    ///     just `basis` or `source` should pattern-match on the variant
    ///     directly)
    ///   - `DerivedFrom (parent, _)` → recurse on parent
    ///   - `V1Mapped (v1, _)` → `v1.ToString "N"`
    /// Never used for structural keying — A4 equality is by full DU
    /// value, not by `rootOriginal`.
    let rec rootOriginal (key: SsKey) : string =
        match key with
        | OssysOriginal g -> g.ToString "N"
        | Synthesized (source, basis) ->
            // **Per-site analysis (chapter 3.5 deep audit, hard
            // line)**: `rootOriginal` is the *terminal diagnostic
            // projection* of `SsKey` to a single human-readable
            // string. Alternatives considered for the `Synthesized`
            // case:
            //   - **Typed JSON emission via `Utf8JsonWriter`**:
            //     would change the wire format
            //     (`{kind:"synthesized", source, basis}` vs
            //     `<source>_<basis>`); breaks 4+ emitter consumers
            //     (`JsonEmitter`, `DistributionsEmitter`,
            //     `RawTextEmitter` comments, `RefactorLogEmitter`
            //     UUIDv5 input). Architectural; deferred to
            //     chapter 4+ when typed `SsKey` payload propagates
            //     through `LineageEvent` and `DiagnosticEntry`.
            //   - **Pattern-match at each consumer**: each consumer
            //     would build its own typed string form for SsKey;
            //     duplicates the projection logic at four sites.
            //     Worse than centralizing here.
            //   - **`StringBuilder.Append` chain**: concatenation
            //     under the hood; same Big-O; less readable.
            //   - **Typed `SsKey` carried through to the terminal
            //     UI / JSON serializer**: the right architectural
            //     answer; deferred (see Chapter 4+).
            //   - **Adopted: `String.concat "_"` over typed list**.
            //     Terminal-text-emission boundary; the typed
            //     2-element list IS the structure; the separator
            //     `_` is parameterized; the consumers ALREADY have
            //     typed access via pattern-matching on `Synthesized
            //     (source, basis)` directly when they need
            //     structural data. `rootOriginal` is the *display
            //     projection* alone.
            String.concat "_" [ source; basis ]  // LINT-ALLOW: terminal diagnostic projection; typed `Synthesized (s, b)` available via pattern-match for structural consumers
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

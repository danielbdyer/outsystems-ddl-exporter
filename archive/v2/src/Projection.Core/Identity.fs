namespace Projection.Core

// LINT-ALLOW-FILE: diagnostic identity projection. `describeKey` renders an operator-facing
//   `<root> [derived]` string via `sprintf`/concat for diagnostic surfaces; the
//   typed `SsKey` variant DU remains the structural identity (consumers query
//   via `isDerived` / `rootOriginal`). Only the human-readable projection uses
//   string composition, per `DECISIONS 2026-05-09 — Built-in obligation`.

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
/// The closed vocabulary of pass-introduced derivation reasons (recon #14;
/// DECISIONS 2026-05-06 deferred this DU "when more than one is in use", brought
/// forward by operator decision 2026-06-27 — identity-is-a-type outweighs the
/// wait-for-a-second-reason heuristic; the cost is one DU case, the win is
/// unforgeable derivation provenance). Closing the set means a typo can no longer
/// mint a silently-different identity, and `Reference.isInverse` becomes a total
/// match instead of a string compare. New reasons are added HERE, never as free
/// strings. The serialized token is the wire form the SsKey codec length-prefixes:
/// `Inverse` ⇄ `"inverse"` keeps the byte format identical to the prior
/// open-string representation (the only value ever constructed).
type DerivationReason =
    /// Symmetric-closure pass — the synthesized inverse of a reference.
    | Inverse

/// Serialization + parsing for the closed `DerivationReason` set. The codec
/// (`SsKey.serialize`/`deserialize`) goes through `serialize`/`parse` so the wire
/// token stays the single source of truth and an unknown stored token fails loud.
[<RequireQualifiedAccess>]
module DerivationReason =

    let serialize (r: DerivationReason) : string =
        match r with
        | Inverse -> "inverse"

    let parse (s: string) : Result<DerivationReason> =
        match s with
        | "inverse" -> Result.success Inverse
        | other ->
            Result.failureOf
                (ValidationError.create "sskey.derivationReason.unknown"
                    (String.concat "" [ "unknown derivation reason '"; other; "'" ]))

type SsKey =
    | OssysOriginal of System.Guid
    | Synthesized of source: string * basisParts: string list
    | DerivedFrom of parent: SsKey * reason: DerivationReason
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

    /// Build a `DerivedFrom` SsKey from a parent identity and a closed
    /// `DerivationReason`. Total — the reason is unforgeable by construction (the
    /// prior blank-string rejection is now structural), so no `Result`. Used by
    /// passes that introduce new nodes (e.g., `SymmetricClosure`).
    let derivedFrom (parent: SsKey) (reason: DerivationReason) : SsKey =
        DerivedFrom (parent, reason)

    /// Build a `V1Mapped` SsKey from a V1 GUID and the V2 namespace tag.
    /// Total: GUID arithmetic is. The `v2Namespace` GUID is a stable
    /// V2-side namespace identifier; the actual UUIDv5 derivation lives
    /// in adapter code (chapter 4.2 User FK reflow), this constructor
    /// just records the result.
    let fromV1 (v1: System.Guid) (v2Namespace: System.Guid) : SsKey =
        V1Mapped (v1, v2Namespace)

    // ---- Wave 4.1 — round-trippable codec for V2.SsKey persistence ----
    // `display`/`rootOriginal` are lossy projections. `serialize` is the
    // *recoverable* form written to the frozen schema's `V2.SsKey` extended
    // property so a later-run Transfer reads identity from disk instead of
    // re-synthesizing `Synthesized ("READSIDE_KIND", …)` from physical names
    // (A1: identity survives rename). Encoding is tag-prefixed; each field is
    // length-prefixed `<len>:<content>`, so it stays unambiguous under nesting
    // (`DerivedFrom` carries a parent key; `Synthesized` carries a list) with
    // no delimiter-escaping.

    let private deserErr (detail: string) : ValidationError =
        ValidationError.create "sskey.deserialize" detail

    let private field (s: string) : string =
        String.concat "" [ string s.Length; ":"; s ]

    /// Consume one `<len>:<content>` field; return (content, remainder).
    let private readField (s: string) : Result<string * string> =
        let colon = s.IndexOf ':'
        if colon < 0 then Result.failureOf (deserErr "missing field-length delimiter")
        else
            match System.Int32.TryParse (s.Substring(0, colon)) with
            | false, _ -> Result.failureOf (deserErr "non-numeric field length")
            | true, len ->
                let start = colon + 1
                if len < 0 || start + len > s.Length then
                    Result.failureOf (deserErr "field length out of range")
                else
                    Result.success (s.Substring(start, len), s.Substring(start + len))

    let private parseGuid (s: string) : Result<System.Guid> =
        match System.Guid.TryParse s with
        | true, g -> Result.success g
        | false, _ -> Result.failureOf (deserErr (String.concat "" [ "malformed GUID '"; s; "'" ]))

    /// Total over the four variants; round-trips through `deserialize`.
    let rec serialize (key: SsKey) : string =
        match key with
        | OssysOriginal g -> String.concat "" [ "O"; field (g.ToString "N") ]
        | Synthesized (source, basisParts) ->
            String.concat "" (
                [ "S"; field source; field (string (List.length basisParts)) ]
                @ (basisParts |> List.map field))
        | DerivedFrom (parent, reason) ->
            String.concat "" [ "D"; field (DerivationReason.serialize reason); field (serialize parent) ]
        | V1Mapped (v1, v2) ->
            String.concat "" [ "V"; field (v1.ToString "N"); field (v2.ToString "N") ]

    let rec private readFields (n: int) (acc: string list) (s: string) : Result<string list * string> =
        if n <= 0 then Result.success (List.rev acc, s)
        else
            readField s |> Result.bind (fun (part, rest) -> readFields (n - 1) (part :: acc) rest)

    let rec private parse (s: string) : Result<SsKey> =
        if s.Length = 0 then Result.failureOf (deserErr "empty input")
        else
            let body = s.Substring 1
            let noTrailing rest ok =
                if rest <> "" then Result.failureOf (deserErr "trailing data after key")
                else Result.success ok
            match s.[0] with
            | 'O' ->
                readField body |> Result.bind (fun (g, rest) ->
                    parseGuid g |> Result.bind (fun guid -> noTrailing rest (OssysOriginal guid)))
            | 'S' ->
                readField body |> Result.bind (fun (source, r1) ->
                    readField r1 |> Result.bind (fun (countStr, r2) ->
                        match System.Int32.TryParse countStr with
                        | false, _ -> Result.failureOf (deserErr "non-numeric basisParts count")
                        | true, count ->
                            readFields count [] r2 |> Result.bind (fun (parts, rest) ->
                                noTrailing rest (Synthesized (source, parts)))))
            | 'D' ->
                readField body |> Result.bind (fun (reasonStr, r1) ->
                    DerivationReason.parse reasonStr |> Result.bind (fun reason ->
                        readField r1 |> Result.bind (fun (parentStr, rest) ->
                            if rest <> "" then Result.failureOf (deserErr "trailing data after derived key")
                            else parse parentStr |> Result.map (fun parent -> DerivedFrom (parent, reason)))))
            | 'V' ->
                readField body |> Result.bind (fun (v1s, r1) ->
                    readField r1 |> Result.bind (fun (v2s, rest) ->
                        parseGuid v1s |> Result.bind (fun v1 ->
                            parseGuid v2s |> Result.bind (fun v2 -> noTrailing rest (V1Mapped (v1, v2))))))
            | other ->
                Result.failureOf (deserErr (String.concat "" [ "unknown variant tag '"; string other; "'" ]))

    /// Inverse of `serialize`; total parser returning a structured error.
    let deserialize (s: string) : Result<SsKey> = parse s


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

    /// The root leaf identity — peel every `DerivedFrom` wrapper. The
    /// stability of an identity under rename is the stability of its root:
    /// a `DerivedFrom` rooted in a `Synthesized` key is as name-unstable as
    /// the `Synthesized` root itself.
    let rec rootKey (key: SsKey) : SsKey =
        match key with
        | DerivedFrom (parent, _) -> rootKey parent
        | leaf -> leaf

    /// True iff the identity's root is `Synthesized` — i.e. the key is
    /// derived from the entity's *name* (`(schema, table)` / JSON path), so a
    /// rename CHANGES the key and A1 identity cannot be threaded across it
    /// without a reconciliation rule or a persisted V2 SsKey. `OssysOriginal`
    /// / `V1Mapped` roots are GUID-stable; renames thread natively. 6.A.7.
    let isSynthesizedRoot (key: SsKey) : bool =
        match rootKey key with
        | Synthesized _ -> true
        | OssysOriginal _ | V1Mapped _ -> false
        | DerivedFrom _ -> false  // unreachable: rootKey peels DerivedFrom

    /// The synthesis convention (`source`) of a `Synthesized`-rooted key, or
    /// `None` for a GUID-rooted identity. Two `Synthesized` keys with the
    /// same `source` came from the same adapter convention — a precondition
    /// for treating a Removed+Added pair as a plausible rename (6.A.7).
    let synthesisSource (key: SsKey) : string option =
        match rootKey key with
        | Synthesized (source, _) -> Some source
        | OssysOriginal _ | V1Mapped _ | DerivedFrom _ -> None

    /// Sequence of derivation reasons from the root outward, oldest first.
    /// Empty for leaf identities (`OssysOriginal`, `Synthesized`,
    /// `V1Mapped`).
    let rec derivationReasons (key: SsKey) : DerivationReason list =
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

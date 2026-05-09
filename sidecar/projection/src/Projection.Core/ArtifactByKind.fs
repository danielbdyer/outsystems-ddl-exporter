namespace Projection.Core

/// Π-side error envelope. Names the structured failure modes the
/// `ArtifactByKind` smart constructor surfaces. `EmitError` flows through
/// FSharp.Core's two-arity `Result<'a, 'b>` (the type alias `Emitter
/// <'element>` in `Types.fs` uses `Result<ArtifactByKind<'element>,
/// EmitError>`); this coexists with `Projection.Core.Result<'a>`
/// per the arity-disambiguation note in `Result.fs`.
///
/// Per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md` §2, the strict-
/// equality invariant produces two complementary error variants —
/// `KindNotProduced` for a key absent from the slice, `UnexpectedKind`
/// for a key absent from the source Catalog. Both surface bugs:
/// missing keys come from emitters that forgot a kind; extra keys come
/// from stale fixtures, copy-and-paste errors, or layering violations
/// (derived keys not registered on the Catalog).
type EmitError =
    /// The emitter did not produce output for an SsKey present in the
    /// source Catalog. Surfaces a missing key under strict equality.
    | KindNotProduced of SsKey
    /// The emitter produced output for an SsKey not present in the
    /// source Catalog. Surfaces a stale fixture, copy-and-paste error,
    /// or layering violation (derived keys whose registration on the
    /// Catalog is missing).
    | UnexpectedKind of SsKey
    /// Per-key rendering failed; reason is human-readable. Surfaces a
    /// structural failure on a present, expected SsKey.
    | RenderFailed of SsKey * reason: string


/// Per-kind output indexed by SsKey root. The smart constructor
/// `ArtifactByKind.create catalog slices` enforces strict equality
/// between the slice's keyset and `Catalog.allKinds catalog`'s SsKey
/// set: every kind appears as a key, no extra keys are tolerated.
///
/// T11 (sibling-Π commutativity per `AXIOMS.md`) becomes a structural
/// consequence of construction: any two `ArtifactByKind` values built
/// from the same Catalog have equal keysets by construction. The
/// chapter-3 cross-cutting AXIOMS amendment ("T11 amended (structural
/// type encoding)") names the codification: T11 stops being a
/// substring-search property and becomes a type theorem. The substring
/// T11 enforcement at `JsonEmitterTests.fs:96-105` and
/// `RichProfilingEndToEndTests.fs:280` retires when slice 5.6 lands.
///
/// The constructor is `private` — callers cannot bypass the smart
/// constructor's invariant. Construction goes through
/// `ArtifactByKind.create`; introspection goes through
/// `ArtifactByKind.toMap`, `tryFind`, `keys`.
type ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>

[<RequireQualifiedAccess>]
module ArtifactByKind =

    /// Smart constructor — strict equality between the slice's keyset
    /// and `Catalog.allKinds`. Returns `Ok` when both subsets are
    /// empty: `missing → KindNotProduced`; `extra → UnexpectedKind`.
    /// Per the prescope §2 strict-equal decision: the type proves
    /// the keyset, including the "no extras" half.
    let create (catalog: Catalog) (slices: Map<SsKey, 'a>)
        : Result<ArtifactByKind<'a>, EmitError> =
        let required =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey)
            |> Set.ofList
        let provided =
            slices |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let missing = Set.difference required provided
        let extra = Set.difference provided required
        match Set.toList missing, Set.toList extra with
        | [], [] -> Ok (ArtifactByKind slices)
        | k :: _, _ -> Error (KindNotProduced k)
        | [], k :: _ -> Error (UnexpectedKind k)

    /// Project the underlying `Map<SsKey, 'a>`. Read-only; callers
    /// must not reconstruct an `ArtifactByKind` from this — the smart
    /// constructor is the only path that re-validates.
    let toMap (ArtifactByKind m) : Map<SsKey, 'a> = m

    /// Lookup by SsKey. `None` if the key is absent.
    let tryFind (key: SsKey) (a: ArtifactByKind<'a>) : 'a option =
        Map.tryFind key (toMap a)

    /// The SsKey set of the artifact. Equal to
    /// `Catalog.allKinds catalog |> List.map (_.SsKey) |> Set.ofList`
    /// by construction (T11 structural).
    let keys (a: ArtifactByKind<'a>) : Set<SsKey> =
        toMap a |> Map.toSeq |> Seq.map fst |> Set.ofSeq

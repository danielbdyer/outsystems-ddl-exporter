namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: realization-layer accumulator — the packed
//   assignment dictionaries mutate per capture (the write path's hot
//   loop); the abstraction is reified behind capture/tryFind.

open System.Collections.Generic
open Projection.Core

/// The realization layer's surrogate-remap accumulator for the
/// `AssignedBySink` capture path at estate scale. `SurrogateRemapContext`
/// (the pure Core carrier) holds an immutable string-keyed Map — right for
/// plan-side / operator-supplied remaps, but at 10⁸ FK-target rows (the
/// hundreds-of-millions-row estate with its huge tables FK-referenced) the boxed-string
/// tree costs ~250B/entry and O(log n) string compares per lookup.
/// IDENTITY surrogates are integral, so assignments pack into
/// `Dictionary<int64, int64>` (~40B/entry, O(1)); a non-integral raw (an
/// exotic identity shape) falls back to a per-kind string dictionary so
/// capture stays TOTAL — never a dropped capture, never a crash on shape.
/// Semantics mirror `SurrogateRemapContext`: within a kind a source key
/// binds at most once — a duplicate capture keeps the FIRST binding.
/// Realization-layer policy (A36): consumed through
/// `SurrogateRemap.remapRowFksWith` via `tryFind`; no IR change.
type PackedSurrogateRemap =
    private
        {
            Packed   : Dictionary<SsKey, Dictionary<int64, int64>>
            Fallback : Dictionary<SsKey, Dictionary<string, string>>
        }

[<RequireQualifiedAccess>]
module PackedSurrogateRemap =

    let create () : PackedSurrogateRemap =
        { Packed = Dictionary(); Fallback = Dictionary() }

    let private packedOf (kind: SsKey) (remap: PackedSurrogateRemap) : Dictionary<int64, int64> =
        match remap.Packed.TryGetValue kind with
        | true, d -> d
        | false, _ ->
            let d = Dictionary<int64, int64>()
            remap.Packed[kind] <- d
            d

    let private fallbackOf (kind: SsKey) (remap: PackedSurrogateRemap) : Dictionary<string, string> =
        match remap.Fallback.TryGetValue kind with
        | true, d -> d
        | false, _ ->
            let d = Dictionary<string, string>()
            remap.Fallback[kind] <- d
            d

    let private tryFindPacked (remap: PackedSurrogateRemap) (kind: SsKey) (src: int64) : string option =
        match remap.Packed.TryGetValue kind with
        | true, d ->
            match d.TryGetValue src with
            | true, assigned -> Some (string assigned)
            | false, _ -> None
        | false, _ -> None

    let private tryFindFallback (remap: PackedSurrogateRemap) (kind: SsKey) (sourceRaw: string) : string option =
        match remap.Fallback.TryGetValue kind with
        | true, d ->
            match d.TryGetValue sourceRaw with
            | true, assigned -> Some assigned
            | false, _ -> None
        | false, _ -> None

    /// The lookup `SurrogateRemap.remapRowFksWith` consumes. An integral
    /// source raw resolves through the packed dictionary FIRST, then the
    /// fallback (an integral source whose ASSIGNED raw was non-integral
    /// lives there); a non-integral source only ever lives in the fallback.
    let tryFind (remap: PackedSurrogateRemap) (kind: SsKey) (sourceRaw: string) : string option =
        match System.Int64.TryParse sourceRaw with
        | true, src ->
            match tryFindPacked remap kind src with
            | Some assigned -> Some assigned
            | None -> tryFindFallback remap kind sourceRaw
        | false, _ -> tryFindFallback remap kind sourceRaw

    /// Record one `(source raw → assigned raw)` capture. Empty raws are
    /// ignored (a NULL source key is inserted, never captured); a duplicate
    /// source keeps the FIRST binding ACROSS BOTH STORES (the
    /// `SurrogateRemapContext.capture` invariant, realized as keep-first
    /// instead of `Error`). The pair packs only when BOTH raws are
    /// integral; otherwise it lands in the fallback — `tryFind`'s routing
    /// mirrors this exactly.
    let capture (kind: SsKey) (sourceRaw: string) (assignedRaw: string) (remap: PackedSurrogateRemap) : unit =
        if sourceRaw <> "" && assignedRaw <> "" && (tryFind remap kind sourceRaw).IsNone then
            match System.Int64.TryParse sourceRaw, System.Int64.TryParse assignedRaw with
            | (true, src), (true, assigned) ->
                (packedOf kind remap)[src] <- assigned
            | _ ->
                (fallbackOf kind remap)[sourceRaw] <- assignedRaw

    let assignmentCount (remap: PackedSurrogateRemap) : int =
        let packed = remap.Packed.Values |> Seq.sumBy (fun d -> d.Count)
        let fallback = remap.Fallback.Values |> Seq.sumBy (fun d -> d.Count)
        packed + fallback

namespace Projection.Core

/// THE_SYNTHETIC_DATA_DESIGN.md §1 / §8 — synthesis is the section of
/// profiling: `σ : Profile ⟶ Data` such that `π ∘ σ ≈ id`. This module is
/// the algorithmic heart (slice S1): a **pure Core** function
/// `Catalog(B) × Profile × SyntheticConfig × seed → Map<SsKey, StaticRow list>`.
///
/// **Determinism (T1; design §4).** `σ` is a pure function of its inputs —
/// no clock, no `System.Random`, no `float`. The PRNG is a host-independent
/// integer generator (splitmix64; design suggested "splitmix64-style"). All
/// continuous evidence is consumed with `decimal` arithmetic; all byte
/// material (Guid / Binary) is laid out big-endian by explicit shift so the
/// output is byte-identical across hosts.
///
/// **Constraint hierarchy (design §3), per column in precedence order:**
///   1. PK / identity → a unique surrogate (sequential / type-rendered).
///   2. FK → drawn from the already-generated parent kind's PK pool
///      (zero orphans by construction); NULL at the observed rate for
///      nullable FKs; NULL when the parent pool is empty (named —
///      a non-nullable FK to an empty parent is an unsatisfiable structure
///      the load surfaces, never a silent orphan).
///   3. Unique (non-PK, single-column unique index) → a distinct value.
///   4. Everything else → sampled from the column's `Profile` distribution
///      (categorical hybrid-by-cardinality per design §2.1 / numeric per the
///      percentile shape), honoring the null rate; a type-default
///      deterministic value when there is no profile evidence.
///
/// **Two-pass over the catalog.** Pass 1 mints every kind's PK pool (PKs are
/// independent of FKs); pass 2 fills each row, drawing FK values from any
/// target's complete pool. This makes FK integrity hold for cycle FKs too
/// (the parent pool exists regardless of generation order), so the load's
/// two-phase nulls-then-FKs handling only reorders inserts — it never has to
/// invent a value.
///
/// **Drift-by-SsKey (design §6).** Evidence is joined to the target schema B
/// by `SsKey` (`Profile.tryFind* attributeKey`). B-only columns get
/// type-defaults; A-only profile entries are ignored. Profiled-legacy →
/// cloud-preview works under rename as long as identity is preserved.
///
/// **Values are emitted in `RawValueCodec` raw-string form** (design §8): the
/// load renders raw → SQL via `SqlLiteral.ofRaw` / `.toString`. This module
/// never hand-formats SQL. The empty raw `""` is the NULL sentinel; a column
/// at NULL is simply omitted from the row's `Values` map (the write path
/// treats a missing key as `""` = NULL).

/// Per-column value-fidelity decision for a categorical column (design §2.1).
/// `Preserve` emits the real reference values at their observed frequencies
/// (low re-identification risk); `Synthesize` emits fresh tokens that
/// preserve the frequency *shape* but never a real value (the explicit,
/// testable privacy contract for likely-PII / free-text / identifier columns).
[<RequireQualifiedAccess>]
type ValueFidelityMode =
    | Preserve
    | Synthesize

/// THE_SYNTHETIC_DATA_FUZZING.md §6.2 — the per-kind volume target (slice F1):
/// generate at ARBITRARY scale, decoupled from the source corpus row count.
/// `Absolute` sets an exact synthetic row count for the kind; `Multiplier`
/// scales the observed `RowCount`. A kind with no target falls back to the
/// config's global `Scale`, so existing callers are byte-identical.
[<RequireQualifiedAccess>]
type VolumeTarget =
    | Absolute of rows: int
    | Multiplier of factor: decimal

/// The hybrid-by-cardinality policy (design §2.1) plus the volume axis
/// (design §7). `PreserveCardinalityMax` is τ — categoricals with
/// `DistinctCount ≤ τ` and not truncated are preserved; the rest are
/// synthesized. `PreserveColumns` / `SynthesizeColumns` are per-column
/// overrides keyed by the attribute's logical `Name` text. `Scale` is the
/// global volume factor over the profiled `RowCount` per kind (1.0 = full);
/// `VolumeByKind` is the per-kind override (FUZZING §6.2, slice F1) — empty =
/// every kind uses `Scale`.
type SyntheticConfig = {
    PreserveCardinalityMax : int64
    PreserveColumns        : Set<string>
    SynthesizeColumns      : Set<string>
    Scale                  : decimal
    VolumeByKind           : Map<SsKey, VolumeTarget>
}

[<RequireQualifiedAccess>]
module SyntheticConfig =

    /// The conservative default (design §2.1: "default τ conservative,
    /// recommend ≤ 50"): τ = 50, no overrides, full scale, no per-kind volume.
    let defaultConfig : SyntheticConfig =
        { PreserveCardinalityMax = 50L
          PreserveColumns        = Set.empty
          SynthesizeColumns      = Set.empty
          Scale                  = 1M
          VolumeByKind           = Map.empty }


/// NM-21 — a named lineage event σ emits when a **non-nullable** FK column
/// draws against an **empty** parent pool. The constraint hierarchy (design
/// §3 step 2) emits `NULL` for such a column because there is no parent key
/// to point at — an *unsatisfiable structure* the load surfaces. Previously
/// σ emitted that `NULL` with NO Core-level signal, so a DryRun preview (which
/// never reaches the load-time failure) saw the erasure silently. This record
/// names it: σ now returns one `SyntheticDiagnostic` per unsatisfiable
/// non-nullable FK. Keys are `SsKey` (strongly typed; no name lookup); `Reason`
/// is human-readable — surface it to operator diagnostics, never parse it.
type SyntheticDiagnostic = {
    /// Stable diagnostic code (`category.subject.problem` lower-dot convention,
    /// mirroring `ValidationError.Code`). Always `"synthetic.fk.unsatisfiable"`
    /// for this variant; carried explicitly so a consumer filters on the code,
    /// not the message.
    Code            : string
    /// The owning kind whose row would carry the FK.
    Kind            : SsKey
    /// The FK source attribute (the non-nullable column forced to `NULL`).
    SourceAttribute : SsKey
    /// The target kind whose PK pool is empty.
    TargetKind      : SsKey
    /// Human-readable narration for operator diagnostics. Never parse it.
    Reason          : string
}

[<RequireQualifiedAccess>]
module SyntheticDiagnostic =

    /// The stable code for the unsatisfiable-non-nullable-FK event.
    [<Literal>]
    let UnsatisfiableForeignKeyCode : string = "synthetic.fk.unsatisfiable"

    /// Build the unsatisfiable-FK diagnostic for one (kind, source attr,
    /// target) triple drawn against an empty parent pool.
    let unsatisfiableForeignKey (kind: SsKey) (sourceAttr: SsKey) (target: SsKey) : SyntheticDiagnostic =
        { Code            = UnsatisfiableForeignKeyCode
          Kind            = kind
          SourceAttribute = sourceAttr
          TargetKind      = target
          Reason          =
            String.concat "" [
                "non-nullable FK "; SsKey.serialize sourceAttr
                " (kind "; SsKey.serialize kind
                ") references kind "; SsKey.serialize target
                " whose synthetic PK pool is empty: the column is forced to NULL "
                "(an unsatisfiable structure the load surfaces, never a silent orphan)" ] }


[<RequireQualifiedAccess>]
module SyntheticData =

    // -- Host-independent integer PRNG (splitmix64; design §4) ---------------
    //
    // Pure-integer mixing over uint64. F#'s default arithmetic operators are
    // unchecked (the `<CheckForOverflowUnderflow>` flag governs only the
    // `Checked.*` operators), so the wrapping multiply / add splitmix64 needs
    // are well-defined and byte-identical across hosts.

    [<Literal>]
    let private SplitGamma : uint64 = 0x9E3779B97F4A7C15UL

    let private mix (z0: uint64) : uint64 =
        let z1 = (z0 ^^^ (z0 >>> 30)) * 0xBF58476D1CE4E5B9UL
        let z2 = (z1 ^^^ (z1 >>> 27)) * 0x94D049BB133111EBUL
        z2 ^^^ (z2 >>> 31)

    /// A splitmix64 generator state. Threaded functionally; one `draw`
    /// advances the state and yields a uniformly-mixed 64-bit value.
    type private Rng = { State : uint64 }

    let private rngOf (seed: uint64) : Rng = { State = seed }

    let private draw (r: Rng) : uint64 * Rng =
        let s = r.State + SplitGamma
        mix s, { State = s }

    /// A uniform draw in `[0, n)` for `n > 0` (modulo bias is acceptable for
    /// synthesis fidelity; the design's correctness theorem is distributional
    /// "within ε", not a uniformity proof). `n = 0` returns `(0, r)`.
    let private drawBelow (n: uint64) (r: Rng) : uint64 * Rng =
        if n = 0UL then 0UL, r
        else
            let v, r' = draw r
            v % n, r'

    // -- Host-independent byte layout (big-endian; design §4) ----------------

    /// Big-endian bytes of a uint64 — explicit shift, never `BitConverter`
    /// (whose endianness is host-dependent).
    let private bytesBE (v: uint64) : byte[] =
        [| for i in 0..7 -> byte ((v >>> (8 * (7 - i))) &&& 0xFFUL) |]

    /// A deterministic GUID from two 64-bit halves. `System.Guid(byte[16])`
    /// interprets the bytes by the fixed GUID layout (host-independent).
    let private guidOf (hi: uint64) (lo: uint64) : System.Guid =
        System.Guid(Array.append (bytesBE hi) (bytesBE lo))

    // FNV-1a over a string — a stable per-key 64-bit seed contribution. The
    // string is a `SsKey.serialize` (recoverable, deterministic); UTF-16 code
    // units are host-independent.
    let private fnv1a (s: string) : uint64 =
        let mutable h = 14695981039346656037UL
        for ch in s do
            h <- (h ^^^ uint64 (uint16 ch)) * 1099511628211UL
        h

    /// The per-kind base seed: the master seed mixed with a stable hash of the
    /// kind's identity. Independent per kind and stable under kind reordering.
    let private kindSeed (master: uint64) (key: SsKey) : uint64 =
        mix (master ^^^ fnv1a (SsKey.serialize key))

    // -- Type-rendered surrogates and defaults (raw-form; design §3 / §8) -----

    let private ticksPerDay : int64 = 864000000000L
    let private baseDate : System.DateTime = System.DateTime(2000, 1, 1, 0, 0, 0, System.DateTimeKind.Unspecified)
    let private inv (v: 'a when 'a :> System.IFormattable) : string =
        v.ToString(null, System.Globalization.CultureInfo.InvariantCulture)

    /// A unique PK surrogate for row `index` of a kind, rendered in raw form
    /// for the attribute's `PrimitiveType`. Uniqueness within the kind is
    /// carried by `index` (integer / text / temporal sequences) or by encoding
    /// `index` into the value's bytes (Guid / Binary). Booleans cannot be a
    /// unique surrogate beyond two rows — a named degeneracy (`PrimaryKey on a
    /// Boolean` is not the OutSystems single-surrogate-Id norm, design §7); the
    /// alternating value is emitted and the load's PK uniqueness surfaces it.
    let private surrogateRaw (ptype: PrimitiveType) (kindHash: uint64) (index: int) : string =
        match ptype with
        | Integer  -> inv (int64 (index + 1))
        | Decimal  -> inv (decimal (index + 1))
        | Text     -> "row-" + inv (index + 1)
        | Guid     -> RawValueCodec.formatGuid (guidOf kindHash (uint64 index))
        | DateTime -> RawValueCodec.formatDateTime (baseDate.AddTicks(int64 index * ticksPerDay))
        | Date     -> RawValueCodec.formatDate (baseDate.AddTicks(int64 index * ticksPerDay))
        | Time     -> RawValueCodec.formatTime (System.TimeSpan(int64 index * (ticksPerDay / 86400L)))
        | Binary   -> System.Convert.ToHexString(Array.append (bytesBE kindHash) (bytesBE (uint64 index)))
        | Boolean  -> RawValueCodec.formatBoolean (index % 2 = 0)

    /// A type-default deterministic value (design §3: "a type-default
    /// deterministic value when there is no profile evidence"). Exhaustive
    /// over `PrimitiveType` — every variant has a generator (total decisions,
    /// named skips: there is no silent fallthrough).
    let private typeDefaultRaw (ptype: PrimitiveType) (d: uint64) : string =
        match ptype with
        | Integer  -> inv (int64 (d % 1000UL))
        | Decimal  -> inv (decimal (d % 100000UL) / 100M)
        | Text     -> "text-" + inv (d % 10000UL)
        | Boolean  -> RawValueCodec.formatBoolean (d % 2UL = 0UL)
        | DateTime -> RawValueCodec.formatDateTime (baseDate.AddTicks(int64 (d % 3650UL) * ticksPerDay))
        | Date     -> RawValueCodec.formatDate (baseDate.AddTicks(int64 (d % 3650UL) * ticksPerDay))
        | Time     -> RawValueCodec.formatTime (System.TimeSpan(int64 (d % uint64 ticksPerDay)))
        | Binary   -> System.Convert.ToHexString(bytesBE d)
        | Guid     -> RawValueCodec.formatGuid (guidOf d (mix d))

    // -- Distribution samplers (design §2.1 / §3) ----------------------------

    /// Weighted index pick over cumulative `int64` counts (design §4: "weighted
    /// categorical via cumulative counts + draw mod total"). Returns the bucket
    /// index for the drawn position; `0` for an all-zero / empty weight list.
    let private weightedIndex (counts: int64 list) (r: Rng) : int * Rng =
        let total = counts |> List.sumBy (fun c -> if c > 0L then uint64 c else 0UL)
        if total = 0UL then 0, r
        else
            let pos, r' = drawBelow total r
            let rec walk i acc = function
                | [] -> max 0 (List.length counts - 1)
                | c :: rest ->
                    let acc' = acc + (if c > 0L then uint64 c else 0UL)
                    if pos < acc' then i else walk (i + 1) acc' rest
            walk 0 0UL counts, r'

    /// The hybrid-by-cardinality decision (design §2.1). Overrides win;
    /// otherwise truncation ⇒ synthesize (a capped vocabulary means the tail
    /// is unseen), high distinct-count ⇒ synthesize, else preserve.
    let private valueFidelityFor
        (config: SyntheticConfig)
        (attrName: string)
        (cat: CategoricalDistribution)
        : ValueFidelityMode =
        if Set.contains attrName config.SynthesizeColumns then ValueFidelityMode.Synthesize
        elif Set.contains attrName config.PreserveColumns then ValueFidelityMode.Preserve
        elif cat.IsTruncated then ValueFidelityMode.Synthesize
        elif cat.DistinctCount > config.PreserveCardinalityMax then ValueFidelityMode.Synthesize
        else ValueFidelityMode.Preserve

    /// The synthetic token for bucket `i` of a categorical column under
    /// `Synthesize` (design §2.1: "fresh tokens preserving the frequency
    /// shape, never emitting a real value"). The per-attribute namespace
    /// prefix makes the emitted set provably disjoint from the real
    /// `Frequencies` — the privacy contract, true by construction.
    let private synthToken (attrHash: uint64) (bucket: int) : string =
        "syn:" + (attrHash &&& 0xFFFFFFFFUL).ToString("x8") + ":" + string bucket

    /// Sample a categorical column. `Preserve` emits the real bucket value;
    /// `Synthesize` emits the bucket's synthetic token. Bucket selection is
    /// the same weighted draw in both modes, so the frequency shape is
    /// preserved either way.
    let private sampleCategorical
        (mode: ValueFidelityMode)
        (attrHash: uint64)
        (cat: CategoricalDistribution)
        (r: Rng)
        : string * Rng =
        match cat.Frequencies with
        | [] -> "", r
        | freqs ->
            let counts = freqs |> List.map snd
            let i, r' = weightedIndex counts r
            let i = min i (List.length freqs - 1)
            match mode with
            | ValueFidelityMode.Preserve   -> fst freqs.[i], r'
            | ValueFidelityMode.Synthesize -> synthToken attrHash i, r'

    /// Sample a numeric column within the percentile shape (design §3 / §4:
    /// "numeric via decimal arithmetic over the percentile shape"). Picks one
    /// of the six inter-percentile segments by its approximate mass, then
    /// interpolates within `[lo, hi]` with `decimal` arithmetic. Integer types
    /// truncate; the result is clamped to `[Min, Max]`.
    let private sampleNumeric (ptype: PrimitiveType) (nd: NumericDistribution) (r: Rng) : string * Rng =
        let segments =
            [ nd.Min, nd.P25, 25L
              nd.P25, nd.P50, 25L
              nd.P50, nd.P75, 25L
              nd.P75, nd.P95, 20L
              nd.P95, nd.P99, 4L
              nd.P99, nd.Max, 1L ]
        let segIdx, r1 = weightedIndex (segments |> List.map (fun (_, _, w) -> w)) r
        let segIdx = min segIdx (List.length segments - 1)
        let lo, hi, _ = segments.[segIdx]
        let fracNum, r2 = drawBelow 1000UL r1
        let value = lo + (hi - lo) * decimal fracNum / 1000M
        let clamped = max nd.Min (min nd.Max value)
        let raw =
            match ptype with
            | Integer -> inv (int64 (System.Decimal.Truncate clamped))
            | _       -> inv clamped
        raw, r2

    // -- Per-column generation, in the §3 precedence order -------------------

    /// True iff `attr` is the single key column of a single-column unique
    /// (or PK) index on `kind` — the §3 step-3 "unique non-PK" signal.
    let private isSingleColumnUnique (kind: Kind) (attr: Attribute) : bool =
        kind.Indexes
        |> List.exists (fun idx ->
            IndexUniqueness.isUnique idx.Uniqueness
            && (match idx.Columns with
                | [ only ] -> only.Attribute = attr.SsKey
                | _        -> false))

    /// The observed null rate for an attribute as the integer pair
    /// `(nullCount, rowCount)`; `None` when there is no column profile or the
    /// row count is zero. Used for rational (integer) null decisions — no
    /// float (design §4).
    let private nullBudget (profile: Profile) (attrKey: SsKey) : (int64 * int64) option =
        match Profile.tryFindColumn attrKey profile with
        | Some c when c.RowCount > 0L -> Some (c.NullCount, c.RowCount)
        | _ -> None

    /// Decide NULL for a nullable column at its observed rate (rational
    /// comparison, design §4). Columns with no evidence are never nulled.
    let private drawsNull (profile: Profile) (attrKey: SsKey) (r: Rng) : bool * Rng =
        match nullBudget profile attrKey with
        | Some (nullCount, rowCount) when nullCount > 0L ->
            let pos, r' = drawBelow (uint64 rowCount) r
            pos < uint64 nullCount, r'
        | _ -> false, r

    // The full row-cell generation result is built into the row's Values map
    // (raw strings keyed by attribute Name). A column at NULL is omitted.

    /// Build one kind's rows. `pkPools` carries every kind's PK pool (raw PK
    /// values, one per row) so FK columns draw from the target's pool. The
    /// per-kind `Rng` is threaded over rows × attributes in a fixed order
    /// (declaration order, ascending row index) — deterministic by
    /// construction.
    let private generateKindRows
        (catalog: Catalog)
        (profile: Profile)
        (config: SyntheticConfig)
        (master: uint64)
        (pkPools: Map<SsKey, string list>)
        (kind: Kind)
        : StaticRow list * SyntheticDiagnostic list =
        let kindHash = fnv1a (SsKey.serialize kind.SsKey)
        let rowCount = pkPools |> Map.tryFind kind.SsKey |> Option.map List.length |> Option.defaultValue 0
        let pkPool = pkPools |> Map.tryFind kind.SsKey |> Option.defaultValue []
        let pkAttr = kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey)
        // Reference (FK) source attributes → target kind, for §3 step 2.
        let fkByAttr =
            kind.References
            |> List.map (fun ref -> ref.SourceAttribute, ref.TargetKind)
            |> Map.ofList
        // §6.3 (F5) — SourceAttribute → ReferenceKey, for the captured FK
        // selectivity lookup. The selectivity's count-DESC frequencies weight the
        // SYNTHETIC parent pool BY RANK (the most-referenced synthetic parent ←
        // the highest source frequency), reproducing the fan-out SKEW without the
        // real parent keys the synthetic pool does not share. `None` (no evidence
        // / all-zero support) keeps the uniform draw — byte-identical to pre-F5.
        let refKeyByAttr =
            kind.References
            |> List.map (fun ref -> ref.SourceAttribute, ref.SsKey)
            |> Map.ofList
        let fkSelectivityWeights (sourceAttr: SsKey) (poolLen: int) : int64 list option =
            match Map.tryFind sourceAttr refKeyByAttr with
            | None -> None
            | Some refKey ->
                match Profile.tryFindForeignKeySelectivity refKey profile with
                | Some sel when not (List.isEmpty sel.Frequencies) ->
                    let counts = sel.Frequencies |> List.map snd |> List.toArray
                    let weights = [ for i in 0 .. poolLen - 1 -> if i < counts.Length then counts.[i] else 0L ]
                    if weights |> List.sumBy (fun c -> if c > 0L then c else 0L) > 0L then Some weights else None
                | _ -> None
        // §6.3 (F5b) — the kind's captured FK JOINT distribution, if any. σ draws
        // a correlated FK-TUPLE per row (preserving which parent-combinations
        // co-occur) instead of drawing each FK independently. Self-contained from
        // the joint alone: per tuple-position, the real values rank by descending
        // marginal frequency, and each drawn component maps to the synthetic parent
        // pool BY RANK (the same discipline as F5a) — so the co-occurrence STRUCTURE
        // is reproduced on synthetic keys without the real keys the pool lacks. The
        // tuple key is `String.Join("|", "<tag>:<value>" parts)` in reference order
        // (LiveProfiler `projectTupleKeys`); null rows were excluded at capture.
        // `None` (no joint evidence) → each FK draws independently — byte-identical
        // to pre-F5b. The returned thunk advances the Rng by exactly ONE draw.
        let jointDraw : (Rng -> Map<SsKey, string> * Rng) option =
            profile.JointDistributions
            |> List.tryFind (fun j -> j.KindKey = kind.SsKey)
            |> Option.bind (fun j ->
                let decoded =
                    j.Frequencies
                    |> List.choose (fun (key, c) ->
                        let parts = key.Split('|')
                        if c > 0L && parts.Length = List.length j.AttributeKeys then Some (parts, c) else None)
                if List.isEmpty decoded then None
                else
                    let posCount = List.length j.AttributeKeys
                    let rankMaps =
                        [ for p in 0 .. posCount - 1 ->
                            decoded
                            |> List.groupBy (fun (parts, _) -> parts.[p])
                            |> List.map (fun (partVal, grp) -> partVal, grp |> List.sumBy snd)
                            |> List.sortWith (fun (vA, cA) (vB, cB) ->
                                let c = compare cB cA in if c <> 0 then c else compare vA vB)
                            |> List.mapi (fun rank (partVal, _) -> partVal, rank)
                            |> Map.ofList ]
                    let posPools =
                        j.AttributeKeys
                        |> List.map (fun attrKey ->
                            match Map.tryFind attrKey fkByAttr with
                            | Some target -> pkPools |> Map.tryFind target |> Option.defaultValue []
                            | None        -> [])
                    let weights = decoded |> List.map snd
                    let tuples = decoded |> List.map fst |> List.toArray
                    let attrKeys = j.AttributeKeys
                    Some (fun (r: Rng) ->
                        let idx, r' = weightedIndex weights r
                        let parts = tuples.[min idx (tuples.Length - 1)]
                        let assignment =
                            attrKeys
                            |> List.mapi (fun p attrKey ->
                                let pool = posPools.[p]
                                let rank = rankMaps.[p] |> Map.tryFind parts.[p] |> Option.defaultValue 0
                                let v = if List.isEmpty pool then "" else pool.[min rank (List.length pool - 1)]
                                attrKey, v)
                            |> List.filter (fun (_, v) -> v <> "")
                            |> Map.ofList
                        assignment, r'))
        // NM-21 — name the unsatisfiable FKs once per kind (row-independent):
        // a non-nullable FK column whose target PK pool is empty is forced to
        // NULL below; σ now emits a `synthetic.fk.unsatisfiable` diagnostic so
        // the erasure is named in Core (a DryRun preview that never reaches the
        // load-time failure still surfaces it), never silent.
        let unsatisfiableFks =
            kind.Attributes
            |> List.choose (fun attr ->
                match Map.tryFind attr.SsKey fkByAttr with
                | Some target when
                        not attr.Column.IsNullable
                        && (pkPools |> Map.tryFind target |> Option.defaultValue [] |> List.isEmpty) ->
                    Some (SyntheticDiagnostic.unsatisfiableForeignKey kind.SsKey attr.SsKey target)
                | _ -> None)
        // pass-2 rng, salted off the PK-pool rng so FK / distribution draws
        // don't correlate with surrogate minting.
        let mutable state = rngOf (kindSeed master kind.SsKey ^^^ 0xD1B54A32D192ED03UL)
        let nextDraw () =
            let v, s = draw state in state <- s; v
        let rows =
          [ for i in 0 .. rowCount - 1 ->
            // §6.3 F5b — draw the row's correlated FK tuple ONCE (when a joint
            // exists); the joint FK columns below take their value from it.
            let jointAssignment =
                match jointDraw with
                | Some draw -> let m, s = draw state in state <- s; m
                | None      -> Map.empty
            let cells =
                kind.Attributes
                |> List.choose (fun attr ->
                    let attrHash = fnv1a (SsKey.serialize attr.SsKey)
                    let nullable = attr.Column.IsNullable
                    // §3 precedence: PK → FK → unique → distribution/default.
                    let raw =
                        if attr.IsPrimaryKey then
                            // step 1 — the surrogate from the kind's PK pool.
                            match List.tryItem i pkPool with
                            | Some v -> Some v
                            | None   -> Some (surrogateRaw attr.Type kindHash i)
                        elif Map.containsKey attr.SsKey fkByAttr then
                            // step 2 — draw from the parent pool (zero orphans).
                            let target = fkByAttr.[attr.SsKey]
                            let pool = pkPools |> Map.tryFind target |> Option.defaultValue []
                            let nulled, s = drawsNull profile attr.SsKey state
                            state <- s
                            if nulled && nullable then None
                            elif List.isEmpty pool then
                                // empty parent pool — NULL. For a non-nullable FK
                                // this is an unsatisfiable structure: NM-21 names it
                                // via the `unsatisfiableFks` diagnostics computed
                                // above (`synthetic.fk.unsatisfiable`), so the load
                                // surfaces it but Core no longer emits it silently.
                                None
                            else
                                // §6.3 — precedence: a correlated JOINT value (F5b,
                                // drawn once for the row above) wins; else the
                                // selectivity-weighted skew (F5a); else uniform
                                // (byte-identical to pre-F5). The joint path already
                                // consumed its Rng draw, so it does not draw again here.
                                match Map.tryFind attr.SsKey jointAssignment with
                                | Some v -> Some v
                                | None ->
                                    match fkSelectivityWeights attr.SsKey (List.length pool) with
                                    | Some weights ->
                                        let j, s = weightedIndex weights state
                                        state <- s
                                        Some pool.[min j (List.length pool - 1)]
                                    | None ->
                                        let j = int (nextDraw () % uint64 (List.length pool))
                                        Some pool.[j]
                        else
                            let nulled, s = drawsNull profile attr.SsKey state
                            state <- s
                            if nulled && nullable then None
                            elif isSingleColumnUnique kind attr then
                                // step 3 — a distinct value per row.
                                Some ("u:" + attrHash.ToString("x8") + ":" + string i)
                            else
                                // step 4 — distribution sample / type default.
                                match Profile.tryFindCategorical attr.SsKey profile with
                                | Some cat when not (List.isEmpty cat.Frequencies) ->
                                    let mode = valueFidelityFor config (Name.value attr.Name) cat
                                    let v, s = sampleCategorical mode attrHash cat state
                                    state <- s
                                    Some v
                                | _ ->
                                    match Profile.tryFindNumeric attr.SsKey profile with
                                    | Some nd ->
                                        let v, s = sampleNumeric attr.Type nd state
                                        state <- s
                                        Some v
                                    | None ->
                                        Some (typeDefaultRaw attr.Type (nextDraw ()))
                    raw |> Option.map (fun v -> attr.Name, v))
            { Identifier =
                (match pkAttr, List.tryItem i pkPool with
                 | Some _, Some pkRaw ->
                     // a stable per-row identity rooted in the kind + PK value.
                     SsKey.synthesizedComposite "SYNTH_ROW" [ SsKey.rootOriginal kind.SsKey; pkRaw ]
                     |> function Ok k -> k | Error _ -> kind.SsKey
                 | _ ->
                     SsKey.synthesizedComposite "SYNTH_ROW" [ SsKey.rootOriginal kind.SsKey; string i ]
                     |> function Ok k -> k | Error _ -> kind.SsKey)
              Values = Map.ofList cells } ]
        rows, unsatisfiableFks

    /// The volume for a kind (design §7: "profiled `RowCount` per kind", with
    /// the `Scale` factor). `RowCount` is read from any of the kind's column
    /// profiles (they share the kind's row count); `0` when the kind is
    /// unprofiled (named — no evidence, no rows).
    let private rowCountFor (profile: Profile) (config: SyntheticConfig) (kind: Kind) : int =
        let observed =
            kind.Attributes
            |> List.choose (fun a -> Profile.tryFindColumn a.SsKey profile |> Option.map (fun c -> c.RowCount))
            |> function [] -> 0L | xs -> List.max xs
        // §6.2 — a per-kind VolumeTarget overrides the global Scale (slice F1);
        // absent, the observed × Scale default holds (byte-identical to pre-F1).
        match Map.tryFind kind.SsKey config.VolumeByKind with
        | Some (VolumeTarget.Absolute rows)     -> max 0 rows
        | Some (VolumeTarget.Multiplier factor) -> max 0 (int (System.Decimal.Truncate (decimal observed * factor)))
        | None                                  -> max 0 (int (System.Decimal.Truncate (decimal observed * config.Scale)))

    /// Pass 1 — mint every kind's PK pool (raw PK values, one per generated
    /// row). PKs are independent of FKs, so this completes before any row is
    /// filled and makes FK integrity hold for cycle FKs (design §8).
    let private mintPkPools
        (catalog: Catalog)
        (profile: Profile)
        (config: SyntheticConfig)
        (master: uint64)
        : Map<SsKey, string list> =
        Catalog.allKinds catalog
        |> List.map (fun kind ->
            let n = rowCountFor profile config kind
            let kindHash = fnv1a (SsKey.serialize kind.SsKey)
            let pool =
                match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey) with
                | None -> List.replicate n ""   // PK-less kind: no FK-referenceable pool.
                | Some pk -> [ for i in 0 .. n - 1 -> surrogateRaw pk.Type kindHash i ]
            kind.SsKey, pool)
        |> Map.ofList

    /// `σ` with its lineage — generate the synthetic dataset (design §8, slice
    /// S1) AND the named diagnostics σ raised. Pure; `seed`-deterministic;
    /// FK-aware; hybrid-by-cardinality; drift-by-SsKey. The result keys are
    /// kind `SsKey`s; values are rows in `RawValueCodec` raw-string form for the
    /// load to render via `SqlLiteral`.
    ///
    /// NM-21 — the diagnostics list names every `synthetic.fk.unsatisfiable`
    /// event: a non-nullable FK column forced to NULL because its parent pool is
    /// empty. The unsatisfiable structure is now named in Core (visible even on a
    /// DryRun preview that never reaches the load-time failure), never a silent
    /// NULL. Diagnostics are ordered by kind (catalog order), then by attribute
    /// declaration order — deterministic by construction.
    let generateWithDiagnostics
        (catalog: Catalog)
        (profile: Profile)
        (config: SyntheticConfig)
        (seed: uint64)
        : Map<SsKey, StaticRow list> * SyntheticDiagnostic list =
        let pkPools = mintPkPools catalog profile config seed
        let perKind =
            Catalog.allKinds catalog
            |> List.map (fun kind ->
                let rows, diags = generateKindRows catalog profile config seed pkPools kind
                kind.SsKey, rows, diags)
        let dataset = perKind |> List.map (fun (k, rows, _) -> k, rows) |> Map.ofList
        let diagnostics = perKind |> List.collect (fun (_, _, diags) -> diags)
        dataset, diagnostics

    /// `σ` — generate the synthetic dataset (design §8, slice S1). Pure;
    /// `seed`-deterministic; FK-aware; hybrid-by-cardinality; drift-by-SsKey.
    /// The result keys are kind `SsKey`s; values are rows in `RawValueCodec`
    /// raw-string form for the load to render via `SqlLiteral`.
    ///
    /// The dataset-only projection of `generateWithDiagnostics`; callers that
    /// need the NM-21 `synthetic.fk.unsatisfiable` lineage use that form.
    let generate
        (catalog: Catalog)
        (profile: Profile)
        (config: SyntheticConfig)
        (seed: uint64)
        : Map<SsKey, StaticRow list> =
        generateWithDiagnostics catalog profile config seed |> fst

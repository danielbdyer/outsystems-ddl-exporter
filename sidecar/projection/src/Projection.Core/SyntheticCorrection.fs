namespace Projection.Core

/// THE_SYNTHETIC_DATA_FUZZING.md §2 — the blessed *correction artifact*'s Core
/// carrier (slice F0). A durable, operator-blessed override/intent layer on top
/// of the captured `Profile` / default `SyntheticConfig`. Each entry is a NAMED
/// divergence from naive fidelity — the synthesis sibling of the RefactorLog
/// (durable schema-intent) and the `Tolerance` registry (named, accepted
/// divergences). Keyed by `SsKey` (drift-stable; A1 survives rename — NEVER a
/// name lookup), so a blessed correction follows its column across a rename.
///
/// **Why it exists.** v1 synthesis (`THE_SYNTHETIC_DATA_DESIGN.md`) is a
/// *faithful* section of profiling (`π ∘ σ ≈ id`): it reproduces the source's
/// skew. The correction artifact records the operator's deliberate, governed
/// departures from that faithfulness (PII typing, fidelity overrides; coverage /
/// rotation / Faker land in later slices), turning the theorem into
/// `π ∘ σ ≈ id MODULO the blessed Correction` — the engine's core-adjunction
/// shape ("identity, modulo named, closed erasures") applied to the section.
///
/// **F0 scope (IR grows under evidence).** The carrier ships the two entries
/// that map onto TODAY's `SyntheticConfig` consumer — `Pii` and `Fidelity` — so
/// the fold (`applyToConfig`) drives the EXISTING σ with ZERO σ change. The
/// remaining entries named in the design (`CoverageFloor`, `FakerFieldSet`,
/// `Volume`, `DistributionOverride`) land with their own slices, each with its
/// σ/boundary consumer. The durable codec + flow wiring + propose verb (the rest
/// of F0's surface) are the next increment (F0b).

/// §2.3 / §5 — explicit PII classification for a column. Supersedes the coarse
/// hybrid-by-cardinality proxy (`SyntheticData.valueFidelityFor`). `None` = the
/// column is not PII. The fine-grained kinds drive the boundary Faker
/// realization (slice F2); F0 consumes only the is-PII-ness (any kind other than
/// `None` ⇒ synthesize fresh tokens, never a real value — the privacy contract).
[<RequireQualifiedAccess>]
type PiiKind =
    | None
    | Email
    | PersonName
    | Phone
    | Address
    | FreeText
    | Reference

/// §2.3 — one NAMED correction entry. Closed DU; grows under evidence (a new
/// variant lands with its σ/boundary consumer, never speculatively).
[<RequireQualifiedAccess>]
type CorrectionEntry =
    /// Explicit PII typing for a column → drives Synthesize / Faker realization.
    | Pii of column: SsKey * kind: PiiKind
    /// Operator override of a column's value-fidelity mode (Preserve / Synthesize).
    | Fidelity of column: SsKey * mode: ValueFidelityMode
    /// §6.2 (slice F1) — a per-KIND volume target (absolute / multiplier),
    /// generating at arbitrary scale decoupled from the source corpus size.
    | Volume of kind: SsKey * target: VolumeTarget

/// §2 — the blessed correction artifact: a set of named correction entries
/// layered onto the captured `Profile` / default `SyntheticConfig`. Smart-
/// constructed (private case): a column carries at most one entry in any one
/// *conflict class*, so the blessed intent on an axis is unambiguous — a
/// conflicting double-correction is a named refusal, never a last-write-wins
/// silent pick.
type Correction =
    private { Entries : CorrectionEntry list }

[<RequireQualifiedAccess>]
module Correction =

    /// The empty correction — the identity of `applyToConfig` (a config folded
    /// through `empty` is byte-unchanged: blessing nothing changes nothing).
    let empty : Correction = { Entries = [] }

    /// The conflict CLASS + column an entry occupies. Entries sharing a (class,
    /// column) conflict. `Pii` and `Fidelity` share the FIDELITY class — both
    /// determine the per-column value-fidelity decision, so a column may carry
    /// one OR the other, not both. Future axes (coverage, volume) get their own
    /// class, so they never conflict with a fidelity correction on the same column.
    let private conflictKey (entry: CorrectionEntry) : string * SsKey =
        match entry with
        | CorrectionEntry.Pii (col, _)      -> "fidelity", col
        | CorrectionEntry.Fidelity (col, _) -> "fidelity", col
        // Volume is keyed by KIND, in its own class — it never conflicts with a
        // fidelity correction on a column (different class AND different SsKey space).
        | CorrectionEntry.Volume (kind, _)  -> "volume", kind

    /// Smart constructor. Refuses a conflicting double-correction (two entries in
    /// the same conflict class for one column); a blessed artifact's intent must
    /// be unambiguous. Order-independent: the entries are a set of decisions, not
    /// a sequence.
    let create (entries: CorrectionEntry list) : Result<Correction> =
        let conflicts =
            entries
            |> List.map conflictKey
            |> List.groupBy id
            |> List.choose (fun (key, group) -> if List.length group > 1 then Some key else Option.None)
        match conflicts with
        | [] -> Result.success { Entries = entries }
        | (axis, col) :: _ ->
            Result.failureOf
                (ValidationError.create
                    "synthetic.correction.conflict"
                    (String.concat "" [
                        "conflicting "; axis; " corrections for column "; SsKey.serialize col
                        " — a column carries at most one correction per axis "
                        "(a blessed artifact's intent must be unambiguous)" ]))

    /// The entries, in construction order (a stable projection for codecs /
    /// diagnostics; the SEMANTICS are order-independent — see `applyToConfig`).
    let entries (correction: Correction) : CorrectionEntry list = correction.Entries

    let isEmpty (correction: Correction) : bool = List.isEmpty correction.Entries

    /// §2.3 — fold the blessed corrections onto a default `SyntheticConfig`, the
    /// PURE hinge `Profile ⊕ Correction` (the corpus stays on the π side, so σ's
    /// T1 determinism is untouched). F0 translates the two fidelity-class entries
    /// onto the config's `Preserve`/`Synthesize` column sets (resolving each
    /// `SsKey` → its logical `Name` by the drift-stable catalog join):
    ///   • `Pii (_, kind)` with `kind ≠ None` ⇒ Synthesize (never a real PII value).
    ///   • `Fidelity (_, Synthesize | Preserve)` ⇒ the named set, verbatim.
    /// A column whose `SsKey` resolves to no attribute (B-only / stale) is a
    /// no-op — the correction simply does not bind (drift-by-SsKey, design §6).
    /// Order-independent: the smart ctor forbids same-class collisions, so no
    /// column lands in both sets. `applyToConfig empty` is the identity.
    let applyToConfig (catalog: Catalog) (correction: Correction) (config: SyntheticConfig) : SyntheticConfig =
        let nameOf (col: SsKey) : string option =
            Catalog.allKinds catalog
            |> List.tryPick (fun k ->
                k.Attributes
                |> List.tryFind (fun a -> a.SsKey = col)
                |> Option.map (fun a -> Name.value a.Name))
        correction.Entries
        |> List.fold (fun (cfg: SyntheticConfig) entry ->
            match entry with
            | CorrectionEntry.Pii (col, kind) ->
                match kind with
                | PiiKind.None -> cfg
                | _ ->
                    match nameOf col with
                    | Some name -> { cfg with SynthesizeColumns = Set.add name cfg.SynthesizeColumns }
                    | Option.None -> cfg
            | CorrectionEntry.Fidelity (col, mode) ->
                match nameOf col with
                | Option.None -> cfg
                | Some name ->
                    match mode with
                    | ValueFidelityMode.Synthesize -> { cfg with SynthesizeColumns = Set.add name cfg.SynthesizeColumns }
                    | ValueFidelityMode.Preserve   -> { cfg with PreserveColumns   = Set.add name cfg.PreserveColumns }
            | CorrectionEntry.Volume (kind, target) ->
                // §6.2 — keyed by KIND SsKey; rowCountFor consults it directly (no
                // Name resolution). A kind not in the catalog simply never generates,
                // so a stale Volume target is inert (drift-by-SsKey).
                { cfg with VolumeByKind = Map.add kind target cfg.VolumeByKind }) config

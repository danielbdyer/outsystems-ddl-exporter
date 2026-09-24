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

/// FUZZING §2.3 / §5 (slice F-Faker) — a human-authorable LOGICAL address for a
/// column: `(module, entity, attribute)` BY NAME. The operator-facing sibling of
/// the opaque `SsKey` (an OSSYS attribute is an `OssysOriginal` GUID — not
/// hand-authorable), so a blessed Faker binding can name the SPECIFIC location an
/// operator reads in the model. Resolved against the catalog (case-insensitive,
/// refusing ambiguity) to the `SsKey` at the realization boundary; a rename that
/// breaks the coordinate is a NAMED refusal (the synthetic flow refuses before
/// generating), never a silent miss — the operator re-points the binding.
type AttributeCoordinate =
    { Module : string; Entity : string; Attribute : string }

[<RequireQualifiedAccess>]
module AttributeCoordinate =

    let create (m: string) (e: string) (a: string) : AttributeCoordinate =
        { Module = m; Entity = e; Attribute = a }

    let private ciEq (a: string) (b: string) : bool =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    /// Resolve a coordinate to `(owning kind SsKey, attribute Name, attribute
    /// SsKey)` against the catalog (case-insensitive on each segment). Exactly one
    /// match → `Ok`; zero → a named not-found refusal; more than one → a named
    /// ambiguity refusal. A TOTAL decision — every outcome is named, never a
    /// silent best-guess. The one matching primitive `resolve`/`resolveColumn`
    /// share, so the named refusals live in one place.
    let resolveFull (catalog: Catalog) (coord: AttributeCoordinate) : Result<SsKey * Name * SsKey> =
        let matches =
            Catalog.allModulesKinds catalog
            |> List.collect (fun (m, k) ->
                if ciEq (Name.value m.Name) coord.Module && ciEq (Name.value k.Name) coord.Entity then
                    k.Attributes
                    |> List.filter (fun a -> ciEq (Name.value a.Name) coord.Attribute)
                    |> List.map (fun a -> k.SsKey, a.Name, a.SsKey)
                else [])
        match matches with
        | [ one ] -> Result.success one
        | [] ->
            Result.failureOf
                (ValidationError.create "synthetic.coordinate.notFound"
                    (sprintf "no attribute at %s/%s/%s — the blessed Faker binding names a location not in the model (a rename or typo); re-point it." coord.Module coord.Entity coord.Attribute))  // LINT-ALLOW: terminal operator-facing diagnostic naming the unresolved coordinate at the ValidationError boundary; no structured / typed-AST artifact applies to a free-text refusal reason
        | many ->
            Result.failureOf
                (ValidationError.create "synthetic.coordinate.ambiguous"
                    (sprintf "%d attributes match %s/%s/%s — the coordinate is ambiguous (duplicate names across the resolved scope); narrow the model scope." (List.length many) coord.Module coord.Entity coord.Attribute))  // LINT-ALLOW: terminal operator-facing diagnostic naming the ambiguous coordinate at the ValidationError boundary; no structured / typed-AST artifact applies to a free-text refusal reason

    /// Resolve a coordinate to the attribute's `SsKey` (the not-found / ambiguous
    /// refusals named by `resolveFull`).
    let resolve (catalog: Catalog) (coord: AttributeCoordinate) : Result<SsKey> =
        resolveFull catalog coord |> Result.map (fun (_, _, attrKey) -> attrKey)

    /// Resolve a coordinate to `(owning kind SsKey, attribute Name)` — what the
    /// boundary realization needs to rewrite the right kind's rows by column name.
    /// `None` when the coordinate does not resolve (the synthetic flow already
    /// refused that BY NAME via `unresolvedFakerCoordinates`, so a `None` here is
    /// the defensive backstop, never the surfaced path).
    let resolveColumn (catalog: Catalog) (coord: AttributeCoordinate) : (SsKey * Name) option =
        match resolveFull catalog coord with
        | Ok (kindKey, attrName, _) -> Some (kindKey, attrName)
        | Error _ -> Option.None

/// FUZZING §5 (slice F-Faker) — a format-preserving MASK rule over the σ-emitted
/// (Preserved) value: the real value is OBSCURED in place, keeping its shape.
/// Distinct from fresh-fake replacement — `Redact`/`Hash` are privacy-safe;
/// `KeepLast`/`KeepFirst` reveal a fragment (the operator's explicit choice).
[<RequireQualifiedAccess>]
type MaskRule =
    /// Every character → `*`.
    | Redact
    /// Mask all but the last `n` characters (e.g. `***-**-1234`).
    | KeepLast of n: int
    /// Mask all but the first `n` characters.
    | KeepFirst of n: int
    /// A deterministic short hex digest of the value (privacy-safe; stable).
    | Hash

/// FUZZING §2.3 / §5 (slice F-Faker) — the wide, tunable Faker generator catalog.
/// Each variant names a Bogus dataset (or a mask / a constant override) the
/// boundary realization (`FakerRealization`, OUTSIDE Core) interprets. Core only
/// CARRIES the description (T1 — no Bogus, no RNG here). Closed; grows under
/// evidence. Every variant except `Mask` realizes over a SYNTHESIZED token (the
/// privacy substrate — σ never emits a real value); `Mask` realizes over the
/// PRESERVED real value (it has something to obscure).
[<RequireQualifiedAccess>]
type FakerGenerator =
    // person
    | FullName | FirstName | LastName | UserName
    | Email | Phone
    // address
    | StreetAddress | City | ZipCode | Country | FullAddress
    // company
    | Company | JobTitle
    // internet
    | Url | DomainName
    // text
    | Word | Sentence | Paragraph
    // identifiers / numbers / dates (tunable shape)
    | Guid
    | IntBetween of lo: int * hi: int
    | DecimalBetween of lo: decimal * hi: decimal
    | PastDate | FutureDate
    // privacy / override
    | Mask of MaskRule
    | Constant of value: string

/// FUZZING §5 — the tunable Faker spec bound to a column location: the generator
/// plus an optional Bogus locale (e.g. `"en"`, `"de"`). `None` locale = the
/// default (`"en"`). The "various tunable degrees of resolution/configuration/
/// shape" the operator named: the generator selects the dataset, its parameters
/// (ranges, mask rule, constant) tune the shape, the locale tunes the register.
type FakerSpec =
    { Generator : FakerGenerator; Locale : string option }

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
    /// FUZZING §5 (slice F-Faker) — a COORDINATE-addressed Faker binding: realize
    /// this column-location's cells via the tunable `FakerSpec`. Keyed by the
    /// human-authorable `(module, entity, attribute)` coordinate (the operator
    /// hand-authors it by NAME; resolved to `SsKey` against the catalog at load).
    | Faker of location: AttributeCoordinate * spec: FakerSpec

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

    /// The conflict CLASS an entry occupies — entries sharing a key conflict (a
    /// location carries at most one entry per axis, so the blessed intent is
    /// unambiguous). `Pii` and `Fidelity` share the FIDELITY class on the column
    /// `SsKey` — a column may carry one OR the other, not both. `Volume` keys by
    /// KIND in its own class. `Faker` keys by its COORDINATE in its own class — a
    /// coordinate that RESOLVES to a fidelity-corrected column is a separate
    /// keying the (catalog-free) ctor cannot see; the realization applies Faker
    /// AFTER Pii, so the more-specific Faker wins (named, never silent).
    [<RequireQualifiedAccess>]
    type private ConflictKey =
        | OnColumn of axis: string * column: SsKey
        | OnCoordinate of coord: AttributeCoordinate

    let private conflictKey (entry: CorrectionEntry) : ConflictKey =
        match entry with
        | CorrectionEntry.Pii (col, _)      -> ConflictKey.OnColumn ("fidelity", col)
        | CorrectionEntry.Fidelity (col, _) -> ConflictKey.OnColumn ("fidelity", col)
        | CorrectionEntry.Volume (kind, _)  -> ConflictKey.OnColumn ("volume", kind)
        | CorrectionEntry.Faker (loc, _)    -> ConflictKey.OnCoordinate loc

    /// Smart constructor. Refuses a conflicting double-correction (two entries in
    /// the same conflict class); a blessed artifact's intent must be unambiguous.
    /// Order-independent: the entries are a set of decisions, not a sequence.
    let create (entries: CorrectionEntry list) : Result<Correction> =
        let conflicts =
            entries
            |> List.map conflictKey
            |> List.groupBy id
            |> List.choose (fun (key, group) -> if List.length group > 1 then Some key else Option.None)
        match conflicts with
        | [] -> Result.success { Entries = entries }
        | key :: _ ->
            let detail =
                match key with
                | ConflictKey.OnColumn (axis, col) ->
                    sprintf "conflicting %s corrections for column %s — a column carries at most one correction per axis (a blessed artifact's intent must be unambiguous)" axis (SsKey.serialize col)  // LINT-ALLOW: terminal operator-facing diagnostic text at the ValidationError message boundary; no structured / SQL / typed-AST artifact applies to a free-text refusal reason
                | ConflictKey.OnCoordinate c ->
                    sprintf "conflicting Faker corrections for %s/%s/%s — a location carries at most one Faker binding (a blessed artifact's intent must be unambiguous)" c.Module c.Entity c.Attribute  // LINT-ALLOW: terminal operator-facing diagnostic text at the ValidationError message boundary; no structured / SQL / typed-AST artifact applies to a free-text refusal reason
            Result.failureOf (ValidationError.create "synthetic.correction.conflict" detail)

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
                { cfg with VolumeByKind = Map.add kind target cfg.VolumeByKind }
            | CorrectionEntry.Faker (loc, spec) ->
                // FUZZING §5 — resolve the coordinate → SsKey → Name and set the σ
                // SUBSTRATE the boundary realization rewrites: a `Mask` generator
                // obscures σ's REAL value → Preserve; every other generator
                // overwrites a fresh token → Synthesize (the privacy substrate, so
                // no real value reaches the realization for a fresh fake). Inert if
                // the coordinate does not resolve — but the synthetic flow refuses
                // BY NAME on an unresolved coordinate first (`unresolvedFaker
                // Coordinates`), so this is a defensive backstop, not the surfaced path.
                match AttributeCoordinate.resolve catalog loc with
                | Error _ -> cfg
                | Ok ssKey ->
                    match nameOf ssKey with
                    | Option.None -> cfg
                    | Some name ->
                        match spec.Generator with
                        | FakerGenerator.Mask _ -> { cfg with PreserveColumns   = Set.add name cfg.PreserveColumns }
                        | _                     -> { cfg with SynthesizeColumns = Set.add name cfg.SynthesizeColumns }) config

    /// FUZZING §5 — the Faker coordinates that DO NOT resolve against the catalog
    /// (a rename or a typo). The synthetic flow refuses BY NAME when this is
    /// non-empty: a blessed Faker binding that names a location not in the model is
    /// an operator error to surface, never a silent no-op (the
    /// hand-authored-coordinate analogue of "refuse rather than corrupt"). Empty
    /// for a correction with no Faker entries (byte-identical default).
    let unresolvedFakerCoordinates (catalog: Catalog) (correction: Correction) : AttributeCoordinate list =
        correction.Entries
        |> List.choose (function
            | CorrectionEntry.Faker (loc, _) ->
                match AttributeCoordinate.resolve catalog loc with
                | Ok _    -> Option.None
                | Error _ -> Some loc
            | _ -> Option.None)


/// THE_SYNTHETIC_DATA_FUZZING.md §2.2 (slice F0c-propose) — propose a FIRST-DRAFT
/// `Correction` from the catalog: heuristic PII typing by column-name stem, for the
/// operator to review / fine-tune / BLESS. Pure (no I/O, no clock); the durable
/// artifact and the CLI `synth-correct` verb that writes it are the remaining F0c
/// I/O work. Conservative by design — it UNDER-claims (only well-known PII stems
/// classify; everything else stays unclassified), so the operator ADDS what the
/// heuristic misses rather than having to UNDO over-claims. The blessed artifact
/// is the operator's, never the heuristic's.
[<RequireQualifiedAccess>]
module CorrectionProposer =

    /// Lowercased-substring → `PiiKind`, most-specific first. Only canonical PII
    /// column-name stems classify; an unmatched name is `PiiKind.None` (no entry).
    let private classify (logical: string) : PiiKind =
        let n = logical.ToLowerInvariant()
        let has (sub: string) : bool = n.Contains sub
        if   has "email" || has "e_mail" then PiiKind.Email
        elif has "phone" || has "mobile" then PiiKind.Phone
        elif has "address" || has "street" || has "postal" || has "zip" then PiiKind.Address
        elif has "firstname" || has "lastname" || has "fullname" || has "surname" || has "givenname" then PiiKind.PersonName
        else PiiKind.None

    /// Propose the PII-typing corrections for a catalog — one `Pii` entry per
    /// attribute whose name matches a PII stem. Attribute `SsKey`s are distinct,
    /// so the smart ctor always succeeds (the `Error` arm is unreachable; the
    /// defensive `empty` keeps the proposer total).
    let propose (catalog: Catalog) : Correction =
        let entries =
            Catalog.allKinds catalog
            |> List.collect (fun k -> k.Attributes)
            |> List.choose (fun a ->
                match classify (Name.value a.Name) with
                | PiiKind.None -> Option.None
                | kind         -> Some (CorrectionEntry.Pii (a.SsKey, kind)))
        match Correction.create entries with
        | Ok c    -> c
        | Error _ -> Correction.empty

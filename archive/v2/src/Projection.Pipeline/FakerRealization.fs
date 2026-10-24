namespace Projection.Pipeline

// LINT-ALLOW-FILE: Faker boundary realization — Bogus (RNG-based) lives OUTSIDE
//   Core (T1). This post-σ pass rewrites PII-typed columns to coherent realistic
//   values, seeded deterministically per row identity. The "string work" here is
//   realistic-value GENERATION at the realization boundary, not SQL/structure
//   emission — the typed-AST / built-in-obligation disciplines do not apply.
// LINT-ALLOW-FILE-MUTATION: the FNV seed accumulator + the per-row Bogus global
//   seed set are realization-layer mutation; synthesis realization is sequential,
//   so the set-then-construct is deterministic per row.

open Projection.Core

/// THE_SYNTHETIC_DATA_FUZZING.md §5 (slice F2) — the Faker boundary realization.
/// σ (Core) emits deterministic placeholder tokens for PII-typed columns (it never
/// emits a real value — the privacy contract); this pass, OUTSIDE Core, rewrites
/// those columns to coherent realistic values via Bogus, **seeded from the ROW's
/// identity** so (a) the same row yields ONE consistent fake person across its PII
/// columns (referential consistency — the email derives from the same person as the
/// name), and (b) the output is reproducible (same dataset → same fakes). Bogus
/// never enters Core, so σ's T1 determinism is untouched; this is the boundary the
/// design reserves for it.
[<RequireQualifiedAccess>]
module FakerRealization =

    /// FNV-1a continuation over a string — the streaming step both seed
    /// derivations share. PL-10 (S34): FNV-1a is a streaming hash, so
    /// `fnvContinue (fnvContinue init prefix) suffix` is bit-identical to
    /// hashing `prefix + suffix` — the per-row basis below continues per
    /// cell without re-hashing (or re-serializing) the row key, and every
    /// seed value is UNCHANGED (the deterministic-draw law).
    let private fnvContinue (state: uint32) (s: string) : uint32 =
        let mutable h = state
        for ch in s do h <- (h ^^^ uint32 (uint16 ch)) * 16777619u
        h

    [<Literal>]
    let private fnvInit = 2166136261u

    /// A stable, host-independent `int` seed from an `SsKey` (FNV-1a over the
    /// serialized key, truncated). Deterministic — the realization replays.
    let private seedOf (key: SsKey) : int =
        int (fnvContinue fnvInit (SsKey.serialize key))

    /// A deterministically-seeded `Bogus.Faker` for one row. The global
    /// `Randomizer.Seed` is set immediately before construction; synthesis
    /// realization is single-threaded, so the set-then-construct yields a faker
    /// whose generation depends only on this row's seed (reproducible).
    let private fakerOf (seed: int) : Bogus.Faker =
        Bogus.Randomizer.Seed <- System.Random(seed)
        Bogus.Faker()

    /// PiiKind → a coherent fake value from a row's faker. `f.Person` is one
    /// cached individual, so a row's Email / FullName / Phone / Address all
    /// describe the SAME person (referential consistency). `Reference` (a real
    /// reference value, preserved) and `None` are no-ops.
    let private fieldOf (f: Bogus.Faker) (kind: PiiKind) : string option =
        match kind with
        | PiiKind.Email      -> Some f.Person.Email
        | PiiKind.PersonName -> Some f.Person.FullName
        | PiiKind.Phone      -> Some f.Person.Phone
        | PiiKind.Address    -> Some f.Person.Address.Street
        | PiiKind.FreeText   -> Some (f.Lorem.Sentence())
        | PiiKind.Reference  -> None
        | PiiKind.None       -> None

    /// The PII-typed attribute keys from a Correction (skipping `None`).
    let private piiColumns (correction: Correction) : Map<SsKey, PiiKind> =
        Correction.entries correction
        |> List.choose (function
            | CorrectionEntry.Pii (col, kind) when kind <> PiiKind.None -> Some (col, kind)
            | _ -> None)
        |> Map.ofList

    /// Rewrite every PII-typed column's cell to a coherent fake value, seeded from
    /// the row's identity (referential consistency + determinism). Columns with no
    /// PII typing are untouched; a `Reference`/`None` typing is a no-op; a cell
    /// absent from the row (NULL) stays absent. Empty PII set → the dataset
    /// verbatim (byte-identical to no realization).
    let realizePii
        (catalog: Catalog)
        (correction: Correction)
        (dataset: Map<SsKey, StaticRow list>)
        : Map<SsKey, StaticRow list> =
        let pii = piiColumns correction
        if Map.isEmpty pii then dataset
        else
            // attribute → (logical Name = the StaticRow.Values key, PiiKind).
            let piiByName : (Name * PiiKind) list =
                Catalog.allKinds catalog
                |> List.collect (fun k -> k.Attributes)
                |> List.choose (fun a ->
                    match Map.tryFind a.SsKey pii with
                    | Some kind -> Some (a.Name, kind)
                    | None      -> Option.None)
            if List.isEmpty piiByName then dataset
            else
                dataset
                |> Map.map (fun _ rows ->
                    rows
                    |> List.map (fun row ->
                        let faker = fakerOf (seedOf row.Identifier)
                        let values =
                            piiByName
                            |> List.fold (fun (acc: Map<Name, string option>) (colName, kind) ->
                                if Map.containsKey colName acc then
                                    match fieldOf faker kind with
                                    | Some v      -> Map.add colName (Some v) acc
                                    | Option.None -> acc
                                else acc) row.Values
                        { row with Values = values }))

    // ======================================================================
    // FUZZING §5 (slice F-Faker) — the coordinate-addressed, tunable Faker
    // realization. The WIDE generator catalog, seeded per row identity, bound to
    // a column LOCATION the operator hand-authored by `(module, entity, attribute)`
    // coordinate. Person-based generators read one materialized `Bogus.Person`
    // per (row, locale) (referential consistency); fresh-draw generators re-seed
    // per (row, column) (order-independent); mask/constant are deterministic.
    // ======================================================================

    /// A locale-aware faker for one cell. The global `Randomizer.Seed` is set
    /// immediately before construction (single-threaded → deterministic). An
    /// unregistered locale falls back to the default — the locale is the
    /// operator's tuning knob, not a structural input.
    let private fakerOfLocale (locale: string) (seed: int) : Bogus.Faker =
        Bogus.Randomizer.Seed <- System.Random(seed)
        try Bogus.Faker(locale)
        with _ -> Bogus.Faker()

    /// The row's per-cell seed BASIS (PL-10/S34): the FNV-1a state over
    /// `serialize rowKey + " "`, bound once per row; each fresh-draw cell
    /// CONTINUES it over the column name. Bit-identical to the prior
    /// per-cell `serialize rowKey + " " + column` hash (streaming FNV),
    /// so each cell still depends only on (row, column), independent of
    /// the order columns realize in.
    let private rowSeedBasis (rowKey: SsKey) : uint32 =
        fnvContinue fnvInit (SsKey.serialize rowKey + " ")  // LINT-ALLOW: seed-basis text (the historical hashed byte sequence; changing the composition would change every seed)

    let private seedOfCellUsing (basis: uint32) (column: Name) : int =
        int (fnvContinue basis (Name.value column))

    let private localeOf (spec: FakerSpec) : string = defaultArg spec.Locale "en"

    /// A fixed reference date so `PastDate`/`FutureDate` are reproducible (NOT a
    /// clock read — Bogus's default `DateTime.Now` reference would make the
    /// realization non-reproducible; a fixed anchor keeps it seeded-deterministic).
    let private refDate = System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)

    let private inv = System.Globalization.CultureInfo.InvariantCulture

    /// FUZZING §5 — format-preserving masking of the σ-Preserved real value.
    let private applyMask (rule: MaskRule) (value: string) : string =
        let n = value.Length
        match rule with
        | MaskRule.Redact -> System.String('*', n)
        | MaskRule.KeepLast k ->
            if k <= 0 then System.String('*', n)
            elif k >= n then value
            else System.String('*', n - k) + value.Substring(n - k)
        | MaskRule.KeepFirst k ->
            if k <= 0 then System.String('*', n)
            elif k >= n then value
            else value.Substring(0, k) + System.String('*', n - k)
        | MaskRule.Hash ->
            let mutable h = 2166136261u
            for ch in value do h <- (h ^^^ uint32 (uint16 ch)) * 16777619u
            h.ToString("x8")

    /// Whether a generator reads the row's one coherent `Bogus.Person` (so its
    /// fields are referentially consistent across the row) vs. a fresh draw.
    let private isPersonBased (g: FakerGenerator) : bool =
        match g with
        | FakerGenerator.FullName | FakerGenerator.FirstName | FakerGenerator.LastName
        | FakerGenerator.UserName | FakerGenerator.Email | FakerGenerator.Phone
        | FakerGenerator.StreetAddress | FakerGenerator.City | FakerGenerator.ZipCode
        | FakerGenerator.FullAddress -> true
        | _ -> false

    /// A person-based generator → a field of the row's one materialized person.
    let private personField (p: Bogus.Person) (g: FakerGenerator) : string =
        match g with
        | FakerGenerator.FullName      -> p.FullName
        | FakerGenerator.FirstName     -> p.FirstName
        | FakerGenerator.LastName      -> p.LastName
        | FakerGenerator.UserName      -> p.UserName
        | FakerGenerator.Email         -> p.Email
        | FakerGenerator.Phone         -> p.Phone
        | FakerGenerator.StreetAddress -> p.Address.Street
        | FakerGenerator.City          -> p.Address.City
        | FakerGenerator.ZipCode       -> p.Address.ZipCode
        | FakerGenerator.FullAddress   -> sprintf "%s, %s %s" p.Address.Street p.Address.City p.Address.ZipCode
        | _                            -> p.FullName  // unreachable (isPersonBased gate); defensive

    /// A fresh-draw / mask / constant generator → the cell value. Self-contained:
    /// the faker is constructed, drawn, and discarded within this call (held
    /// across no other faker construction → Bogus global-seed-safe).
    let private freshOrValue (rowSeedBasis: uint32) (column: Name) (existing: string) (spec: FakerSpec) : string =
        match spec.Generator with
        | FakerGenerator.Constant v -> v
        | FakerGenerator.Mask rule  -> applyMask rule existing
        | g ->
            let f = fakerOfLocale (localeOf spec) (seedOfCellUsing rowSeedBasis column)
            match g with
            | FakerGenerator.Country    -> f.Address.Country()
            | FakerGenerator.Company    -> f.Company.CompanyName()
            | FakerGenerator.JobTitle   -> f.Name.JobTitle()
            | FakerGenerator.Url        -> f.Internet.Url()
            | FakerGenerator.DomainName -> f.Internet.DomainName()
            | FakerGenerator.Word       -> f.Lorem.Word()
            | FakerGenerator.Sentence   -> f.Lorem.Sentence()
            | FakerGenerator.Paragraph  -> f.Lorem.Paragraph()
            | FakerGenerator.Guid       -> (f.Random.Guid()).ToString()
            | FakerGenerator.IntBetween (lo, hi) ->
                let lo2, hi2 = if lo <= hi then lo, hi else hi, lo
                string (f.Random.Int(lo2, hi2))
            | FakerGenerator.DecimalBetween (lo, hi) ->
                let lo2, hi2 = if lo <= hi then lo, hi else hi, lo
                (f.Random.Decimal(lo2, hi2)).ToString(inv)
            | FakerGenerator.PastDate   -> (f.Date.Between(refDate.AddYears(-2), refDate)).ToString("yyyy-MM-dd HH:mm:ss", inv)
            | FakerGenerator.FutureDate -> (f.Date.Between(refDate, refDate.AddYears(2))).ToString("yyyy-MM-dd HH:mm:ss", inv)
            | _ -> existing  // unreachable (person/mask/constant handled above); defensive

    /// Realize one row's bound cells. Person-based groups process FIRST (each
    /// faker's person fully materialized before the next faker construction —
    /// Bogus's global `Randomizer.Seed` footgun); fresh-draw/value cells follow.
    let private realizeRow (rowKey: SsKey) (bindings: (Name * FakerSpec) list) (values: Map<Name, string option>) : Map<Name, string option> =
        let present = bindings |> List.filter (fun (n, _) -> Map.containsKey n values)
        if List.isEmpty present then values
        else
            // PL-10 (S34) — the row's seed and cell-seed basis bind ONCE
            // (the row key was previously re-serialized per locale group
            // and per fresh-draw cell); every seed value is unchanged.
            let rowSeed = seedOf rowKey
            let basis = rowSeedBasis rowKey
            let afterPerson =
                present
                |> List.filter (fun (_, s) -> isPersonBased s.Generator)
                |> List.groupBy (fun (_, s) -> localeOf s)
                |> List.fold (fun (acc: Map<Name, string option>) (locale, group) ->
                    let f = fakerOfLocale locale rowSeed
                    let p = f.Person  // materialize the coherent individual under the row seed
                    group |> List.fold (fun acc2 (colName, spec) ->
                        Map.add colName (Some (personField p spec.Generator)) acc2) acc) values
            present
            |> List.filter (fun (_, s) -> not (isPersonBased s.Generator))
            |> List.fold (fun (acc: Map<Name, string option>) (colName, spec) ->
                // A masked NULL masks the empty string — the pre-WP-3
                // behavior (NULL read as `""` upstream).
                let existing = acc.[colName] |> Option.defaultValue ""
                Map.add colName (Some (freshOrValue basis colName existing spec)) acc) afterPerson

    /// FUZZING §5 — the coordinate-addressed Faker pass. Resolve each `Faker`
    /// binding → (owning kind SsKey, column Name) against the catalog (the
    /// synthetic flow already refused unresolved coordinates BY NAME), then rewrite
    /// ONLY the owning kind's rows by column name. Empty Faker set → the dataset
    /// verbatim (byte-identical).
    let realizeFaker
        (catalog: Catalog)
        (correction: Correction)
        (dataset: Map<SsKey, StaticRow list>)
        : Map<SsKey, StaticRow list> =
        let bindings : (SsKey * Name * FakerSpec) list =
            Correction.entries correction
            |> List.choose (function
                | CorrectionEntry.Faker (loc, spec) ->
                    match AttributeCoordinate.resolveColumn catalog loc with
                    | Some (kindKey, attrName) -> Some (kindKey, attrName, spec)
                    | Option.None              -> Option.None
                | _ -> Option.None)
        if List.isEmpty bindings then dataset
        else
            let byKind = bindings |> List.groupBy (fun (k, _, _) -> k) |> Map.ofList
            dataset
            |> Map.map (fun kindKey rows ->
                match Map.tryFind kindKey byKind with
                | Option.None -> rows
                | Some kindBindings ->
                    let colSpecs = kindBindings |> List.map (fun (_, n, s) -> n, s)
                    rows |> List.map (fun row ->
                        { row with Values = realizeRow row.Identifier colSpecs row.Values }))

    /// The full boundary realization: the PiiKind pass (F2) THEN the coordinate
    /// -addressed Faker pass (F-Faker), so a column carrying BOTH a `Pii`
    /// correction and a `Faker` binding takes the more-specific Faker value
    /// (applied last). Identity when the correction has neither (byte-identical to
    /// the pre-F0c load — the π∘σ≈id canary's contract).
    let realize
        (catalog: Catalog)
        (correction: Correction)
        (dataset: Map<SsKey, StaticRow list>)
        : Map<SsKey, StaticRow list> =
        dataset
        |> realizePii catalog correction
        |> realizeFaker catalog correction

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

    /// A stable, host-independent `int` seed from an `SsKey` (FNV-1a over the
    /// serialized key, truncated). Deterministic — the realization replays.
    let private seedOf (key: SsKey) : int =
        let s = SsKey.serialize key
        let mutable h = 2166136261u
        for ch in s do h <- (h ^^^ uint32 (uint16 ch)) * 16777619u
        int h

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
                            |> List.fold (fun (acc: Map<Name, string>) (colName, kind) ->
                                if Map.containsKey colName acc then
                                    match fieldOf faker kind with
                                    | Some v      -> Map.add colName v acc
                                    | Option.None -> acc
                                else acc) row.Values
                        { row with Values = values }))

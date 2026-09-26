module Projection.Tests.SyntheticCorrectionTests

open Xunit
open Projection.Core
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// THE_SYNTHETIC_DATA_FUZZING.md §2 / §2.3 / §6 — slice F0, the blessed
// correction-artifact Core substrate. The smart-ctor conflict refusal, the
// pure `Profile ⊕ Correction` fold onto `SyntheticConfig` (PII typing +
// fidelity overrides → the existing Preserve/Synthesize sets, ZERO σ change),
// the empty-correction identity, drift-by-SsKey no-op, and order-independence.
// ---------------------------------------------------------------------------

let private name (s: string) : Name = Name.create s |> Result.value

let private mkOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun (e: ValidationError) -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture construction failed: %s" codes)

let private attr (key: SsKey) (logical: string) : Attribute =
    { Attribute.create key (name logical) Text with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) true |> Result.value }

let private emailKey  = attrKey ["C"; "Email"]
let private nameKey   = attrKey ["C"; "FullName"]
let private statusKey = attrKey ["C"; "Status"]
let private absentKey = attrKey ["C"; "Ghost"]   // deliberately NOT in the catalog

let private catalog : Catalog =
    let customer =
        Kind.create (kindKey ["C"]) (name "Customer") (mkTableId "dbo" "CUSTOMER")
            [ attr emailKey  "Email"
              attr nameKey   "FullName"
              attr statusKey "Status" ]
    Catalog.create [ mkModule (modKey "M") (name "M") [ customer ] ] [] |> mkOk

[<Fact>]
let ``F0/§2: an empty correction is the identity of applyToConfig`` () =
    let cfg = SyntheticConfig.defaultConfig
    let got = Correction.applyToConfig catalog Correction.empty cfg
    Assert.Equal<Set<string>>(cfg.SynthesizeColumns, got.SynthesizeColumns)
    Assert.Equal<Set<string>>(cfg.PreserveColumns, got.PreserveColumns)
    Assert.Equal(cfg.PreserveCardinalityMax, got.PreserveCardinalityMax)
    Assert.Equal(cfg.Scale, got.Scale)

[<Fact>]
let ``F0/§2.3: a PII typing (kind <> None) routes the column to Synthesize (never a real value)`` () =
    let corr = Correction.create [ CorrectionEntry.Pii (emailKey, PiiKind.Email) ] |> mkOk
    let got = Correction.applyToConfig catalog corr SyntheticConfig.defaultConfig
    Assert.Contains("Email", got.SynthesizeColumns)
    Assert.DoesNotContain("Email", got.PreserveColumns)

[<Fact>]
let ``F0/§2.3: PiiKind.None does not bind (the column is not PII)`` () =
    let corr = Correction.create [ CorrectionEntry.Pii (emailKey, PiiKind.None) ] |> mkOk
    let got = Correction.applyToConfig catalog corr SyntheticConfig.defaultConfig
    Assert.DoesNotContain("Email", got.SynthesizeColumns)
    Assert.DoesNotContain("Email", got.PreserveColumns)

[<Fact>]
let ``F0/§2.3: a Fidelity override routes Preserve/Synthesize to the named config sets`` () =
    let corr =
        Correction.create
            [ CorrectionEntry.Fidelity (statusKey, ValueFidelityMode.Preserve)
              CorrectionEntry.Fidelity (nameKey,   ValueFidelityMode.Synthesize) ]
        |> mkOk
    let got = Correction.applyToConfig catalog corr SyntheticConfig.defaultConfig
    Assert.Contains("Status", got.PreserveColumns)
    Assert.Contains("FullName", got.SynthesizeColumns)

[<Fact>]
let ``F0/§2: create refuses a conflicting double-correction in the fidelity class`` () =
    // Pii + Fidelity on the SAME column both determine fidelity → conflict.
    match Correction.create
              [ CorrectionEntry.Pii (emailKey, PiiKind.Email)
                CorrectionEntry.Fidelity (emailKey, ValueFidelityMode.Preserve) ] with
    | Ok _ -> Assert.Fail("expected a synthetic.correction.conflict refusal")
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "synthetic.correction.conflict")

[<Fact>]
let ``F0/§2: create accepts fidelity corrections on DISTINCT columns`` () =
    let r =
        Correction.create
            [ CorrectionEntry.Pii (emailKey, PiiKind.Email)
              CorrectionEntry.Fidelity (statusKey, ValueFidelityMode.Preserve) ]
    Assert.True((match r with Ok _ -> true | Error _ -> false))

[<Fact>]
let ``F0/§6: a correction whose SsKey is absent from the catalog is a no-op (drift-by-SsKey)`` () =
    let corr = Correction.create [ CorrectionEntry.Pii (absentKey, PiiKind.Email) ] |> mkOk
    let got = Correction.applyToConfig catalog corr SyntheticConfig.defaultConfig
    Assert.Empty(got.SynthesizeColumns)
    Assert.Empty(got.PreserveColumns)

[<Fact>]
let ``F0/§2.3: applyToConfig is order-independent (corrections are a set of decisions)`` () =
    let entries =
        [ CorrectionEntry.Pii (emailKey, PiiKind.Email)
          CorrectionEntry.Fidelity (statusKey, ValueFidelityMode.Preserve) ]
    let got1 = Correction.applyToConfig catalog (Correction.create entries |> mkOk) SyntheticConfig.defaultConfig
    let got2 = Correction.applyToConfig catalog (Correction.create (List.rev entries) |> mkOk) SyntheticConfig.defaultConfig
    Assert.Equal<Set<string>>(got1.SynthesizeColumns, got2.SynthesizeColumns)
    Assert.Equal<Set<string>>(got1.PreserveColumns, got2.PreserveColumns)

[<Fact>]
let ``F1/§6.2: a Volume correction lands in VolumeByKind (keyed by kind, no name resolution)`` () =
    let k = kindKey ["C"]
    let corr = Correction.create [ CorrectionEntry.Volume (k, VolumeTarget.Absolute 500) ] |> mkOk
    let got = Correction.applyToConfig catalog corr SyntheticConfig.defaultConfig
    match Map.tryFind k got.VolumeByKind with
    | Some (VolumeTarget.Absolute 500) -> ()
    | other -> Assert.Fail(sprintf "expected Some (Absolute 500), got %A" other)

[<Fact>]
let ``F1/§2: two Volume corrections for one kind conflict`` () =
    let k = kindKey ["C"]
    match Correction.create
              [ CorrectionEntry.Volume (k, VolumeTarget.Absolute 1)
                CorrectionEntry.Volume (k, VolumeTarget.Multiplier 2M) ] with
    | Ok _ -> Assert.Fail("expected a synthetic.correction.conflict refusal")
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "synthetic.correction.conflict")

[<Fact>]
let ``F1/§2: a Volume (kind) and a fidelity (column) correction on the same SsKey do NOT conflict (distinct classes)`` () =
    let r =
        Correction.create
            [ CorrectionEntry.Volume (emailKey, VolumeTarget.Absolute 10)
              CorrectionEntry.Pii (emailKey, PiiKind.Email) ]
    Assert.True((match r with Ok _ -> true | Error _ -> false))

[<Fact>]
let ``F0c-propose: heuristic PII typing classifies known stems and leaves the rest unclassified`` () =
    // The fixture catalog has Email (→ Email), FullName (→ PersonName), Status (→ none).
    let entries = Correction.entries (CorrectionProposer.propose catalog)
    Assert.Contains(entries, fun e -> e = CorrectionEntry.Pii (emailKey, PiiKind.Email))
    Assert.Contains(entries, fun e -> e = CorrectionEntry.Pii (nameKey, PiiKind.PersonName))
    Assert.DoesNotContain(entries, fun e -> match e with CorrectionEntry.Pii (k, _) -> k = statusKey | _ -> false)

[<Fact>]
let ``F0c-propose: a proposed correction drives Synthesize for the typed PII columns`` () =
    let cfg = Correction.applyToConfig catalog (CorrectionProposer.propose catalog) SyntheticConfig.defaultConfig
    Assert.Contains("Email", cfg.SynthesizeColumns)
    Assert.Contains("FullName", cfg.SynthesizeColumns)
    Assert.DoesNotContain("Status", cfg.SynthesizeColumns)

// -- F-Faker: coordinate addressing + the Faker correction entry --------------

let private coord m e a = AttributeCoordinate.create m e a
let private fakerSpec g : FakerSpec = { Generator = g; Locale = Option.None }

[<Fact>]
let ``F-Faker: a coordinate resolves to the attribute SsKey (case-insensitive)`` () =
    Assert.Equal(Ok emailKey, AttributeCoordinate.resolve catalog (coord "m" "customer" "email"))

[<Fact>]
let ``F-Faker: a coordinate naming no attribute is a named not-found refusal`` () =
    match AttributeCoordinate.resolve catalog (coord "M" "Customer" "Nope") with
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "synthetic.coordinate.notFound")
    | Ok _ -> Assert.Fail("expected a not-found refusal")

[<Fact>]
let ``F-Faker: a Faker (fresh-fake) binding routes its column to Synthesize (privacy substrate)`` () =
    let corr = Correction.create [ CorrectionEntry.Faker (coord "M" "Customer" "Email", fakerSpec FakerGenerator.Email) ] |> mkOk
    let got = Correction.applyToConfig catalog corr SyntheticConfig.defaultConfig
    Assert.Contains("Email", got.SynthesizeColumns)
    Assert.DoesNotContain("Email", got.PreserveColumns)

[<Fact>]
let ``F-Faker: a Faker MASK binding routes its column to Preserve (it obscures σ's real value)`` () =
    let corr = Correction.create [ CorrectionEntry.Faker (coord "M" "Customer" "Status", fakerSpec (FakerGenerator.Mask MaskRule.Redact)) ] |> mkOk
    let got = Correction.applyToConfig catalog corr SyntheticConfig.defaultConfig
    Assert.Contains("Status", got.PreserveColumns)
    Assert.DoesNotContain("Status", got.SynthesizeColumns)

[<Fact>]
let ``F-Faker: two Faker bindings on the same coordinate conflict`` () =
    match Correction.create
              [ CorrectionEntry.Faker (coord "M" "Customer" "Email", fakerSpec FakerGenerator.Email)
                CorrectionEntry.Faker (coord "M" "Customer" "Email", fakerSpec FakerGenerator.UserName) ] with
    | Ok _ -> Assert.Fail("expected a synthetic.correction.conflict refusal")
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "synthetic.correction.conflict")

[<Fact>]
let ``F-Faker: a Faker (coordinate) and a Pii (SsKey) on the same column do NOT conflict at the ctor`` () =
    // Different keying (coordinate vs SsKey) — the catalog-free ctor cannot see the
    // overlap; the realization applies Faker AFTER Pii (the more-specific wins, §5).
    let r =
        Correction.create
            [ CorrectionEntry.Pii (emailKey, PiiKind.Email)
              CorrectionEntry.Faker (coord "M" "Customer" "Email", fakerSpec FakerGenerator.Email) ]
    Assert.True((match r with Ok _ -> true | Error _ -> false))

[<Fact>]
let ``F-Faker: unresolvedFakerCoordinates names a coordinate that does not resolve, empty when all resolve`` () =
    let good = Correction.create [ CorrectionEntry.Faker (coord "M" "Customer" "Email", fakerSpec FakerGenerator.Email) ] |> mkOk
    Assert.Empty(Correction.unresolvedFakerCoordinates catalog good)
    let bad = Correction.create [ CorrectionEntry.Faker (coord "M" "Customer" "Ghost", fakerSpec FakerGenerator.Email) ] |> mkOk
    Assert.Equal<AttributeCoordinate list>([ coord "M" "Customer" "Ghost" ], Correction.unresolvedFakerCoordinates catalog bad)

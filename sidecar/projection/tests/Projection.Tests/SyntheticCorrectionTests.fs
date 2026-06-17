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

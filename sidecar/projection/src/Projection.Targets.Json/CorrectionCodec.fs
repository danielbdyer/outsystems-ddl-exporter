namespace Projection.Targets.Json

open System.Globalization
open System.Text.Json
open Projection.Core

/// Round-trippable `Correction ↔ JSON` codec — the durable, blessed correction
/// artifact (THE_SYNTHETIC_DATA_FUZZING.md §2.4, slice F0b). The propose step
/// (`synth-correct`) writes it; the operator reviews / edits / BLESSES it; the
/// synthetic flow reads it (`correction: file:<path>`). Sibling to `ProfileCodec`
/// — the `Profile` (fidelity evidence) and the `Correction` (blessed intent)
/// both persist by the same realize/ingest idiom.
///
/// **Total** — every `CorrectionEntry` variant + every `PiiKind` / `ValueFidelityMode`
/// arm is encoded (the inventory is the totality contract; a missed arm would be
/// a silent drop, forbidden). **Deterministic (T1)** — fixed write order, `SsKey`
/// via `SsKey.serialize`. **Re-validating (A39)** — decode rebuilds through
/// `Correction.create`, so a decoded artifact re-proves the
/// no-conflicting-double-correction invariant and surfaces a `Result<Correction>`
/// (a hand-edited artifact with a duplicated fidelity correction is REFUSED on
/// load, not silently last-write-wins).
///
/// **The universal law** (`∀ c. deserialize (serialize c) = Ok c`) is the codec
/// discipline's keystone, tested over a constructed-valid generator.
[<RequireQualifiedAccess>]
module CorrectionCodec =

    [<Literal>]
    let version : int = 1

    // ======================================================================
    // ENCODE
    // ======================================================================

    let private wSsKeyVal (jw: Utf8JsonWriter) (k: SsKey) : unit = jw.WriteStringValue (SsKey.serialize k)

    let private piiKindString (kind: PiiKind) : string =
        match kind with
        | PiiKind.None       -> "none"
        | PiiKind.Email      -> "email"
        | PiiKind.PersonName -> "personName"
        | PiiKind.Phone      -> "phone"
        | PiiKind.Address    -> "address"
        | PiiKind.FreeText   -> "freeText"
        | PiiKind.Reference  -> "reference"

    let private fidelityString (mode: ValueFidelityMode) : string =
        match mode with
        | ValueFidelityMode.Preserve   -> "preserve"
        | ValueFidelityMode.Synthesize -> "synthesize"

    let private inv (d: decimal) : string = JsonCodecKernel.inv d

    let private wVolumeTarget (jw: Utf8JsonWriter) (target: VolumeTarget) : unit =
        jw.WriteStartObject()
        match target with
        | VolumeTarget.Absolute rows ->
            jw.WriteString("target", "absolute")
            jw.WriteNumber("rows", rows)
        | VolumeTarget.Multiplier factor ->
            jw.WriteString("target", "multiplier")
            jw.WriteString("factor", inv factor)
        jw.WriteEndObject()

    // -- FUZZING §5 (slice F-Faker): the coordinate-addressed Faker binding ----

    let private wMaskRule (jw: Utf8JsonWriter) (rule: MaskRule) : unit =
        jw.WriteStartObject()
        match rule with
        | MaskRule.Redact      -> jw.WriteString("rule", "redact")
        | MaskRule.KeepLast n  -> jw.WriteString("rule", "keepLast");  jw.WriteNumber("n", n)
        | MaskRule.KeepFirst n -> jw.WriteString("rule", "keepFirst"); jw.WriteNumber("n", n)
        | MaskRule.Hash        -> jw.WriteString("rule", "hash")
        jw.WriteEndObject()

    let private wFakerSpec (jw: Utf8JsonWriter) (spec: FakerSpec) : unit =
        jw.WriteStartObject()
        (match spec.Generator with
         | FakerGenerator.FullName       -> jw.WriteString("generator", "fullName")
         | FakerGenerator.FirstName      -> jw.WriteString("generator", "firstName")
         | FakerGenerator.LastName       -> jw.WriteString("generator", "lastName")
         | FakerGenerator.UserName       -> jw.WriteString("generator", "userName")
         | FakerGenerator.Email          -> jw.WriteString("generator", "email")
         | FakerGenerator.Phone          -> jw.WriteString("generator", "phone")
         | FakerGenerator.StreetAddress  -> jw.WriteString("generator", "streetAddress")
         | FakerGenerator.City           -> jw.WriteString("generator", "city")
         | FakerGenerator.ZipCode        -> jw.WriteString("generator", "zipCode")
         | FakerGenerator.Country        -> jw.WriteString("generator", "country")
         | FakerGenerator.FullAddress    -> jw.WriteString("generator", "fullAddress")
         | FakerGenerator.Company        -> jw.WriteString("generator", "company")
         | FakerGenerator.JobTitle       -> jw.WriteString("generator", "jobTitle")
         | FakerGenerator.Url            -> jw.WriteString("generator", "url")
         | FakerGenerator.DomainName     -> jw.WriteString("generator", "domainName")
         | FakerGenerator.Word           -> jw.WriteString("generator", "word")
         | FakerGenerator.Sentence       -> jw.WriteString("generator", "sentence")
         | FakerGenerator.Paragraph      -> jw.WriteString("generator", "paragraph")
         | FakerGenerator.Guid           -> jw.WriteString("generator", "guid")
         | FakerGenerator.IntBetween (lo, hi) ->
             jw.WriteString("generator", "intBetween"); jw.WriteNumber("lo", lo); jw.WriteNumber("hi", hi)
         | FakerGenerator.DecimalBetween (lo, hi) ->
             jw.WriteString("generator", "decimalBetween"); jw.WriteString("lo", inv lo); jw.WriteString("hi", inv hi)
         | FakerGenerator.PastDate       -> jw.WriteString("generator", "pastDate")
         | FakerGenerator.FutureDate     -> jw.WriteString("generator", "futureDate")
         | FakerGenerator.Mask rule      -> jw.WriteString("generator", "mask"); jw.WritePropertyName "mask"; wMaskRule jw rule
         | FakerGenerator.Constant value -> jw.WriteString("generator", "constant"); jw.WriteString("value", value))
        (match spec.Locale with Some l -> jw.WriteString("locale", l) | None -> ())
        jw.WriteEndObject()

    let private wEntry (jw: Utf8JsonWriter) (entry: CorrectionEntry) : unit =
        jw.WriteStartObject()
        match entry with
        | CorrectionEntry.Pii (col, kind) ->
            jw.WriteString("entry", "pii")
            jw.WritePropertyName "column"
            wSsKeyVal jw col
            jw.WriteString("pii", piiKindString kind)
        | CorrectionEntry.Fidelity (col, mode) ->
            jw.WriteString("entry", "fidelity")
            jw.WritePropertyName "column"
            wSsKeyVal jw col
            jw.WriteString("mode", fidelityString mode)
        | CorrectionEntry.Volume (kind, target) ->
            jw.WriteString("entry", "volume")
            jw.WritePropertyName "kind"
            wSsKeyVal jw kind
            jw.WritePropertyName "target"
            wVolumeTarget jw target
        | CorrectionEntry.Faker (loc, spec) ->
            jw.WriteString("entry", "faker")
            jw.WriteString("module", loc.Module)
            jw.WriteString("entity", loc.Entity)
            jw.WriteString("attribute", loc.Attribute)
            jw.WritePropertyName "faker"
            wFakerSpec jw spec
        jw.WriteEndObject()

    let private wCorrection (jw: Utf8JsonWriter) (correction: Correction) : unit =
        jw.WriteStartObject()
        jw.WriteNumber("version", version)
        jw.WritePropertyName "entries"
        jw.WriteStartArray()
        for entry in Correction.entries correction do wEntry jw entry
        jw.WriteEndArray()
        jw.WriteEndObject()

    /// `Correction → JSON`. Deterministic (T1).
    let serialize (correction: Correction) : string =
        JsonWriting.writeToString (fun jw -> wCorrection jw correction)

    // ======================================================================
    // DECODE — rebuilt through `Correction.create` (A39 re-validation).
    // ======================================================================

    // Decode kernel — thin delegations to the shared `JsonCodecKernel` (prefix
    // `"correctionCodec"`), so the emitted error codes stay byte-identical.
    let private fail (code: string) (msg: string) : Result<'a> = JsonCodecKernel.fail code msg
    let private asString (el: JsonElement) : Result<string> = JsonCodecKernel.asString "correctionCodec" el
    let private prop (el: JsonElement) (name: string) : Result<JsonElement> = JsonCodecKernel.prop "correctionCodec" el name
    let private field (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a> = JsonCodecKernel.field "correctionCodec" el name read
    let private listField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a list> = JsonCodecKernel.listField "correctionCodec" el name read

    let private readSsKey (el: JsonElement) : Result<SsKey> =
        asString el |> Result.bind SsKey.deserialize

    let private readPiiKind (el: JsonElement) : Result<PiiKind> =
        asString el
        |> Result.bind (function
            | "none"       -> Ok PiiKind.None
            | "email"      -> Ok PiiKind.Email
            | "personName" -> Ok PiiKind.PersonName
            | "phone"      -> Ok PiiKind.Phone
            | "address"    -> Ok PiiKind.Address
            | "freeText"   -> Ok PiiKind.FreeText
            | "reference"  -> Ok PiiKind.Reference
            | o -> fail "correctionCodec.piiKind.unknown" (sprintf "unknown PiiKind '%s'" o))

    let private readFidelity (el: JsonElement) : Result<ValueFidelityMode> =
        asString el
        |> Result.bind (function
            | "preserve"   -> Ok ValueFidelityMode.Preserve
            | "synthesize" -> Ok ValueFidelityMode.Synthesize
            | o -> fail "correctionCodec.fidelity.unknown" (sprintf "unknown ValueFidelityMode '%s'" o))

    let private asInt (el: JsonElement) : Result<int> = JsonCodecKernel.asInt "correctionCodec" el
    let private asDecimal (el: JsonElement) : Result<decimal> = JsonCodecKernel.asDecimal "correctionCodec" el

    let private readVolumeTarget (el: JsonElement) : Result<VolumeTarget> =
        field el "target" asString
        |> Result.bind (function
            | "absolute"   -> field el "rows" asInt |> Result.map VolumeTarget.Absolute
            | "multiplier" -> field el "factor" asDecimal |> Result.map VolumeTarget.Multiplier
            | o -> fail "correctionCodec.volumeTarget.unknown" (sprintf "unknown VolumeTarget '%s'" o))

    // -- FUZZING §5 (slice F-Faker): the coordinate-addressed Faker binding ----

    /// An optional string field — absent or JSON null ⇒ `None` (the locale's
    /// "use the default" form); a present non-string is a typed refusal.
    let private readOptString (el: JsonElement) (name: string) : Result<string option> =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String ->
            match v.GetString() with
            | null -> Ok None
            | s    -> Ok (Some s)
        | true, v when v.ValueKind = JsonValueKind.Null -> Ok None
        | true, v -> fail "correctionCodec.expectedString" (sprintf "field '%s': expected string, got %A" name v.ValueKind)
        | _ -> Ok None

    let private readMaskRule (el: JsonElement) : Result<MaskRule> =
        field el "rule" asString
        |> Result.bind (function
            | "redact"    -> Ok MaskRule.Redact
            | "keepLast"  -> field el "n" asInt |> Result.map MaskRule.KeepLast
            | "keepFirst" -> field el "n" asInt |> Result.map MaskRule.KeepFirst
            | "hash"      -> Ok MaskRule.Hash
            | o -> fail "correctionCodec.maskRule.unknown" (sprintf "unknown MaskRule '%s'" o))

    let private readFakerGenerator (el: JsonElement) : Result<FakerGenerator> =
        field el "generator" asString
        |> Result.bind (function
            | "fullName"       -> Ok FakerGenerator.FullName
            | "firstName"      -> Ok FakerGenerator.FirstName
            | "lastName"       -> Ok FakerGenerator.LastName
            | "userName"       -> Ok FakerGenerator.UserName
            | "email"          -> Ok FakerGenerator.Email
            | "phone"          -> Ok FakerGenerator.Phone
            | "streetAddress"  -> Ok FakerGenerator.StreetAddress
            | "city"           -> Ok FakerGenerator.City
            | "zipCode"        -> Ok FakerGenerator.ZipCode
            | "country"        -> Ok FakerGenerator.Country
            | "fullAddress"    -> Ok FakerGenerator.FullAddress
            | "company"        -> Ok FakerGenerator.Company
            | "jobTitle"       -> Ok FakerGenerator.JobTitle
            | "url"            -> Ok FakerGenerator.Url
            | "domainName"     -> Ok FakerGenerator.DomainName
            | "word"           -> Ok FakerGenerator.Word
            | "sentence"       -> Ok FakerGenerator.Sentence
            | "paragraph"      -> Ok FakerGenerator.Paragraph
            | "guid"           -> Ok FakerGenerator.Guid
            | "intBetween"     ->
                field el "lo" asInt |> Result.bind (fun lo ->
                    field el "hi" asInt |> Result.map (fun hi -> FakerGenerator.IntBetween (lo, hi)))
            | "decimalBetween" ->
                field el "lo" asDecimal |> Result.bind (fun lo ->
                    field el "hi" asDecimal |> Result.map (fun hi -> FakerGenerator.DecimalBetween (lo, hi)))
            | "pastDate"       -> Ok FakerGenerator.PastDate
            | "futureDate"     -> Ok FakerGenerator.FutureDate
            | "mask"           -> field el "mask" readMaskRule |> Result.map FakerGenerator.Mask
            | "constant"       -> field el "value" asString |> Result.map FakerGenerator.Constant
            | o -> fail "correctionCodec.fakerGenerator.unknown" (sprintf "unknown FakerGenerator '%s'" o))

    let private readFakerSpec (el: JsonElement) : Result<FakerSpec> =
        readFakerGenerator el
        |> Result.bind (fun g ->
            readOptString el "locale"
            |> Result.map (fun locale -> { Generator = g; Locale = locale }))

    let private readEntry (el: JsonElement) : Result<CorrectionEntry> =
        field el "entry" asString
        |> Result.bind (function
            | "pii" ->
                field el "column" readSsKey
                |> Result.bind (fun col ->
                    field el "pii" readPiiKind
                    |> Result.map (fun kind -> CorrectionEntry.Pii (col, kind)))
            | "fidelity" ->
                field el "column" readSsKey
                |> Result.bind (fun col ->
                    field el "mode" readFidelity
                    |> Result.map (fun mode -> CorrectionEntry.Fidelity (col, mode)))
            | "volume" ->
                field el "kind" readSsKey
                |> Result.bind (fun kind ->
                    field el "target" readVolumeTarget
                    |> Result.map (fun target -> CorrectionEntry.Volume (kind, target)))
            | "faker" ->
                field el "module" asString
                |> Result.bind (fun m ->
                    field el "entity" asString
                    |> Result.bind (fun e ->
                        field el "attribute" asString
                        |> Result.bind (fun a ->
                            field el "faker" readFakerSpec
                            |> Result.map (fun spec -> CorrectionEntry.Faker (AttributeCoordinate.create m e a, spec)))))
            | o -> fail "correctionCodec.entry.unknown" (sprintf "unknown correction entry '%s'" o))

    let private readCorrection (el: JsonElement) : Result<Correction> =
        listField el "entries" readEntry |> Result.bind Correction.create

    /// `JSON → Correction`. Total parser; re-proves the conflict invariant.
    let deserialize (json: string) : Result<Correction> =
        let parsed = try Ok (JsonDocument.Parse json) with ex -> fail "correctionCodec.parse" ex.Message
        parsed
        |> Result.bind (fun doc ->
            use doc = doc
            readCorrection doc.RootElement)

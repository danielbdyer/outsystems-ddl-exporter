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

    let private inv (d: decimal) : string = d.ToString(CultureInfo.InvariantCulture)

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

    let private fail (code: string) (msg: string) : Result<'a> =
        Result.failureOf (ValidationError.create code msg)

    let private asString (el: JsonElement) : Result<string> =
        if el.ValueKind = JsonValueKind.String then
            match el.GetString() with
            | null -> fail "correctionCodec.expectedString" "string element returned null"
            | s -> Ok s
        else fail "correctionCodec.expectedString" (sprintf "expected string, got %A" el.ValueKind)

    let private prop (el: JsonElement) (name: string) : Result<JsonElement> =
        match el.TryGetProperty name with
        | true, v -> Ok v
        | _ -> fail "correctionCodec.missingField" (sprintf "missing field '%s'" name)

    let private field (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a> =
        prop el name |> Result.bind read

    let private listField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a list> =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray() |> Seq.map read |> Result.collect
        | true, v -> fail "correctionCodec.expectedArray" (sprintf "field '%s': expected array, got %A" name v.ValueKind)
        | _ -> Ok []

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

    let private asInt (el: JsonElement) : Result<int> =
        if el.ValueKind = JsonValueKind.Number then
            match el.TryGetInt32() with
            | true, n -> Ok n
            | _ -> fail "correctionCodec.expectedInt" "number is not an int32"
        else fail "correctionCodec.expectedInt" (sprintf "expected number, got %A" el.ValueKind)

    let private asDecimal (el: JsonElement) : Result<decimal> =
        asString el
        |> Result.bind (fun s ->
            match System.Decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture) with
            | true, d -> Ok d
            | _ -> fail "correctionCodec.expectedDecimal" (sprintf "not an invariant-culture decimal: '%s'" s))

    let private readVolumeTarget (el: JsonElement) : Result<VolumeTarget> =
        field el "target" asString
        |> Result.bind (function
            | "absolute"   -> field el "rows" asInt |> Result.map VolumeTarget.Absolute
            | "multiplier" -> field el "factor" asDecimal |> Result.map VolumeTarget.Multiplier
            | o -> fail "correctionCodec.volumeTarget.unknown" (sprintf "unknown VolumeTarget '%s'" o))

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

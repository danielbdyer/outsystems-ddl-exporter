namespace Projection.Targets.Json

open System.Globalization
open System.Text.Json
open Projection.Core
open FsToolkit.ErrorHandling

/// The shared JSON decode kernel every round-trip codec consumes
/// (`CatalogCodec` / `ProfileCodec` / `CorrectionCodec` / `GoldenCodec` /
/// `SliceCodec`). The five codecs each declared a byte-identical copy of these
/// helpers, differing ONLY in their error-code prefix (`codec.` / `profileCodec.`
/// / `correctionCodec.` / `golden.` / `slice.`). This single-sources the logic;
/// each codec passes its `prefix` so the emitted codes (`<prefix>.expectedString`
/// etc.) stay byte-identical to the pre-collapse copies. Same precedent as
/// `Binding.fs` collapsing the config binders' resolve/parse skeletons.
///
/// `fail` takes an already-namespaced code (codecs compose `<prefix>.<x>`
/// themselves for their bespoke decode errors); the typed-leaf helpers compose
/// `prefix + "."` for the shared shapes.
[<RequireQualifiedAccess>]
module JsonCodecKernel =

    /// Fail with an already-namespaced code.
    let fail (code: string) (msg: string) : Result<'a> =
        Result.failureOf (ValidationError.create code msg)

    /// Read a required property; missing → `<prefix>.missingField`.
    let prop (prefix: string) (el: JsonElement) (name: string) : Result<JsonElement> =
        match el.TryGetProperty name with
        | true, v -> Ok v
        | _ -> fail (prefix + ".missingField") (sprintf "missing field '%s'" name) // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary

    let asString (prefix: string) (el: JsonElement) : Result<string> =
        if el.ValueKind = JsonValueKind.String then
            match el.GetString() with
            | null -> fail (prefix + ".expectedString") "string element returned null" // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary
            | s -> Ok s
        else fail (prefix + ".expectedString") (sprintf "expected string, got %A" el.ValueKind) // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary

    let asBool (prefix: string) (el: JsonElement) : Result<bool> =
        match el.ValueKind with
        | JsonValueKind.True  -> Ok true
        | JsonValueKind.False -> Ok false
        | k -> fail (prefix + ".expectedBool") (sprintf "expected bool, got %A" k) // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary

    let asInt (prefix: string) (el: JsonElement) : Result<int> =
        if el.ValueKind = JsonValueKind.Number then
            match el.TryGetInt32() with
            | true, n -> Ok n
            | _ -> fail (prefix + ".expectedInt") "number is not an int32" // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary
        else fail (prefix + ".expectedInt") (sprintf "expected number, got %A" el.ValueKind) // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary

    let asInt64 (prefix: string) (el: JsonElement) : Result<int64> =
        if el.ValueKind = JsonValueKind.Number then
            match el.TryGetInt64() with
            | true, n -> Ok n
            | _ -> fail (prefix + ".expectedInt64") "number is not an int64" // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary
        else fail (prefix + ".expectedInt64") (sprintf "expected number, got %A" el.ValueKind) // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary

    let asDecimal (prefix: string) (el: JsonElement) : Result<decimal> =
        asString prefix el
        |> Result.bind (fun s ->
            match System.Decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture) with
            | true, d -> Ok d
            | _ -> fail (prefix + ".expectedDecimal") (sprintf "not an invariant-culture decimal: '%s'" s)) // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary

    /// Read a required named field through a value-reader.
    let field (prefix: string) (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a> =
        prop prefix el name |> Result.bind read

    /// Read an optional named field: missing or JSON null → `None`.
    let optField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a option> =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Null -> Ok None
        | true, v -> read v |> Result.map Some
        | _ -> Ok None

    /// Read a named array field; missing → empty list.
    let listField (prefix: string) (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a list> =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray() |> Seq.map read |> Seq.toList |> Result.collect
        | true, v -> fail (prefix + ".expectedArray") (sprintf "field '%s': expected array, got %A" name v.ValueKind) // LINT-ALLOW: terminal error-code path composition at the JSON-decode boundary; prefix + suffix are two typed string segments, the irreducible primitive at this terminal boundary
        | _ -> Ok []

    /// Decimal → invariant-culture string (the write-side determinism primitive
    /// shared by the value-encoding codecs).
    let inv (d: decimal) : string = d.ToString(CultureInfo.InvariantCulture)

    // ======================================================================
    // ENCODE — the structural write-side twins of `field` / `optField` /
    // `listField`. `CatalogCodec` and `ProfileCodec` each declared a
    // byte-identical private copy of these; they carry no error-code prefix
    // (pure structural writers, nothing to fail), so — unlike the decode
    // helpers — they take no `prefix`. Single-sources the write semantics
    // (how an absent option is written, array framing) the same way the
    // decode kernel single-sources the read side.
    // ======================================================================

    /// Write a named field through a value-writer (write-side twin of `field`).
    let wField (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (v: 'a) : unit =
        jw.WritePropertyName name
        write jw v

    /// Write an optional named field; `None` → JSON null (write-side twin of `optField`).
    let wOpt (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (v: 'a option) : unit =
        jw.WritePropertyName name
        match v with
        | Some x -> write jw x
        | None   -> jw.WriteNullValue()

    /// Write a named array field (write-side twin of `listField`).
    let wList (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (xs: 'a list) : unit =
        jw.WritePropertyName name
        jw.WriteStartArray()
        for x in xs do write jw x
        jw.WriteEndArray()

namespace Projection.Core

/// Structured-tag rendering — V2's typed builder for the
/// `Tag(field1=value1, field2=value2)` diagnostic-string idiom.
/// Per the user's "string-concat is still brittle" critique
/// (chapter 3.5 sidebar): the punctuation (`(`, `=`, `,`, `)`)
/// lives in ONE place — `StructuredString.render`. Adding a new
/// variant or changing the format adjusts that single function;
/// the per-variant `toDiagnosticString` callers remain
/// structurally typed.
///
/// **Postdoctoral discipline.** This is the typed-display
/// equivalent of ScriptDom for SQL: a typed AST + canonical
/// renderer, not a per-call string-concatenation. Per `DECISIONS
/// 2026-05-09 — Built-in obligation`, when a typed renderer
/// exists, callers must use it; the renderer is the structural
/// commitment that prevents format drift across consumers.

/// One named field in a structured diagnostic string.
type StructuredField =
    {
        Name  : string
        Value : string
    }

/// A typed tag-with-fields value. The empty `Fields` list emits
/// just the tag (`"PrimaryKey"`); a non-empty list emits the
/// `Tag(field1=value1, field2=value2)` form. The construction is
/// deterministic in the field order (List preserves insertion
/// order); rendering is byte-deterministic by virtue of
/// `String.Concat` over typed segments.
type StructuredString =
    {
        Tag    : string
        Fields : StructuredField list
    }

[<RequireQualifiedAccess>]
module StructuredField =
    /// Compose a typed field. Mirrors the named-pair construction
    /// at diagnostic-emission sites; constructed via this function
    /// rather than record-literal so callers don't need to re-type
    /// `{ Name = ...; Value = ... }` repeatedly.
    let create (name: string) (value: string) : StructuredField =
        { Name = name; Value = value }

    /// Render one field as `name=value`. Punctuation lives here
    /// (and only here); changing the separator or the equals sign
    /// is a one-line edit.
    let render (f: StructuredField) : string =
        System.String.Concat(f.Name, "=", f.Value)

[<RequireQualifiedAccess>]
module StructuredString =

    /// Tag-only construction (no fields). Renders to `"<Tag>"`.
    let tag (name: string) : StructuredString =
        { Tag = name; Fields = [] }

    /// Construct from a tag and a list of (name, value) pairs.
    /// Field order is preserved.
    let create (tag: string) (fields: (string * string) list) : StructuredString =
        {
            Tag = tag
            Fields =
                fields
                |> List.map (fun (n, v) -> StructuredField.create n v)
        }

    /// Append one field to an existing structured string. Returns
    /// a new value (immutable).
    let withField (name: string) (value: string) (s: StructuredString) : StructuredString =
        { s with Fields = s.Fields @ [ StructuredField.create name value ] }  // LINT-ALLOW: cons-at-end is intentional for field-order preservation; bounded by per-variant field count

    /// Render the typed value to its canonical string form.
    /// `Tag(field1=value1, field2=value2)` for non-empty fields;
    /// `Tag` alone for empty fields. Punctuation is centralized
    /// here; per the no-string-concatenation discipline, callers
    /// build typed values and route through `render` rather than
    /// hand-composing strings per variant.
    let render (s: StructuredString) : string =
        match s.Fields with
        | [] -> s.Tag
        | _ ->
            let inner =
                s.Fields
                |> List.map StructuredField.render
                |> String.concat ", "
            System.String.Concat(s.Tag, "(", inner, ")")

/// Invariant-culture integer formatters. Diagnostic strings carry
/// numeric values; rendering through invariant culture keeps T1
/// byte-determinism culture-independent.
[<RequireQualifiedAccess>]
module Inv =
    let private culture = System.Globalization.CultureInfo.InvariantCulture
    let int32 (i: int) : string = i.ToString culture
    let int64 (i: int64) : string = i.ToString culture
    let dec (d: decimal) : string = d.ToString culture

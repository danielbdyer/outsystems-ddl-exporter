namespace Projection.Targets.Json

open System.Text
open Projection.Core

/// The second sibling Π for V2. Emits the catalog as JSON text. Together
/// with `Projection.Targets.SSDT.RawTextEmitter` this demonstrates the
/// sibling-functor factoring: same enriched IR, two surfaces, identity
/// preserved across both (T4 / T11).
///
/// Π is mechanical (A18): no policy parameter. Hand-rolled JSON keeps
/// the algebra visible and avoids serializer-driven ordering surprises;
/// for production-grade JSON consumption, a future commit may swap in
/// `System.Text.Json` with a stable property ordering.
[<RequireQualifiedAccess>]
module JsonEmitter =

    [<Literal>]
    let version : int = 1

    // -----------------------------------------------------------------------
    // String escaping per RFC 8259. Hand-rolled because the value space
    // here is small (catalog identifiers, names, small string values).
    // -----------------------------------------------------------------------

    let private escape (s: string) : string =
        let sb = StringBuilder(s.Length + 2)
        sb.Append('"') |> ignore
        for c in s do
            match c with
            | '"'  -> sb.Append("\\\"")    |> ignore
            | '\\' -> sb.Append("\\\\")    |> ignore
            | '\n' -> sb.Append("\\n")     |> ignore
            | '\r' -> sb.Append("\\r")     |> ignore
            | '\t' -> sb.Append("\\t")     |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore
        sb.Append('"') |> ignore
        sb.ToString()

    let private bool (b: bool) : string = if b then "true" else "false"

    // -----------------------------------------------------------------------
    // Indented writer state. The emitter writes pretty-printed JSON with
    // two-space indents so the output is human-diffable. The pretty-print
    // is deterministic: identical input ⇒ identical output, byte for byte.
    // -----------------------------------------------------------------------

    let private indent (level: int) : string =
        System.String(' ', level * 2)

    let private renderSsKey (key: SsKey) : string =
        // For the synthetic milestone we render the root identifier. Derived
        // keys are flagged so consumers can distinguish them.
        let root = SsKey.rootOriginal key
        if SsKey.isDerived key then
            sprintf "%s [derived]" root
        else
            root

    let private originString (o: Origin) : string =
        match o with
        | OsNative                     -> "OsNative"
        | ExternalViaIntegrationStudio -> "ExternalViaIntegrationStudio"
        | ExternalDirect               -> "ExternalDirect"

    let private primitiveString (t: PrimitiveType) : string =
        match t with
        | Integer  -> "Integer"
        | Decimal  -> "Decimal"
        | Text     -> "Text"
        | Boolean  -> "Boolean"
        | DateTime -> "DateTime"
        | Date     -> "Date"
        | Time     -> "Time"
        | Binary   -> "Binary"
        | Guid     -> "Guid"

    let private actionString (a: ReferenceAction) : string =
        match a with
        | NoAction -> "NoAction"
        | Cascade  -> "Cascade"
        | SetNull  -> "SetNull"
        | Restrict -> "Restrict"

    // -----------------------------------------------------------------------
    // Per-element renderers. Each takes a depth so indentation is stable.
    // -----------------------------------------------------------------------

    let private renderModalityArray (sb: StringBuilder) (depth: int) (marks: ModalityMark list) : unit =
        if List.isEmpty marks then
            sb.Append("[]") |> ignore
        else
            sb.AppendLine("[") |> ignore
            marks
            |> List.iteri (fun i m ->
                let label =
                    match m with
                    | Static rows  -> sprintf "Static(%d)" rows.Length
                    | TenantScoped  -> "TenantScoped"
                    | SoftDeletable -> "SoftDeletable"
                sb.Append(indent (depth + 1)).Append(escape label) |> ignore
                if i < marks.Length - 1 then sb.AppendLine(",") |> ignore
                else sb.AppendLine() |> ignore)
            sb.Append(indent depth).Append("]") |> ignore

    let private renderAttribute (sb: StringBuilder) (depth: int) (a: Attribute) : unit =
        sb.AppendLine("{") |> ignore
        sb.Append(indent (depth + 1)).Append("\"ssKey\": ").Append(escape (renderSsKey a.SsKey)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"name\": ").Append(escape (Name.value a.Name)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"type\": ").Append(escape (primitiveString a.Type)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"column\": ").Append(escape a.Column.ColumnName).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"nullable\": ").AppendLine(bool a.Column.IsNullable)
        |> ignore
        sb.Append(indent depth).Append("}") |> ignore

    let private renderReference (sb: StringBuilder) (depth: int) (r: Reference) : unit =
        sb.AppendLine("{") |> ignore
        sb.Append(indent (depth + 1)).Append("\"ssKey\": ").Append(escape (renderSsKey r.SsKey)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"name\": ").Append(escape (Name.value r.Name)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"sourceAttribute\": ").Append(escape (renderSsKey r.SourceAttribute)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"targetKind\": ").Append(escape (renderSsKey r.TargetKind)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"onDelete\": ").AppendLine(escape (actionString r.OnDelete))
        |> ignore
        sb.Append(indent depth).Append("}") |> ignore

    let private renderArrayOf<'T> (sb: StringBuilder) (depth: int) (items: 'T list) (renderItem: StringBuilder -> int -> 'T -> unit) : unit =
        if List.isEmpty items then
            sb.Append("[]") |> ignore
        else
            sb.AppendLine("[") |> ignore
            items
            |> List.iteri (fun i item ->
                sb.Append(indent (depth + 1)) |> ignore
                renderItem sb (depth + 1) item
                if i < items.Length - 1 then sb.AppendLine(",") |> ignore
                else sb.AppendLine() |> ignore)
            sb.Append(indent depth).Append("]") |> ignore

    let private renderKind (sb: StringBuilder) (depth: int) (k: Kind) : unit =
        sb.AppendLine("{") |> ignore
        sb.Append(indent (depth + 1)).Append("\"ssKey\": ").Append(escape (renderSsKey k.SsKey)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"name\": ").Append(escape (Name.value k.Name)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"origin\": ").Append(escape (originString k.Origin)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"modality\": ")
        |> ignore
        renderModalityArray sb (depth + 1) k.Modality
        sb.AppendLine(",") |> ignore
        sb.Append(indent (depth + 1)).Append("\"physical\": { \"schema\": ")
            .Append(escape k.Physical.Schema).Append(", \"table\": ")
            .Append(escape k.Physical.Table).AppendLine(" },")
        |> ignore
        sb.Append(indent (depth + 1)).Append("\"attributes\": ") |> ignore
        renderArrayOf sb (depth + 1) k.Attributes renderAttribute
        sb.AppendLine(",") |> ignore
        sb.Append(indent (depth + 1)).Append("\"references\": ") |> ignore
        renderArrayOf sb (depth + 1) k.References renderReference
        sb.AppendLine() |> ignore
        sb.Append(indent depth).Append("}") |> ignore

    let private renderModule (sb: StringBuilder) (depth: int) (m: Module) : unit =
        sb.AppendLine("{") |> ignore
        sb.Append(indent (depth + 1)).Append("\"ssKey\": ").Append(escape (renderSsKey m.SsKey)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"name\": ").Append(escape (Name.value m.Name)).AppendLine(",")
            .Append(indent (depth + 1)).Append("\"kinds\": ")
        |> ignore
        renderArrayOf sb (depth + 1) m.Kinds renderKind
        sb.AppendLine() |> ignore
        sb.Append(indent depth).Append("}") |> ignore

    /// Emit the catalog as JSON text. Output is deterministic: byte-
    /// identical for byte-identical input (T1).
    let emit (catalog: Catalog) : string =
        let sb = StringBuilder(2048)
        sb.AppendLine("{") |> ignore
        sb.Append(indent 1).Append("\"emitter\": \"Projection.Targets.Json\",")
            .AppendLine() |> ignore
        sb.Append(indent 1).Append("\"version\": ").Append(version).AppendLine(",") |> ignore
        sb.Append(indent 1).Append("\"modules\": ") |> ignore
        renderArrayOf sb 1 catalog.Modules renderModule
        sb.AppendLine() |> ignore
        sb.Append("}") |> ignore
        sb.ToString()

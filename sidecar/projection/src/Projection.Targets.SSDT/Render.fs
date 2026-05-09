namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE: terminal text-emission boundary. This file IS
//   a string-builder surface — its purpose is to emit text from
//   typed values at the canonical boundary (SQL identifiers,
//   diagnostic structured-string output). Per the supreme operating
//   discipline (data-structure-oriented), the typed input flows in;
//   the string output flows out; nothing is parsed back. Future
//   chapters may retire `String.Concat` here in favor of typed
//   AST + a dedicated serializer (e.g., ScriptDom for SQL).

open System
open System.Globalization
open System.Text
open Projection.Core

/// Pure rendering of `Statement` to T-SQL text. The other realization
/// of Π's statement stream — `Deploy.executeStream` — uses the same
/// per-statement formatters (`columnSqlType`, `formatSqlLiteral`) so
/// emit-time text and deploy-time SQL are byte-identical wherever
/// they overlap. Per A18, no policy parameter enters here; per T1,
/// rendering is deterministic in its input.
///
/// Per the no-string-concatenation discipline (`DECISIONS 2026-05-09`),
/// SQL fragments compose via `String.Concat` (BCL multi-arg overload,
/// no array allocation) and `String.concat` (BCL collection joiner)
/// rather than `sprintf` or `+`. Integer formatting goes through
/// `Int32.ToString CultureInfo.InvariantCulture` to keep T1
/// byte-determinism culture-invariant by construction.
[<RequireQualifiedAccess>]
module Render =

    /// Invariant-culture integer formatting. `int.ToString` without
    /// arguments respects current culture for some derivations; the
    /// invariant-culture form is byte-deterministic across runs and
    /// platforms.
    let private intInv (i: int) : string =
        i.ToString CultureInfo.InvariantCulture

    /// Bracket-quote an identifier. `String.Concat`'s 3-arg overload
    /// is allocation-light and avoids a format-string indirection.
    let quote (s: string) : string = String.Concat("[", s, "]")

    /// Per session-36 — delegate to the canonical `TableId.qualified`
    /// in Core so SSDT renderers and the bulk path produce identical
    /// `[schema].[table]` strings by construction.
    let tableQualified (t: TableId) : string = TableId.qualified t

    /// `<typeName>(<length>)` SQL type expression. Composes a typed
    /// 4-tuple of segments via `String.Concat`; no `sprintf`.
    let private sqlTypeWithLength (typeName: string) (length: int) : string =
        String.Concat(typeName, "(", intInv length, ")")

    /// `DECIMAL(<precision>, <scale>)` SQL type expression.
    let private sqlDecimal (precision: int) (scale: int) : string =
        // `String.Concat`'s params overload covers 5 parts; allocates
        // a small array but stays O(1) on input shape.
        String.Concat(
            "DECIMAL(",
            intInv precision,
            ", ",
            intInv scale,
            ")")

    /// IR `(Type, Length, Precision, Scale)` → SQL type expression.
    /// Shared by emit (`toText`) and deploy paths so the two never
    /// drift.
    let columnSqlType (c: ColumnDef) : string =
        match c.Type with
        | Text ->
            match c.Length with
            | Some n when n > 0 -> sqlTypeWithLength "NVARCHAR" n
            | _ -> "NVARCHAR(MAX)"
        | Binary ->
            match c.Length with
            | Some n when n > 0 -> sqlTypeWithLength "VARBINARY" n
            | _ -> "VARBINARY(MAX)"
        | Decimal ->
            match c.Precision, c.Scale with
            | Some p, Some s -> sqlDecimal p s
            | Some p, None -> sqlDecimal p 0
            | _ -> "DECIMAL(18, 4)"
        | Integer  -> "INT"
        | Boolean  -> "BIT"
        | DateTime -> "DATETIME2"
        | Date     -> "DATE"
        | Time     -> "TIME"
        | Guid     -> "UNIQUEIDENTIFIER"

    /// Quote a string literal as `'<raw>'`. SQL injection isn't a
    /// concern for emitter output (the IR is the input contract);
    /// callers responsible for escape-doubling apostrophes pre-call.
    let private quoteSingle (raw: string) : string =
        String.Concat("'", raw, "'")

    /// Raw IR string → SQL literal. `""` is NULL; Text gets `N'…'`
    /// with single-quote doubling; temporal / Guid get `'…'`; Binary
    /// gets `0x…`. Mirrors the canonical adapter convention
    /// (`ReadSide.formatRawValue` produces the inverse).
    let formatSqlLiteral (typ: PrimitiveType) (raw: string) : string =
        if raw = "" then "NULL"
        else
            match typ with
            | Integer | Decimal -> raw
            | Boolean ->
                match raw.ToLowerInvariant() with
                | "true" | "1" -> "1"
                | _ -> "0"
            | DateTime | Date | Time | Guid ->
                quoteSingle raw
            | Text ->
                String.Concat("N", quoteSingle (raw.Replace("'", "''")))
            | Binary ->
                if raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                then raw
                else String.Concat("0x", raw)

    let private actionSql (a: ReferenceActionSql) : string =
        match a with
        | NoActionSql -> "NO ACTION"
        | CascadeSql  -> "CASCADE"
        | SetNullSql  -> "SET NULL"

    let private renderColumn (sb: StringBuilder) (c: ColumnDef) (sep: string) : unit =
        let identityClause = if c.IsIdentity then " IDENTITY(1,1)" else ""
        let nullness = if c.Nullable then "NULL" else "NOT NULL"
        let pkTag = if c.IsPrimaryKey then " PK" else ""
        sb.Append("    ").Append(quote c.Name).Append(' ').Append(columnSqlType c)
            .Append(identityClause).Append(' ').Append(nullness)
            .Append(sep).Append("  -- ").Append(c.Provenance).Append(pkTag).AppendLine()
        |> ignore

    /// Render one Statement into the StringBuilder. Per-statement
    /// bench scope so per-shape distribution surfaces in stats.
    let toSql (sb: StringBuilder) (s: Statement) : unit =
        use _ = Bench.scope "render.statement"
        match s with
        | Blank ->
            sb.AppendLine() |> ignore
        | Comment text ->
            sb.Append("-- ").AppendLine(text) |> ignore
        | CreateTable (table, columns, pk, fks) ->
            sb.Append("CREATE TABLE ").Append(tableQualified table).AppendLine(" (") |> ignore
            let hasPk = Option.isSome pk
            let hasFks = not (List.isEmpty fks)
            let lastColIdx = columns.Length - 1
            columns
            |> List.iteri (fun i c ->
                let needsComma = i < lastColIdx || hasPk || hasFks
                let sep = if needsComma then "," else ""
                renderColumn sb c sep)
            match pk with
            | Some p ->
                let cols = p.Columns |> List.map quote |> String.concat ", "
                let sep = if hasFks then "," else ""
                sb.Append("    CONSTRAINT ").Append(quote p.Name)
                    .Append(" PRIMARY KEY (").Append(cols).Append(")").AppendLine(sep)
                |> ignore
            | None -> ()
            let lastFkIdx = fks.Length - 1
            fks
            |> List.iteri (fun i fk ->
                let sep = if i < lastFkIdx then "," else ""
                sb.Append("    CONSTRAINT ").Append(quote fk.Name)
                    .Append(" FOREIGN KEY (").Append(quote fk.SourceColumn)
                    .Append(") REFERENCES ").Append(tableQualified fk.Target)
                    .Append(" (").Append(quote fk.TargetColumn).Append(")")
                    .AppendLine(sep)
                |> ignore
                // OnDelete clause is deferred — current emitter relies on
                // SQL Server's NO ACTION default (matches the V2 IR for
                // OutSystems-shaped fixtures). Surface here when the
                // canary surfaces a non-default delete-rule.
                ignore (actionSql fk.OnDelete))
            sb.AppendLine(");") |> ignore
        | InsertRow (table, values) ->
            let cols = values |> List.map (fun v -> quote v.Column) |> String.concat ", "
            let vals = values |> List.map (fun v -> formatSqlLiteral v.Type v.Raw) |> String.concat ", "
            sb.Append("INSERT INTO ").Append(tableQualified table)
                .Append(" (").Append(cols).Append(") VALUES (")
                .Append(vals).AppendLine(");")
            |> ignore
        | SetIdentityInsert (table, enabled) ->
            sb.Append("SET IDENTITY_INSERT ").Append(tableQualified table)
                .AppendLine(if enabled then " ON;" else " OFF;")
            |> ignore

    /// Fold a statement stream into a single SQL-text artifact. The
    /// canonical text realization of Π's output. Stream-aware bench
    /// at the boundary (`render.toText`) records throughput across
    /// the full sequence.
    ///
    /// The StringBuilder grows by power-of-two doubling. Pre-sizing
    /// to the rendered output's actual length matters most for the
    /// `RawTextEmitter.emit` path on enterprise catalogs (≥100k
    /// statements), where un-sized accumulation pays repeated copy
    /// costs. Per session-35 — `IList`-backed `seq` short-circuits
    /// to a count probe; otherwise the default seed remains 64 KB
    /// (handles the 300-table fixture without resize).
    let toText (statements: seq<Statement>) : string =
        use _ = Bench.scope "render.toText"
        let initialCapacity =
            match statements with
            | :? System.Collections.Generic.IReadOnlyCollection<Statement> as coll ->
                // Heuristic: ~120 chars per statement (DDL averages
                // higher, comments lower). Rounding up to nearest KB
                // avoids fine-grained over-tuning while preventing
                // resize-doubling on large catalogs.
                max 65_536 (coll.Count * 120)
            | _ -> 65_536
        let sb = StringBuilder(initialCapacity)
        statements
        |> Bench.streamProbe "render.toText.stream"
        |> Seq.iter (toSql sb)
        sb.ToString()

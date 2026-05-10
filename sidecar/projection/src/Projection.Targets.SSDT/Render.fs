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
/// rather than `sprintf` or `+`. As of chapter-3.7 slice β', the SQL
/// DDL type expression (`columnSqlType`) flows through ScriptDom's
/// typed `SqlDataTypeReference` AST emitted by `Sql160ScriptGenerator`
/// — pillar 7 (gold-standard library) supersedes the per-call
/// composition for that surface. Identifier quoting goes through
/// `ScriptDom.Identifier.EncodeIdentifier`. The remaining
/// `String.Concat` sites (string-literal quoting, `N'…'` Text prefix)
/// stay because they're terminal text-formatting (escape sequences)
/// for which no use-case-specific BCL primitive exists.
[<RequireQualifiedAccess>]
module Render =

    /// Bracket-quote an identifier. Per chapter 3.5 deep audit
    /// (2026-05-09): the use-case-specific library for SQL identifier
    /// handling is `Microsoft.SqlServer.TransactSql.ScriptDom`'s
    /// `Identifier.EncodeIdentifier(string)` static method — the
    /// canonical, vendor-supplied bracket-quoter. Default
    /// `QuoteType.SquareBracket` is the SSDT convention. Same
    /// observable behavior as the legacy `String.Concat` form
    /// (verified by build-time round-trip via the existing
    /// emitter test suite); zero ambiguity about escape semantics.
    let quote (s: string) : string =
        Microsoft.SqlServer.TransactSql.ScriptDom.Identifier.EncodeIdentifier s

    /// Per chapter 3.5 deep audit (2026-05-09): bracket-quoting +
    /// dot-join via use-case-specific library calls — ScriptDom's
    /// `Identifier.EncodeIdentifier` for each segment, then `String
    /// .Join(".", ...)` (BCL collection joiner) for the dot-
    /// separator. Replaces `String.Concat("[", schema, "].[", table,
    /// "]")` with two BCL-primitive calls; the `[…].[…]` shape lives
    /// in ScriptDom's grammar definition, not in V2 source code.
    /// Mirrors how `MultiPartIdentifier` would render through
    /// `Sql160ScriptGenerator` if we built the typed AST inline.
    let tableQualified (t: TableId) : string =
        System.String.Join(
            ".",
            [|
                Microsoft.SqlServer.TransactSql.ScriptDom.Identifier.EncodeIdentifier t.Schema
                Microsoft.SqlServer.TransactSql.ScriptDom.Identifier.EncodeIdentifier t.Table
            |])

    /// IR `(Type, Length, Precision, Scale)` → SQL DDL type
    /// expression. Shared by emit (`toText`) and deploy paths so
    /// the two never drift.
    ///
    /// **Pillar 7 cash-out (chapter-3.7 slice β').** The use-case-
    /// specific library for SQL DDL type expression is ScriptDom's
    /// `SqlDataTypeReference` typed AST + `Sql160ScriptGenerator`
    /// emitter. `ScriptDomBuild.dataTypeReference` builds the typed
    /// fragment; `ScriptDomGenerate.generateDataType` renders it
    /// through the pinned-options generator. The forward direction
    /// here goes *exclusively* through that path; no `String.Concat`
    /// segments at the call site.
    ///
    /// The earlier per-call composition (`sqlTypeWithLength`,
    /// `sqlDecimal`) and its `String.Concat` dispatch retired with
    /// this slice — gold-standard library replaces the hand-rolled
    /// composition. Pillar 1 (data-structure-oriented over string-
    /// parsing) holds: typed `DataTypeReference` flows through; the
    /// string emerges only at ScriptDom's BCL writer boundary.
    let columnSqlType (c: ColumnDef) : string =
        ScriptDomBuild.dataTypeReference c.Type c.Length c.Precision c.Scale
        |> ScriptDomGenerate.generateDataType

    /// Raw IR string → SQL literal. Delegates to `SqlLiteral.formatRaw`
    /// per the Tier-1 #4 transition (RawTextEmitter retirement arc):
    /// the typed `SqlLiteral` value lives in Core; this is the SSDT-
    /// resident shim that preserves the legacy call-site shape. Same
    /// semantics by construction (the test suite's golden-output
    /// equivalence is preserved); the typed middle layer enables
    /// MERGE → ScriptDom MergeStatement migration (Tier-1 #1) which
    /// needs typed VALUES literals.
    let formatSqlLiteral (typ: PrimitiveType) (raw: string) : string =
        SqlLiteral.formatRaw typ raw

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
        | CreateIndex _ ->
            // Per pillar 7 four-question analysis at this site:
            //   1. Use-case-specific library: ScriptDom's
            //      `Sql160ScriptGenerator` via `ScriptDomGenerate
            //      .generateOne`.
            //   2. Already in codebase: yes (chapter 3.5).
            //   3. Cost: trivial (one delegate call).
            //   4. Structural reason it doesn't apply: NO — ScriptDom
            //      emits CREATE INDEX correctly per Sql160 grammar.
            // Conclusion: delegate to ScriptDomGenerate (pillar-7 right
            // move). This keeps the Render.toSql legacy text-renderer
            // partially ScriptDom-driven (consistent with chapter-3.7
            // slice β' Render.columnSqlType-through-ScriptDom precedent).
            // The full Render→ScriptDomGenerate migration is a separate
            // slice when the second consumer pressures it.
            match ScriptDomBuild.buildStatement s with
            | Some fragment ->
                sb.Append(ScriptDomGenerate.generateOne fragment).AppendLine() |> ignore
            | None ->
                // Unreachable: CreateIndex always builds a typed fragment
                // (per ScriptDomBuild.buildStatement's exhaustive match).
                ()

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

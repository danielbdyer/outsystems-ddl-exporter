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
/// of Π's statement stream — `Deploy.executeStream` — invokes the
/// same per-statement dispatcher (`toSql`) so emit-time text and
/// deploy-time SQL are byte-identical wherever they overlap. Per
/// A18, no policy parameter enters here; per T1, rendering is
/// deterministic in its input.
///
/// Per slice 5.13.render-stringbuilder-retirement (2026-05-18), all
/// SQL-bearing variants delegate to ScriptDom's typed-AST emitter via
/// `ScriptDomBuild.buildStatement` + `ScriptDomGenerate.generateOne`.
/// The earlier per-call StringBuilder helpers (`columnSqlType`,
/// `formatSqlLiteral`, `actionSql`, `renderColumn`) retired as part of
/// that slice — they were the relic of the chapter-4.1.A CREATE TABLE
/// StringBuilder path that fully migrated to ScriptDom via the column-
/// features-emit slice. Only the identifier-quoting boundary helpers
/// (`quote`, `tableQualified`) remain as public surface; both are
/// consumed by sibling realization paths (Bulk, Deploy, RefactorLog).
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

    /// Render one Statement into the StringBuilder. Per-statement
    /// bench scope so per-shape distribution surfaces in stats.
    ///
    /// Per slice 5.13.render-stringbuilder-retirement (2026-05-18):
    /// the per-variant StringBuilder paths for `InsertRow` +
    /// `SetIdentityInsert` retired in favor of full ScriptDom
    /// delegation. The single delegation arm now handles every
    /// SQL-bearing variant (CreateTable / CreateIndex /
    /// SetExtendedProperty / AlterTableNoCheckConstraint /
    /// AlterIndexDisable / InsertRow / SetIdentityInsert). `Blank`
    /// + `Comment` remain as the only non-SQL variants — they're
    /// terminal text-formatting (Blank is a newline; Comment carries
    /// a `-- ` prefix). Pillar 7 four-question analysis stands: the
    /// gold-standard library is ScriptDom's `Sql160ScriptGenerator`;
    /// the cost is trivial; no structural reason to bypass.
    let toSql (sb: StringBuilder) (s: Statement) : unit =
        use _ = Bench.scope "render.statement"
        match s with
        | Blank ->
            sb.AppendLine() |> ignore
        | Comment text ->
            sb.Append("-- ").AppendLine(text) |> ignore
        | _ ->
            match ScriptDomBuild.buildStatement s with
            | Some fragment ->
                sb.Append(ScriptDomGenerate.generateOne fragment).AppendLine() |> ignore
            | None ->
                // Unreachable: every SQL-bearing Statement variant
                // builds a typed fragment via `ScriptDomBuild
                // .buildStatement`. The closed-DU dispatcher returns
                // `None` only for `Blank` + `Comment`, both handled
                // above. Future Statement variants must add their
                // ScriptDom builder before reaching this arm.
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

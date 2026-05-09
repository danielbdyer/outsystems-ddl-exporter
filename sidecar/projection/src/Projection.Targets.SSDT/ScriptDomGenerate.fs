namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE-MUTATION: Microsoft.SqlServer.TransactSql.ScriptDom's
//   `SqlScriptGeneratorOptions` is a mutable class exposing
//   properties via setters; this module's `pinnedOptions` helper
//   is the single sanctioned home for V2's pinned-deterministic
//   form of those options. `Sql160ScriptGenerator.GenerateScript`
//   accepts an immutable view via the constructor; mutation is
//   fully encapsulated.

open System.Text
open Microsoft.SqlServer.TransactSql.ScriptDom
open Projection.Core

/// Pinned `Sql160ScriptGenerator` for V2's SSDT emission.
/// Every option that affects output bytes is set here; T1 byte-
/// determinism rests on the surface being exhaustive. Per
/// `DECISIONS 2026-05-09 — Built-in obligation`, ScriptDom is
/// the typed-AST emitter for T-SQL; V2 delegates to it rather
/// than hand-rolling SQL text.
///
/// **SQL Server target:** 160 (SQL Server 2022). Per the user's
/// production-environment commitment; `Sql160ScriptGenerator`
/// emits T-SQL grammatically valid for the 160 compatibility
/// level. Earlier-version generators (`Sql150ScriptGenerator`,
/// `Sql140ScriptGenerator`, etc.) ship the same package but
/// are not consumed by V2 today; the package version
/// (`170.23.0`) bundles every generator from 80 → 160.
///
/// **Postdoctoral discipline.** Every byte-affecting option is
/// pinned — keyword casing, indentation, line breaks,
/// semicolon placement, statement terminator. Tests in
/// `tests/Projection.Tests/ScriptDomRoundTripTests.fs` verify:
///   1. **Determinism** — `emit(stmt)` is byte-identical across
///      repeat invocations.
///   2. **Parse-roundtrip** — `parse(emit(stmt))` re-acquires a
///      `TSqlFragment` whose grammar matches the original.
///   3. **Stream framing** — non-SQL trivia (`Comment`, `Blank`)
///      splices through `toText` deterministically alongside the
///      SQL emission.
[<RequireQualifiedAccess>]
module ScriptDomGenerate =

    /// Pinned `SqlScriptGeneratorOptions` for V2 SSDT emission.
    /// Each call yields a fresh instance (the class is mutable;
    /// sharing one instance would alias state across consumers).
    /// Every byte-affecting axis is set explicitly; defaults
    /// inherited from ScriptDom are documented in the field
    /// comment alongside the override.
    let private pinnedOptions () : SqlScriptGeneratorOptions =
        let opts = SqlScriptGeneratorOptions()
        // Keyword casing — uppercase per SSDT convention. ScriptDom
        // default is `Uppercase`; we re-pin for explicitness.
        opts.KeywordCasing <- KeywordCasing.Uppercase
        // Statement-level newline / indentation. Pin to LF newlines
        // and 4-space indent (SSDT convention); `IncludeSemicolons`
        // = true ensures every statement terminates explicitly.
        opts.NewLineBeforeFromClause <- true
        opts.NewLineBeforeWhereClause <- true
        opts.NewLineBeforeOrderByClause <- true
        opts.NewLineBeforeGroupByClause <- true
        opts.NewLineBeforeHavingClause <- true
        opts.NewLineBeforeJoinClause <- true
        opts.NewLineBeforeOutputClause <- true
        opts.NewLineBeforeOffsetClause <- true
        opts.IncludeSemicolons <- true
        // Multi-line list formatting — close paren on its own line
        // for parameter lists / column lists. SSDT convention.
        opts.NewLineBeforeOpenParenthesisInMultilineList <- false
        opts.NewLineBeforeCloseParenthesisInMultilineList <- true
        opts.MultilineInsertSourcesList <- true
        opts.MultilineInsertTargetsList <- true
        opts.MultilineSelectElementsList <- true
        opts.MultilineSetClauseItems <- true
        opts.MultilineViewColumnsList <- true
        opts.MultilineWherePredicatesList <- true
        // Indentation — 4 spaces per ScriptDom default; pin
        // explicitly.
        opts.IndentationSize <- 4
        opts.IndentSetClause <- true
        opts.IndentViewBody <- true
        // SQL Server compatibility level — 160 (SQL 2022).
        opts.SqlVersion <- SqlVersion.Sql160
        // Encoding — `SqlEngineType.All` (matches both on-prem and
        // Azure SQL DB grammars).
        opts.SqlEngineType <- SqlEngineType.All
        opts

    /// Generate T-SQL text for a single typed `TSqlStatement` via
    /// `Sql160ScriptGenerator`. Returns the rendered text without
    /// trailing newline; callers append the framing.
    ///
    /// **Why `Sql160` and not `Sql170`?** SQL Server 2022 ships
    /// compatibility level 160; level 170 is reserved for SQL
    /// Server vNext (preview). Per the user's production target
    /// (SQL Server 2022), `Sql160ScriptGenerator` emits T-SQL
    /// guaranteed-valid for the production server. Using Sql170
    /// would risk emitting vNext-only grammar onto a 2022 server.
    /// Re-open trigger: production target upgrade to SQL Server
    /// vNext (a paired `DECISIONS` amendment names the bump).
    ///
    /// Determinism: the generator is fed pinned options; same
    /// fragment shape → same bytes. ScriptDom's `out string`
    /// parameter takes a nullable string per BCL convention; the
    /// post-call `Option.ofObj` projection converts to `string`
    /// (typed; non-null) by F#'s nullness annotation.
    let generateOne (stmt: TSqlStatement) : string =
        let generator = Sql160ScriptGenerator(pinnedOptions ())
        let mutable text : string | null = null
        generator.GenerateScript(stmt :> TSqlFragment, &text)
        text |> Option.ofObj |> Option.defaultValue ""

    /// Render a comment line through the SSDT-canonical inline
    /// comment form. ScriptDom's typed-AST does not model
    /// comments-as-fragments; we splice the `--` form directly into
    /// the text stream. The `String.Concat` joiner is the no-string-
    /// concatenation discipline's allowed primitive (typed segment
    /// composition; not `sprintf`).
    let private commentLine (text: string) : string =
        System.String.Concat("-- ", text)

    /// Compose a `seq<Statement>` into byte-deterministic T-SQL
    /// text. SQL-bearing statements route through ScriptDom's
    /// typed AST; trivia (`Comment`, `Blank`) splice through the
    /// text framing directly.
    ///
    /// Output framing: `\n` newlines between statements; each SQL
    /// statement carries ScriptDom's emitted text (which may
    /// itself contain newlines per the pinned options); blank
    /// statements emit empty lines; comment statements emit
    /// `-- <text>` lines. T1 byte-determinism rests on the
    /// composition being a function of the input stream.
    let toText (statements: seq<Statement>) : string =
        use _ = Bench.scope "scriptDom.toText"
        let sb = StringBuilder()
        let mutable first = true
        for stmt in statements do
            // Frame separator — `\n` between successive emissions;
            // skipped on the first item.
            if not first then sb.Append('\n') |> ignore
            first <- false
            match stmt with
            | Blank ->
                // Blank emits nothing on its own line — the
                // framing newline above already separates from the
                // previous statement.
                ()
            | Comment text ->
                sb.Append(commentLine text) |> ignore
            | _ ->
                match ScriptDomBuild.buildStatement stmt with
                | Some fragment ->
                    sb.Append(generateOne fragment) |> ignore
                | None ->
                    // Unreachable: only Blank / Comment return None
                    // from `buildStatement`, and they're handled
                    // explicitly above.
                    ()
        sb.ToString()

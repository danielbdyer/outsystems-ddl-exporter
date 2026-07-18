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
    /// Every byte-affecting axis is set explicitly; defaults
    /// inherited from ScriptDom are documented in the field
    /// comment alongside the override.
    let private pinnedOptions () : SqlScriptGeneratorOptions =
        let opts = SqlScriptGeneratorOptions()
        // Keyword casing — uppercase per SSDT convention. ScriptDom
        // default is `Uppercase`; we re-pin for explicitness.
        opts.KeywordCasing <- KeywordCasing.Uppercase
        // Statement-level line breaks / indentation. (The newline
        // *character* is pinned to LF separately in `pinNewlines` —
        // ScriptDom emits `Environment.NewLine`, which these flags do
        // not control.) 4-space indent (SSDT convention);
        // `IncludeSemicolons` = true ensures every statement
        // terminates explicitly.
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

    /// The ONE shared pinned-options instance. The options object is
    /// construction-set and read-only thereafter: no consumer in this
    /// module mutates it post-construction, and the generator only READS
    /// options during `GenerateScript` — so sharing removes a ~25-property
    /// object build from EVERY rendered statement (and, on the data lane,
    /// from every rendered per-row Phase-2 UPDATE) without aliasing
    /// hazard. The GENERATOR stays per-call (it holds real per-render
    /// state).
    let private sharedPinnedOptions : SqlScriptGeneratorOptions = pinnedOptions ()

    /// ScriptDom's `Sql160ScriptGenerator.GenerateScript` emits the
    /// host's `Environment.NewLine` (CRLF on Windows, LF on Linux);
    /// `SqlScriptGeneratorOptions` exposes no newline-character axis.
    /// Pin to LF so emission is byte-identical across platforms — T1
    /// determinism is constructed at the boundary, not inherited from
    /// the host. A no-op on LF hosts; collapses CRLF (and any lone CR)
    /// to LF.
    let private pinNewlines (text: string) : string =
        text.Replace("\r\n", "\n").Replace("\r", "\n")

    // -----------------------------------------------------------------------
    // DECISIONS 2026-07-18 (#669 M-8 / EF-19) — the physical-identifier
    // rewrite for computed-column and CHECK-constraint expressions.
    // -----------------------------------------------------------------------

    /// AST visitor: renames every column reference whose LAST identifier
    /// part matches a PHYSICAL column name (case-insensitive) to its
    /// LOGICAL name. Counts the rewrites so an untouched expression can
    /// return verbatim.
    type private PhysicalIdentifierRewriter(physicalToLogical: Map<string, string>) =
        inherit TSqlFragmentVisitor()
        member val Rewritten = 0 with get, set
        override this.Visit(node: ColumnReferenceExpression) : unit =
            match Option.ofObj node.MultiPartIdentifier with
            | Some mpi when mpi.Identifiers.Count > 0 ->
                let last = mpi.Identifiers.[mpi.Identifiers.Count - 1]
                match Option.ofObj last.Value with
                | Some v ->
                    match Map.tryFind (v.ToUpperInvariant()) physicalToLogical with
                    | Some logical when v <> logical ->
                        last.Value <- logical
                        this.Rewritten <- this.Rewritten + 1
                    | _ -> ()
                | None -> ()
            | _ -> ()

    /// The rewriter's own thread-local parser (`TSql160Parser` is not
    /// thread-safe; same discipline as `ScriptDomBuild`'s).
    let private rewriterParser =
        new System.Threading.ThreadLocal<TSql160Parser>(
            fun () -> TSql160Parser(initialQuotedIdentifiers = false))

    /// Rewrite PHYSICAL column identifiers inside a computed-column or
    /// CHECK-constraint expression to their LOGICAL names (DECISIONS
    /// 2026-07-18; #669 M-8 / EF-19). `sys.computed_columns.definition`
    /// and `sys.check_constraints.definition` carry the source's physical
    /// identifiers; the emitted table renames every column to its logical
    /// name, so an unrewritten expression references columns that do not
    /// exist in the published table — a hard deploy failure on a
    /// case-sensitive database (`Msg 207`). The expression parses through
    /// the pinned parser (scalar first, boolean predicate second), the
    /// visitor renames matching column references, and the pinned
    /// generator re-renders. The ORIGINAL text returns verbatim when
    /// nothing matched (byte-stability for already-logical expressions)
    /// and when neither parse succeeds (the estate board names the
    /// unrewritable residue; the emission stays faithful to the source
    /// text rather than guessing).
    let rewritePhysicalIdentifiers (physicalToLogical: Map<string, string>) (expressionSql: string) : string =
        if Map.isEmpty physicalToLogical || System.String.IsNullOrWhiteSpace expressionSql then expressionSql
        else
            let parseFragment () : TSqlFragment option =
                use scalarReader = new System.IO.StringReader(expressionSql)
                let scalar, _ = rewriterParser.Value.ParseExpression(scalarReader)
                match Option.ofObj scalar with
                | Some s -> Some (s :> TSqlFragment)
                | None ->
                    use boolReader = new System.IO.StringReader(expressionSql)
                    let boolean, _ = rewriterParser.Value.ParseBooleanExpression(boolReader)
                    Option.ofObj boolean |> Option.map (fun b -> b :> TSqlFragment)
            match parseFragment () with
            | None -> expressionSql
            | Some fragment ->
                let visitor = PhysicalIdentifierRewriter(physicalToLogical)
                fragment.Accept(visitor)
                if visitor.Rewritten = 0 then expressionSql
                else
                    let generator = Sql160ScriptGenerator(sharedPinnedOptions)
                    let mutable text : string | null = null
                    generator.GenerateScript(fragment, &text)
                    match Option.ofObj text with
                    | Some t when not (System.String.IsNullOrWhiteSpace t) -> pinNewlines t
                    | _ -> expressionSql

    // -----------------------------------------------------------------------
    // Family 4e (DECISIONS 2026-07-18; #669 EF-20) — the trigger-definition
    // GATE. The rewrite itself is the passes' half (`LogicalTableEmission`
    // v2 rewrites table references; `LogicalColumnEmission` v3 the owning
    // kind's columns — Core-side, token grain). This layer owns the two
    // refusal predicates the ScriptDom-free Core cannot: (a) the body does
    // not PARSE, so no rewrite can be trusted; (b) the body still carries
    // an OutSystems physical identifier (`OSUSR_*` / `OSSYS_*`) after the
    // passes ran — the rewrite missed it, and the published body would
    // target tables that do not exist in the renamed estate. The estate
    // board's `EmissionTriggerUnrewritten` names the same two shapes.
    // -----------------------------------------------------------------------

    /// Parse a trigger definition as a full T-SQL script. `Ok` when it
    /// parses clean; `Error` carries the first parse error. Shared by the
    /// emission gate (refusal) and the estate board's detector, so a red
    /// finding and a refused publish are the same predicate.
    let tryParseTriggerDefinition (definition: string) : Result<unit, string> =
        if System.String.IsNullOrWhiteSpace definition then Error "empty trigger definition"
        else
            use reader = new System.IO.StringReader(definition)
            let fragment, errors = rewriterParser.Value.Parse(reader)
            if not (isNull (box errors)) && errors.Count > 0 then
                let e = errors.[0]
                Error (sprintf "line %d: %s" e.Line e.Message)
            elif Option.ofObj fragment |> Option.isSome then Ok ()
            else Error "trigger definition did not parse"

    let private ossysResiduePattern =
        System.Text.RegularExpressions.Regex(
            "\\b(?:OSUSR|OSSYS)_[A-Za-z0-9_]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ||| System.Text.RegularExpressions.RegexOptions.Compiled)

    /// The first OutSystems physical identifier still present in a
    /// trigger definition after the logical-emission passes, `None` when
    /// the body is clean. An `OSUSR_` / `OSSYS_` token in the published
    /// estate is residue by construction — those prefixes exist only in
    /// the source's physical layer.
    let firstPhysicalResidue (definition: string) : string option =
        if System.String.IsNullOrWhiteSpace definition then None
        else
            let m = ossysResiduePattern.Match(definition)
            if m.Success then Some m.Value else None

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
        let generator = Sql160ScriptGenerator(sharedPinnedOptions)
        let mutable text : string | null = null
        generator.GenerateScript(stmt :> TSqlFragment, &text)
        text |> Option.ofObj |> Option.defaultValue "" |> pinNewlines

    /// Generate T-SQL text for a LIST of typed `TSqlStatement`s rendered as
    /// ONE batch — the statements are placed in a single `TSqlBatch` inside a
    /// fresh `TSqlScript`, so the pinned generator emits the inter-statement
    /// `;` terminators uniformly (the `IncludeSemicolons` axis governs the
    /// whole batch, not a per-statement render). This is the canonical render
    /// path for `ScriptDomBuild.buildAtomicBatch`'s two top-level statements:
    /// rendering them as one batch (rather than `generateOne` joined by text)
    /// guarantees the inner MERGE inside the `TRY` block is `;`-terminated by
    /// the generator — the bare-MERGE-terminator quirk that bites
    /// `generateOne` does not apply inside a multi-statement batch (verified by
    /// deploy, not assumed). Callers append the `GO` batch separator (a client
    /// directive ScriptDom does not model) at the terminal boundary.
    let generateBatch (statements: TSqlStatement list) : string =
        use _ = Bench.scope "scriptDom.generateBatch"
        let script = TSqlScript()
        let batch = TSqlBatch()
        for stmt in statements do
            batch.Statements.Add(stmt)
        script.Batches.Add(batch)
        let generator = Sql160ScriptGenerator(sharedPinnedOptions)
        let mutable text : string | null = null
        generator.GenerateScript(script :> TSqlFragment, &text)
        text |> Option.ofObj |> Option.defaultValue "" |> pinNewlines

    /// Render a list of data statements (`Statement.Merge` / `Statement.Update`
    /// / `Statement.SetIdentityInsert`) as ONE deploy-ready `GO` batch — the
    /// data lane's SINGLE terminal-text boundary. Each statement is rendered via
    /// its typed AST and `;`-terminated (`generateOne` omits the trailing `;`
    /// SQL Server requires after a MERGE), framed by `\n`, closed by the sqlcmd
    /// `GO` separator (no ScriptDom AST equivalent). The emitters hand typed
    /// `Statement` values and never compose SQL text themselves.
    ///
    /// BYTE-IDENTICAL to the prior per-emitter `String.Concat(generateOne …,
    /// ";\nGO\n")` framing: same per-statement `generateOne`, same `;` + `GO`
    /// literals, same `\n` joins — so the golden-locked inline data format is
    /// preserved. (Distinct from `generateBatch`, which renders a single
    /// `TSqlScript` whose generator-emitted inter-statement spacing differs from
    /// this hand-framing and would re-bless the goldens.)
    let renderDataBatch (statements: Statement list) : string =
        use _ = Bench.scope "scriptDom.renderDataBatch"
        let sb = StringBuilder()
        for s in statements do
            match ScriptDomBuild.buildStatement s with
            | Some frag ->
                // LINT-ALLOW: the data lane's terminal-text boundary — each typed
                // ScriptDom render gets SQL Server's required statement terminator
                // `;` (generateOne emits none on a single-statement render) + the
                // `\n` frame; StringBuilder is the BCL accumulation primitive, the
                // `;`/`\n` are statement framing, not composed SQL structure.
                sb.Append(generateOne frag).Append(";\n") |> ignore
            | None -> ()
        // LINT-ALLOW: the sqlcmd `GO` batch separator — not T-SQL syntax (no
        // ScriptDom AST equivalent), so the literal `GO` IS the canonical form
        // per BatchSplitter's `^GO$` recognition rule; appended once at the
        // terminal batch boundary.
        sb.Append("GO\n").ToString()

    /// The M22 atomic-deploy envelope OPENER — `SET XACT_ABORT ON; BEGIN
    /// TRANSACTION;` — rendered from typed nodes as one batch. MigrationRun's
    /// streaming deploy can't use `ScriptDomBuild.buildAtomicBatch` (it
    /// interleaves per-statement progress logging between open and commit), so
    /// it renders the envelope's open / commit / rollback control statements
    /// individually through these three helpers — the same typed primitives
    /// `buildAtomicBatch` composes, replacing the prior string literals.
    let renderAtomicEnvelopeOpen () : string =
        generateBatch
            [ ScriptDomBuild.buildSetXactAbort true :> TSqlStatement
              ScriptDomBuild.buildBeginTransaction () :> TSqlStatement ]

    /// `COMMIT TRANSACTION;` — the envelope's success close.
    let renderCommitTransaction () : string =
        generateOne (ScriptDomBuild.buildCommitTransaction () :> TSqlStatement)

    /// `IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;` — the envelope's CATCH-side
    /// rollback guard (a no-op when `XACT_ABORT` already rolled back).
    let renderRollbackIfActive () : string =
        generateOne (ScriptDomBuild.buildRollbackIfActive () :> TSqlStatement)

    /// Generate T-SQL text for any `TSqlFragment` sub-tree (data type
    /// references, identifiers, expressions) that doesn't constitute a
    /// full statement. Used by `Render.columnSqlType` (chapter-3.7
    /// slice β') so the SQL DDL type expression flows through
    /// ScriptDom's typed AST rather than being composed via
    /// `String.Concat` at the call site. Pillar 7 cash-out: the
    /// gold-standard library emits the structure; V2 delegates.
    ///
    /// **Perf:** instantiates a fresh `Sql160ScriptGenerator` per call.
    /// Per the chapter-3.5 ScriptDom adoption pattern (`generateOne`
    /// also instantiates per call), the pinned-options object is
    /// allocated per call as well; ScriptDom's generator state is
    /// stateful so sharing across calls would alias options. Bench
    /// label `scriptDom.generateDataType` surfaces the per-call cost
    /// in the rollup table.
    let generateDataType (fragment: DataTypeReference) : string =
        use _ = Bench.scope "scriptDom.generateDataType"
        let generator = Sql160ScriptGenerator(sharedPinnedOptions)
        let mutable text : string | null = null
        generator.GenerateScript(fragment :> TSqlFragment, &text)
        text |> Option.ofObj |> Option.defaultValue "" |> pinNewlines

    /// Render a comment line through the SSDT-canonical inline
    /// comment form. Per chapter 3.5 deep audit (2026-05-09):
    /// considered alternatives —
    ///   - ScriptDom typed `MultilineCommentTrivia` fragment:
    ///     rejected. The trivia API requires splicing into a
    ///     `TSqlScript`'s token stream, which is heavier than
    ///     producing a single `-- text` line; ScriptDom's
    ///     `TSqlScript` doesn't model comment-only batches.
    ///   - `String.Concat("-- ", text)`: rejected. Per the chapter-
    ///     3.5 supreme discipline, `String.Concat` is the
    ///     least-defensive option.
    ///   - **Adopted: `text.Insert(0, "-- ")`** — BCL `string.Insert
    ///     (int startIndex, string value)` returns a new string
    ///     with the prefix inserted. Same complexity (O(N) one
    ///     allocation) as String.Concat but uses the BCL string-
    ///     mutation primitive (which does NOT mutate the original;
    ///     `string.Insert` returns a new value). Defensive: the
    ///     primitive's purpose is exactly this prefix-insertion
    ///     case.
    let private commentLine (text: string) : string =
        text.Insert(0, "-- ")

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
                    // `Blank` / `Comment` are handled explicitly above, so the
                    // only statement reaching this `None` is a `CreateTrigger`
                    // whose body failed to parse (H-019). M2 (THE VECTOR, Wave
                    // 0): the prior bare `()` was a SILENT drop — the named-
                    // erasure law forbids it. Emit an in-band marker comment
                    // naming the closed tolerance
                    // `ToleratedDivergence.TriggerBodyUnparsedDropped` (Schema
                    // OpenGap) so this text path matches `Render.toSql` and the
                    // `.dacpac` refusal (NM-24). Static phrase only.
                    match stmt with
                    | CreateTrigger _ ->
                        sb.Append(commentLine "ToleratedDivergence.TriggerBodyUnparsedDropped: a CreateTrigger body failed to parse and was omitted from this SSDT text artifact (the .dacpac path refuses outright, NM-24).") |> ignore
                    | _ -> ()
        sb.ToString()

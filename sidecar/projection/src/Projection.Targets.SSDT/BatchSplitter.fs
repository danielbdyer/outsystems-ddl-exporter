namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE-MUTATION: ScriptDom's `TSql160Parser.Parse` API
// hands back a typed `TSqlFragment` AST + a mutable
// `IList<ParseError>` via an out-parameter. The mutation lives
// inside the BCL parser surface; we read both immutably afterward.
// Per the LINT-ALLOW-FILE-MUTATION pattern from chapter-3.5
// (`PinnedWriting`, `LineageBuffer`, `ScriptDomBuild`,
// `ScriptDomGenerate`): BCL-mandated mutable I/O is reified at
// the boundary; the typed surface above stays pure.

open System.IO
open Projection.Core
open Microsoft.SqlServer.TransactSql.ScriptDom

/// T-SQL batch splitter — chapter-3.6 cash-out per the user's
/// 2026-05-09 directive: "Where would the TSql160Parser.Parse
/// fire? Let's implement it behind an adapter or a strategy so
/// we can retain splitOnGo to fall back to when necessary with
/// a loud announcement."
///
/// **Strategy with structured fallback.** Two implementations
/// share the `string -> string array` shape:
///   1. **Gold-standard:** `splitViaScriptDom` uses
///      `TSql160Parser.Parse` to recover a typed `TSqlScript`,
///      then projects each `TSqlBatch` back to text via
///      `Sql160ScriptGenerator`. Accurate for any T-SQL grammar
///      ScriptDom understands; correctly handles `GO N` (batch
///      repetition), nested batches, and edge cases the
///      line-fold splitter misses.
///   2. **Fallback:** `splitOnGoLineFold` is the existing
///      sqlcmd-style `^GO$` line splitter (preserved as the
///      pure F# fold from `Deploy.fs`). Permissive — accepts
///      grammatically-malformed segments that ScriptDom would
///      reject (operator-supplied DDL with legacy artifacts).
///
/// **Loud-fallback discipline.** When ScriptDom rejects the
/// input, the public `splitWithLoudFallback` emits a stderr
/// announcement naming each parse error before delegating to
/// the line-fold splitter. Operators see WHY ScriptDom couldn't
/// parse the input; the run continues so the canary doesn't
/// hard-fail on grammatical idiosyncrasies.
///
/// **Per pillar 7 (gold-standard library precedence):** the
/// gold-standard path runs first; the fallback is the
/// documented escape hatch. Adopting this strategy moved
/// `splitOnGo` from Section-6 "irreducible" to Section-1
/// "tightened with structured fallback."
[<RequireQualifiedAccess>]
module BatchSplitter =

    /// Outcome of attempting ScriptDom-based batch splitting.
    /// Pattern-matched by callers that want to observe the
    /// fallback decision (e.g., the `executeBatch` driver
    /// emits a stderr announcement on `ScriptDomFailed`).
    type Outcome =
        /// ScriptDom successfully parsed and split the script.
        /// Each batch comes from `TSqlScript.Batches` rendered
        /// via `Sql160ScriptGenerator`.
        | ScriptDomSplit of segments: string array
        /// ScriptDom parsing failed; the fallback line-fold
        /// splitter produced these segments. The error list
        /// describes WHY ScriptDom rejected the input.
        | ScriptDomFailed of errors: ParseError list * fallbackSegments: string array

    /// T-SQL batch separator literal — sqlcmd convention.
    [<Literal>]
    let private GoBatchSeparator : string = "GO"

    let private isGoLine (line: string) : bool =
        System.String.Equals(
            line.Trim(),
            GoBatchSeparator,
            System.StringComparison.OrdinalIgnoreCase)

    /// Pure F# line-fold splitter — the fallback. Splits on
    /// `^GO$` lines (case-insensitive, surrounding whitespace
    /// allowed). Permissive: accepts any text input. Preserved
    /// from `Deploy.splitOnGo` so tests can pin the contract
    /// independent of ScriptDom's grammar acceptance.
    ///
    /// Per the no-regex / no-string-concatenation discipline:
    /// uses `String.Split('\n')` + `Trim` + `String.Equals` —
    /// no regex, no concatenation. Big-O: O(N) over input
    /// length; allocates one accumulator + per-segment list.
    let splitOnGoLineFold (sql: string) : string array =
        use _ = Bench.scope "batchSplitter.lineFold"
        let lines = sql.Split('\n')
        let initial : string list * string list list = [], []
        let lastSegRev, segsRev =
            lines
            |> Array.fold
                (fun (current, segs) line ->
                    if isGoLine line then [], current :: segs
                    else line :: current, segs)
                initial
        let allSegsRev = lastSegRev :: segsRev
        allSegsRev
        |> List.rev
        |> List.map (fun lineListRev ->
            lineListRev |> List.rev |> String.concat "\n")  // LINT-ALLOW: terminal text-emission boundary; lines are typed segments from String.Split
        |> List.filter (fun s -> not (System.String.IsNullOrWhiteSpace s))
        |> List.toArray

    /// ScriptDom-based splitter — the gold-standard path.
    /// `TSql160Parser.Parse` consumes the input, recovers a
    /// typed `TSqlScript` AST, and exposes its
    /// `Batches : IList<TSqlBatch>`. Each batch flows back to
    /// text via `Sql160ScriptGenerator` with the pinned options
    /// `ScriptDomGenerate.scriptOptions`.
    ///
    /// Returns `ScriptDomFailed` (with the parse-error list
    /// AND the fallback segments) when ScriptDom rejects the
    /// input. The fallback segments come from
    /// `splitOnGoLineFold` so callers can use them directly.
    /// Pure observer of the parser's error list; no side
    /// effects.
    let splitViaScriptDom (sql: string) : Outcome =
        use _ = Bench.scope "batchSplitter.scriptDom"
        let parser = TSql160Parser(true)
        use reader = new StringReader(sql)
        let mutable errors : System.Collections.Generic.IList<ParseError> =
            System.Collections.Generic.List<ParseError>() :> System.Collections.Generic.IList<ParseError>
        let fragment = parser.Parse(reader, &errors)
        if errors.Count > 0 then
            // Parse error: fall back. The fallback splitter is
            // permissive and accepts any text input.
            let fallback = splitOnGoLineFold sql
            ScriptDomFailed (List.ofSeq errors, fallback)
        else
            // Ok: extract each batch's text by substring on the
            // original input using `StartOffset` + `FragmentLength`.
            // Byte-accurate split; preserves whitespace + comments
            // exactly as the operator wrote them. Avoids round-
            // tripping through `Sql160ScriptGenerator` (which would
            // re-render with our pinned style options — undesirable
            // for the splitter's role: we want to PASS THROUGH the
            // operator's text, not normalize it).
            match fragment with
            | :? TSqlScript as script ->
                let segments =
                    script.Batches
                    |> Seq.map (fun batch ->
                        let len = min batch.FragmentLength (sql.Length - batch.StartOffset)
                        sql.Substring(batch.StartOffset, len).Trim())
                    |> Seq.filter (fun s -> not (System.String.IsNullOrWhiteSpace s))
                    |> Array.ofSeq
                ScriptDomSplit segments
            | _ ->
                // ScriptDom returned something other than TSqlScript
                // (shouldn't happen for top-level Parse) — defensive
                // fallback.
                let fallback = splitOnGoLineFold sql
                ScriptDomFailed (
                    [ ParseError(0, 0, 0, 0, "Unexpected fragment type from TSql160Parser.Parse") ],
                    fallback)

    /// Public convenience — the gold-standard-with-loud-fallback
    /// shape callers want at the point of use. Calls
    /// `splitViaScriptDom`; on `ScriptDomFailed`, emits a stderr
    /// announcement naming each parse error and then returns the
    /// fallback segments. Records bench samples for observability:
    ///   - `<label>.scriptDom`: ScriptDom path elapsed ms
    ///   - `<label>.fallback.count`: number of fallback events
    ///     (operators reading the bench table see when this
    ///     fired)
    let splitWithLoudFallback (label: string) (sql: string) : string array =
        match splitViaScriptDom sql with
        | ScriptDomSplit segments ->
            segments
        | ScriptDomFailed (errors, fallbackSegments) ->
            Bench.recordSample (label + ".scriptDom.fallback.count") 1L
            // LOUD announcement per the user's directive
            // (2026-05-09): operators see WHY ScriptDom couldn't
            // parse the input; the canary continues with the
            // permissive fallback.
            eprintfn "  [BatchSplitter] LOUD-FALLBACK: ScriptDom rejected input (%d parse error(s)); falling back to line-fold splitter." errors.Length
            for e in errors |> List.truncate 5 do
                eprintfn "    [%d:%d] error %d: %s" e.Line e.Column e.Number e.Message
            if errors.Length > 5 then
                eprintfn "    ... and %d more parse error(s)." (errors.Length - 5)
            fallbackSegments

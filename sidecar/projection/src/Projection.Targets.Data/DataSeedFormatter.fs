namespace Projection.Targets.Data

// LINT-ALLOW-FILE: terminal text-emission post-processor at the data lane's
//   published-file boundary (`DataEmissionComposer.renderArtifactInTopoOrder`).
//   ScriptDom's `Sql160ScriptGenerator` renders a MERGE's inline `USING (VALUES …)`
//   on a single line with no option to break it per row, and models neither the
//   sqlcmd banner / `SET NOCOUNT ON` framing nor the per-entity comments V1's
//   `StaticEntitySeedScriptGenerator` emitted. This module reflows the rendered
//   text AFTER ScriptDom emits it — the SAME sanctioned pattern as
//   `Projection.Targets.SSDT.ConstraintFormatter` (NM-38), whose four-question
//   pillar-7 analysis applies identically: (1) the use-case library IS ScriptDom;
//   (2) already in the codebase; (3) the cost of coercing ScriptDom to this shape
//   is "there is no option"; (4) the structural reason — a MERGE derived-table
//   `(VALUES …)` has no multi-line generator axis. Text post-processing IS the
//   canonical fit.

open System
open System.Text

/// V1-parity data-lane formatting (2026-07-19). Reformats the composer's compact
/// per-kind MERGE/UPDATE text into V1's `StaticEntitySeedScriptGenerator` shape:
/// a file banner + `SET NOCOUNT ON;`, per-module banners, a `-- <Entity> (N rows)`
/// header before each block, and the `USING (VALUES …)` list one row per line.
/// Applied at the published-file boundary only; the parallel-deploy path keeps the
/// compact form.
[<RequireQualifiedAccess>]
module DataSeedFormatter =

    /// Operator-toggleable mode mirroring `ConstraintFormatter.Mode`. `Enabled`
    /// is the production default (V1 readable shape); `Disabled` passes the
    /// composer's compact concatenation through byte-for-byte (the diagnostic /
    /// bisect opt-out).
    type Mode =
        | Enabled
        | Disabled

    /// One kind's contribution to a lane, in topological position. `Phase1` /
    /// `Phase2` are the kind's already-rendered `RenderedPhase1` / `RenderedPhase2`
    /// (each terminated by `;\nGO\n`, or empty). `Module` / `Entity` are the
    /// operator-readable logical names; `RowCount` is the Phase-1 row count.
    type SeedBlock =
        { Module   : string
          Entity   : string
          RowCount : int
          Phase1   : string
          Phase2   : string }

    // LF, not Environment.NewLine — the reflowed text IS the emitted artifact, so
    // its newline must be byte-identical across platforms (T1). Mirrors
    // `ConstraintFormatter.newLine`.
    [<Literal>]
    let private nl = "\n"

    // 76-char horizontal rule (the `=` run V1's banners use).
    [<Literal>]
    let private ruleEq = "============================================================================"

    /// Compose parts with no separator — the file-boundary join primitive (the
    /// whole module is `LINT-ALLOW-FILE`; F#'s `String.concat ""` reads cleanest
    /// here and dodges the >4-arg `String.Concat` overload).
    let private cat (parts: string list) : string = String.concat "" parts

    /// Split a VALUES row-list (`(r1), (r2), …`) into its top-level `(…)` tuples —
    /// depth / quote / bracket-aware so a comma inside a tuple, a `N'…'` literal
    /// (with `''` escape), or a `[bracketed]` identifier never splits. Mirrors
    /// `ConstraintFormatter.topLevelMatches`' scanner.
    let private splitTopLevelTuples (s: string) : string list =
        let results = System.Collections.Generic.List<string>()
        let sb = StringBuilder()
        let mutable depth = 0
        let mutable inQuote = false
        let mutable inBracket = false
        let mutable i = 0
        while i < s.Length do
            let c = s.[i]
            if inQuote then
                sb.Append(c) |> ignore
                if c = '\'' then
                    if i + 1 < s.Length && s.[i + 1] = '\'' then
                        sb.Append(s.[i + 1]) |> ignore
                        i <- i + 1
                    else inQuote <- false
            elif inBracket then
                sb.Append(c) |> ignore
                if c = ']' then inBracket <- false
            else
                match c with
                | '\'' -> inQuote <- true;    sb.Append(c) |> ignore
                | '['  -> inBracket <- true;  sb.Append(c) |> ignore
                | '('  -> depth <- depth + 1; sb.Append(c) |> ignore
                | ')'  -> depth <- depth - 1; sb.Append(c) |> ignore
                | ','  when depth = 0 -> results.Add(sb.ToString()); sb.Clear() |> ignore
                | _    -> sb.Append(c) |> ignore
            i <- i + 1
        if sb.Length > 0 then results.Add(sb.ToString())
        results
        |> Seq.map (fun t -> t.Trim())
        |> Seq.filter (fun t -> t.Length > 0)
        |> List.ofSeq

    [<Literal>]
    let private usingMarker = "USING (VALUES "

    /// Reflow a rendered MERGE's inline `USING (VALUES (r1), (r2), …) AS …` into
    /// V1's multi-line block:
    /// ```
    /// USING
    /// (
    ///     VALUES
    ///         (r1),
    ///         (r2)
    /// ) AS …
    /// ```
    /// Returns the text unchanged when there is no inline `USING (VALUES …)` — a
    /// staged `#temp` MERGE (`USING [#seed_…]`, whose `INSERT` batches ScriptDom
    /// already renders one row per line) or a Phase-2 `UPDATE`. Reflows the FIRST
    /// occurrence (the default `Standard` MERGE has exactly one; the non-default
    /// `ValidateBeforeApply` guard is not golden-locked).
    let reflowMergeValues (sql: string) : string =
        let idx = sql.IndexOf(usingMarker, StringComparison.Ordinal)
        if idx < 0 then sql
        else
            // The '(' that opens the VALUES constructor sits right after "USING ".
            let openParen = idx + "USING ".Length
            // Scan for its matching ')' (depth / quote / bracket-aware).
            let mutable depth = 0
            let mutable inQuote = false
            let mutable inBracket = false
            let mutable j = openParen
            let mutable closeIdx = -1
            while j < sql.Length && closeIdx < 0 do
                let c = sql.[j]
                if inQuote then
                    if c = '\'' then
                        if j + 1 < sql.Length && sql.[j + 1] = '\'' then j <- j + 1
                        else inQuote <- false
                elif inBracket then
                    if c = ']' then inBracket <- false
                else
                    match c with
                    | '\'' -> inQuote <- true
                    | '['  -> inBracket <- true
                    | '('  -> depth <- depth + 1
                    | ')'  ->
                        depth <- depth - 1
                        if depth = 0 then closeIdx <- j
                    | _    -> ()
                j <- j + 1
            if closeIdx < 0 then sql   // unbalanced — leave verbatim (never expected)
            else
                let rowlistStart = idx + usingMarker.Length
                let rowlist = sql.Substring(rowlistStart, closeIdx - rowlistStart)
                let prefix  = sql.Substring(0, idx)
                let suffix  = sql.Substring(closeIdx)   // ") AS [Source](…) ON …"
                match splitTopLevelTuples rowlist with
                | [] -> sql   // no tuples parsed — leave verbatim
                | tuples ->
                    let body =
                        tuples
                        |> List.map (fun t -> cat [ "        "; t ])   // 8-space indent (V1)
                        |> String.concat (cat [ ","; nl ])
                    cat [ prefix; "USING"; nl; "("; nl; "    VALUES"; nl; body; nl; suffix ]

    let private entityHeader (b: SeedBlock) : string =
        let plural = if b.RowCount = 1 then "" else "s"
        cat [ "-- "; b.Entity; " ("; string b.RowCount; " row"; plural; ")"; nl ]

    let private moduleBanner (laneTitle: string) (moduleName: string) : string =
        cat [ "-- "; ruleEq; nl; "-- "; laneTitle; ": "; moduleName; nl; "-- "; ruleEq; nl; nl ]

    let private fileBanner (laneTitle: string) : string =
        cat [ "/* "; ruleEq; nl
              "   "; laneTitle; " — generated by the projection engine."; nl
              "   Idempotent upserts, one block per entity, in FK-safe order."; nl
              "   "; ruleEq; " */"; nl; nl
              "SET NOCOUNT ON;"; nl; "GO"; nl; nl ]

    /// Assemble a lane's text from its per-kind blocks (in topological order).
    /// `Disabled` reproduces the composer's prior compact output BYTE-FOR-BYTE
    /// (all Phase-1 texts, then all Phase-2 texts, concatenated). `Enabled` emits
    /// the V1 shape: file banner + `SET NOCOUNT ON;`, per-module banners, a
    /// `-- <Entity> (N rows)` header + the reflowed MERGE per block (Phase-1), then
    /// all Phase-2 texts (reflowed; an `UPDATE` has no VALUES to reflow) — the
    /// global Phase-1-before-Phase-2 ordering multi-kind cycles need is preserved.
    /// An all-empty lane (no populated blocks) renders `""` so the pipeline's
    /// `IsNullOrWhiteSpace` lane-file filter drops it, exactly as the compact path
    /// did.
    let renderLane (mode: Mode) (laneTitle: string) (blocks: SeedBlock list) : string =
        match mode with
        | Disabled ->
            let phase1 = blocks |> List.map (fun b -> b.Phase1)
            let phase2 = blocks |> List.map (fun b -> b.Phase2)
            Seq.append phase1 phase2 |> String.concat ""
        | Enabled ->
            let populated = blocks |> List.filter (fun b -> not (String.IsNullOrWhiteSpace b.Phase1))
            if List.isEmpty populated then ""
            else
                let sb = StringBuilder()
                sb.Append(fileBanner laneTitle) |> ignore
                let mutable curModule : string option = None
                for b in populated do
                    if curModule <> Some b.Module then
                        sb.Append(moduleBanner laneTitle b.Module) |> ignore
                        curModule <- Some b.Module
                    sb.Append(entityHeader b) |> ignore
                    sb.Append(reflowMergeValues b.Phase1) |> ignore
                // Phase-2 (global, after all Phase-1). Empty for the static lane.
                for b in populated do
                    if not (String.IsNullOrWhiteSpace b.Phase2) then
                        sb.Append(reflowMergeValues b.Phase2) |> ignore
                sb.ToString()

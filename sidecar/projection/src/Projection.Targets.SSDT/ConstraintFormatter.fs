namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE: terminal text-emission post-processor at the
//   `Render.toText` boundary. ScriptDom's `Sql160ScriptGenerator`
//   has no per-constraint formatting option that produces V1's
//   column-inline multi-line shape; subclassing the generator was
//   considered and rejected (visibility lift cost too high for the
//   single consumer). This module operates on the rendered text
//   after ScriptDom emits it, mirroring V1's `ConstraintFormatter`
//   (admire entry below). Per pillar 7 amendment four-question
//   analysis: (1) the use-case-specific library IS ScriptDom; (2)
//   already in codebase; (3) cost of using it for THIS output
//   shape is "rewrite the formatter" (too high); (4) structural
//   reason — ScriptDom's generator emits column-inline constraints
//   on the same line as the column definition with no documented
//   way to split them. Text post-processing IS the canonical fit.
//
// Carbon-copied from V1's `src/Osm.Smo/PerTableEmission/ConstraintFormatter.cs`
// (2026-05-23). Per V2-self-containment + carbon-copy editorial
// inheritance discipline: file-header citation + ADMIRE entry +
// refactor freely from here. The F# port adapts the input pattern
// to V2's ScriptDom-shaped output (CONSTRAINT inline on column
// line for PK / DEFAULT; CONSTRAINT on its own line at table level
// for FK) — V1's formatter expected SMO's shape (CONSTRAINT
// already on its own line for both).

open System
open System.Text
open Projection.Core

/// Multi-line column-inline constraint layout. Reformats the
/// ScriptDom-emitted single-line column-inline constraints
/// (`[Col] TYPE NOT NULL CONSTRAINT [name] PRIMARY KEY CLUSTERED,`)
/// into V1's elegant three-line shape:
///
/// ```
///     [Col]    TYPE     NOT NULL
///         CONSTRAINT [name]
///             PRIMARY KEY CLUSTERED,
/// ```
///
/// Three indentation levels: column (4), constraint name (8),
/// constraint body (12). ON DELETE / ON UPDATE clauses on FOREIGN
/// KEY get an additional 4 spaces (16) and emit on separate lines.
/// DEFAULT collapses into the constraint-body line.
///
/// **Scope.** Recognises three patterns:
///   1. Column-inline PRIMARY KEY: `[col] type ... CONSTRAINT [pk] PRIMARY KEY [CLUSTERED]`
///   2. Column-inline DEFAULT: `[col] type ... CONSTRAINT [df] DEFAULT (value)` (named)
///   3. Table-level FOREIGN KEY: `CONSTRAINT [fk] FOREIGN KEY (cols) REFERENCES table (cols) [ON DELETE x] [ON UPDATE y]`
///
/// **Non-scope.** Anonymous DEFAULT, CHECK constraints, ALTER
/// TABLE / ALTER INDEX statements pass through unchanged.
[<RequireQualifiedAccess>]
module ConstraintFormatter =

    let private newLine = Environment.NewLine

    /// True iff the (already-trimmed) text starts with `CONSTRAINT [`
    /// — a table-level constraint declaration in ScriptDom's output.
    let private startsWithConstraint (trimmed: string) : bool =
        trimmed.StartsWith("CONSTRAINT [", StringComparison.OrdinalIgnoreCase)

    /// True iff the (already-trimmed) text starts with `[` (a bracketed
    /// column identifier) — a column-definition line in ScriptDom's
    /// CREATE TABLE output.
    let private startsWithColumnIdentifier (trimmed: string) : bool =
        trimmed.StartsWith("[", StringComparison.Ordinal)

    /// Find the index of ` CONSTRAINT [` in a column line. Returns
    /// -1 when no inline constraint is present.
    let private inlineConstraintIndex (line: string) : int =
        line.IndexOf(" CONSTRAINT [", StringComparison.OrdinalIgnoreCase)

    /// Extract leading whitespace from a line (the column indent).
    let private indentOf (line: string) : string =
        let trimmed = line.TrimStart()
        line.Substring(0, line.Length - trimmed.Length)

    /// Strip trailing comma from a line; returns (without-comma, ",")
    /// or (line, "") when no trailing comma.
    let private splitTrailingComma (line: string) : string * string =
        if line.EndsWith(",", StringComparison.Ordinal) then
            line.Substring(0, line.Length - 1).TrimEnd(), ","
        else
            line, ""

    // ---------------------------------------------------------------
    // Column-inline PRIMARY KEY: split the column line into three.
    //
    // Input:  `    [Id]    INT    NOT NULL CONSTRAINT [PK_*] PRIMARY KEY CLUSTERED,`
    // Output: `    [Id]    INT    NOT NULL\n`
    //         `        CONSTRAINT [PK_*]\n`
    //         `            PRIMARY KEY CLUSTERED,\n`
    // ---------------------------------------------------------------
    let private formatColumnInlinePrimaryKey
        (sb: StringBuilder)
        (line: string)
        (constraintIdx: int)
        : bool =
        // working: the constraint-segment (e.g. "CONSTRAINT [PK_*] PRIMARY KEY CLUSTERED,")
        let columnPart = line.Substring(0, constraintIdx)
        let constraintWithComma = line.Substring(constraintIdx + 1).TrimEnd()
        let constraintPart, trailingComma = splitTrailingComma constraintWithComma
        let primaryKeyIdx =
            constraintPart.IndexOf("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
        if primaryKeyIdx < 0 then
            false
        else
            let constraintName =
                constraintPart.Substring(0, primaryKeyIdx).TrimEnd()
            let primaryKeyBody = constraintPart.Substring(primaryKeyIdx)
            let columnIndent = indentOf line
            let nameIndent = columnIndent + "    "  // LINT-ALLOW: terminal-text-emission indentation; 4-space convention from V1's ConstraintFormatter; mirrors V1 carbon-copy
            let bodyIndent = columnIndent + "        "  // LINT-ALLOW: terminal-text-emission indentation; same V1 convention
            sb.Append(columnPart).Append(newLine) |> ignore
            sb.Append(nameIndent).Append(constraintName).Append(newLine) |> ignore
            sb.Append(bodyIndent).Append(primaryKeyBody).Append(trailingComma).Append(newLine) |> ignore
            true

    // ---------------------------------------------------------------
    // Column-inline DEFAULT: split into two indented lines.
    //
    // Input:  `    [IsActive] BIT NOT NULL CONSTRAINT [DF_*] DEFAULT 1,`
    // Output: `    [IsActive] BIT NOT NULL\n`
    //         `        CONSTRAINT [DF_*] DEFAULT 1,\n`
    // ---------------------------------------------------------------
    let private formatColumnInlineDefault
        (sb: StringBuilder)
        (line: string)
        (constraintIdx: int)
        : bool =
        let columnPart = line.Substring(0, constraintIdx)
        let constraintWithComma = line.Substring(constraintIdx + 1).TrimEnd()
        let constraintPart, trailingComma = splitTrailingComma constraintWithComma
        if not (constraintPart.IndexOf("DEFAULT", StringComparison.OrdinalIgnoreCase) > 0) then
            false
        else
            let columnIndent = indentOf line
            let constraintIndent = columnIndent + "    "  // LINT-ALLOW: same V1 4-space convention
            sb.Append(columnPart).Append(newLine) |> ignore
            sb.Append(constraintIndent).Append(constraintPart).Append(trailingComma).Append(newLine) |> ignore
            true

    // ---------------------------------------------------------------
    // Table-level FOREIGN KEY: split into multi-line shape.
    //
    // Input:  `    CONSTRAINT [FK_*] FOREIGN KEY ([col]) REFERENCES [s].[t] ([id]) ON DELETE CASCADE ON UPDATE NO ACTION`
    // Output: `    CONSTRAINT [FK_*]\n`
    //         `        FOREIGN KEY ([col]) REFERENCES [s].[t] ([id])\n`
    //         `            ON DELETE CASCADE\n`
    //         `            ON UPDATE NO ACTION,\n`
    //
    // Mirrors V1's `ConstraintFormatter.AppendFormattedForeignKey`.
    // ---------------------------------------------------------------
    let private formatTableLevelForeignKey
        (sb: StringBuilder)
        (line: string)
        : bool =
        let indent = indentOf line
        let trimmed = line.TrimStart()
        let withoutComma, trailingComma = splitTrailingComma trimmed
        let fkIdx = withoutComma.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
        let refIdx = withoutComma.IndexOf("REFERENCES", StringComparison.OrdinalIgnoreCase)
        if fkIdx <= 0 || refIdx <= fkIdx then
            false
        else
            let constraintName = withoutComma.Substring(0, fkIdx).TrimEnd()
            let ownerPart = withoutComma.Substring(fkIdx + "FOREIGN KEY".Length, refIdx - fkIdx - "FOREIGN KEY".Length).Trim()
            let mutable referencesPart = withoutComma.Substring(refIdx).Trim()
            let onDeleteIdx = referencesPart.IndexOf("ON DELETE", StringComparison.OrdinalIgnoreCase)
            let onUpdateIdx = referencesPart.IndexOf("ON UPDATE", StringComparison.OrdinalIgnoreCase)
            let mutable onDeleteClause : string option = None
            let mutable onUpdateClause : string option = None
            if onDeleteIdx >= 0 && onUpdateIdx >= 0 then
                if onDeleteIdx < onUpdateIdx then
                    onDeleteClause <- Some (referencesPart.Substring(onDeleteIdx, onUpdateIdx - onDeleteIdx).TrimEnd())
                    onUpdateClause <- Some (referencesPart.Substring(onUpdateIdx).TrimEnd())
                    referencesPart <- referencesPart.Substring(0, onDeleteIdx).TrimEnd()
                else
                    onUpdateClause <- Some (referencesPart.Substring(onUpdateIdx, onDeleteIdx - onUpdateIdx).TrimEnd())
                    onDeleteClause <- Some (referencesPart.Substring(onDeleteIdx).TrimEnd())
                    referencesPart <- referencesPart.Substring(0, onUpdateIdx).TrimEnd()
            elif onDeleteIdx >= 0 then
                onDeleteClause <- Some (referencesPart.Substring(onDeleteIdx).TrimEnd())
                referencesPart <- referencesPart.Substring(0, onDeleteIdx).TrimEnd()
            elif onUpdateIdx >= 0 then
                onUpdateClause <- Some (referencesPart.Substring(onUpdateIdx).TrimEnd())
                referencesPart <- referencesPart.Substring(0, onUpdateIdx).TrimEnd()
            // V1 emission convention (carbon-copied): if exactly one of
            // ON DELETE / ON UPDATE is present, fill the other with the
            // explicit "NO ACTION" form so the deployed shape matches V1.
            // If both are NO ACTION, drop both (server default).
            let isNoActionClause (clauseOpt: string option) (prefix: string) : bool =
                match clauseOpt with
                | None -> false
                | Some c ->
                    c.Equals(prefix + " NO ACTION", StringComparison.OrdinalIgnoreCase)
            let hasDelete = onDeleteClause.IsSome
            let hasUpdate = onUpdateClause.IsSome
            if hasDelete <> hasUpdate then
                if not hasDelete then onDeleteClause <- Some "ON DELETE NO ACTION"
                if not hasUpdate then onUpdateClause <- Some "ON UPDATE NO ACTION"
            if isNoActionClause onDeleteClause "ON DELETE"
               && isNoActionClause onUpdateClause "ON UPDATE" then
                onDeleteClause <- None
                onUpdateClause <- None
            // V1 indentation convention (carbon-copied from
            // ConstraintFormatter.AppendFormattedForeignKey):
            //   CONSTRAINT line at `indent`        (table-level indent, 4 chars)
            //   FOREIGN KEY body at `indent + 4`   (8 chars)
            //   ON DELETE / ON UPDATE at `indent + 8` (12 chars)
            let bodyIndent = indent + "    "     // LINT-ALLOW: V1 4-space convention; ownerIndent in V1
            let clauseIndent = indent + "        "  // LINT-ALLOW: V1 8-space convention; ownerIndent + 4 in V1
            sb.Append(indent).Append(constraintName).Append(newLine) |> ignore
            sb.Append(bodyIndent).Append("FOREIGN KEY ").Append(ownerPart)
              .Append(" ").Append(referencesPart) |> ignore
            let hasClauses = onDeleteClause.IsSome || onUpdateClause.IsSome
            if hasClauses then
                sb.Append(newLine) |> ignore
                let clauses =
                    [ onDeleteClause; onUpdateClause ]
                    |> List.choose id
                let lastIdx = List.length clauses - 1
                clauses
                |> List.iteri (fun i clause ->
                    sb.Append(clauseIndent).Append(clause) |> ignore
                    if i = lastIdx then
                        sb.Append(trailingComma).Append(newLine) |> ignore
                    else
                        sb.Append(newLine) |> ignore)
            else
                sb.Append(trailingComma).Append(newLine) |> ignore
            true

    /// Detect which constraint shape, if any, the line carries; format
    /// accordingly into the StringBuilder. Returns true on
    /// reformat, false when the line passes through unchanged.
    let private tryFormatLine (sb: StringBuilder) (line: string) : bool =
        let trimmed = line.TrimStart()
        if startsWithColumnIdentifier trimmed then
            let constraintIdx = inlineConstraintIndex line
            if constraintIdx < 0 then false
            else
                // The substring AFTER " CONSTRAINT [" tells us PK vs DEFAULT.
                let after = line.Substring(constraintIdx + " CONSTRAINT ".Length)
                if after.IndexOf("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) >= 0 then
                    formatColumnInlinePrimaryKey sb line constraintIdx
                elif after.IndexOf("DEFAULT", StringComparison.OrdinalIgnoreCase) >= 0 then
                    formatColumnInlineDefault sb line constraintIdx
                else
                    false
        elif startsWithConstraint trimmed
             && trimmed.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) > 0
             && trimmed.IndexOf("REFERENCES", StringComparison.OrdinalIgnoreCase) > 0 then
            formatTableLevelForeignKey sb line
        else
            false

    /// Format a rendered T-SQL script. Reformats CREATE TABLE inline
    /// constraints into V1's multi-line elegant shape; passes other
    /// lines through unchanged.
    let format (script: string) : string =
        use _ = Bench.scope "ssdt.constraintFormatter.format"
        let lines = script.Split([| '\n' |], StringSplitOptions.None)
        let sb = StringBuilder(script.Length + 256)
        for raw in lines do
            // Normalize CR before the LF was stripped (handles \r\n input).
            let line =
                if raw.Length > 0 && raw.[raw.Length - 1] = '\r' then
                    raw.Substring(0, raw.Length - 1)
                else
                    raw
            if not (tryFormatLine sb line) then
                sb.Append(line).Append(newLine) |> ignore
        sb.ToString()

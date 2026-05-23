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

    /// Operator-toggleable mode mirroring `LogicalTableEmission.Mode`
    /// (slice D.1.a). `Enabled` is the production default — V2 emits
    /// V1's elegant multi-line shape. `Disabled` short-circuits to
    /// ScriptDom's compact column-inline default, preserving the
    /// operator's option to fall back for diagnostic / V1-parity
    /// reasons (e.g., a regression bisect that wants to inspect the
    /// raw ScriptDom output).
    type Mode =
        /// Reformat ScriptDom's column-inline constraints into V1's
        /// multi-line elegant shape (PK / DEFAULT / CHECK / FK with
        /// multi-level indentation + EXEC sys.sp_addextendedproperty
        /// multi-line wrap).
        | Enabled
        /// Pass-through; ScriptDom's compact default emission.
        | Disabled

    /// Slice D.3.b — registry metadata for the realization-layer
    /// overlay. Carries `OperatorIntent Emission` classification per
    /// pillar 9: the multi-line elegant shape IS the operator's
    /// emission-axis preference (V1 parity over ScriptDom default).
    /// Default-on production wiring IS the operator's intent in 2026
    /// per the same framing as `LogicalTableEmission` / `LogicalColumnEmission`.
    ///
    /// Metadata-only registration (no `RegisteredTransform.Run`)
    /// mirrors the SSDT emitter precedent — the `string -> string`
    /// shape doesn't fit the typed `RegisteredTransform<'In, 'Out>
    /// .Run : 'In -> Lineage<Diagnostics<'Out>>` shell because
    /// realization-layer transformations operate on rendered text,
    /// not on the typed Catalog / Statement-stream surfaces. The
    /// metadata view is what the registry's totality-coverage scan +
    /// manifest emission need; per-invocation execution happens
    /// inside `Render.toText` when Mode = Enabled.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter
            "ssdtConstraintFormatter" Schema
            [ TransformSite.operatorIntent "columnInlinePrimaryKey" Emission
                "Split column-inline `[col] type CONSTRAINT [PK_*] PRIMARY KEY [CLUSTERED]` into V1's 3-line shape (column / CONSTRAINT [name] at +4 / PRIMARY KEY body at +8). Operator emission-axis intent — V1 parity over ScriptDom default."
              TransformSite.operatorIntent "columnInlineNamedDefault" Emission
                "Split column-inline `[col] type CONSTRAINT [DF_*] DEFAULT value` into 2-line shape (column / CONSTRAINT [name] DEFAULT value at +4). Operator emission-axis intent."
              TransformSite.operatorIntent "columnInlineAnonymousDefault" Emission
                "Split column-inline `[col] type DEFAULT (value)` (no CONSTRAINT prefix) into 2-line shape. Fires when V2's `Attribute.DefaultName = None`. Operator emission-axis intent."
              TransformSite.operatorIntent "columnInlineCheck" Emission
                "Split column-inline `[col] type CONSTRAINT [CK_*] CHECK (expr)` into 2-line shape. Operator emission-axis intent."
              TransformSite.operatorIntent "tableLevelForeignKey" Emission
                "Split table-level `CONSTRAINT [FK_*] FOREIGN KEY (cols) REFERENCES table (cols) [ON DELETE x] [ON UPDATE y]` into multi-line shape (CONSTRAINT name / FOREIGN KEY body at +4 / ON DELETE-UPDATE at +8). V1's NO ACTION normalization preserved. Operator emission-axis intent."
              TransformSite.operatorIntent "tableLevelPrimaryKey" Emission
                "Split table-level `CONSTRAINT [PK_*] PRIMARY KEY ([cols])` (composite or single-column) into 2-line shape (constraint name / PRIMARY KEY body at +4). Operator emission-axis intent."
              TransformSite.operatorIntent "tableLevelCheck" Emission
                "Split table-level `CONSTRAINT [CK_*] CHECK (expr)` into 2-line shape (constraint name / CHECK body at +4). V2 emits `Kind.ColumnChecks` at the table-constraint section. Operator emission-axis intent."
              TransformSite.operatorIntent "extendedPropertyExecWrap" Emission
                "Wrap `EXEC sys.sp_addextendedproperty` calls at @level0type / @level1type / @level2type boundaries with 4-space continuation indent. Mirrors V1's MS_Description emission shape. Operator emission-axis intent." ]

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

    // ---------------------------------------------------------------
    // Column-inline named CHECK: split into two indented lines.
    //
    // Input:  `    [Status] NVARCHAR (50) NOT NULL CONSTRAINT [CK_*] CHECK ([Status] IN ('A', 'B')),`
    // Output: `    [Status] NVARCHAR (50) NOT NULL\n`
    //         `        CONSTRAINT [CK_*] CHECK ([Status] IN ('A', 'B')),\n`
    //
    // Slice D.2.b — sibling to formatColumnInlineDefault; same
    // two-line shape, distinguished by the constraint body keyword
    // ("CHECK" vs "DEFAULT"). V1 reference fixture:
    // tests/Fixtures/emission-matrix/temporal-audit/Modules/Matrix/
    // dbo.TemporalOrder.sql (slice-D.2.b carbon-copy delta).
    // ---------------------------------------------------------------
    let private formatColumnInlineCheck
        (sb: StringBuilder)
        (line: string)
        (constraintIdx: int)
        : bool =
        let columnPart = line.Substring(0, constraintIdx)
        let constraintWithComma = line.Substring(constraintIdx + 1).TrimEnd()
        let constraintPart, trailingComma = splitTrailingComma constraintWithComma
        if not (constraintPart.IndexOf("CHECK", StringComparison.OrdinalIgnoreCase) > 0) then
            false
        else
            let columnIndent = indentOf line
            let constraintIndent = columnIndent + "    "  // LINT-ALLOW: V1 4-space convention
            sb.Append(columnPart).Append(newLine) |> ignore
            sb.Append(constraintIndent).Append(constraintPart).Append(trailingComma).Append(newLine) |> ignore
            true

    // ---------------------------------------------------------------
    // Column-inline ANONYMOUS DEFAULT (no CONSTRAINT name): split
    // into two indented lines.
    //
    // Input:  `    [FirstName] NVARCHAR (100) NULL DEFAULT (''),`
    // Output: `    [FirstName] NVARCHAR (100) NULL\n`
    //         `        DEFAULT (''),\n`
    //
    // Slice D.2.b — fires when ScriptDom emits the anonymous-default
    // shape (V2's `Attribute.DefaultName = None`). The named-default
    // case (`CONSTRAINT [name] DEFAULT value`) is handled by
    // formatColumnInlineDefault; this branch covers the unnamed
    // case where CONSTRAINT prefix is absent. V1 reference fixture:
    // tests/Fixtures/emission/edge-case/Modules/AppCore/dbo.Customer.sql.
    // ---------------------------------------------------------------
    let private formatColumnInlineAnonymousDefault
        (sb: StringBuilder)
        (line: string)
        (defaultIdx: int)
        : bool =
        let columnPart = line.Substring(0, defaultIdx)
        let defaultWithComma = line.Substring(defaultIdx + 1).TrimEnd()
        let defaultPart, trailingComma = splitTrailingComma defaultWithComma
        let columnIndent = indentOf line
        let defaultIndent = columnIndent + "    "  // LINT-ALLOW: V1 4-space convention
        sb.Append(columnPart).Append(newLine) |> ignore
        sb.Append(defaultIndent).Append(defaultPart).Append(trailingComma).Append(newLine) |> ignore
        true

    // ---------------------------------------------------------------
    // Table-level two-line constraint split, parameterised by the
    // body-keyword. Used by PRIMARY KEY (composite or single-column
    // at table-level) and CHECK (V2 emits ColumnChecks at the table-
    // constraint section).
    //
    // Input:  `    CONSTRAINT [<name>] <BODY_KEYWORD> <body>`
    // Output: `    CONSTRAINT [<name>]\n`
    //         `        <BODY_KEYWORD> <body>\n`
    //
    // Slice D.2.b — mirrors V1's AppendFormattedPrimaryKey shape;
    // generalised because PRIMARY KEY and CHECK at the table level
    // share the same two-line split semantic (constraint name on
    // its own line at `indent`; constraint body at indent + 4).
    // ---------------------------------------------------------------
    let private formatTableLevelTwoLine
        (sb: StringBuilder)
        (line: string)
        (bodyKeyword: string)
        : bool =
        let indent = indentOf line
        let trimmed = line.TrimStart()
        let withoutComma, trailingComma = splitTrailingComma trimmed
        let bodyIdx =
            withoutComma.IndexOf(bodyKeyword, StringComparison.OrdinalIgnoreCase)
        if bodyIdx <= 0 then
            false
        else
            let constraintName = withoutComma.Substring(0, bodyIdx).TrimEnd()
            let constraintBody = withoutComma.Substring(bodyIdx)
            let bodyIndent = indent + "    "  // LINT-ALLOW: V1 4-space convention
            sb.Append(indent).Append(constraintName).Append(newLine) |> ignore
            sb.Append(bodyIndent).Append(constraintBody).Append(trailingComma).Append(newLine) |> ignore
            true

    let private formatTableLevelPrimaryKey
        (sb: StringBuilder)
        (line: string)
        : bool =
        formatTableLevelTwoLine sb line "PRIMARY KEY"

    let private formatTableLevelCheck
        (sb: StringBuilder)
        (line: string)
        : bool =
        formatTableLevelTwoLine sb line "CHECK"

    // ---------------------------------------------------------------
    // EXECUTE sys.sp_addextendedproperty multi-line formatting:
    // wrap each @level-pair onto its own continuation line.
    //
    // Input:  `EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Customer', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Customer'`
    // Output: `EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Customer',\n`
    //         `    @level0type = N'SCHEMA', @level0name = N'dbo',\n`
    //         `    @level1type = N'TABLE', @level1name = N'Customer'\n`
    //
    // Slice D.2.b — mirrors V1's emission shape for sp_addextendedproperty
    // (reference: tests/Fixtures/emission/edge-case/Modules/AppCore/
    // dbo.Customer.sql lines 34-43). Splits at the boundary BEFORE
    // each `@levelNtype` token (where N ∈ {0, 1, 2}); pass-through
    // for EXEC lines without level pairs.
    // ---------------------------------------------------------------
    let private formatExtendedPropertyExec
        (sb: StringBuilder)
        (line: string)
        : bool =
        let level0Idx = line.IndexOf(" @level0type", StringComparison.OrdinalIgnoreCase)
        if level0Idx < 0 then
            false
        else
            let level1Idx = line.IndexOf(" @level1type", StringComparison.OrdinalIgnoreCase)
            let level2Idx = line.IndexOf(" @level2type", StringComparison.OrdinalIgnoreCase)
            let continuationIndent = "    "  // LINT-ALLOW: V1 4-space convention
            // Segment 1: head (everything before @level0type).
            let head = line.Substring(0, level0Idx).TrimEnd()
            sb.Append(head).Append(newLine) |> ignore
            // Segment 2: @level0type ... @level0name pair, ending at @level1type or @level2type or end-of-line.
            let level0End =
                if level1Idx > 0 then level1Idx
                elif level2Idx > 0 then level2Idx
                else line.Length
            let level0Segment = line.Substring(level0Idx + 1, level0End - level0Idx - 1).TrimEnd()
            sb.Append(continuationIndent).Append(level0Segment).Append(newLine) |> ignore
            // Segment 3 (optional): @level1type ... @level1name pair, ending at @level2type or end-of-line.
            if level1Idx > 0 then
                let level1End = if level2Idx > 0 then level2Idx else line.Length
                let level1Segment = line.Substring(level1Idx + 1, level1End - level1Idx - 1).TrimEnd()
                sb.Append(continuationIndent).Append(level1Segment).Append(newLine) |> ignore
            // Segment 4 (optional): @level2type ... @level2name pair.
            if level2Idx > 0 then
                let level2Segment = line.Substring(level2Idx + 1).TrimEnd()
                sb.Append(continuationIndent).Append(level2Segment).Append(newLine) |> ignore
            true

    /// Detect which constraint shape, if any, the line carries; format
    /// accordingly into the StringBuilder. Returns true on
    /// reformat, false when the line passes through unchanged.
    let private tryFormatLine (sb: StringBuilder) (line: string) : bool =
        let trimmed = line.TrimStart()
        if startsWithColumnIdentifier trimmed then
            let constraintIdx = inlineConstraintIndex line
            if constraintIdx >= 0 then
                // The substring AFTER " CONSTRAINT [" tells us PK / DEFAULT / CHECK.
                let after = line.Substring(constraintIdx + " CONSTRAINT ".Length)
                if after.IndexOf("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) >= 0 then
                    formatColumnInlinePrimaryKey sb line constraintIdx
                elif after.IndexOf("CHECK", StringComparison.OrdinalIgnoreCase) >= 0 then
                    formatColumnInlineCheck sb line constraintIdx
                elif after.IndexOf("DEFAULT", StringComparison.OrdinalIgnoreCase) >= 0 then
                    formatColumnInlineDefault sb line constraintIdx
                else
                    false
            else
                // No CONSTRAINT prefix — check for column-inline anonymous DEFAULT.
                let defaultIdx = line.IndexOf(" DEFAULT ", StringComparison.OrdinalIgnoreCase)
                if defaultIdx > 0 then formatColumnInlineAnonymousDefault sb line defaultIdx
                else false
        elif startsWithConstraint trimmed then
            // Table-level constraint dispatch. FK has REFERENCES;
            // composite PK has PRIMARY KEY without REFERENCES;
            // CHECK has CHECK without PRIMARY KEY or FOREIGN KEY.
            if trimmed.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) > 0
               && trimmed.IndexOf("REFERENCES", StringComparison.OrdinalIgnoreCase) > 0 then
                formatTableLevelForeignKey sb line
            elif trimmed.IndexOf("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) > 0 then
                formatTableLevelPrimaryKey sb line
            elif trimmed.IndexOf("CHECK", StringComparison.OrdinalIgnoreCase) > 0 then
                formatTableLevelCheck sb line
            else
                false
        elif trimmed.StartsWith("EXECUTE [sys].[sp_addextendedproperty]", StringComparison.OrdinalIgnoreCase)
             || trimmed.StartsWith("EXEC sys.sp_addextendedproperty", StringComparison.OrdinalIgnoreCase) then
            formatExtendedPropertyExec sb line
        else
            false

    /// Format a rendered T-SQL script. When `mode = Enabled`,
    /// reformats CREATE TABLE inline constraints into V1's multi-line
    /// elegant shape; when `mode = Disabled`, returns the input
    /// unchanged (operator opt-out for diagnostic / V1-parity-bisect
    /// reasons).
    let format (mode: Mode) (script: string) : string =
        use _ = Bench.scope "ssdt.constraintFormatter.format"
        match mode with
        | Disabled -> script
        | Enabled ->
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

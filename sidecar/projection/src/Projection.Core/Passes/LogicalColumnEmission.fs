namespace Projection.Core.Passes

open Projection.Core

/// Catalog-driven emission-axis pass: V2 emits each attribute's
/// **logical name** (`Name.value a.Name`) in place of its OSSYS-flavored
/// physical column name. Operators see `[Email]` in deployed SSDT rather
/// than `[EMAIL]`.
///
/// **Not a rename.** A rename authors a NEW name. This pass authors
/// nothing — both the logical attribute name (`Attribute.Name`) and the
/// physical column name (`Attribute.Column.ColumnName`) already exist
/// in the catalog. The pass SUBSTITUTES the logical value into the
/// physical-realization slot the emitter reads, so emission shows the
/// operator-meaningful name without the emitter layer changing what it
/// reads.
///
/// **Sibling to `LogicalTableEmission`.** Same classification
/// (`OperatorIntent Emission`); same default-on production wiring;
/// same identity-preservation guarantee (only `ColumnName` is touched;
/// `Attribute.SsKey` / `Attribute.Name` / `IsNullable` / every other
/// `Attribute` field unchanged). Together the two passes constitute
/// slice D.1.a's emission-axis substitution — V2 emits the logical
/// name at both the table and the column granularity.
///
/// **No `ColumnRename` generic equivalent ships yet.** Per the "IR
/// grows under evidence" discipline, the operator-supplied column-
/// rename machinery (analogous to `TableRename`) would land when a
/// second consumer demands it. Today's only consumer is logical-name
/// emission, which this pass owns directly.
///
/// **Length guard.** If `Name.value a.Name` exceeds the SQL Server
/// identifier length limit (128 chars), the attribute's `ColumnName`
/// is left unchanged.
[<RequireQualifiedAccess>]
module LogicalColumnEmission =

    /// Pass version. Bump when substitution semantics change.
    /// v1 — attribute `ColumnName` substitution only.
    /// v2 — reconciliation slice 3 (DECISIONS 2026-06-13): the
    ///      substitution follows the column into `ColumnCheck.Definition`
    ///      and `Index.Filter` (bracketed physical references rewritten
    ///      to the logical names; an unrewritten definition would
    ///      reference a column that no longer exists on the emitted
    ///      table).
    /// v3 — family 4e (DECISIONS 2026-07-18; #669 EF-20): the
    ///      substitution follows the column into `Trigger.Definition`
    ///      (the OWNING kind's columns only — a cross-table column
    ///      reference stays, the same bracket-token grain as v2; table
    ///      names are `LogicalTableEmission` v2's half of the slice).
    [<Literal>]
    let version : int = 3

    [<Literal>]
    let private passName : string = "logicalColumnEmission"

    /// Operator-toggleable mode. Mirrors `LogicalTableEmission.Mode` —
    /// `Enabled` is the production default (slice D.1.a); `Disabled`
    /// short-circuits to a no-op pass-through.
    type Mode =
        /// Substitute `Attribute.Column.ColumnName = Name.value a.Name`
        /// for every attribute whose logical name differs from its
        /// physical column name.
        | Enabled
        /// Pass-through; no substitutions, no lineage events.
        | Disabled

    let private classification : Classification = OperatorIntent Emission

    let private substitutedEvent (key: SsKey) (kind: TableId) (before: string) (after: string) : LineageEvent =
        LineageEvent.forPass passName version classification key
            (ColumnPhysicallyRenamed { Kind = kind; Before = before; After = after })

    let private substituteAttribute (events: LineageBuffer.Buffer) (kind: TableId) (a: Attribute) : Attribute =
        let logical = Name.value a.Name
        if System.String.IsNullOrWhiteSpace logical
           || logical.Length > CoordinatesLimits.SqlServerIdentifierMaxLength then
            a
        elif logical = ColumnRealization.columnNameText a.Column then
            a
        else
            // Pre-checks above (non-blank + length ≤ 128) match
            // `ColumnName.create`'s validation exactly; the create
            // call therefore cannot fail by construction. The
            // `Result.value` unwrap is safe here.
            let logicalColumnName = ColumnName.create logical |> Result.value
            LineageBuffer.add (substitutedEvent a.SsKey kind (ColumnRealization.columnNameText a.Column) logical) events
            a |> Lens.over CatalogLenses.columnOf (fun col -> { col with ColumnName = logicalColumnName })

    /// v2 — rewrite a SQL definition's bracketed physical column
    /// references to the logical names the columns now carry. Bracket-
    /// token substitution, not a parse: source-reality definitions
    /// (sys.check_constraints / sys.indexes.filter_definition) always
    /// bracket column references, and Core stays ScriptDom-free. A
    /// physical name occurring inside a string literal would also be
    /// rewritten — accepted limitation, documented at the pass grain.
    let private rewriteDefinition (pairs: (string * string) list) (definition: string) : string =
        pairs
        |> List.fold
            (fun (acc: string) (physical, logical) ->
                acc.Replace(  // LINT-ALLOW: bracketed-identifier token rewrite at the substitution pass; the tokens are typed names lifted from the IR (ColumnRealization/Name), not composed prose; Core is ScriptDom-free by design so a typed-AST rewrite is not available at this layer
                    SqlIdentifier.quote physical,   // recon #8 — the one Core quoter (also `]`-escapes, which the prior inline literal did not)
                    SqlIdentifier.quote logical,
                    System.StringComparison.OrdinalIgnoreCase))
            definition

    let private substituteKind (events: LineageBuffer.Buffer) (k: Kind) : Kind option =
        let attrs' = k.Attributes |> List.map (substituteAttribute events k.Physical)
        // v2 — the substitution follows the column into CHECK
        // definitions and index FILTER predicates (pairs = exactly the
        // attributes the substitution touched; identity when none did).
        let pairs =
            List.zip k.Attributes attrs'
            |> List.choose (fun (before, after) ->
                let b = ColumnRealization.columnNameText before.Column
                let a = ColumnRealization.columnNameText after.Column
                if b = a then None else Some (b, a))
        let k' = Lens.set CatalogLenses.attributesOf attrs' k
        if List.isEmpty pairs then Some k'
        else
            Some
                { k' with
                    ColumnChecks =
                        k'.ColumnChecks
                        |> List.map (fun chk ->
                            { chk with Definition = rewriteDefinition pairs chk.Definition })
                    Indexes =
                        k'.Indexes
                        |> List.map (fun idx ->
                            match idx.Filter with
                            | None -> idx
                            | Some f -> { idx with Filter = Some (rewriteDefinition pairs f) })
                    // v3 (family 4e) — the owning kind's column renames
                    // follow into its trigger bodies.
                    Triggers =
                        k'.Triggers
                        |> List.map (fun t ->
                            { t with Definition = rewriteDefinition pairs t.Definition }) }

    let private run (mode: Mode) (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.logicalColumnEmission"
        match mode with
        | Disabled -> Lineage.ofValue c
        | Enabled  -> c |> CatalogTraversal.mapKinds substituteKind

    /// Factory. `Mode` is captured in the closure; `RegisteredTransforms`
    /// wires the production chain with `Enabled` (the slice D.1.a default).
    let registered (mode: Mode) : RegisteredTransform<Catalog, Catalog> =
        { Name = passName
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "logicalEmission"
                Classification = classification
                Rationale = "Substitute Name.value a.Name into Attribute.Column.ColumnName for emission. Not a rename — the logical name already exists in the catalog; the pass aligns the physical-realization slot the emitter reads. Operator emission-axis intent; production default. Identity (SsKey) and Attribute.Name untouched per A1." } ]
          Run = fun c -> run mode c |> Lineage.map Diagnostics.ofValue
          Status = Active }

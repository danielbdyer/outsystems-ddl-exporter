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
    [<Literal>]
    let version : int = 1

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
        { PassName       = passName
          PassVersion    = version
          SsKey          = key
          TransformKind  = ColumnPhysicallyRenamed { Kind = kind; Before = before; After = after }
          Classification = classification }

    let private substituteAttribute (events: LineageBuffer.Buffer) (kind: TableId) (a: Attribute) : Attribute =
        let logical = Name.value a.Name
        if System.String.IsNullOrWhiteSpace logical
           || logical.Length > CoordinatesLimits.SqlServerIdentifierMaxLength then
            a
        elif logical = a.Column.ColumnName then
            a
        else
            LineageBuffer.add (substitutedEvent a.SsKey kind a.Column.ColumnName logical) events
            a |> Lens.over CatalogLenses.columnOf (fun col -> { col with ColumnName = logical })

    let private substituteKind (events: LineageBuffer.Buffer) (k: Kind) : Kind option =
        let attrs' = k.Attributes |> List.map (substituteAttribute events k.Physical)
        Some (Lens.set CatalogLenses.attributesOf attrs' k)

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

namespace Projection.Core

/// A column's physical-schema coordinate — the structural-fidelity
/// axis that survives the deploy → read round-trip. Used by the
/// canary's round-trip property test (M3 onward) to compare two
/// Catalogs produced by different adapters (e.g., OSSYS JSON vs.
/// `Projection.Adapters.Sql.ReadSide`) without false negatives on
/// V2-IR-only metadata or SsKey-synthesis-source differences.
///
/// Per `DECISIONS 2026-05-23 — Source SQL Server with OutSystems
/// semantics is the canary's primary wide integration surface`,
/// `PhysicalSchema` is the comparison primitive both halves of the
/// round-trip use:
///
///   - **Source half.** OutSystems-shaped DDL → deploy → read →
///     `sourceCatalog`. Project to `PhysicalSchema` via
///     `PhysicalSchema.ofCatalog`.
///   - **Target half.** V2 emit → deploy → read → `targetCatalog`.
///     Project to `PhysicalSchema` via the same function.
///   - **Assertion.** `PhysicalSchema.diff source target` returns
///     `(missingInTarget, extraInTarget)` — both empty means the
///     emitter preserved the source's structural intent.
///
/// **What's compared.** The set of `(schema, table, column, type,
/// nullable, isPrimaryKey)` tuples across both Catalogs.
///
/// **What's NOT compared.** SsKey identity, Module structure,
/// Origin / Modality marks, References, Indexes, static
/// populations, comment metadata. These are V2-IR-only axes that
/// SQL Server's catalog cannot recover. M4's Tolerance taxonomy
/// will name additional comparison flags (e.g., column length /
/// precision; FK structure when emitter gains PK emission;
/// indexes).
type PhysicalColumn =
    {
        Schema : string
        Table : string
        Column : string
        Type : PrimitiveType
        Nullable : bool
        IsPrimaryKey : bool
    }

/// The set of all `PhysicalColumn` tuples in a Catalog. Equality
/// on this surface is the structural-fidelity round-trip property.
type PhysicalSchema = Set<PhysicalColumn>

/// The diff between two `PhysicalSchema` values. Both empty means
/// the structural intent matches; populated `MissingInTarget`
/// means the emitter dropped columns the source had; populated
/// `ExtraInTarget` means the emitter added columns the source did
/// not. Either is a canary-blocking divergence under R6.
type PhysicalSchemaDiff =
    {
        MissingInTarget : PhysicalColumn list
        ExtraInTarget : PhysicalColumn list
    }

[<RequireQualifiedAccess>]
module PhysicalSchema =

    let private toPhysicalColumns (k: Kind) : PhysicalColumn list =
        k.Attributes
        |> List.map (fun a ->
            {
                Schema = k.Physical.Schema
                Table = k.Physical.Table
                Column = a.Column.ColumnName
                Type = a.Type
                Nullable = a.Column.IsNullable
                IsPrimaryKey = a.IsPrimaryKey
            })

    /// Project a Catalog to its `PhysicalSchema` view — the set of
    /// `(schema, table, column, type, nullable, isPrimaryKey)`
    /// tuples reachable through every Module's Kinds. Modules,
    /// Origin, Modality, References, and Indexes are projected
    /// out by construction.
    let ofCatalog (c: Catalog) : PhysicalSchema =
        c.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.collect toPhysicalColumns
        |> Set.ofList

    /// Diff two `PhysicalSchema` values. The first is the source
    /// (operator's reality); the second is the target (V2's
    /// projection after emit + deploy + readback).
    ///
    /// `MissingInTarget` are columns the source has that the target
    /// does not — the emitter dropped them.
    /// `ExtraInTarget` are columns the target has that the source
    /// does not — the emitter added them.
    /// Both empty means structural fidelity holds.
    let diff (source: PhysicalSchema) (target: PhysicalSchema) : PhysicalSchemaDiff =
        {
            MissingInTarget = Set.difference source target |> Set.toList
            ExtraInTarget = Set.difference target source |> Set.toList
        }

    /// True iff the diff is empty in both directions — source and
    /// target are structurally equivalent on the
    /// `PhysicalSchema` axis.
    let isEqual (d: PhysicalSchemaDiff) : bool =
        List.isEmpty d.MissingInTarget && List.isEmpty d.ExtraInTarget

    /// Render a diff as a human-readable multi-line string. Used by
    /// canary failure messages so the operator sees exactly which
    /// columns mismatched, not just "they differ."
    let renderDiff (d: PhysicalSchemaDiff) : string =
        let renderColumn (c: PhysicalColumn) : string =
            sprintf
                "  [%s].[%s].[%s] %A nullable=%b pk=%b"
                c.Schema
                c.Table
                c.Column
                c.Type
                c.Nullable
                c.IsPrimaryKey
        let missing =
            if List.isEmpty d.MissingInTarget then
                "  (none)"
            else
                d.MissingInTarget |> List.map renderColumn |> String.concat "\n"
        let extra =
            if List.isEmpty d.ExtraInTarget then
                "  (none)"
            else
                d.ExtraInTarget |> List.map renderColumn |> String.concat "\n"
        sprintf
            "PhysicalSchema diff:\nMissing in target (source had, target lost):\n%s\nExtra in target (target has, source did not):\n%s"
            missing
            extra

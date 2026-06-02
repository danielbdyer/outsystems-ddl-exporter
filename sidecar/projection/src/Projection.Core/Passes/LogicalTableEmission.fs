namespace Projection.Core.Passes

open Projection.Core

/// Catalog-driven emission-axis pass: V2 emits each kind's **logical
/// name** (`Name.value k.Name`) in place of its OSSYS-flavored physical
/// table name. Operators see `[dbo].[Customer]` in deployed SSDT rather
/// than `[dbo].[OSUSR_ABC_CUSTOMER]`.
///
/// **Not a rename.** A rename authors a NEW name (`old → new` is a
/// creative act, the operator's choice of what to call something).
/// This pass authors nothing — both the logical name (`Kind.Name`) and
/// the physical name (`Kind.Physical.Table`) already exist in the
/// catalog. The pass SUBSTITUTES the logical value into the physical-
/// realization slot the emitter reads, so downstream consumers see the
/// operator-meaningful name without the emitter layer changing what it
/// reads. Compare to `TableRename`, which IS a rename — operators
/// supply explicit `{ source → target }` pairs authoring new physical
/// names.
///
/// **Order in the chain (slice D.1.b correction).** Runs BEFORE
/// `TableRename` so operator-supplied physical pinnings dominate.
/// `TableRename` is the LAST writer to `Kind.Physical`; the logical
/// substitution lands first and the operator override applies on top
/// when present. Reverses the D.1.a-as-shipped ordering, which had the
/// substitution running last and silently overwriting operator pins —
/// caught during D.1.b planning when the conflict between the
/// docstring's "operator pins dominate" claim and the actual chain
/// order became visible.
///
/// **Identity preservation (A1).** Only `Kind.Physical.Table` is
/// rewritten. `Kind.SsKey`, `Kind.Name`, `Kind.Physical.Catalog`,
/// `Kind.Physical.Schema`, every `Attribute` field, every `Reference`
/// (which carries `TargetKind` as `SsKey`, never a physical name) all
/// remain byte-identical.
///
/// **Length guard.** If `Name.value k.Name` exceeds the SQL Server
/// identifier length limit (128 chars), the kind's `Physical` is left
/// unchanged — the substitution would produce an invalid SQL
/// identifier otherwise. Source catalogs from OSSYS never produce such
/// names in practice; defensive boundary, not hot path.
[<RequireQualifiedAccess>]
module LogicalTableEmission =

    /// Pass version. Bump when substitution semantics change.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "logicalTableEmission"

    /// Operator-toggleable mode. `Enabled` is the production default
    /// (slice D.1.a — V2 emits logical names by default); `Disabled`
    /// short-circuits to a no-op pass-through so operators who want
    /// physical-name emission for diagnostic / V1-parity reasons can
    /// opt out. Both modes carry `OperatorIntent Emission` — the
    /// classification is invariant of which mode is selected (the
    /// operator chose either way).
    type Mode =
        /// Substitute `Kind.Physical.Table = Name.value k.Name` for
        /// every kind whose logical name differs from its physical
        /// table.
        | Enabled
        /// Pass-through; no substitutions, no lineage events.
        | Disabled

    /// Pillar 9 (chapter A.4.7 slice α): logical-name emission is the
    /// operator's emission-axis choice. The default-on production wiring
    /// IS the operator's intent ("V2 emits logical names"); a Disabled
    /// mode preserves the operator's option to fall back to physical
    /// emission for diagnostic / V1-parity reasons.
    let private classification : Classification = OperatorIntent Emission

    let private substitutedEvent (key: SsKey) (before: TableId) (after: TableId) : LineageEvent =
        { PassName       = passName
          PassVersion    = version
          SsKey          = key
          TransformKind  = PhysicallyRenamed { Before = before; After = after }
          Classification = classification }

    let private substituteKind (events: LineageBuffer.Buffer) (k: Kind) : Kind option =
        let logical = Name.value k.Name
        if System.String.IsNullOrWhiteSpace logical
           || logical.Length > CoordinatesLimits.SqlServerIdentifierMaxLength then
            Some k
        elif logical = TableName.value k.Physical.Table then
            Some k
        else
            // The pre-checks above (non-blank + length ≤ 128) match
            // `TableName.create`'s validation exactly; the create call
            // therefore cannot fail by construction. The `Result.value`
            // unwrap is safe here.
            let after = { k.Physical with Table = TableName.create logical |> Result.value }
            LineageBuffer.add (substitutedEvent k.SsKey k.Physical after) events
            Some { k with Physical = after }

    let private run (mode: Mode) (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.logicalTableEmission"
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
                Rationale = "Substitute Name.value k.Name into Kind.Physical.Table for emission. Not a rename — the logical name already exists in the catalog; the pass aligns the physical-realization slot the emitter reads. Operator emission-axis intent; production default. Identity (SsKey) untouched per A1." } ]
          Run = fun c -> run mode c |> Lineage.map Diagnostics.ofValue
          Status = Active }

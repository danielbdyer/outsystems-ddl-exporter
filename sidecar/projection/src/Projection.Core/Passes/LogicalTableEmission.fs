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
    /// v1 — `Kind.Physical.Table` substitution only.
    /// v2 — family 4e (DECISIONS 2026-07-18; #669 EF-20): the
    ///      substitution follows the TABLE into every `Trigger.Definition`
    ///      catalog-wide (a trigger body references tables by their
    ///      source physical names — bracketed and bare forms both
    ///      rewrite to the bracketed logical name; a physical name
    ///      inside a string literal would also rewrite, the same
    ///      accepted limitation `LogicalColumnEmission` v2 documents).
    [<Literal>]
    let version : int = 2

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
        LineageEvent.forPass passName version classification key
            (PhysicallyRenamed { Before = before; After = after })

    /// Substitute the logical name into the physical-realization slot. A kind
    /// whose SsKey is in `pins` is SKIPPED: an operator-supplied PHYSICAL-form
    /// `tableRenames` override (`{ "from": { schema, table }, "to": … }`) pins
    /// that kind's `Kind.Physical` deliberately (the operator authored the
    /// physical name), so the logical-name substitution must not clobber it (the
    /// S6.3 fix — the docstring's "operator pins dominate" contract honored).
    /// `pins` is empty for every flow without a physical-form rename, so the
    /// default emission is byte-identical.
    // NM-50 — returns a `Kind` (never drops): this pass rewrites `Kind.Physical`
    // and must conserve the kind count (A1 identity preservation). Routed through
    // the total `mapKindsTotal`, so a drop is structurally impossible here.
    let private substituteKind (pins: Set<SsKey>) (events: LineageBuffer.Buffer) (k: Kind) : Kind =
        let logical = Name.value k.Name
        if Set.contains k.SsKey pins then
            k
        elif System.String.IsNullOrWhiteSpace logical
             || logical.Length > CoordinatesLimits.SqlServerIdentifierMaxLength then
            k
        elif logical = TableName.value k.Physical.Table then
            k
        else
            // The pre-checks above (non-blank + length ≤ 128) match
            // `TableName.create`'s validation exactly; the create call
            // therefore cannot fail by construction. The `Result.value`
            // unwrap is safe here.
            let after = { k.Physical with Table = TableName.create logical |> Result.value }
            LineageBuffer.add (substitutedEvent k.SsKey k.Physical after) events
            { k with Physical = after }

    /// v2 (family 4e) — rewrite a trigger definition's references to the
    /// substituted tables: the bracketed physical form via the one Core
    /// quoter, and the bare (unbracketed) form via a word-boundary match
    /// (`ON dbo.OSUSR_ABC_CUSTOMER` is legal T-SQL), both to the
    /// BRACKETED logical name. String-token grain, not a parse — Core is
    /// ScriptDom-free by design; the emitter's gate refuses any residue.
    let private rewriteTriggerTables (pairs: (string * string) list) (definition: string) : string =
        pairs
        |> List.fold
            (fun (acc: string) (physical, logical) ->
                let bracketed =
                    acc.Replace(  // LINT-ALLOW: bracketed-identifier token rewrite at the substitution pass (the LogicalColumnEmission v2 precedent); tokens are typed names lifted from the IR
                        SqlIdentifier.quote physical,
                        SqlIdentifier.quote logical,
                        System.StringComparison.OrdinalIgnoreCase)
                System.Text.RegularExpressions.Regex.Replace(
                    bracketed,
                    "(?<![\\[\\w])" + System.Text.RegularExpressions.Regex.Escape physical + "(?![\\w\\]])",
                    (SqlIdentifier.quote logical).Replace("$", "$$"),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            definition

    let private run (pins: Set<SsKey>) (mode: Mode) (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.logicalTableEmission"
        match mode with
        | Disabled -> Lineage.ofValue c
        | Enabled  ->
            c
            |> CatalogTraversal.mapKindsTotal (substituteKind pins)
            |> Lineage.map (fun substituted ->
                // v2 (family 4e) — catalog-wide before/after table pairs
                // (a trigger may reference OTHER kinds' tables), then the
                // definition rewrite over every kind's triggers. Pairs
                // come from zipping the pre/post kind walks: mapKindsTotal
                // conserves count and order (A1), so the zip is positional
                // by construction.
                let pairs =
                    List.zip (Catalog.allKinds c) (Catalog.allKinds substituted)
                    |> List.choose (fun (before, after) ->
                        let b = TableName.value before.Physical.Table
                        let a = TableName.value after.Physical.Table
                        if b = a then None else Some (b, a))
                if List.isEmpty pairs then substituted
                else
                    let rewriteKind (k: Kind) : Kind =
                        if List.isEmpty k.Triggers then k
                        else
                            { k with
                                Triggers =
                                    k.Triggers
                                    |> List.map (fun t ->
                                        { t with Definition = rewriteTriggerTables pairs t.Definition }) }
                    { substituted with
                        Modules =
                            substituted.Modules
                            |> List.map (fun m -> { m with Kinds = m.Kinds |> List.map rewriteKind }) })

    /// Factory with operator physical-rename pins (S6.3). The pinned SsKeys are
    /// the kinds whose `Kind.Physical` an operator-supplied physical-form
    /// `tableRenames` override authored — the substitution skips them so the
    /// operator's physical name survives into the emitted physical `Kind.Table`.
    /// `Set.empty` is the byte-identical default (`registered` below).
    let registeredWithPins (pins: Set<SsKey>) (mode: Mode) : RegisteredTransform<Catalog, Catalog> =
        { Name = passName
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "logicalEmission"
                Classification = classification
                Rationale = "Substitute Name.value k.Name into Kind.Physical.Table for emission. Not a rename — the logical name already exists in the catalog; the pass aligns the physical-realization slot the emitter reads. Operator emission-axis intent; production default. Identity (SsKey) untouched per A1. Skips kinds an operator physical-form tableRename pinned (S6.3)." } ]
          Run = fun c -> run pins mode c |> Lineage.map Diagnostics.ofValue
          Status = Active }

    /// Factory. `Mode` is captured in the closure; `RegisteredTransforms`
    /// wires the production chain with `Enabled` (the slice D.1.a default).
    /// No physical-rename pins — byte-identical to the pre-S6.3 behavior.
    let registered (mode: Mode) : RegisteredTransform<Catalog, Catalog> =
        registeredWithPins Set.empty mode

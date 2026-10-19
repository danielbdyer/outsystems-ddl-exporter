namespace Projection.Core

/// The emitted (presentation) names for a kind's indexes, keyed by
/// `SsKey` — derived from typed logical IR (`Index.Columns` →
/// `Attribute.Name`, `Kind.Name`, and the overlay-adjusted uniqueness
/// decision), never parsed back out of rendered SQL and never inherited
/// from SQL Server's physical auto-names. Once table/column names are
/// logicalized, every related object name derives from the same logical
/// vocabulary:
///
///   - non-PK indexes: `IX_<KindName>_<AttributeName...>`, or `UIX_...`
///     when the source declares UNIQUE or the overlay's `EnforceUnique`
///     decision applies (the same disjunction the CREATE INDEX emission
///     uses, so name and constraint agree);
///   - PK-marked indexes: the PK constraint-name convention
///     (`PK_<KindName>_<KeyColumn…>`, WP-8) so the deployed PK backing index
///     and any extended property on it follow the emitted constraint name.
///
/// This is an emitted-NAME policy, not an identity policy — `SsKey`
/// stays the durable identity; these are presentation identifiers.
/// Collision handling is proof-triggered: names start concise, and only
/// colliding names (within the kind's per-table index namespace) gain a
/// deterministic 1-based ordinal suffix in SsKey order. Every generated
/// name rides the identifier budget.
///
/// Two consumers share this one derivation — `SsdtDdlEmitter` (the
/// CREATE / DISABLE / extended-property surfaces) and
/// `PhysicalSchema.ofCatalogWith` (the deployed-reality expectation) —
/// so an emit → deploy → read-back comparison agrees on index names by
/// construction.
[<RequireQualifiedAccess>]
module IndexNaming =

    /// The primary-key constraint name — V1's `PK_<LogicalKind>_<KeyColumn…>`
    /// convention (WP-8, DECISIONS 2026-07-16; e.g. `PK_Customer_Id`),
    /// replacing the earlier schema-qualified `PK_<Schema>_<Table>`. Derived
    /// from the kind's PK-marked attributes in attribute order (the single
    /// source of truth `SsdtDdlEmitter.pkDef` and the PK backing-index name
    /// below both consume) so the deployed PK constraint and its backing index
    /// agree by construction. The `[]` case (no PK-marked attribute) mirrors
    /// V1's `PK_<name>` fallback; `pkDef` never emits a PK there. The generated
    /// name rides the identifier budget at the call sites.
    let primaryKeyName (k: Kind) : string =
        let keyCols =
            k.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map (fun a -> Name.value a.Name)
        match keyCols with
        | []   -> System.String.Concat("PK_", Name.value k.Name)  // LINT-ALLOW: V1 PK naming-convention fallback; no BCL/ScriptDom primitive emits naming-convention identifiers; Name.value unwraps the typed logical name
        | cols -> System.String.Concat("PK_", Name.value k.Name, "_", String.concat "_" cols)  // LINT-ALLOW: V1 PK naming-convention (PK_<logical kind>_<logical key columns>); String.concat is the irreducible primitive joining the typed name segments

    let emittedNames (overlay: DecisionOverlay) (k: Kind) : Map<SsKey, string> =
        let attrNameOf (columnSsKey: SsKey) : string =
            match k.Attributes |> List.tryFind (fun a -> a.SsKey = columnSsKey) with
            | Some a -> Name.value a.Name
            | None ->
                // Unreachable post-`Catalog.create` (referential integrity:
                // every Index.Column resolves within its owning Kind).
                invalidOp (sprintf "IndexNaming.emittedNames: column SsKey %A not found in kind %A (unreachable; Catalog.create invariant)" columnSsKey k.SsKey)  // LINT-ALLOW: terminal invariant-violation message; unreachable post-Catalog.create, sprintf is the irreducible primitive for this diagnostic-only text, no AST applies
        let baseNameOf (idx: Index) : string =
            if IndexUniqueness.isPrimaryKey idx.Uniqueness then
                // WP-8 — the PK backing-index name follows the PK constraint
                // name (`PK_<LogicalKind>_<KeyColumn…>`), derived from the
                // kind's PK attributes so it agrees with `pkDef` by
                // construction (not from `idx.Columns`, which could disagree
                // on order for a composite PK).
                primaryKeyName k
            else
                let isUnique =
                    IndexUniqueness.isUnique idx.Uniqueness
                    || Set.contains idx.SsKey overlay.EnforceUnique
                let columnNames =
                    idx.Columns |> List.map (fun c -> attrNameOf c.Attribute)
                System.String.Concat(  // LINT-ALLOW: generated index-name convention (IX_/UIX_ + logical kind + logical columns); no BCL/ScriptDom primitive emits naming-convention identifiers; segments are typed Name values unwrapped via Name.value
                    (if isUnique then "UIX_" else "IX_"),
                    Name.value k.Name, "_", String.concat "_" columnNames)  // LINT-ALLOW: generated index-name convention; no BCL/ScriptDom primitive emits naming-convention identifiers, String.concat is the irreducible primitive joining the typed column-name segments
        k.Indexes
        |> List.sortBy (fun idx -> idx.SsKey)
        |> List.map (fun idx -> idx, baseNameOf idx)
        |> List.groupBy snd
        |> List.collect (fun (baseName, members) ->
            match members with
            | [ (only, _) ] -> [ only.SsKey, IdentifierBudget.fit baseName ]
            | colliding ->
                // Proof-triggered disambiguation: only names that actually
                // collide gain the ordinal, in SsKey order (deterministic).
                colliding
                |> List.mapi (fun i (idx, _) ->
                    idx.SsKey, IdentifierBudget.fit (System.String.Concat(baseName, "_", string (i + 1)))))  // LINT-ALLOW: deterministic collision ordinal on a generated identifier; terminal name construction
        |> Map.ofList

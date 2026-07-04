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
///     (`PK_<Schema>_<Table>`) so the deployed PK backing index and any
///     extended property on it follow the emitted constraint name.
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
                System.String.Concat("PK_", TableId.schemaText k.Physical, "_", TableId.tableText k.Physical)  // LINT-ALLOW: V1 naming-convention PK constraint name (pkDef's shape); segments pre-unwrapped via TableId helpers
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

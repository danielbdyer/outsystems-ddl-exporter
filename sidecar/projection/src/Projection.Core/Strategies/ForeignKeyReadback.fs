namespace Projection.Core
// LINT-ALLOW-FILE: terminal operator-diagnostic coordinate text (the cross-schema FK-skip ValidationError messages) at the readback boundary; BCL String.Concat is the use-case-specific primitive (moved from ReadSide, recon #20).

/// E2 (debrief G4) — classify an FK metadata row before it becomes a
/// `Reference`. `SCHEMA_NAME()` returns NULL when a referenced/parent schema was
/// dropped between the metadata read and the FK probe, or when the account lacks
/// `VIEW DEFINITION` on that schema. Such a row cannot be faithfully
/// reconstructed; per the no-silent-drop boundary axiom it must surface a NAMED
/// diagnostic — not a silent skip, not an opaque `GetString` cast failure that
/// aborts the whole readback.
///
/// Pure NULL-coordinate classification — no SQL, no driver types. Recon #20
/// brought it home from the `ReadSide` SQL adapter to Core (next to the FK rules)
/// where it belongs: identity/coordinate logic is Core's, and the classifier is
/// unit-witnessed without a live substrate
/// (`tests/Projection.Tests/ForeignKeyReadbackTests.fs`).
[<RequireQualifiedAccess>]
module ForeignKeyReadback =

    /// All coordinates resolved non-blank — the row reconstructs.
    type FkCoordinates =
        {
            SourceSchema : string
            SourceTable  : string
            SourceColumn : string
            TargetSchema : string
            TargetTable  : string
            TargetColumn : string
            IsNotTrusted : bool
        }

    /// align-II.4b (a4-8) — WHICH side's schema was lost, typed. The
    /// four-way distinction `classify` always computed but interned
    /// into prose; a consumer aggregating "N FKs skipped for missing
    /// VIEW DEFINITION on the parent side" reads the value, not a
    /// sentence.
    type LostSide =
        | BothSchemas
        | ParentSchema
        | ReferencedSchema

    /// The endpoint coordinates as read, with unreadable segments shown
    /// as `<unreadable>` — the visible half of an unreadable row (what
    /// the operator greps for to locate the grant).
    type FkVisible =
        {
            Source : string
            Target : string
        }

    type Classification =
        | Reconstructable of FkCoordinates
        /// align-II.4b: the computed classification rides TYPED; the
        /// operator sentence is minted by `describe` (the
        /// ResolutionReason/describe precedent — copy ownership at the
        /// projection, aggregation on the value).
        | Unreadable of side: LostSide * visible: FkVisible

    let private norm (o: string option) : string option =
        o |> Option.map (fun (s: string) -> s.Trim()) |> Option.filter (fun s -> s <> "")

    /// Classify a raw FK row. `None` models a NULL read (`SCHEMA_NAME()` or a NULL
    /// column); a blank/whitespace string is treated identically. On an unreadable
    /// coordinate the reason names the visible endpoints and which side's schema
    /// was lost, plus the two likely causes, so an operator can locate and fix the
    /// grant.
    let classify
        (sourceSchema: string option) (sourceTable: string option) (sourceColumn: string option)
        (targetSchema: string option) (targetTable: string option) (targetColumn: string option)
        (isNotTrusted: bool)
        : Classification =
        match norm sourceSchema, norm sourceTable, norm sourceColumn,
              norm targetSchema, norm targetTable, norm targetColumn with
        | Some ss, Some st, Some sc, Some ts, Some tt, Some tc ->
            Reconstructable
                { SourceSchema = ss; SourceTable = st; SourceColumn = sc
                  TargetSchema = ts; TargetTable = tt; TargetColumn = tc
                  IsNotTrusted = isNotTrusted }
        | nSrcSchema, _, _, nTgtSchema, _, _ ->
            let show (o: string option) = norm o |> Option.defaultValue "<unreadable>"
            let src = System.String.Concat(show sourceSchema, ".", show sourceTable, ".", show sourceColumn)
            let tgt = System.String.Concat(show targetSchema, ".", show targetTable, ".", show targetColumn)
            let side =
                match nSrcSchema, nTgtSchema with
                | None, None -> BothSchemas
                | None, _ -> ParentSchema
                // The remaining arm: the source schema read fine, so the
                // loss is on the referenced side (a non-schema coordinate
                // NULL also lands here — the schema pair is the classifier's
                // named axis; the visible string shows exactly which
                // segment read `<unreadable>`).
                | _, _ -> ReferencedSchema
            Unreadable (side, { Source = src; Target = tgt })

    /// The operator sentence for an unreadable row — byte-identical to
    /// the prose the DU payload used to intern (align-II.4b moved the
    /// classification onto the value; this projection owns the copy).
    let describe (side: LostSide) (visible: FkVisible) : string =
        let which =
            match side with
            | BothSchemas -> "both endpoints' schemas"
            | ParentSchema -> "the parent schema"
            | ReferencedSchema -> "the referenced schema"
        System.String.Concat(
            "readside.foreignKeys: cross-schema FK ", visible.Source, " -> ", visible.Target,
            " skipped — ", which,
            " unreadable (NULL SCHEMA_NAME: dropped schema, or missing VIEW DEFINITION grant on a least-privilege account)")

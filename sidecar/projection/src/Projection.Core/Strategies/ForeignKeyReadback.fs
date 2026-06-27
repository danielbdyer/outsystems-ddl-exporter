namespace Projection.Core

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

    type Classification =
        | Reconstructable of FkCoordinates
        | Unreadable of reason: string

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
            let which =
                match nSrcSchema, nTgtSchema with
                | None, None -> "both endpoints' schemas"
                | None, _ -> "the parent schema"
                | _, None -> "the referenced schema"
                | _ -> "a coordinate"
            Unreadable
                (System.String.Concat(
                    "readside.foreignKeys: cross-schema FK ", src, " -> ", tgt,
                    " skipped — ", which,
                    " unreadable (NULL SCHEMA_NAME: dropped schema, or missing VIEW DEFINITION grant on a least-privilege account)"))

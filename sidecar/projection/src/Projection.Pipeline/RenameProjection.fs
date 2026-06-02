namespace Projection.Pipeline

open Projection.Core

/// 6.B.2 — RefactorLog-aware Transfer (orthogonality T-V: Identity↔Schema). A
/// rename changes a column's physical coordinates, so the source (read back)
/// carries OLD names and the sink (read back) carries NEW names. A Transfer
/// that matched columns positionally (ordinal) — or by the source name — would
/// silently mis-map a renamed column. This re-points by IDENTITY instead: the
/// attribute SsKey is stable across the rename (A1), so the A→B `CatalogDiff`'s
/// attribute renames give a source-Name → sink-Name re-key that moves each
/// source row's values onto the sink's names BEFORE the write. The two
/// strategies — RefactorLog (in-place rename) and Transfer (cross-DB move) —
/// compose here: the rename map projects the rows Transfer carries.
[<RequireQualifiedAccess>]
module RenameProjection =

    /// One attribute renamed between the source (A) and the sink (B): the
    /// stable SsKey (A1) and the source/sink logical names — the keys
    /// `StaticRow.Values` is indexed by on each side.
    type ColumnRename =
        {
            Attribute  : SsKey
            SourceName : Name
            SinkName   : Name
        }

    /// Extract the attribute renames from an A→B diff: every kind's
    /// `AttributeDiff.Renamed` is a same-SsKey name change (6.A.10). Pure;
    /// deterministic (sorted by attribute identity, T1).
    let renames (diff: CatalogDiff) : ColumnRename list =
        CatalogDiff.attributeDiffs diff
        |> Map.toList
        |> List.collect (fun (_, ad) ->
            ad.Renamed
            |> Map.toList
            |> List.map (fun (attrKey, record) ->
                { Attribute = attrKey; SourceName = record.OldName; SinkName = record.NewName }))
        |> List.sortBy (fun r -> SsKey.rootOriginal r.Attribute)

    /// The source-Name → sink-Name re-key map the row projection consumes.
    let renameMap (cols: ColumnRename list) : Map<Name, Name> =
        cols |> List.map (fun c -> c.SourceName, c.SinkName) |> Map.ofList

    /// Re-point one source row's values onto the sink's names: every key in the
    /// rename map is re-keyed to its sink name; un-renamed keys pass through
    /// untouched. The match is by NAME (identity-derived, A1-stable), never by
    /// ordinal — so a renamed column lands in the correct sink column regardless
    /// of column order. Empty map → identity (the no-rename Transfer is
    /// byte-identical).
    let repointRow (map: Map<Name, Name>) (row: StaticRow) : StaticRow =
        if Map.isEmpty map then row
        else
            let values =
                row.Values
                |> Map.toList
                |> List.map (fun (name, v) ->
                    (Map.tryFind name map |> Option.defaultValue name), v)
                |> Map.ofList
            { row with Values = values }

    /// Re-point every row in a per-kind row source. Convenience for the
    /// Transfer ingest→write seam: ingest with the source contract (old names),
    /// re-point, then write with the sink contract (new names).
    let repointRows (map: Map<Name, Name>) (rows: StaticRow list) : StaticRow list =
        if Map.isEmpty map then rows
        else rows |> List.map (repointRow map)

namespace Twin.Core

open Projection.Core

/// THE_TWIN — the schema-derived evidence FLOOR (zero-configuration realism).
///
/// A pure projection from the estate's own catalog — column names, types,
/// declared lengths, nullability — to a rich-tier evidence pack of PLAUSIBLE,
/// INVENTED distributions: an email-named column gets an email-shaped
/// vocabulary, an amount-named numeric gets a non-negative range, a
/// status-named text column gets a small stable vocabulary the cardinality
/// rule then preserves, a chronological column gets a fixed recent window.
/// Every value is derived, never captured, so the pack carries no privacy
/// dimension; every vocabulary fits the column's declared length and stays
/// at or under the preserve threshold.
///
/// The floor layers UNDER captured evidence (Mint: floor → shape → rich →
/// scenario overlay): wherever a sampled checkpoint speaks, the checkpoint
/// wins; wherever it does not — a column added after the last capture, or an
/// estate with no capture at all — the floor replaces the bare type-default
/// so the mint stays realistic with zero configuration. Volume resolution
/// deliberately ignores the floor (Mint computes evidenced-kinds from
/// captured evidence only), so nominal row counts here can never hijack how
/// many rows a kind mints.
///
/// Determinism: constants only — no clock, no randomness; the chronological
/// window is a fixed tick range. The same catalog derives the same pack,
/// byte-identical, so S-stable re-mints hold across checkpoint refreshes.
[<RequireQualifiedAccess>]
module DerivedEvidence =

    /// Provenance label carried in the pack's Sources.
    let sourceLabel : string = "schema-derived"

    /// Nominal per-column counts: distributions need a denominator; volumes
    /// never read these (see the module header).
    let private nominalRows : int64 = 1000L
    let private nullableNulls : int64 = 100L

    /// The fixed chronological window (ticks): 2025-01-01 .. 2026-01-01.
    let private windowStartTicks : decimal = decimal (System.DateTime(2025, 1, 1)).Ticks
    let private windowEndTicks : decimal = decimal (System.DateTime(2026, 1, 1)).Ticks

    let private shapeBetween (lo: decimal) (hi: decimal) : NumericShape =
        let at (f: decimal) = lo + (hi - lo) * f
        { Min = lo; P25 = at 0.25m; P50 = at 0.5m; P75 = at 0.75m
          P95 = at 0.95m; P99 = at 0.99m; Max = hi }

    // ------------------------------------------------------------------
    // vocabularies (each ≤ the preserve threshold; invented, never captured)
    // ------------------------------------------------------------------

    let private smallVocab : string list =
        [ "Alpha"; "Beta"; "Gamma"; "Delta"; "Epsilon" ]

    let private wordA = [ "amber"; "cedar"; "delta"; "ember"; "flint"; "harbor"; "juniper"; "meadow" ]
    let private wordB = [ "brook"; "field"; "gate"; "hill"; "lane"; "point"; "ridge"; "stone" ]

    let private emails : string list =
        [ for i in 1 .. 24 -> sprintf "user%02d@example.test" i ]

    let private phones : string list =
        [ for i in 1 .. 20 -> sprintf "0500%06d" (100000 + i * 3571) ]

    let private personNames : string list =
        List.allPairs (List.truncate 6 wordA) (List.truncate 4 wordB)
        |> List.map (fun (a, b) -> sprintf "%c%s %c%s" (System.Char.ToUpperInvariant a.[0]) (a.Substring 1) (System.Char.ToUpperInvariant b.[0]) (b.Substring 1))

    let private places : string list =
        List.truncate 12 (List.allPairs wordA wordB |> List.map (fun (a, b) -> sprintf "%c%s %c%s" (System.Char.ToUpperInvariant a.[0]) (a.Substring 1) (System.Char.ToUpperInvariant b.[0]) (b.Substring 1)))

    let private codes : string list =
        [ for i in 1 .. 30 -> sprintf "AB-%04d" (1000 + i * 97) ]

    let private sentences : string list =
        List.allPairs (List.truncate 4 wordA) (List.truncate 4 wordB)
        |> List.map (fun (a, b) -> sprintf "A note about the %s %s." a b)

    let private genericWords : string list =
        List.allPairs (List.truncate 5 wordA) (List.truncate 4 wordB)
        |> List.map (fun (a, b) -> sprintf "%s %s" a b)

    // ------------------------------------------------------------------
    // per-column derivation
    // ------------------------------------------------------------------

    let private containsAny (name: string) (needles: string list) : bool =
        needles |> List.exists (fun n -> name.Contains(n: string))

    /// Fit a vocabulary to a declared length: truncate each value, then
    /// dedupe; a vocabulary that collapses below two distinct values derives
    /// nothing (the type default is honest at that width).
    let private fitted (length: int option) (vocab: string list) : (string * int64) list option =
        let cut (s: string) =
            match length with
            | Some n when n > 0 && s.Length > n -> s.Substring(0, n)
            | _ -> s
        let distinct = vocab |> List.map cut |> List.distinct
        if List.length distinct < 2 then None
        else
            let count = List.length distinct
            Some (distinct |> List.mapi (fun i v -> v, int64 (count - i + 1)))

    let private textFrequencies (columnName: string) (length: int option) : (string * int64) list option =
        let n = columnName.ToLowerInvariant()
        let vocab =
            if containsAny n [ "email"; "mail" ] then emails
            elif containsAny n [ "phone"; "mobile"; "fax" ] then phones
            elif containsAny n [ "status"; "type"; "category"; "channel"; "state"; "kind"; "tier"; "level"; "stage"; "priority"; "severity" ] then smallVocab
            elif containsAny n [ "code"; "sku"; "reference"; "token" ] then codes
            elif containsAny n [ "city"; "country"; "region"; "province" ] then places
            elif containsAny n [ "name"; "title"; "label" ] then personNames
            elif containsAny n [ "description"; "note"; "comment"; "memo"; "detail"; "summary" ] then sentences
            else genericWords
        match length with
        | Some width when width < 3 -> None // no room for a vocabulary; the type default is honest
        | _ -> fitted length vocab

    let private numericShape (columnName: string) (ptype: PrimitiveType) : NumericShape =
        let n = columnName.ToLowerInvariant()
        if containsAny n [ "amount"; "total"; "price"; "cost"; "fee"; "salary"; "balance"; "revenue" ] then shapeBetween 0m 10000m
        elif containsAny n [ "percent"; "rate"; "ratio"; "discount" ] then shapeBetween 0m 100m
        elif containsAny n [ "year" ] then shapeBetween 1990m 2026m
        elif containsAny n [ "age" ] then shapeBetween 18m 90m
        elif containsAny n [ "quantity"; "qty"; "count"; "units" ] then shapeBetween 1m 500m
        else
            match ptype with
            | Decimal -> shapeBetween 0m 1000m
            | _ -> shapeBetween 1m 1000m

    /// Derive one column's floor evidence; `None` where the type default is
    /// already the honest answer (booleans, binaries, guids, times) or where
    /// the declared width leaves no room for a vocabulary.
    let deriveColumn (a: Attribute) : ColumnEvidence option =
        let columnName = ColumnName.value a.Column.ColumnName
        let nulls = if a.Column.IsNullable then nullableNulls else 0L
        let column frequencies numeric =
            let distinct = frequencies |> List.length |> int64
            Some
                { Column = columnName
                  RowCount = nominalRows
                  NullCount = nulls
                  MaxLength = a.Length
                  DistinctCount = (if List.isEmpty frequencies then None else Some distinct)
                  Truncated = false
                  Frequencies = frequencies
                  Numeric = numeric }
        if a.IsPrimaryKey then None
        else
            match a.Type with
            | Text ->
                match textFrequencies columnName a.Length with
                | Some freqs -> column freqs None
                | None -> None
            | Integer | Decimal -> column [] (Some (numericShape columnName a.Type))
            | DateTime | Date -> column [] (Some (shapeBetween windowStartTicks windowEndTicks))
            | Boolean | Time | Binary | Guid -> None

    /// The whole estate's floor pack. Pure; deterministic; rich-tier (its
    /// values are invented, so the tier carries no captured literal).
    let pack (catalog: Catalog) : EvidencePack =
        let tables =
            Catalog.allKinds catalog
            |> List.choose (fun kind ->
                let columns = kind.Attributes |> List.choose deriveColumn
                if List.isEmpty columns then None
                else
                    Some
                        { Table = TableCoordinate.text (TwinIdentity.coordinateOfKind kind)
                          RowCount = nominalRows
                          Columns = columns })
        { Tier = RichTier; Sources = [ sourceLabel ]; Tables = tables; FanOuts = [] }
